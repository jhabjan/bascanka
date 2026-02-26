namespace Bascanka.Plugins.Api;

/// <summary>
/// Event arguments for document lifecycle events (opened, closed, saved).
/// </summary>
/// <remarks>
/// Initializes a new instance of <see cref="DocumentEventArgs"/>.
/// </remarks>
/// <param name="filePath">The file system path of the document.</param>
public class DocumentEventArgs(string filePath) : EventArgs
{

	/// <summary>
	/// Gets the absolute file system path of the affected document.
	/// May be an empty string for documents that have never been saved.
	/// </summary>
	public string FilePath { get; } = filePath;
}
