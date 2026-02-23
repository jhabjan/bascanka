using Bascanka.Core.Syntax.Lexers;
using System.Reflection;

namespace Bascanka.Core.Syntax;

/// <summary>
/// Central registry of all available <see cref="ILexer"/> implementations.
/// Provides lookup by language identifier and by file extension.
/// </summary>
public sealed class LexerRegistry
{
    private static readonly Lazy<LexerRegistry> _instance = new(() =>
    {
        var registry = new LexerRegistry();
        registry.RegisterBuiltInLexers();
        return registry;
    });

    /// <summary>
    /// The singleton instance with all built-in lexers pre-registered.
    /// </summary>
    public static LexerRegistry Instance => _instance.Value;

    private readonly Dictionary<string, ILexer> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ILexer> _byExtension = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a lexer.  If a lexer with the same language ID is already
    /// registered it will be replaced.
    /// </summary>
    public void Register(ILexer lexer)
    {
        ArgumentNullException.ThrowIfNull(lexer);

        _byId[lexer.LanguageId] = lexer;

        foreach (string ext in lexer.FileExtensions)
        {
            _byExtension[ext] = lexer;
        }
    }

    /// <summary>
    /// Returns the lexer for the given language identifier, or <see langword="null"/>
    /// if none is registered.
    /// </summary>
    public ILexer? GetLexerById(string languageId)
    {
        ArgumentNullException.ThrowIfNull(languageId);
        return _byId.GetValueOrDefault(languageId);
    }

    /// <summary>
    /// Returns the lexer associated with the given file extension (including the
    /// leading dot, e.g. <c>".cs"</c>), or <see langword="null"/> if none matches.
    /// </summary>
    public ILexer? GetLexerByExtension(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return _byExtension.GetValueOrDefault(extension);
    }

    /// <summary>
    /// All registered language identifiers.
    /// </summary>
    public IReadOnlyList<string> LanguageIds => _byId.Keys.OrderBy(k=>k).ToList().AsReadOnly();

    /// <summary>
    /// Registers every built-in lexer that ships with Bascanka.
    /// </summary>
    public void RegisterBuiltInLexers()
    {
        Assembly asm = typeof(LexerRegistry).Assembly;
        var types = asm
            .GetTypes()
            .Where(t => t.IsClass && ! t.IsAbstract && t.Namespace == "Bascanka.Core.Syntax.Lexers")
            .ToList();
        foreach (var type in types)
        {
            if (Activator.CreateInstance(type) is ILexer instance)
            {
                Register(instance);
            }
        }      
    }
}
