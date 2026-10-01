using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public sealed class OAuthClient(string id, string? secret)
{
    public string Id { get; } = id;
    public string? Secret { get; } = secret;
    public override string ToString() => "OAuthClient [redacted]";
    public static OAuthClient FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("installed", out var client)) throw new InvalidDataException("A desktop OAuth client JSON is required.");
        var id = client.GetProperty("client_id").GetString();
        if (string.IsNullOrWhiteSpace(id) || !id.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal)) throw new InvalidDataException("Invalid OAuth client ID.");
        return new(id, client.TryGetProperty("client_secret", out var s) ? s.GetString() : null);
    }
}
public sealed class GoogleTokens(string clientId, string accountId, string accessToken, string refreshToken, DateTimeOffset expiresAt)
{
    public string ClientId { get; } = clientId;
    public string AccountId { get; } = accountId;
    public string AccessToken { get; } = accessToken;
    public string RefreshToken { get; } = refreshToken;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
    public override string ToString() => "GoogleTokens [redacted]";
}
public interface ITokenVault
{
    GoogleTokens? Load();
    void Save(GoogleTokens tokens);
}
public sealed class DpapiTokenVault : ITokenVault
{
    private readonly string path;
    private readonly byte[] entropy;
    public DpapiTokenVault(string directory, string clientId)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Token persistence requires Windows DPAPI.");
        PrivateDirectory.Create(directory);
        entropy = SHA256.HashData(Encoding.UTF8.GetBytes(clientId));
        path = Path.Combine(directory, Convert.ToHexString(entropy) + ".tokens.dat");
    }
    public GoogleTokens? Load()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 65536) throw new AuthorizationRequiredException();
        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), entropy, DataProtectionScope.CurrentUser);
            try { return JsonSerializer.Deserialize<GoogleTokens>(bytes) ?? throw new AuthorizationRequiredException(); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (CryptographicException) { throw new AuthorizationRequiredException(); }
    }
    public void Save(GoogleTokens tokens)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(tokens);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var encrypted = ProtectedData.Protect(bytes, entropy, DataProtectionScope.CurrentUser);
            using (var f = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(encrypted); f.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
public sealed class OAuthProof
{
    public string State { get; } = Base64(RandomNumberGenerator.GetBytes(32));
    public string Verifier { get; } = Base64(RandomNumberGenerator.GetBytes(32));
    public string Challenge => Base64(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)));
    private static string Base64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public Uri AuthorizationUri(string clientId, Uri redirect)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirect.AbsoluteUri,
            ["response_type"] = "code",
            ["scope"] = GoogleOAuth.Scope,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = State,
            ["code_challenge"] = Challenge,
            ["code_challenge_method"] = "S256"
        };
        return new Uri("https://accounts.google.com/o/oauth2/v2/auth?" + string.Join('&', query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))));
    }
    public string? Callback(string target, string expectedPath)
    {
        if (!Uri.TryCreate("http://127.0.0.1" + target, UriKind.Absolute, out var uri) || uri.AbsolutePath != expectedPath) return null;
        var fields = new Dictionary<string, string>();
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2); if (pair.Length != 2 || !fields.TryAdd(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1].Replace('+', ' ')))) return null;
        }
        if (!fields.TryGetValue("state", out var state) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(State))) return null;
        if (fields.ContainsKey("error")) throw new AuthorizationRequiredException();
        return fields.TryGetValue("code", out var code) && !string.IsNullOrWhiteSpace(code) ? code : null;
    }
}
public sealed record AuthorizationCode(string Code, Uri Redirect);
public interface IAuthorizationReceiver
{ Task<AuthorizationCode> ReceiveAsync(OAuthClient client, OAuthProof proof, CancellationToken ct); }
public sealed class LoopbackAuthorizationReceiver(Action<Uri> openBrowser) : IAuthorizationReceiver
{
    public async Task<AuthorizationCode> ReceiveAsync(OAuthClient client, OAuthProof proof, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var path = "/oauth/" + Guid.NewGuid().ToString("N"); var redirect = new Uri($"http://127.0.0.1:{port}{path}");
            openBrowser(proof.AuthorizationUri(client.Id, redirect)); // listener already owns port
            while (true)
            {
                using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await using var stream = socket.GetStream();
                try
                {
                    var raw = new List<byte>(); var one = new byte[1];
                    while (raw.Count < 8192)
                    {
                        if (await stream.ReadAsync(one, timeout.Token) == 0) break; raw.Add(one[0]);
                        if (raw.Count >= 4 && raw.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break;
                    }
                    var lines = Encoding.ASCII.GetString(raw.ToArray()).Split("\r\n");
                    var first = lines[0].Split(' '); string? code = null;
                    if (raw.Count < 8192 && first.Length == 3 && first[0] == "GET" && first[2] == "HTTP/1.1"
                        && lines.Any(l => l.Equals($"Host: 127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase))) code = proof.Callback(first[1], path);
                    var body = Encoding.UTF8.GetBytes(code is null ? "Resposta inválida. Continue o login na janela Google." : "Resposta recebida. Aguarde a conclusão no aplicativo.");
                    var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {(code is null ? "400 Bad Request" : "200 OK")}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(head, timeout.Token); await stream.WriteAsync(body, timeout.Token);
                    if (code is not null) return new(code, redirect);
                }
                catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { }
                catch (AuthorizationRequiredException) { throw; }
                catch (IOException) { }
            }
        }
        finally { listener.Stop(); }
    }
}
public sealed class GoogleOAuth(HttpClient http, OAuthClient client, ITokenVault vault, IAuthorizationReceiver receiver, IOperationLock locks) : IGoogleAuth
{
    public const string Scope = "https://www.googleapis.com/auth/drive.file";
    private string Key => "oauth:" + client.Id;
    public async Task<GoogleAccess> ConnectAsync(CancellationToken ct = default)
    {
        await using var held = await locks.AcquireAsync(Key, ct);
        var proof = new OAuthProof(); var result = await receiver.ReceiveAsync(client, proof, ct);
        var body = new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = result.Code, ["code_verifier"] = proof.Verifier, ["redirect_uri"] = result.Redirect.AbsoluteUri };
        var raw = await TokenAsync(body, ct); var account = await AccountAsync(raw.Access, ct);
        var tokens = new GoogleTokens(client.Id, account, raw.Access, raw.Refresh ?? throw new AuthorizationRequiredException(), DateTimeOffset.UtcNow.AddSeconds(raw.Seconds));
        vault.Save(tokens); return new(account, tokens.AccessToken);
    }
    public async Task<GoogleAccess> AccessAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        await using var held = await locks.AcquireAsync(Key, cancellationToken);
        var tokens = vault.Load() ?? throw new AuthorizationRequiredException();
        if (tokens.ClientId != client.Id || string.IsNullOrWhiteSpace(tokens.RefreshToken) || string.IsNullOrWhiteSpace(tokens.AccountId)) throw new AuthorizationRequiredException();
        if (refresh || tokens.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            var raw = await TokenAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = tokens.RefreshToken }, cancellationToken);
            var account = await AccountAsync(raw.Access, cancellationToken);
            if (account != tokens.AccountId) throw new AuthorizationRequiredException();
            tokens = new(client.Id, account, raw.Access, raw.Refresh ?? tokens.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(raw.Seconds)); vault.Save(tokens);
        }
        return new(tokens.AccountId, tokens.AccessToken);
    }
    private async Task<(string Access, string? Refresh, int Seconds)> TokenAsync(Dictionary<string, string> values, CancellationToken ct)
    {
        values["client_id"] = client.Id; if (client.Secret is not null) values["client_secret"] = client.Secret;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token") { Content = new FormUrlEncodedContent(values) };
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.BadRequest) throw new AuthorizationRequiredException();
        if (!response.IsSuccessStatusCode) throw new GoogleApiException((int)response.StatusCode);
        using var stream = await response.Content.ReadAsStreamAsync(ct); using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct); var e = doc.RootElement;
        var access = e.GetProperty("access_token").GetString(); var seconds = e.GetProperty("expires_in").GetInt32();
        if (string.IsNullOrWhiteSpace(access) || seconds <= 0 || !e.GetProperty("token_type").GetString()!.Equals("Bearer", StringComparison.OrdinalIgnoreCase)) throw new AuthorizationRequiredException();
        if (e.TryGetProperty("scope", out var scope) && !scope.GetString()!.Split(' ').Contains(Scope)) throw new AuthorizationRequiredException();
        return (access, e.TryGetProperty("refresh_token", out var r) ? r.GetString() : null, seconds);
    }
    private async Task<string> AccountAsync(string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/drive/v3/about?fields=user(permissionId)"); request.Headers.Authorization = new("Bearer", token);
        using var response = await http.SendAsync(request, ct); if (!response.IsSuccessStatusCode) throw new GoogleApiException((int)response.StatusCode);
        using var stream = await response.Content.ReadAsStreamAsync(ct); using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var id = doc.RootElement.GetProperty("user").GetProperty("permissionId").GetString(); GoogleDriveClient.ValidateId(id!);
        return client.Id + ":" + id;
    }
}
