using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using PickWeight.Core.Abstractions;

namespace PickWeight.Core.Services;

/// <summary>Reads the first worksheet of a simple .xlsx roster: name in column A, optional weight in B.</summary>
public sealed class ExcelMemberImportService : IMemberSpreadsheetImportService
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public async Task<string> ReadAsMemberTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var sharedStrings = await ReadSharedStringsAsync(archive, cancellationToken);
        var worksheet = archive.Entries
            .Where(entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? throw new InvalidDataException("Excel 文件没有工作表。");
        var document = await LoadXmlAsync(worksheet, cancellationToken);
        var lines = new List<string>();
        foreach (var row in document.Descendants(Main + "row"))
        {
            var cells = row.Elements(Main + "c").ToDictionary(
                cell => ColumnName((string?)cell.Attribute("r")),
                cell => ReadCell(cell, sharedStrings),
                StringComparer.OrdinalIgnoreCase);
            cells.TryGetValue("A", out var name);
            cells.TryGetValue("B", out var weight);
            name = name?.Trim() ?? string.Empty;
            if (name.Length == 0 || string.Equals(name, "姓名", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "name", StringComparison.OrdinalIgnoreCase)) continue;
            lines.Add(double.TryParse(weight, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWeight)
                ? $"{name} {parsedWeight.ToString("0.########", CultureInfo.InvariantCulture)}"
                : name);
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<IReadOnlyList<string>> ReadSharedStringsAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        var document = await LoadXmlAsync(entry, cancellationToken);
        return document.Descendants(Main + "si")
            .Select(item => string.Concat(item.Descendants(Main + "t").Select(text => text.Value)))
            .ToList();
    }

    private static async Task<XDocument> LoadXmlAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        return await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
    }

    private static string ColumnName(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return string.Empty;
        return new string(reference.TakeWhile(char.IsLetter).ToArray());
    }

    private static string ReadCell(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = (string?)cell.Attribute("t");
        if (type == "inlineStr") return string.Concat(cell.Descendants(Main + "t").Select(text => text.Value));
        var value = cell.Element(Main + "v")?.Value ?? string.Empty;
        return type == "s" && int.TryParse(value, out var index) && index >= 0 && index < sharedStrings.Count
            ? sharedStrings[index]
            : value;
    }
}
