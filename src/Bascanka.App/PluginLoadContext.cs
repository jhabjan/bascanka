using System.Reflection;
using System.Runtime.Loader;

namespace Bascanka.App;


/// <summary>
/// Isolated, collectible <see cref="AssemblyLoadContext"/> for loading plugin assemblies.
/// This allows plugins to be unloaded at runtime.
/// </summary>
internal sealed class PluginLoadContext(string pluginPath) : AssemblyLoadContext(isCollectible: true)
{
	private readonly AssemblyDependencyResolver _resolver = new(pluginPath);

	protected override Assembly? Load(AssemblyName assemblyName)
	{
		// Try to resolve from the plugin's directory first.
		string? path = _resolver.ResolveAssemblyToPath(assemblyName);
		if (path is not null)
			return LoadFromAssemblyPath(path);

		// Fall back to the default context (shared assemblies like Bascanka.Plugins.Api).
		return null;
	}

	protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
	{
		string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
		return path is not null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
	}
}

