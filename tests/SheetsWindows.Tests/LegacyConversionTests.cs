using System.IO.Compression;
using System.Xml.Linq;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using SheetsWindows.Infrastructure;
using Xunit;

namespace SheetsWindows.Tests;

public sealed class LegacyConversionTests
{
    [Fact]
    public void ConversionPreservesFormulasNamesDatesStylesAndSheetStructure()
    {
        using var old = new HSSFWorkbook();
        var sheet = old.CreateSheet("Ação"); old.CreateSheet("Auxiliar"); old.SetSheetHidden(1, true);
        var row = sheet.CreateRow(0); row.CreateCell(0).SetCellValue(123.5);
        row.CreateCell(1).SetCellValue("São Paulo"); row.CreateCell(2).SetCellValue(true);
        var date = row.CreateCell(3); date.SetCellValue(new DateTime(2026, 10, 3));
        var style = old.CreateCellStyle(); style.DataFormat = old.CreateDataFormat().GetFormat("yyyy-mm-dd");
        style.FillForegroundColor = IndexedColors.LightGreen.Index; style.FillPattern = FillPattern.SolidForeground;
        var font = old.CreateFont(); font.IsBold = true; style.SetFont(font); date.CellStyle = style;
        var name = old.CreateName(); name.NameName = "Valor"; name.RefersToFormula = "'Ação'!$A$1";
        row.CreateCell(4).SetCellFormula("Valor*2");
        row.CreateCell(5).SetCellFormula("'Auxiliar'!A1+1");
        sheet.CreateRow(1).CreateCell(0).SetCellValue("Mesclada"); sheet.AddMergedRegion(new CellRangeAddress(1, 1, 0, 1));
        sheet.SetColumnWidth(0, 24 * 256); sheet.SetColumnHidden(6, true); row.Height = 500;
        using var input = new MemoryStream(); old.Write(input, true); var bytes = input.ToArray();
        var payload = SpreadsheetFormats.Prepare("xls", bytes);
        using var converted = new XSSFWorkbook(new MemoryStream(payload.Bytes));
        var actual = converted.GetSheetAt(0); var cells = actual.GetRow(0);
        Assert.Equal(2, converted.NumberOfSheets); Assert.True(converted.IsSheetHidden(1));
        Assert.Equal("Ação", actual.SheetName); Assert.Equal(123.5, cells.GetCell(0).NumericCellValue);
        Assert.Equal("São Paulo", cells.GetCell(1).StringCellValue); Assert.True(cells.GetCell(2).BooleanCellValue);
        Assert.Equal(date.NumericCellValue, cells.GetCell(3).NumericCellValue);
        Assert.Equal("yyyy-mm-dd", cells.GetCell(3).CellStyle.GetDataFormatString());
        Assert.True(converted.GetFontAt(cells.GetCell(3).CellStyle.FontIndex).IsBold);
        Assert.Equal(new byte[] { 204, 255, 204 }, ((XSSFCellStyle)cells.GetCell(3).CellStyle).FillForegroundXSSFColor.RGB);
        Assert.Equal("Valor*2", cells.GetCell(4).CellFormula); Assert.Equal(row.GetCell(5).CellFormula, cells.GetCell(5).CellFormula);
        Assert.Equal(name.RefersToFormula, converted.GetName("Valor").RefersToFormula);
        Assert.Equal(1, actual.NumMergedRegions); Assert.Equal(24 * 256, actual.GetColumnWidth(0));
        Assert.True(actual.IsColumnHidden(6)); Assert.Equal(500, cells.Height);
        // A failed fidelity check cannot retire the XLS when formulas are present.
        Assert.Throws<FormulaVerificationException>(() => SpreadsheetFormats.VerifyValues(payload.Expected!, payload.Bytes));
        Assert.Equal(bytes, input.ToArray());
    }

    [Fact]
    public void ConditionalFormattingImportsAsCopyBecauseValueChecksCannotProveFeatureFidelity()
    {
        using var old = new HSSFWorkbook(); var sheet = old.CreateSheet("Dados");
        sheet.CreateRow(0).CreateCell(0).SetCellValue(1);
        var rule = sheet.SheetConditionalFormatting.CreateConditionalFormattingRule("A1>0");
        sheet.SheetConditionalFormatting.AddConditionalFormatting([new CellRangeAddress(0, 0, 0, 0)], rule);
        using var input = new MemoryStream(); old.Write(input, true);
        var payload = SpreadsheetFormats.Prepare("xls", input.ToArray());
        Assert.Equal(SpreadsheetFormats.XlsxMime, payload.MimeType); Assert.Null(payload.Expected);
    }

    [Fact]
    public void ConversionIsStableAcrossDifferentCreationTimesForUploadRecovery()
    {
        var bytes = FormatTests.Xls(); var first = SpreadsheetFormats.Prepare("xls", bytes).Bytes;
        using var zip = new ZipArchive(new MemoryStream(first));
        using var core = zip.GetEntry("docProps/core.xml")!.Open();
        Assert.DoesNotContain(XDocument.Load(core).Descendants(), e => e.Name.LocalName is "created" or "modified");
        Assert.Equal(first, SpreadsheetFormats.Prepare("xls", bytes).Bytes);
    }
}
