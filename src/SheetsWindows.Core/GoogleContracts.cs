namespace SheetsWindows.Core;

public sealed class GoogleAccess(string accountId, string token)
{
    public string AccountId { get; } = accountId;
    public string Token { get; } = token;
    public override string ToString() => "GoogleAccess [redacted]";
}
public interface IGoogleAuth { Task<GoogleAccess> AccessAsync(bool refresh = false, CancellationToken cancellationToken = default); }
public sealed record RemoteAttempt(string Key, string AccountId, string Kind, string Hash, string Marker, string? FileId, bool Verified);
public interface IRemoteRegistry
{
    RemoteAttempt? Get(string key);
    RemoteAttempt Begin(string key, string accountId, string kind, string hash);
    void Candidate(string key, string id);
    void Verify(string key, string id);
}
public sealed class ReconciliationRequiredException() : IOException("Remote creation outcome is uncertain; reconciliation required. No new upload was attempted.");
public sealed class GoogleApiException(int status) : IOException($"Google request failed (HTTP {status}).") { public int Status { get; } = status; }
public sealed class AuthorizationRequiredException() : IOException("Google sign-in is required.");
