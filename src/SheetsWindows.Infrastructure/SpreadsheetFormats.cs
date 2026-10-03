using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ExcelDataReader;

namespace SheetsWindows.Infrastructure;

public sealed class SpreadsheetCapacityException(string message) : NotSupportedException(message);
public sealed class ConversionMismatchException() : IOException("Converted cell values differ; source preserved.");
public sealed class CopyRequiredException() : NotSupportedException("This workbook requires copy-only import.");
public sealed record TextImportOptions(string Encoding = "auto", string Delimiter = "auto");
public sealed record SheetValues(string Name, IReadOnlyList<IReadOnlyList<object?>> Rows);
public sealed record SpreadsheetPayload(byte[] Bytes, string MimeType, IReadOnlyList<SheetValues>? Expected);

public static class SpreadsheetFormats
{
    public const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const int MaxCells = 500000, MaxRows = 50000, MaxColumns = 1000;
    public static readonly string[] Extensions = [".xlsx", ".ods", ".xls", ".csv", ".tsv"];
    public static string Format(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!Extensions.Contains(extension)) throw new NotSupportedException("Unsupported spreadsheet extension.");
        return extension[1..];
    }
    public static SpreadsheetPayload Prepare(string format, byte[] bytes, TextImportOptions? options = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (bytes.Length > GoogleImport.MaxBytes) throw new SpreadsheetCapacityException("O arquivo excede o limite de 20 MiB para importação.");
        switch (format)
        {
            case "xlsx": GoogleImport.ValidateXlsx(bytes); return new(bytes, XlsxMime, null);
            case "csv":
            case "tsv":
                var table = new SheetValues("Dados", ReadText(bytes, format, options ?? new(), ct));
                return new(WriteXlsx([table], ct), XlsxMime, [table]);
            case "ods":
                var expected = ReadOds(bytes);
                return new(bytes, "application/vnd.oasis.opendocument.spreadsheet", expected);
            case "xls":
                // Binary XLS cannot prove absence of legacy macros/formulas. Always preserve it.
                _ = ReadExcel(bytes, binary: true);
                return new(bytes, "application/vnd.ms-excel", null);
            default: throw new NotSupportedException("Unsupported spreadsheet format.");
        }
    }
    private static string Decode(byte[] bytes, string mode)
    {
        Encoding encoding; var offset = 0;
        if (mode == "auto")
        {
            if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) { encoding = new UTF8Encoding(false, true); offset = 3; }
            else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) { encoding = new UnicodeEncoding(false, false, true); offset = 2; }
            else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) { encoding = new UnicodeEncoding(true, false, true); offset = 2; }
            else encoding = new UTF8Encoding(false, true);
        }
        else if (mode == "windows-1252")
        {
            if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) || bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }) || bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) throw new InvalidDataException("Encoding conflicts with BOM.");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            encoding = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        else throw new ArgumentException("Unsupported text encoding.");
        try { var text = encoding.GetString(bytes, offset, bytes.Length - offset); XmlConvert.VerifyXmlChars(text); return text; }
        catch (Exception ex) when (ex is DecoderFallbackException or XmlException) { throw new InvalidDataException("Invalid text encoding or characters."); }
    }
    public static IReadOnlyList<IReadOnlyList<object?>> ReadText(byte[] bytes, string format, TextImportOptions options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (bytes.Length > GoogleImport.MaxBytes) throw new SpreadsheetCapacityException("O arquivo excede o limite de 20 MiB para importação.");
        var text = Decode(bytes, options.Encoding);
        if (text.Length == 0) throw new InvalidDataException("Empty text spreadsheet.");
        if (format == "tsv") return ParseDelimited(text, '\t', ct);
        if (options.Delimiter is "comma" or "semicolon") return ParseDelimited(text, options.Delimiter == "comma" ? ',' : ';', ct);
        if (options.Delimiter != "auto") throw new ArgumentException("Unsupported delimiter.");
        var candidates = new List<IReadOnlyList<IReadOnlyList<object?>>>();
        SpreadsheetCapacityException? capacity = null;
        foreach (var separator in new[] { ',', ';' })
        {
            try { var rows = ParseDelimited(text, separator, ct); if (rows[0].Count > 1) candidates.Add(rows); }
            catch (SpreadsheetCapacityException ex) { capacity ??= ex; }
            catch (InvalidDataException) { }
        }
        // Never use another separator to bypass a resource boundary.
        if (capacity is not null) throw capacity;
        if (candidates.Count > 1) throw new InvalidDataException("Ambiguous CSV delimiter; configure it explicitly.");
        if (candidates.Count == 1) return candidates[0];
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"') { if (quoted && i + 1 < text.Length && text[i + 1] == '"') i++; else quoted = !quoted; }
            else if (!quoted && text[i] is ',' or ';') throw new InvalidDataException("Irregular delimited table.");
        }
        return ParseDelimited(text, ',', ct);
    }
    private static IReadOnlyList<IReadOnlyList<object?>> ParseDelimited(string text, char separator, CancellationToken ct)
    {
        var rows = new List<IReadOnlyList<object?>>(); var row = new List<object?>(); var field = new StringBuilder();
        var quoted = false; var closed = false; var cells = 0;
        void Field()
        {
            if (++cells > MaxCells) throw new SpreadsheetCapacityException($"A tabela excede o limite de {MaxCells:N0} células.");
            if (row.Count >= MaxColumns) throw new SpreadsheetCapacityException($"A tabela excede o limite de {MaxColumns:N0} colunas.");
            if (field.Length > 32767) throw new SpreadsheetCapacityException("Uma célula excede o limite de 32.767 caracteres.");
            row.Add(field.ToString()); field.Clear(); closed = false;
        }
        void Row()
        {
            Field(); if (rows.Count >= MaxRows) throw new SpreadsheetCapacityException($"A tabela excede o limite de {MaxRows:N0} linhas.");
            if (rows.Count > 0 && row.Count != rows[0].Count) throw new InvalidDataException("Irregular delimited table.");
            rows.Add(row); row = [];
        }
        for (var i = 0; i < text.Length; i++)
        {
            if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
            if (field.Length > 32767) throw new SpreadsheetCapacityException("Uma célula excede o limite de 32.767 caracteres.");
            var c = text[i];
            if (quoted)
            {
                if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else { quoted = false; closed = true; } }
                else field.Append(c);
            }
            else if (c == separator) Field();
            else if (c is '\r' or '\n') { Row(); if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; }
            else if (c == '"' && field.Length == 0 && !closed) quoted = true;
            else { if (closed || c == '"') throw new InvalidDataException("Invalid quoted field."); field.Append(c); }
        }
        if (quoted) throw new InvalidDataException("Unclosed quoted field.");
        if (field.Length > 0 || closed || row.Count > 0 || text[^1] == separator) Row();
        if (rows.Count == 0) throw new InvalidDataException("Empty table.");
        return rows;
    }
    private static ZipArchive OpenPackage(byte[] bytes)
    {
        var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        if (zip.Entries.Count > 10000 || zip.Entries.Sum(e => e.Length) > 96 * 1024 * 1024
            || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count)
        { zip.Dispose(); throw new InvalidDataException("Oversized or ambiguous ZIP package."); }
        return zip;
    }
    private static XDocument Xml(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? throw new InvalidDataException("Package part missing.");
        if (entry.Length > 64 * 1024 * 1024) throw new InvalidDataException("XML part too large.");
        using var stream = entry.Open(); using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64 * 1024 * 1024 });
        return XDocument.Load(reader);
    }
    public static IReadOnlyList<SheetValues>? ReadOds(byte[] bytes)
    {
        using var zip = OpenPackage(bytes);
        var mime = zip.GetEntry("mimetype") ?? throw new InvalidDataException("ODS mimetype missing.");
        if (mime.Length > 128) throw new InvalidDataException("Invalid ODS mimetype.");
        using (var reader = new StreamReader(mime.Open())) if (reader.ReadToEnd() != "application/vnd.oasis.opendocument.spreadsheet") throw new InvalidDataException("Not an ODS package.");
        if (zip.Entries.Any(e => e.FullName.StartsWith("Basic/", StringComparison.OrdinalIgnoreCase) || e.FullName.StartsWith("Scripts/", StringComparison.OrdinalIgnoreCase))) throw new NotSupportedException("ODS scripts excluded.");
        var doc = Xml(zip, "content.xml");
        XNamespace office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0", table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0", text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        if (doc.Root?.Name != office + "document-content" || doc.Root.Element(office + "body")?.Element(office + "spreadsheet") is not XElement book) throw new InvalidDataException("Invalid ODS workbook.");
        if (doc.Descendants(office + "scripts").Any(e => e.Elements().Any())) throw new NotSupportedException("ODS scripts excluded.");
        var simple = !doc.Descendants().Any(e => e.Name.LocalName is "scripts" or "script" or "object" or "image" or "frame" or "covered-table-cell"
            || e.Attributes().Any(a => a.Name.LocalName is "formula" or "number-columns-spanned" or "number-rows-spanned" or "href"));
        var sheets = new List<SheetValues>(); var cells = 0;
        foreach (var sheet in book.Elements(table + "table"))
        {
            if (sheets.Count >= 100) throw new InvalidDataException("Too many sheets.");
            var name = (string?)sheet.Attribute(table + "name") ?? throw new InvalidDataException("ODS sheet name missing.");
            var rows = new List<IReadOnlyList<object?>>();
            foreach (var r in sheet.Descendants(table + "table-row"))
            {
                var row = new List<object?>();
                foreach (var c in r.Elements().Where(e => e.Name == table + "table-cell" || e.Name == table + "covered-table-cell"))
                {
                    var repeated = Repeat(c, table + "number-columns-repeated", MaxColumns);
                    object? value = null; var type = (string?)c.Attribute(office + "value-type");
                    switch (type)
                    {
                        case null: if (c.HasElements) simple = false; break;
                        case "string":
                            value = (string?)c.Attribute(office + "string-value") ?? string.Join("\n", c.Elements(text + "p").Select(p => p.Value));
                            if (c.Descendants().Any(e => e.Name != text + "p" && e.Name != text + "span")) simple = false;
                            break;
                        case "float":
                        case "percentage":
                        case "currency":
                            if (!double.TryParse((string?)c.Attribute(office + "value"), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) throw new InvalidDataException("Invalid ODS number."); value = number; break;
                        case "boolean": value = (string?)c.Attribute(office + "boolean-value") switch { "true" => true, "false" => false, _ => throw new InvalidDataException("Invalid ODS boolean.") }; break;
                        default: simple = false; break;
                    }
                    if (row.Count + repeated > MaxColumns) throw new InvalidDataException("ODS columns too large.");
                    for (var i = 0; i < repeated; i++) row.Add(value);
                }
                var copies = Repeat(r, table + "number-rows-repeated", MaxRows);
                if (rows.Count + copies > MaxRows || (long)cells + (long)row.Count * copies > MaxCells) throw new InvalidDataException("ODS repeated cells too large.");
                cells += row.Count * copies; for (var i = 0; i < copies; i++) rows.Add(row.ToArray());
            }
            sheets.Add(new(name, rows));
        }
        if (sheets.Count == 0 || sheets.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count() != sheets.Count) throw new InvalidDataException("Invalid ODS sheets.");
        return simple ? sheets : null;
    }
    private static int Repeat(XElement element, XName name, int max)
    {
        var attr = element.Attribute(name); if (attr is null) return 1;
        if (!int.TryParse(attr.Value, out var value) || value < 1 || value > max) throw new InvalidDataException("Unbounded repetition."); return value;
    }
    public static IReadOnlyList<SheetValues> ReadExcel(byte[] bytes, bool binary = false, CancellationToken ct = default)
    {
        if (!binary)
        {
            GoogleImport.ValidateXlsx(bytes);
            using var zip = OpenPackage(bytes); _ = Xml(zip, "xl/workbook.xml");
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal) && e.FullName.EndsWith(".xml", StringComparison.Ordinal)))
            {
                if (entry.Length > 64 * 1024 * 1024) throw new SpreadsheetCapacityException("Uma aba excede o limite de 64 MiB descompactados para conferência.");
                using var xmlStream = entry.Open();
                using var xmlReader = XmlReader.Create(xmlStream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64 * 1024 * 1024 });
                while (xmlReader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    if (xmlReader.NodeType == XmlNodeType.Element && xmlReader.LocalName == "f" && xmlReader.NamespaceURI == "http://schemas.openxmlformats.org/spreadsheetml/2006/main") throw new ConversionMismatchException();
                }
            }
        }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream = new MemoryStream(bytes);
        using var reader = binary ? ExcelReaderFactory.CreateBinaryReader(stream) : ExcelReaderFactory.CreateOpenXmlReader(stream);
        var sheets = new List<SheetValues>(); var cells = 0;
        do
        {
            if (sheets.Count >= 100 || reader.FieldCount > MaxColumns || reader.RowCount > MaxRows) throw new InvalidDataException("Workbook dimensions exceed limits.");
            var rows = new List<IReadOnlyList<object?>>();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (rows.Count >= MaxRows || (long)cells + reader.FieldCount > MaxCells) throw new InvalidDataException("Workbook cell limit exceeded.");
                var row = new object?[reader.FieldCount]; for (var i = 0; i < row.Length; i++) row[i] = reader.GetValue(i); cells += row.Length; rows.Add(row);
            }
            sheets.Add(new(reader.Name, rows));
        } while (reader.NextResult());
        return sheets;
    }
    public static void VerifyValues(IReadOnlyList<SheetValues> expected, byte[] exported, CancellationToken ct = default)
    {
        var actual = ReadExcel(exported, ct: ct);
        if (actual.Count != expected.Count) throw new ConversionMismatchException();
        for (var s = 0; s < expected.Count; s++)
        {
            if (expected[s].Name != actual[s].Name) throw new ConversionMismatchException();
            var left = expected[s].Rows; var right = actual[s].Rows;
            for (var r = 0; r < Math.Max(left.Count, right.Count); r++)
            {
                ct.ThrowIfCancellationRequested();
                var x = r < left.Count ? left[r] : Array.Empty<object?>();
                var y = r < right.Count ? right[r] : Array.Empty<object?>();
                for (var c = 0; c < Math.Max(x.Count, y.Count); c++)
                {
                    object? Normalize(object? value) => value switch { null or "" => null, int n => (double)n, _ => value };
                    if (!Equals(Normalize(c < x.Count ? x[c] : null), Normalize(c < y.Count ? y[c] : null))) throw new ConversionMismatchException();
                }
            }
        }
    }
    public static byte[] WriteXlsx(IReadOnlyList<SheetValues> sheets, CancellationToken ct = default)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main", rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships", package = "http://schemas.openxmlformats.org/package/2006/relationships", types = "http://schemas.openxmlformats.org/package/2006/content-types";
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            void Part(string name, XElement xml) { var entry = zip.CreateEntry(name); entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero); using var stream = entry.Open(); xml.Save(stream); }
            Part("[Content_Types].xml", new XElement(types + "Types", new XElement(types + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")), new XElement(types + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")), new XElement(types + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")), sheets.Select((_, i) => new XElement(types + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{i + 1}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
            Part("_rels/.rels", new XElement(package + "Relationships", new XElement(package + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", rel.NamespaceName + "/officeDocument"), new XAttribute("Target", "xl/workbook.xml"))));
            Part("xl/workbook.xml", new XElement(main + "workbook", new XElement(main + "sheets", sheets.Select((s, i) => new XElement(main + "sheet", new XAttribute("name", s.Name), new XAttribute("sheetId", i + 1), new XAttribute(rel + "id", "rId" + (i + 1)))))));
            Part("xl/_rels/workbook.xml.rels", new XElement(package + "Relationships", sheets.Select((_, i) => new XElement(package + "Relationship", new XAttribute("Id", "rId" + (i + 1)), new XAttribute("Type", rel.NamespaceName + "/worksheet"), new XAttribute("Target", $"worksheets/sheet{i + 1}.xml")))));
            for (var s = 0; s < sheets.Count; s++)
            {
                var entry = zip.CreateEntry($"xl/worksheets/sheet{s + 1}.xml");
                entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var stream = entry.Open();
                using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false });
                writer.WriteStartElement("worksheet", main.NamespaceName); writer.WriteStartElement("sheetData", main.NamespaceName);
                var rows = sheets[s].Rows;
                for (var r = 0; r < rows.Count; r++)
                {
                    ct.ThrowIfCancellationRequested();
                    writer.WriteStartElement("row", main.NamespaceName); writer.WriteAttributeString("r", (r + 1).ToString(CultureInfo.InvariantCulture));
                    for (var c = 0; c < rows[r].Count; c++)
                    {
                        var value = rows[r][c]; if (value is null) continue;
                        writer.WriteStartElement("c", main.NamespaceName); writer.WriteAttributeString("r", Column(c) + (r + 1));
                        if (value is bool flag)
                        { writer.WriteAttributeString("t", "b"); writer.WriteElementString("v", main.NamespaceName, flag ? "1" : "0"); }
                        else if (value is double or int) writer.WriteElementString("v", main.NamespaceName, Convert.ToString(value, CultureInfo.InvariantCulture));
                        else
                        {
                            writer.WriteAttributeString("t", "inlineStr"); writer.WriteStartElement("is", main.NamespaceName); writer.WriteStartElement("t", main.NamespaceName);
                            writer.WriteAttributeString("xml", "space", XNamespace.Xml.NamespaceName, "preserve");
                            writer.WriteString(Regex.Replace(value.ToString()!, @"_x[0-9a-fA-F]{4}_", m => "_x005F_" + m.Value[1..]));
                            writer.WriteEndElement(); writer.WriteEndElement();
                        }
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
                writer.WriteEndElement(); writer.WriteEndElement();
            }
        }
        var bytes = buffer.ToArray(); if (bytes.Length > GoogleImport.MaxBytes) throw new SpreadsheetCapacityException("A planilha normalizada excede o limite de 20 MiB para upload."); return bytes;
    }
    private static string Column(int index)
    {
        var result = ""; for (var i = index + 1; i > 0; i = (i - 1) / 26) result = (char)('A' + (i - 1) % 26) + result; return result;
    }
}
