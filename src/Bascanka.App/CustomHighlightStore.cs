using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bascanka.Editor.Highlighting;

namespace Bascanka.App;

/// <summary>
/// Loads and saves custom highlighting profiles from/to Bascanka.json
/// next to the executable.
/// </summary>
public sealed class CustomHighlightStore
{
    private static readonly string FilePath =
        Path.Combine(SettingsManager.AppDataFolder, "custom-highlighting.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private List<CustomHighlightProfile> _profiles = [];

    /// <summary>All loaded profiles.</summary>
    public IReadOnlyList<CustomHighlightProfile> Profiles => _profiles;

    /// <summary>Loads profiles from Bascanka.json. Safe to call if file is missing.</summary>
    public void Load()
    {
        _profiles.Clear();

        if (!File.Exists(FilePath)) return;

        try
        {
            string json = File.ReadAllText(FilePath);
            var root = JsonSerializer.Deserialize<RootDto>(json, JsonOptions);
            if (root?.CustomHighlighting is null) return;

            foreach (var dto in root.CustomHighlighting)
            {
                var profile = new CustomHighlightProfile { Name = dto.Name ?? string.Empty };
                if (dto.Rules is not null)
                {
                    foreach (var ruleDto in dto.Rules)
                    {
                        profile.Rules.Add(new CustomHighlightRule
                        {
                            Pattern = ruleDto.Pattern ?? string.Empty,
                            Scope = ruleDto.Scope ?? "match",
                            Foreground = ParseColor(ruleDto.Foreground),
                            Background = ParseColor(ruleDto.Background),
                            BeginPattern = ruleDto.Begin ?? string.Empty,
                            EndPattern = ruleDto.End ?? string.Empty,
                            Foldable = ruleDto.Foldable ?? false,
                        });
                    }
                }
                _profiles.Add(profile);
            }
        }
        catch
        {
            // Silently ignore corrupt files.
        }
    }

    /// <summary>Saves all profiles to Bascanka.json (atomic write).</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        var root = new RootDto
        {
            CustomHighlighting = [.. _profiles.Select(p => new ProfileDto
            {
                Name = p.Name,
                Rules = [.. p.Rules.Select(r =>
                {
                    bool isBlock = string.Equals(r.Scope, "block", StringComparison.OrdinalIgnoreCase);
                    return new RuleDto
                    {
                        Pattern = isBlock ? null : r.Pattern,
                        Scope = r.Scope,
                        Foreground = FormatColor(r.Foreground),
                        Background = FormatColor(r.Background),
                        Begin = isBlock ? r.BeginPattern : null,
                        End = isBlock ? r.EndPattern : null,
                        Foldable = isBlock && r.Foldable ? true : null,
                    };
                })],
            })],
        };

        string json = JsonSerializer.Serialize(root, JsonOptions);

        // Normalize to LF — JsonSerializer may use CRLF on Windows.
        if (json.Contains('\r'))
            json = json.Replace("\r\n", "\n").Replace("\r", "\n");

        // Write to temp file then rename for atomicity.
        string tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    /// <summary>Replaces the in-memory profile list (call Save afterwards).</summary>
    public void SetProfiles(List<CustomHighlightProfile> profiles)
    {
        _profiles = profiles ?? [];
    }

    /// <summary>Finds a profile by name (case-insensitive).</summary>
    public CustomHighlightProfile? FindByName(string name)
    {
        return _profiles.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Exports the given profiles to a JSON string (same format as the config file).</summary>
    public static string ExportToJson(IEnumerable<CustomHighlightProfile> profiles)
    {
        var root = new RootDto
        {
            CustomHighlighting = [.. profiles.Select(p => new ProfileDto
            {
                Name = p.Name,
                Rules = [.. p.Rules.Select(r =>
                {
                    bool isBlock = string.Equals(r.Scope, "block", StringComparison.OrdinalIgnoreCase);
                    return new RuleDto
                    {
                        Pattern = isBlock ? null : r.Pattern,
                        Scope = r.Scope,
                        Foreground = FormatColor(r.Foreground),
                        Background = FormatColor(r.Background),
                        Begin = isBlock ? r.BeginPattern : null,
                        End = isBlock ? r.EndPattern : null,
                        Foldable = isBlock && r.Foldable ? true : null,
                    };
                })],
            })],
        };

        string json = JsonSerializer.Serialize(root, JsonOptions);
        if (json.Contains('\r'))
            json = json.Replace("\r\n", "\n").Replace("\r", "\n");
        return json;
    }

    /// <summary>Imports profiles from a JSON string. Returns null on parse failure.</summary>
    public static List<CustomHighlightProfile>? ImportFromJson(string json)
    {
        try
        {
            var root = JsonSerializer.Deserialize<RootDto>(json, JsonOptions);
            if (root?.CustomHighlighting is null) return null;

            var profiles = new List<CustomHighlightProfile>();
            foreach (var dto in root.CustomHighlighting)
            {
                var profile = new CustomHighlightProfile { Name = dto.Name ?? string.Empty };
                if (dto.Rules is not null)
                {
                    foreach (var ruleDto in dto.Rules)
                    {
                        profile.Rules.Add(new CustomHighlightRule
                        {
                            Pattern = ruleDto.Pattern ?? string.Empty,
                            Scope = ruleDto.Scope ?? "match",
                            Foreground = ParseColor(ruleDto.Foreground),
                            Background = ParseColor(ruleDto.Background),
                            BeginPattern = ruleDto.Begin ?? string.Empty,
                            EndPattern = ruleDto.End ?? string.Empty,
                            Foldable = ruleDto.Foldable ?? false,
                        });
                    }
                }
                profiles.Add(profile);
            }
            return profiles;
        }
        catch
        {
            return null;
        }
    }

    // ── Color helpers ──────────────────────────────────────────────────

    private static Color ParseColor(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return Color.Empty;

        try
        {
            if (hex.StartsWith('#') && hex.Length == 7)
            {
                int r = Convert.ToInt32(hex.Substring(1, 2), 16);
                int g = Convert.ToInt32(hex.Substring(3, 2), 16);
                int b = Convert.ToInt32(hex.Substring(5, 2), 16);
                return Color.FromArgb(r, g, b);
            }
            return ColorTranslator.FromHtml(hex);
        }
        catch
        {
            return Color.Empty;
        }
    }

    private static string? FormatColor(Color color)
    {
        if (color.IsEmpty || color == Color.Empty) return null;
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
