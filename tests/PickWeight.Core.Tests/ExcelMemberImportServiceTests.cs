using System.IO.Compression;
using PickWeight.Core.Services;

namespace PickWeight.Core.Tests;

public sealed class ExcelMemberImportServiceTests
{
    [Fact]
    public async Task ReadAsMemberTextAsync_ReadsFirstTwoColumnsAndSkipsHeader()
    {
        var path = Path.Combine(Path.GetTempPath(), $"PickWeight-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Write(archive, "xl/sharedStrings.xml", """
                    <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>姓名</t></si><si><t>张三</t></si><si><t>李四</t></si></sst>
                    """);
                Write(archive, "xl/worksheets/sheet1.xml", """
                    <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c></row><row r="2"><c r="A2" t="s"><v>1</v></c><c r="B2"><v>80</v></c></row><row r="3"><c r="A3" t="s"><v>2</v></c></row></sheetData></worksheet>
                    """);
            }

            var content = await new ExcelMemberImportService().ReadAsMemberTextAsync(path);

            Assert.Equal($"张三 80{Environment.NewLine}李四", content);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
