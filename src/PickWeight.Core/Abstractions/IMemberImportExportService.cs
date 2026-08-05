using PickWeight.Core.Models;

namespace PickWeight.Core.Abstractions;

public interface IMemberImportExportService
{
    ImportResult Import(string content, IList<Member> members);
    MemberImportPreview PreviewImport(string content, IEnumerable<Member> members);
    ImportResult ApplyImport(MemberImportPreview preview, IList<Member> members);
    string Export(IEnumerable<Member> members);
}
