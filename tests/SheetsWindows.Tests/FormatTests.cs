using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using SheetsWindows.Core;
using SheetsWindows.Infrastructure;
using Xunit;

namespace SheetsWindows.Tests;

public sealed class FormatTests
{
    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
    internal static byte[] Ods(string rows, string name = "Dados", bool script = false)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            void Part(string path, string content) { using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false)); writer.Write(content); }
            Part("mimetype", "application/vnd.oasis.opendocument.spreadsheet");
            Part("content.xml", $"<office:document-content xmlns:office='urn:oasis:names:tc:opendocument:xmlns:office:1.0' xmlns:table='urn:oasis:names:tc:opendocument:xmlns:table:1.0' xmlns:text='urn:oasis:names:tc:opendocument:xmlns:text:1.0'><office:body><office:spreadsheet><table:table table:name='{name}'>{rows}</table:table></office:spreadsheet></office:body></office:document-content>");
            if (script) Part("Basic/script.xml", "macro");
        }
        return buffer.ToArray();
    }
    internal static byte[] Xls() => Convert.FromBase64String(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "10x10.xls.b64")));
    internal static byte[] LargeCsv(int rows = 5000, int columns = 40)
    {
        var text = new StringBuilder();
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++) { if (c > 0) text.Append(';'); text.Append($"{r:D5}-{c:D2}-ação"); }
            text.Append('\n');
        }
        return Encoding.UTF8.GetPreamble().Concat(Utf8(text.ToString())).ToArray();
    }
    [Theory]
    [InlineData(5000)]
    [InlineData(12500)]
    public void LargeCsvRoundTripVerifiesEveryCellAndRejectsTruncatedExport(int rows)
    {
        var payload = SpreadsheetFormats.Prepare("csv", LargeCsv(rows));
        Assert.Equal(rows, payload.Expected![0].Rows.Count); Assert.Equal(40, payload.Expected[0].Rows[0].Count);
        SpreadsheetFormats.VerifyValues(payload.Expected, payload.Bytes);
        var truncated = new SheetValues("Dados", payload.Expected[0].Rows.Take(rows - 1).ToArray());
        Assert.Throws<ConversionMismatchException>(() => SpreadsheetFormats.VerifyValues(payload.Expected, SpreadsheetFormats.WriteXlsx([truncated])));
    }
    [Theory]
    [InlineData("auto")]
    [InlineData("semicolon")]
    public void CapacityFailureIsNotHiddenOrReinterpretedAsSingleColumn(string delimiter)
    {
        var error = Assert.Throws<SpreadsheetCapacityException>(() => SpreadsheetFormats.Prepare("csv", LargeCsv(12501, 40), new(Delimiter: delimiter)));
        Assert.Contains("células", error.Message);
        Assert.Contains("original", LauncherErrors.Message(error));
    }
    [Fact]
    public void RowColumnFieldAndByteLimitsGiveSpecificDiagnostics()
    {
        Assert.Contains("linhas", Assert.Throws<SpreadsheetCapacityException>(() => SpreadsheetFormats.Prepare("csv", Utf8(new string('\n', SpreadsheetFormats.MaxRows + 1)))).Message);
        Assert.Contains("colunas", Assert.Throws<SpreadsheetCapacityException>(() => SpreadsheetFormats.Prepare("tsv", Utf8(new string('\t', SpreadsheetFormats.MaxColumns)))).Message);
        Assert.Contains("caracteres", Assert.Throws<SpreadsheetCapacityException>(() => SpreadsheetFormats.Prepare("csv", Utf8(new string('a', 32768)))).Message);
        Assert.Contains("20 MiB", Assert.Throws<SpreadsheetCapacityException>(() => SpreadsheetFormats.Prepare("csv", new byte[GoogleImport.MaxBytes + 1])).Message);
    }
    [Fact]
    public void CancelledConversionAndVerificationStopWithoutReturningPartialData()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => SpreadsheetFormats.Prepare("csv", Utf8("a;b"), ct: stop.Token));
        Assert.Throws<OperationCanceledException>(() => SpreadsheetFormats.WriteXlsx([new SheetValues("Dados", [new object?[] { "a" }])], stop.Token));
        var payload = SpreadsheetFormats.Prepare("csv", Utf8("a;b"));
        Assert.Throws<OperationCanceledException>(() => SpreadsheetFormats.VerifyValues(payload.Expected!, payload.Bytes, stop.Token));
    }
    [Fact]
    public void BinaryXlsHasExpectedValuesAndKeepsItsOriginalPayload()
    {
        var bytes = Xls(); var payload = SpreadsheetFormats.Prepare("xls", bytes);
        Assert.Equal("application/vnd.ms-excel", payload.MimeType); Assert.NotNull(payload.Expected); Assert.Equal(bytes, payload.Bytes);
        SpreadsheetFormats.VerifyValues(payload.Expected!, SpreadsheetFormats.WriteXlsx(payload.Expected!));
        Assert.NotEmpty(SpreadsheetFormats.ReadExcel(bytes, binary: true));
    }
    [Fact]
    public void CsvRoundTripPreservesLiteralStringsQuotesUnicodeNewlinesAndEmptyCells()
    {
        var bytes = Utf8("id;valor;nota;vazio\r\n00123;\"=SUM(A1:A2)\";\"São Paulo; \"\"oi\"\"\nlinha\";\r\n");
        var payload = SpreadsheetFormats.Prepare("csv", bytes);
        SpreadsheetFormats.VerifyValues(payload.Expected!, payload.Bytes);
        var row = payload.Expected![0].Rows[1];
        Assert.Equal("00123", row[0]); Assert.Equal("=SUM(A1:A2)", row[1]); Assert.Equal("São Paulo; \"oi\"\nlinha", row[2]); Assert.Equal("", row[3]);
    }
    [Theory]
    [InlineData("a,b;c\n1,2;3")]
    [InlineData("a;b\n1;2;3")]
    [InlineData("a,b\n1,2,3")]
    [InlineData("\"unclosed")]
    [InlineData("\"closed\"tail")]
    public void AmbiguousOrMalformedTextIsNeverGuessed(string text) => Assert.Throws<InvalidDataException>(() => SpreadsheetFormats.Prepare("csv", Utf8(text)));
    [Fact]
    public void OpenXmlEscapeSequencesAndEmojiRemainLiteral()
    {
        var payload = SpreadsheetFormats.Prepare("csv", Utf8("_x0041_,😀\n_x000A_,_x005F_"));
        SpreadsheetFormats.VerifyValues(payload.Expected!, payload.Bytes);
    }
    [Fact]
    public void ExportedFormulasAreRejectedEvenIfTheirCachedTextMatches()
    {
        var payload = SpreadsheetFormats.Prepare("csv", Utf8("=1+1"));
        using var stream = new MemoryStream(); stream.Write(payload.Bytes); stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, true))
        {
            var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!; string xml; using (var reader = new StreamReader(entry.Open())) xml = reader.ReadToEnd(); entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open()); writer.Write(xml.Replace("<is>", "<f>HYPERLINK()</f><is>"));
        }
        Assert.Throws<ConversionMismatchException>(() => SpreadsheetFormats.VerifyValues(payload.Expected!, stream.ToArray()));
    }
    [Fact]
    public void ExplicitDelimiterResolvesAmbiguity()
    {
        var bytes = Utf8("a,b;c\n1,2;3");
        Assert.Equal(2, SpreadsheetFormats.Prepare("csv", bytes, new(Delimiter: "semicolon")).Expected![0].Rows[0].Count);
    }
    [Fact]
    public void EncodingIsStrictAndLegacyEncodingRequiresExplicitChoice()
    {
        byte[] legacy = [0x6E, 0x6F, 0x6D, 0x65, 0x0A, 0x63, 0x61, 0x66, 0xE9];
        Assert.Throws<InvalidDataException>(() => SpreadsheetFormats.Prepare("csv", legacy));
        Assert.Equal("café", SpreadsheetFormats.Prepare("csv", legacy, new("windows-1252")).Expected![0].Rows[1][0]);
        var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("ação\t01\nlinha\t02")).ToArray();
        var payload = SpreadsheetFormats.Prepare("tsv", utf16); SpreadsheetFormats.VerifyValues(payload.Expected!, payload.Bytes);
        Assert.Throws<InvalidDataException>(() => SpreadsheetFormats.Prepare("tsv", utf16, new("windows-1252")));
        Assert.Throws<InvalidDataException>(() => SpreadsheetFormats.Prepare("csv", [0, 1, 2]));
    }
    [Fact]
    public void CsvChangesInTypeValuePositionOrSheetsFailFidelity()
    {
        var payload = SpreadsheetFormats.Prepare("csv", Utf8("codigo,valor\n001,=1+1"));
        var changed = new SheetValues("Dados", [new object?[] { "codigo", "valor" }, new object?[] { 1.0, "=1+1" }]);
        Assert.Throws<ConversionMismatchException>(() => SpreadsheetFormats.VerifyValues(payload.Expected!, SpreadsheetFormats.WriteXlsx([changed])));
        var renamed = payload.Expected![0] with { Name = "Outro" };
        Assert.Throws<ConversionMismatchException>(() => SpreadsheetFormats.VerifyValues(payload.Expected!, SpreadsheetFormats.WriteXlsx([renamed])));
    }
    [Fact]
    public void OdsTypedValuesAndRepeatsAreComparedAcrossExport()
    {
        var bytes = Ods("<table:table-row table:number-rows-repeated='2'><table:table-cell office:value-type='string'><text:p>ação</text:p></table:table-cell><table:table-cell office:value-type='float' office:value='12.5'/><table:table-cell office:value-type='boolean' office:boolean-value='true' table:number-columns-repeated='2'/></table:table-row>");
        var payload = SpreadsheetFormats.Prepare("ods", bytes);
        Assert.Equal("application/vnd.oasis.opendocument.spreadsheet", payload.MimeType); Assert.Equal(2, payload.Expected![0].Rows.Count);
        SpreadsheetFormats.VerifyValues(payload.Expected!, SpreadsheetFormats.WriteXlsx(payload.Expected!));
    }
    [Fact]
    public void OdsFormulasDatesAndMergesAreCopyOnlyAndScriptsAreBlocked()
    {
        Assert.Null(SpreadsheetFormats.Prepare("ods", Ods("<table:table-row><table:table-cell table:formula='of:=1+1' office:value-type='float' office:value='2'/></table:table-row>")).Expected);
        Assert.Null(SpreadsheetFormats.Prepare("ods", Ods("<table:table-row><table:table-cell office:value-type='date' office:date-value='2026-10-01'/></table:table-row>")).Expected);
        Assert.Null(SpreadsheetFormats.Prepare("ods", Ods("<table:table-row><table:covered-table-cell/></table:table-row>")).Expected);
        Assert.Throws<NotSupportedException>(() => SpreadsheetFormats.Prepare("ods", Ods("", script: true)));
        Assert.Throws<InvalidDataException>(() => SpreadsheetFormats.Prepare("ods", Ods("<table:table-row table:number-rows-repeated='1048576'><table:table-cell/></table:table-row>")));
    }
    [Fact]
    public async Task RegistryStoresActualFormatAndRecoveryPreservesOriginalBytes()
    {
        using var w = new Workspace(); var path = Path.ChangeExtension(w.Source, ".csv"); File.WriteAllBytes(path, Utf8("codigo,valor\n001,1"));
        var op = await w.Coordinator().PrepareAsync("A", path); Assert.Equal("csv", op.Format);
        var destination = Path.Combine(w.Root, "restored.csv"); await new BackupRecovery(new(Path.Combine(w.Root, "state"))).RestoreAsync(op.Id, destination);
        Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(destination));
        var renamed = Path.ChangeExtension(path, ".tsv"); File.Move(path, renamed);
        if (OperatingSystem.IsWindows()) await Assert.ThrowsAsync<LocalConflictException>(() => w.Coordinator().PrepareAsync("A", renamed));
    }
    [Fact]
    public void SchemaOneMigrationPreservesOperationsAliasesAndJournal()
    {
        using var w = new Workspace(); Directory.CreateDirectory(Path.GetDirectoryName(w.Database)!); var id = Guid.NewGuid();
        using (var db = new SqliteConnection($"Data Source={w.Database};Pooling=False"))
        {
            db.Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE operations(id TEXT PRIMARY KEY,account_id TEXT,source_key TEXT,source_path TEXT,format TEXT CHECK(format='xlsx'),state TEXT,backup_path TEXT,sha256 TEXT,length INTEGER,version INTEGER,UNIQUE(account_id,source_key));
                CREATE TABLE source_aliases(account_id TEXT,source_key TEXT,path TEXT,PRIMARY KEY(account_id,source_key,path));
                CREATE TABLE journal(sequence INTEGER PRIMARY KEY AUTOINCREMENT,operation_id TEXT REFERENCES operations(id),kind TEXT,code TEXT,created_at TEXT);
                INSERT INTO operations VALUES($id,'A','key','original.xlsx','xlsx','Prepared',NULL,NULL,NULL,0);
                INSERT INTO source_aliases VALUES('A','key','original.xlsx');
                INSERT INTO journal VALUES(7,$id,'Prepared',NULL,'2026-10-01');
                PRAGMA user_version=1;
                """; cmd.Parameters.AddWithValue("$id", id.ToString("N")); cmd.ExecuteNonQuery();
        }
        var registry = w.Registry(); Assert.Equal(id, registry.Get(id)!.Id); Assert.Equal(7, Assert.Single(registry.Events(id)).Sequence);
        var csv = registry.GetOrCreate("A", new("new-key", "new.csv", "csv")); Assert.Equal("csv", csv.Format); Assert.True(Assert.Single(registry.Events(csv.Id)).Sequence > 7);
        using var check = new SqliteConnection($"Data Source={w.Database};Pooling=False"); check.Open(); using var query = check.CreateCommand(); query.CommandText = "PRAGMA foreign_key_check"; using var read = query.ExecuteReader(); Assert.False(read.Read());
    }
    [Theory]
    [InlineData(0x9000001Au, true)]
    [InlineData(0x9000F01Au, true)]
    [InlineData(0xA000000Cu, false)]
    [InlineData(0xA0000003u, false)]
    public void CloudTagsNeverIncludeSymlinksOrJunctions(uint tag, bool expected) => Assert.Equal(expected, SourceEnvironment.IsCloudTag(tag));
    [Fact]
    public void ExtendedSetupIsOptInAndCannotSilentlyChangeTextInterpretation()
    {
        using var w = new Workspace(); var storage = new LocalStorage(Path.Combine(w.Root, "settings"));
        ExtendedConfiguration.Save(storage, new(), false); Assert.False(Directory.Exists(storage.Root));
        Assert.Throws<LauncherNotConfiguredException>(() => ExtendedConfiguration.Load(storage));
        ExtendedConfiguration.Save(storage, new(), true); ExtendedConfiguration.Save(storage, new(), true);
        Assert.Throws<InvalidOperationException>(() => ExtendedConfiguration.Save(storage, new("windows-1252"), true)); Assert.Equal(new(), ExtendedConfiguration.Load(storage));
    }
    [Fact]
    public async Task XlsPreferenceDefaultsToReplaceAndCanChangeWithoutTouchingTextSettings()
    {
        using var w = new Workspace(); var storage = new LocalStorage(Path.Combine(w.Root, "settings"));
        Assert.True(XlsReplacementSettings.Load(storage)); ExtendedConfiguration.Save(storage, new(), true);
        await XlsReplacementSettings.SaveAsync(storage, false); Assert.False(XlsReplacementSettings.Load(storage));
        Assert.Equal(new(), ExtendedConfiguration.Load(storage));
        await XlsReplacementSettings.SaveAsync(storage, true); Assert.True(XlsReplacementSettings.Load(storage));
        File.WriteAllText(Path.Combine(storage.Root, "xls-replacement.json"), "invalid");
        Assert.Throws<System.Text.Json.JsonException>(() => XlsReplacementSettings.Load(storage));
    }
    [Fact]
    public void ShortcutUsesStableUserIconAndKeepsLegacyBytesForRecovery()
    {
        using var w = new Workspace(); var storage = new LocalStorage(Path.Combine(w.Root, "settings"));
        var icon = ShortcutIcon.Ensure(storage); Assert.Equal(icon, ShortcutIcon.Ensure(storage));
        var url = new Uri("https://docs.google.com/spreadsheets/d/test/edit");
        var current = InternetShortcut.Bytes(url, icon); Assert.Contains("IconFile=" + icon, Encoding.UTF8.GetString(current));
        Assert.Contains("IconIndex=0", Encoding.UTF8.GetString(current));
        var old = Path.Combine(w.Root, "old.url"); File.WriteAllBytes(old, InternetShortcut.Bytes(url));
        Assert.Equal(InternetShortcut.Bytes(url), InternetShortcut.ForExisting(old, url, icon));
        File.WriteAllBytes(icon, [1]); Assert.Throws<InvalidDataException>(() => ShortcutIcon.Ensure(storage));
    }
    [Theory]
    [InlineData("csv")]
    [InlineData("tsv")]
    [InlineData("ods")]
    [InlineData("xls")]
    public void NewFormatsHaveExplicitOpenAndCopyActivations(string format)
    {
        using var w = new Workspace(); var path = Path.ChangeExtension(w.Source, format);
        Assert.Equal(LauncherAction.Open, LauncherRequest.Parse([path]).Action); Assert.Equal(LauncherAction.Copy, LauncherRequest.Parse(["--copy", path]).Action);
    }
}
