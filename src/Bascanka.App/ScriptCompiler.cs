using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Bascanka.App;

/// <summary>
/// Compiles .csx (C# script) plugin files into assemblies using the .NET SDK's
/// Roslyn compiler (csc.exe). Compiled DLLs are cached in the user's AppData
/// folder and recompiled only when the source file's content hash changes.
/// </summary>
public sealed class ScriptCompiler
{
    private static readonly string CacheDirectory =
        Path.Combine(SettingsManager.AppDataFolder, "plugin-cache");

    /// <summary>
    /// Compiles a .csx script file and returns the resulting assembly.
    /// Returns null if compilation fails.
    /// </summary>
    /// <param name="csxPath">Absolute path to the .csx script file.</param>
    /// <returns>The compiled <see cref="Assembly"/>, or null on failure.</returns>
    public static Assembly? Compile(string csxPath)
    {
        if (!File.Exists(csxPath))
        {
            ReportError(csxPath, "Script file not found.");
            return null;
        }

        Directory.CreateDirectory(CacheDirectory);

        // Compute content hash to detect changes.
        string sourceText = File.ReadAllText(csxPath);
        string hash = ComputeHash(sourceText);
        string cachedDllName = Path.GetFileNameWithoutExtension(csxPath) + "_" + hash + ".dll";
        string cachedDllPath = Path.Combine(CacheDirectory, cachedDllName);

        // Use cached version if available.
        if (File.Exists(cachedDllPath))
        {
            try
            {
                return Assembly.LoadFrom(cachedDllPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load cached assembly: {ex.Message}");
                // Fall through to recompile.
            }
        }

        // Find the C# compiler.
        string? cscPath = FindCsc();
        if (cscPath is null)
        {
            ReportError(csxPath, "Could not locate csc.exe in the .NET SDK.");
            return null;
        }

        // Find the Bascanka.Plugins.Api.dll for referencing.
        string apiDllPath = Path.Combine(AppContext.BaseDirectory, "Bascanka.Plugins.Api.dll");
        if (!File.Exists(apiDllPath))
        {
            ReportError(csxPath, "Bascanka.Plugins.Api.dll not found in application directory.");
            return null;
        }

        // Build the compiler arguments.
        string outputDll = cachedDllPath;
        var args = new StringBuilder();
        args.Append($"/target:library ");
        args.Append($"/out:\"{outputDll}\" ");
        args.Append($"/reference:\"{apiDllPath}\" ");

        // Add references to core framework assemblies.
        string? runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (runtimeDir is not null)
        {
            args.Append($"/reference:\"{Path.Combine(runtimeDir, "System.Runtime.dll")}\" ");
            args.Append($"/reference:\"{Path.Combine(runtimeDir, "System.Console.dll")}\" ");
            args.Append($"/reference:\"{Path.Combine(runtimeDir, "System.Collections.dll")}\" ");
            args.Append($"/reference:\"{Path.Combine(runtimeDir, "System.Linq.dll")}\" ");
            args.Append($"/reference:\"{Path.Combine(runtimeDir, "netstandard.dll")}\" ");
            args.Append($"/reference:\"{Path.Combine(runtimeDir, "mscorlib.dll")}\" ");
        }

        args.Append($"/nullable:enable ");
        args.Append($"/langversion:latest ");
        args.Append($"\"{csxPath}\" ");

        // Run csc.exe.
        var psi = new ProcessStartInfo
        {
            FileName = cscPath,
            Arguments = args.ToString(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(csxPath) ?? CacheDirectory,
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                ReportError(csxPath, "Failed to start csc.exe process.");
                return null;
            }

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(30_000);

            if (process.ExitCode != 0)
            {
                string errors = string.IsNullOrEmpty(stderr) ? stdout : stderr;
                ReportError(csxPath, $"Compilation failed:\n{errors}");
                return null;
            }

            // Load the compiled assembly.
            if (File.Exists(outputDll))
                return Assembly.LoadFrom(outputDll);

            ReportError(csxPath, "Compiled DLL not found after successful compilation.");
            return null;
        }
        catch (Exception ex)
        {
            ReportError(csxPath, $"Exception during compilation: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Locates csc.exe (or csc.dll with dotnet exec) in the .NET SDK.
    /// Searches: sdk/{version}/Roslyn/bincore/csc.dll
    /// </summary>
    public static string? FindCsc()
    {
        // Strategy 1: Look in the .NET SDK directories.
        string? dotnetRoot = GetDotnetRoot();
        if (dotnetRoot is not null)
        {
            string sdkDir = Path.Combine(dotnetRoot, "sdk");
            if (Directory.Exists(sdkDir))
            {
                // Get the latest SDK version directory.
                string[] sdkVersions = [.. Directory.GetDirectories(sdkDir).OrderByDescending(d => d)];

                foreach (string sdkVersion in sdkVersions)
                {
                    string cscDll = Path.Combine(sdkVersion, "Roslyn", "bincore", "csc.dll");
                    if (File.Exists(cscDll))
                    {
                        // Return dotnet exec path.
                        return cscDll;
                    }

                    string cscExe = Path.Combine(sdkVersion, "Roslyn", "bincore", "csc.exe");
                    if (File.Exists(cscExe))
                        return cscExe;
                }
            }
        }

        // Strategy 2: csc.exe on PATH.
        string? pathCsc = FindOnPath("csc.exe");
        if (pathCsc is not null)
            return pathCsc;

        return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static string? GetDotnetRoot()
    {
        // Check DOTNET_ROOT environment variable.
        string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot) && Directory.Exists(dotnetRoot))
            return dotnetRoot;

        // Check default install location on Windows.
        string defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet");
        if (Directory.Exists(defaultPath))
            return defaultPath;

        // Try to find dotnet.exe on PATH and derive the root.
        string? dotnetExe = FindOnPath("dotnet.exe") ?? FindOnPath("dotnet");
        if (dotnetExe is not null)
        {
            string? dir = Path.GetDirectoryName(dotnetExe);
            if (dir is not null && Directory.Exists(dir))
                return dir;
        }

        return null;
    }

    private static string? FindOnPath(string fileName)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv is null) return null;

        foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string fullPath = Path.Combine(dir.Trim(), fileName);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

    private static string ComputeHash(string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        byte[] hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }

    private static void ReportError(string scriptPath, string message)
    {
        string fileName = Path.GetFileName(scriptPath);
        System.Diagnostics.Debug.WriteLine($"ScriptCompiler [{fileName}]: {message}");

        // Also show a message box if on the UI thread.
        if (Application.OpenForms.Count > 0)
        {
            MessageBox.Show(
                $"{Strings.ErrorCompilingScript}\n\n{fileName}:\n{message}",
                Strings.AppTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
