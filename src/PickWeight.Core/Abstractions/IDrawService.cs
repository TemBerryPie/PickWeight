using PickWeight.Core.Models;

namespace PickWeight.Core.Abstractions;

public interface IDrawService
{
    DrawResult Draw(IReadOnlyList<Member> members, IReadOnlyList<Group> groups, int targetCount, DrawOptions? options = null);
    IReadOnlyList<DrawProbabilityPreview> PreviewProbabilities(
        IReadOnlyList<Member> members,
        IReadOnlyList<Group> groups,
        DrawOptions? options = null);
}
