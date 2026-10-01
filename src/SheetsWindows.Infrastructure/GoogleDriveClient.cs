using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public sealed record GoogleFile(string Id, string MimeType, bool Trashed, bool CanEdit, Dictionary<string, string> Properties);
public sealed class GoogleDriveClient(HttpClient http, IGoogleAuth auth)
{
    public const string SheetMime = "application/vnd.google-apps.spreadsheet";
    public const string FolderMime = "application/vnd.google-apps.folder";
    public static void ValidateId(string id)
    { if (string.IsNullOrEmpty(id) || !Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,200}$")) throw new InvalidDataException("Invalid Google file ID."); }
    public static Uri Editor(string id) { ValidateId(id); return new Uri($"https://docs.google.com/spreadsheets/d/{id}/edit"); }
    private async Task<JsonDocument> SendAsync(Func<HttpRequestMessage> request, string account, CancellationToken ct)
    {
        for (var i = 0; i < 2; i++)
        {
            var access = await auth.AccessAsync(i == 1, ct);
            if (access.AccountId != account) throw new InvalidOperationException("Google account changed.");
            using var req = request(); req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.Token);
            using var response = await http.SendAsync(req, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized && req.Method == HttpMethod.Get && i == 0) continue;
            if (!response.IsSuccessStatusCode) throw new GoogleApiException((int)response.StatusCode);
            using var stream = await response.Content.ReadAsStreamAsync(ct); return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        throw new AuthorizationRequiredException();
    }
    private static GoogleFile File(JsonElement e)
    {
        var id = e.GetProperty("id").GetString()!; ValidateId(id);
        var props = new Dictionary<string, string>();
        if (e.TryGetProperty("appProperties", out var p)) foreach (var x in p.EnumerateObject()) props[x.Name] = x.Value.GetString()!;
        return new(id, e.GetProperty("mimeType").GetString()!, e.GetProperty("trashed").GetBoolean(),
            e.TryGetProperty("capabilities", out var caps) && caps.TryGetProperty("canEdit", out var edit) && edit.GetBoolean(), props);
    }
    private const string Fields = "id,mimeType,trashed,capabilities(canEdit),appProperties";
    public async Task<GoogleFile> GetAsync(string account, string id, CancellationToken ct)
    {
        ValidateId(id); using var json = await SendAsync(() => new(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files/{id}?fields={Uri.EscapeDataString(Fields)}"), account, ct);
        return File(json.RootElement);
    }
    public async Task<IReadOnlyList<GoogleFile>> FindAsync(RemoteAttempt attempt, CancellationToken ct)
    {
        var files = new List<GoogleFile>(); string? page = null;
        do
        {
            var q = $"appProperties has {{ key='sw_operation' and value='{attempt.Marker}' }} and trashed=false";
            var url = "https://www.googleapis.com/drive/v3/files?q=" + Uri.EscapeDataString(q) + "&pageSize=100&fields=" + Uri.EscapeDataString($"nextPageToken,files({Fields})");
            if (page is not null) url += "&pageToken=" + Uri.EscapeDataString(page);
            using var json = await SendAsync(() => new(HttpMethod.Get, url), attempt.AccountId, ct);
            foreach (var e in json.RootElement.GetProperty("files").EnumerateArray()) files.Add(File(e));
            if (files.Count > 1) throw new ReconciliationRequiredException();
            page = json.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        } while (page is not null);
        return files;
    }
    public async Task<string> CreateAsync(RemoteAttempt attempt, string name, string? folder, byte[]? bytes, CancellationToken ct)
    {
        var meta = new Dictionary<string, object>
        {
            ["name"] = name,
            ["mimeType"] = attempt.Kind == "sheet" ? SheetMime : FolderMime,
            ["appProperties"] = new Dictionary<string, string> { ["sw_operation"] = attempt.Marker, ["sw_hash"] = attempt.Hash }
        };
        if (folder is not null) { ValidateId(folder); meta["parents"] = new[] { folder }; }
        var metadata = JsonSerializer.Serialize(meta);
        using var json = await SendAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, bytes is null ? "https://www.googleapis.com/drive/v3/files?fields=id" : "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id");
            if (bytes is null) req.Content = new StringContent(metadata, Encoding.UTF8, "application/json");
            else
            {
                var content = new MultipartContent("related"); content.Add(new StringContent(metadata, Encoding.UTF8, "application/json"));
                var media = new ByteArrayContent(bytes); media.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"); content.Add(media); req.Content = content;
            }
            return req;
        }, attempt.AccountId, ct);
        var id = json.RootElement.GetProperty("id").GetString()!; ValidateId(id); return id;
    }
}
