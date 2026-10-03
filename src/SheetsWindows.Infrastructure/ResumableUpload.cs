using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public sealed class ResumableUpload(HttpClient http, IGoogleAuth auth, UploadSessionStore sessions, TimeSpan? requestTimeout = null)
{
    public bool HasSession(RemoteAttempt attempt, byte[] bytes, string mime) => sessions.Get(attempt, bytes, mime) is not null;
    public async Task<string> StartAsync(RemoteAttempt attempt, string metadata, byte[] bytes, string mime, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(requestTimeout ?? TimeSpan.FromSeconds(90)); ct = deadline.Token;
        if (bytes.Length == 0) throw new InvalidDataException("Empty upload.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id")
        { Content = new StringContent(metadata, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Upload-Content-Type", mime); request.Headers.Add("X-Upload-Content-Length", bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var response = await SendAsync(request, attempt.AccountId, false, ct);
        if (!response.IsSuccessStatusCode) throw new GoogleApiException((int)response.StatusCode);
        var location = response.Headers.Location ?? throw new InvalidDataException("Missing upload session.");
        sessions.Save(attempt, bytes, mime, location); // Durable before the first content PUT.
        return await ResumeAsync(attempt, bytes, mime, ct);
    }
    public async Task<string> ResumeAsync(RemoteAttempt attempt, byte[] bytes, string mime, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(requestTimeout ?? TimeSpan.FromSeconds(90)); ct = deadline.Token;
        var session = sessions.Get(attempt, bytes, mime) ?? throw new ReconciliationRequiredException();
        var location = UploadSessionStore.ValidateLocation(session.Location);
        var offset = 0; var query = true; var stalled = 0; var transient = 0; var refresh = false; var refreshed = false;
        for (var requests = 0; requests < 200; requests++)
        {
            ct.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Put, location);
            var count = query ? 0 : Math.Min(256 * 1024, bytes.Length - offset);
            request.Content = new ByteArrayContent(bytes, query ? 0 : offset, count);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(mime);
            request.Content.Headers.ContentRange = query ? new ContentRangeHeaderValue(bytes.Length) : new ContentRangeHeaderValue(offset, offset + count - 1, bytes.Length);
            HttpResponseMessage response;
            try { response = await SendAsync(request, attempt.AccountId, refresh, ct); refresh = false; }
            catch (HttpRequestException) when (++transient <= 3) { query = true; await Task.Delay(TimeSpan.FromMilliseconds(200 * transient), ct); continue; }
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
                { if (++transient > 3) throw new AuthorizationRequiredException(); refresh = true; refreshed = true; query = true; continue; }
                if ((int)response.StatusCode >= 500 || (int)response.StatusCode == 429)
                { if (++transient > 3) throw new GoogleApiException((int)response.StatusCode); query = true; await Task.Delay(TimeSpan.FromMilliseconds(200 * transient), ct); continue; }
                if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
                {
                    if (response.Content.Headers.ContentLength > 65536) throw new InvalidDataException("Upload response too large.");
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var buffer = new MemoryStream(); var chunk = new byte[8192]; int read;
                    while ((read = await stream.ReadAsync(chunk, ct)) != 0) { if (buffer.Length + read > 65536) throw new InvalidDataException("Upload response too large."); buffer.Write(chunk, 0, read); }
                    using var json = JsonDocument.Parse(buffer.ToArray()); var id = json.RootElement.GetProperty("id").GetString()!; GoogleDriveClient.ValidateId(id); return id;
                }
                if ((int)response.StatusCode != 308) throw new ReconciliationRequiredException(); // Never create a second session automatically after expiry.
                var next = 0;
                if (response.Headers.TryGetValues("Range", out var values))
                {
                    var range = values.ToArray(); var match = range.Length == 1 ? Regex.Match(range[0], "^bytes=0-([0-9]+)$") : Match.Empty;
                    if (!match.Success || !int.TryParse(match.Groups[1].Value, out var end) || end >= bytes.Length - 1) throw new InvalidDataException("Invalid upload acknowledgment.");
                    next = end + 1;
                }
                if (!query && (next < offset || next > offset + count)) throw new InvalidDataException("Invalid upload acknowledgment.");
                if (next == offset && !query && ++stalled > 2) throw new ReconciliationRequiredException();
                if (next > offset) stalled = 0;
                offset = next; query = false;
            }
        }
        throw new ReconciliationRequiredException();
    }
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string account, bool refresh, CancellationToken ct)
    {
        var access = await auth.AccessAsync(refresh, ct);
        if (access.AccountId != account) throw new InvalidOperationException("Google account changed.");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.Token);
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }
}
