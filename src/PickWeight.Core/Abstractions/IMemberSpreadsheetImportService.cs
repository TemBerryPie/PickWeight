namespace PickWeight.Core.Abstractions;

public interface IMemberSpreadsheetImportService
{
    Task<string> ReadAsMemberTextAsync(string filePath, CancellationToken cancellationToken = default);
}
