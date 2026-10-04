using System.IO.Compression;
using System.Xml.Linq;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace SheetsWindows.Infrastructure;

// The original XLS remains the snapshot/backup. Only these derived bytes are uploaded.
internal static class LegacyWorkbookConverter
{
    public static byte[] Convert(byte[] bytes, CancellationToken ct, out bool copyOnly)
    {
        try { return ConvertCore(bytes, ct, out copyOnly); }
        catch (Exception ex) when (ex is not OperationCanceledException and not IOException and not NotSupportedException and not OutOfMemoryException)
        {
            throw new InvalidDataException("Não foi possível converter este XLS para XLSX. O original e o backup foram preservados.", ex);
        }
    }
    private static byte[] ConvertCore(byte[] bytes, CancellationToken ct, out bool copyOnly)
    {
        using var input = new MemoryStream(bytes, false);
        using var source = new HSSFWorkbook(input);
        using var target = new XSSFWorkbook();
        copyOnly = source.Directory.HasEntry("_VBA_PROJECT_CUR");
        target.GetCTWorkbook().workbookPr.date1904 = source.IsDate1904();
        var styles = new Dictionary<short, ICellStyle>();
        var fonts = new Dictionary<short, IFont>();
        ICellStyle Style(ICellStyle original)
        {
            if (styles.TryGetValue(original.Index, out var cached)) return cached;
            var style = target.CreateCellStyle();
            style.DataFormat = target.CreateDataFormat().GetFormat(original.GetDataFormatString());
            style.Alignment = original.Alignment; style.VerticalAlignment = original.VerticalAlignment;
            style.WrapText = original.WrapText; style.Rotation = original.Rotation;
            style.Indention = original.Indention; style.IsLocked = original.IsLocked; style.IsHidden = original.IsHidden;
            style.BorderTop = original.BorderTop; style.BorderBottom = original.BorderBottom;
            style.BorderLeft = original.BorderLeft; style.BorderRight = original.BorderRight;
            style.FillPattern = original.FillPattern;
            var xs = (XSSFCellStyle)style;
            XSSFColor? Color(short index)
            {
                var color = source.GetCustomPalette().GetColor(index);
                return color is null ? null : new XSSFColor { RGB = color.RGB };
            }
            if (Color(original.FillForegroundColor) is { } fg) xs.SetFillForegroundColor(fg);
            if (Color(original.FillBackgroundColor) is { } bg) xs.SetFillBackgroundColor(bg);
            if (Color(original.TopBorderColor) is { } top) xs.SetTopBorderColor(top);
            if (Color(original.BottomBorderColor) is { } bottom) xs.SetBottomBorderColor(bottom);
            if (Color(original.LeftBorderColor) is { } left) xs.SetLeftBorderColor(left);
            if (Color(original.RightBorderColor) is { } right) xs.SetRightBorderColor(right);
            if (!fonts.TryGetValue(original.FontIndex, out var font))
            {
                var old = source.GetFontAt(original.FontIndex); font = target.CreateFont();
                font.FontName = old.FontName; font.FontHeight = old.FontHeight; font.IsBold = old.IsBold;
                font.IsItalic = old.IsItalic; font.IsStrikeout = old.IsStrikeout; font.Underline = old.Underline;
                font.TypeOffset = old.TypeOffset; font.Charset = old.Charset;
                if (Color(old.Color) is { } fc) ((XSSFFont)font).SetColor(fc);
                fonts.Add(original.FontIndex, font);
            }
            style.SetFont(font); styles.Add(original.Index, style); return style;
        }
        // Create all tabs and named ranges before assigning formulas that reference them.
        for (var i = 0; i < source.NumberOfSheets; i++) target.CreateSheet(source.GetSheetName(i));
        for (var i = 0; i < source.NumberOfNames; i++)
        {
            ct.ThrowIfCancellationRequested();
            var old = source.GetNameAt(i); var name = target.CreateName();
            name.SheetIndex = old.SheetIndex; name.NameName = old.NameName;
            name.RefersToFormula = old.RefersToFormula;
        }
        for (var i = 0; i < source.NumberOfSheets; i++)
        {
            ct.ThrowIfCancellationRequested();
            var old = source.GetSheetAt(i); var sheet = target.GetSheetAt(i);
            copyOnly |= old.DrawingPatriarch is not null || old.SheetConditionalFormatting.NumConditionalFormattings > 0 || old.Protect;
            target.SetSheetVisibility(i, source.IsSheetVeryHidden(i) ? SheetVisibility.VeryHidden : source.IsSheetHidden(i) ? SheetVisibility.Hidden : SheetVisibility.Visible);
            sheet.DefaultRowHeight = old.DefaultRowHeight; sheet.DefaultColumnWidth = old.DefaultColumnWidth;
            for (var col = 0; col < 256; col++)
            {
                sheet.SetColumnWidth(col, old.GetColumnWidth(col)); sheet.SetColumnHidden(col, old.IsColumnHidden(col));
            }
            foreach (IRow oldRow in old)
            {
                ct.ThrowIfCancellationRequested();
                var row = sheet.CreateRow(oldRow.RowNum); row.Height = oldRow.Height; row.ZeroHeight = oldRow.ZeroHeight;
                foreach (var cell in oldRow.Cells)
                {
                    var next = row.CreateCell(cell.ColumnIndex); next.CellStyle = Style(cell.CellStyle);
                    if (cell.Hyperlink is { } oldLink)
                    {
                        var link = target.GetCreationHelper().CreateHyperlink(oldLink.Type);
                        link.Address = oldLink.Address; next.Hyperlink = link;
                    }
                    if (cell.CellType == CellType.String) copyOnly |= cell.RichStringCellValue.NumFormattingRuns > 0;
                    switch (cell.CellType)
                    {
                        case CellType.Numeric: next.SetCellValue(cell.NumericCellValue); break;
                        case CellType.String: next.SetCellValue(cell.StringCellValue); break;
                        case CellType.Boolean: next.SetCellValue(cell.BooleanCellValue); break;
                        case CellType.Error: next.SetCellErrorValue(cell.ErrorCellValue); break;
                        case CellType.Formula: next.SetCellFormula(cell.CellFormula); break;
                        case CellType.Blank: break;
                        default: throw new InvalidDataException("Unsupported legacy cell type.");
                    }
                }
            }
            for (var m = 0; m < old.NumMergedRegions; m++) sheet.AddMergedRegion(old.GetMergedRegion(m));
        }
        target.SetForceFormulaRecalculation(true);
        using var output = new MemoryStream(); target.Write(output, true);
        // ZIP timestamps must not change the payload hash when resuming an upload.
        using var archiveInput = new MemoryStream(output.ToArray());
        using var archive = new ZipArchive(archiveInput, ZipArchiveMode.Read);
        using var canonical = new MemoryStream();
        using (var zip = new ZipArchive(canonical, ZipArchiveMode.Create, true))
            foreach (var entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                var part = zip.CreateEntry(entry.FullName); part.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var read = entry.Open(); using var write = part.Open();
                if (entry.FullName == "docProps/core.xml")
                {
                    var core = XDocument.Load(read);
                    core.Descendants().Where(e => e.Name.LocalName is "created" or "modified").Remove();
                    core.Save(write);
                }
                else read.CopyTo(write);
            }
        var result = canonical.ToArray();
        if (result.Length > GoogleImport.MaxBytes) throw new SpreadsheetCapacityException("O XLSX convertido excede o limite de 20 MiB para importação.");
        GoogleImport.ValidateXlsx(result); return result;
    }
}
