namespace PickWeight.Core.Models;

public sealed record MemberImportPreview(IReadOnlyList<MemberImportPreviewItem> Items)
{
    public int AddCount => Items.Count(item => item.Action == MemberImportAction.Add);
    public int SkipCount => Items.Count(item => item.Action != MemberImportAction.Add);
    public bool HasErrors => Items.Any(item => item.Action == MemberImportAction.ErrorInvalidWeight);
}

public sealed record MemberImportPreviewItem(
    int LineNumber,
    string RawText,
    string Name,
    double ExplicitWeight,
    MemberImportAction Action,
    string Reason);

public enum MemberImportAction
{
    Add,
    SkipBlank,
    SkipDuplicateExisting,
    SkipDuplicateInFile,
    ErrorInvalidWeight
}
