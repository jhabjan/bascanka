namespace Bascanka.App;

/// <summary>
/// Session persistence moved to recovery manifest; this type is retained to avoid
/// breaking older references but no longer performs any work.
/// </summary>
[System.Obsolete("Session persistence now uses recovery\\manifest.json.")]
public sealed class SessionManager
{
}
