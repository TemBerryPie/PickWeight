namespace PickWeight.Core.Models;

public enum DrawMode
{
    Weighted,
    SmartRandom,
    FullRandom,
    Selection
}

public sealed record DrawOptions(DrawMode Mode);
