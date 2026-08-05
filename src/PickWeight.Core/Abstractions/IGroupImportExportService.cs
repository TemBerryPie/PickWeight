using PickWeight.Core.Models;

namespace PickWeight.Core.Abstractions;

public interface IGroupImportExportService
{
    ImportResult Import(string content, IList<Group> groups, IReadOnlyList<Member> members);
    string Export(IEnumerable<Group> groups);
}
