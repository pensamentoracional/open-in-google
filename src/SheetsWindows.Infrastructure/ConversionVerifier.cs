using System.Security.Cryptography;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public interface IConversionVerifier
{
    Task VerifyAsync(ImportOperation operation, RemoteAttempt mapping, CancellationToken ct);
}
public sealed class ConversionVerifier(GoogleDriveClient drive, TextImportOptions? options = null) : IConversionVerifier
{
    public async Task VerifyAsync(ImportOperation operation, RemoteAttempt mapping, CancellationToken ct)
    {
        var snapshot = operation.Snapshot ?? throw new InvalidDataException("Backup required.");
        if (snapshot.Length > GoogleImport.MaxBytes || mapping.FileId is null || !mapping.Verified || mapping.AccountId != operation.AccountId || mapping.Hash != snapshot.Sha256) throw new InvalidDataException("Fidelity binding invalid.");
        await using var file = new FileStream(snapshot.BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length != snapshot.Length) throw new InvalidDataException("Backup changed.");
        var bytes = new byte[checked((int)file.Length)]; await file.ReadExactlyAsync(bytes, ct);
        if (Convert.ToHexString(SHA256.HashData(bytes)) != snapshot.Sha256) throw new InvalidDataException("Backup changed.");
        var expected = SpreadsheetFormats.Prepare(operation.Format, bytes, options).Expected ?? throw new CopyRequiredException();
        var exported = await drive.ExportXlsxAsync(operation.AccountId, mapping.FileId, ct);
        SpreadsheetFormats.VerifyValues(expected, exported);
    }
}
