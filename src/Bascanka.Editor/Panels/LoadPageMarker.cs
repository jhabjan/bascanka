namespace Bascanka.Editor.Panels;


	/// <summary>
	/// Marker placed in the <see cref="TreeNode.Tag"/> of navigation nodes
	/// ("Show next/previous 10,000") so click handlers can page the results.
	/// </summary>
	internal sealed class LoadPageMarker
    {
        public required SearchSession Session { get; init; }
        public required int TargetOffset { get; init; }
    }

