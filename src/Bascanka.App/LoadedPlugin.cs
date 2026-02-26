using Bascanka.Plugins.Api;
using System.Runtime.Loader;

namespace Bascanka.App;

internal sealed class LoadedPlugin
{
	public required IPlugin Plugin { get; init; }
	public required string SourcePath { get; init; }
	public AssemblyLoadContext? LoadContext { get; init; }
}

