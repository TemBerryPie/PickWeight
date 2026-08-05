namespace PickWeight.Core.Models;

public sealed record DrawResult(
    IReadOnlyList<Member> SelectedMembers,
    int EligibleCandidateCount,
    string? Message = null,
    IReadOnlyList<string>? TriggeredGroups = null,
    IReadOnlyList<DrawnMemberWeight>? SelectedWeights = null);

public sealed record DrawnMemberWeight(
    string Name,
    double ExplicitWeight,
    double CalledWeight);

public sealed record DrawProbabilityPreview(
    string Name,
    double Probability,
    double Score,
    double ExplicitWeight,
    double CalledWeight,
    int CalledCount,
    double GroupMultiplier);
