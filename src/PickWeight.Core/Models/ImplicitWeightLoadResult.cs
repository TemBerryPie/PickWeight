namespace PickWeight.Core.Models;

public sealed record ImplicitWeightLoadResult(
    IReadOnlyDictionary<string, double> Weights,
    bool WasCreated,
    int InvalidEntryCount,
    string FilePath);
