namespace Bascanka.Core.Search;

/// <summary>
/// Represents a parsed sed substitution command.
/// </summary>
public sealed class SedCommand
{
    public required string Pattern { get; init; }
    public required string Replacement { get; init; }
    public bool Global { get; init; }
    public bool IgnoreCase { get; init; }
}
