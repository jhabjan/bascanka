using Bascanka.Core.Navigation;

namespace Bascanka.Editor.Panels;

/// <summary>
/// Event arguments for symbol navigation.
/// </summary>
public sealed class SymbolNavigationEventArgs(SymbolInfo symbol) : EventArgs
{
	/// <summary>The symbol the user wants to navigate to.</summary>
	public SymbolInfo Symbol { get; } = symbol;
}
