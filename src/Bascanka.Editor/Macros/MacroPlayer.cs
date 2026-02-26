using System.Windows.Forms;
using Bascanka.Core.Buffer;

namespace Bascanka.Editor.Macros;

/// <summary>
/// Replays a recorded <see cref="Macro"/> against an editor control by
/// interpreting each <see cref="MacroAction"/> and applying the corresponding
/// edit or navigation command to the provided <see cref="PieceTable"/> buffer.
/// <para>
/// Playback is asynchronous and yields to the UI message loop between actions
/// to keep the application responsive.  Playback can be cancelled at any time
/// via <see cref="CancelPlayback"/>.
/// </para>
/// </summary>
public sealed class MacroPlayer
{
    // ── Fields ──────────────────────────────────────────────────────────

    private CancellationTokenSource? _cts;

    // ── Events ──────────────────────────────────────────────────────────

    /// <summary>Raised when playback begins.</summary>
    public event EventHandler? PlaybackStarted;

    /// <summary>Raised when playback completes or is cancelled.</summary>
    public event EventHandler? PlaybackFinished;

    /// <summary>Raised after each action is executed during playback.</summary>
    public event EventHandler<MacroPlaybackProgressEventArgs>? ActionExecuted;

    /// <summary>Raised when an action fails during playback.</summary>
    public event EventHandler<MacroPlaybackErrorEventArgs>? PlaybackError;

    // ── Properties ──────────────────────────────────────────────────────

    /// <summary><see langword="true"/> while a macro is being played back.</summary>
    public bool IsPlaying => _cts is not null && !_cts.IsCancellationRequested;

    // ── Playback API ────────────────────────────────────────────────────

    /// <summary>
    /// Plays a macro once against the given <paramref name="buffer"/>.
    /// Caret position management must be handled by the caller through
    /// the <paramref name="caretOffset"/> ref parameter and the
    /// <see cref="ActionExecuted"/> event.
    /// </summary>
    /// <param name="macro">The macro to replay.</param>
    /// <param name="buffer">The text buffer to apply edits to.</param>
    /// <param name="caretOffset">
    /// The current caret offset; updated by movement and insertion actions.
    /// </param>
    public async Task PlayAsync(Macro macro, PieceTable buffer, long caretOffset)
    {
        await PlayMultipleAsync(macro, 1, buffer, caretOffset).ConfigureAwait(false);
    }

    /// <summary>
    /// Plays a macro the specified number of times.
    /// </summary>
    /// <param name="macro">The macro to replay.</param>
    /// <param name="times">Number of repetitions (must be at least 1).</param>
    /// <param name="buffer">The text buffer to apply edits to.</param>
    /// <param name="caretOffset">The starting caret offset.</param>
    public async Task PlayMultipleAsync(Macro macro, int times, PieceTable buffer, long caretOffset)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ArgumentNullException.ThrowIfNull(buffer);
        if (times < 1)
            throw new ArgumentOutOfRangeException(nameof(times), times, "Must play at least once.");

        if (_cts is not null)
            throw new InvalidOperationException("A playback session is already in progress.");

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        PlaybackStarted?.Invoke(this, EventArgs.Empty);

        try
        {
            long caret = caretOffset;
            int totalActions = macro.Actions.Count * times;
            int executedCount = 0;

            for (int iteration = 0; iteration < times; iteration++)
            {
                for (int i = 0; i < macro.Actions.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    MacroAction action = macro.Actions[i];
                    try
                    {
                        caret = ExecuteAction(action, buffer, caret);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        PlaybackError?.Invoke(this, new MacroPlaybackErrorEventArgs(action, i, ex));
                        // Continue with next action unless cancelled.
                    }

                    executedCount++;
                    ActionExecuted?.Invoke(this,
                        new MacroPlaybackProgressEventArgs(action, executedCount, totalActions, caret));

                    // Yield to the UI message loop every 64 actions to
                    // keep the application responsive.
                    if (executedCount % 64 == 0)
                        await Task.Delay(1, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Playback was cancelled -- fall through to finally.
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            PlaybackFinished?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Cancels the currently running playback, if any.
    /// </summary>
    public void CancelPlayback()
    {
        _cts?.Cancel();
    }

    // ── Action execution ────────────────────────────────────────────────

    /// <summary>
    /// Applies a single <see cref="MacroAction"/> to the buffer and returns
    /// the updated caret offset.
    /// </summary>
    private static long ExecuteAction(MacroAction action, PieceTable buffer, long caret)
    {
        switch (action.ActionType)
        {
            case MacroActionType.TypeText:
                if (action.Text is not null && action.Text.Length > 0)
                {
                    buffer.Insert(caret, action.Text);
                    caret += action.Text.Length;
                }
                break;

            case MacroActionType.Delete:
                if (caret < buffer.Length)
                    buffer.Delete(caret, 1);
                break;

            case MacroActionType.Backspace:
                if (caret > 0)
                {
                    caret--;
                    buffer.Delete(caret, 1);
                }
                break;

            case MacroActionType.MoveCaret:
                if (action.Offset.HasValue)
                {
                    caret = Math.Clamp(action.Offset.Value, 0, buffer.Length);
                }
                else if (action.Key.HasValue)
                {
                    caret = ApplyMovementKey(action.Key.Value, buffer, caret);
                }
                break;

            case MacroActionType.Select:
                // Selection is a UI-layer concept; the macro player records
                // the target offset so that the host can update the visual
                // selection after receiving the ActionExecuted event.
                if (action.Offset.HasValue)
                    caret = Math.Clamp(action.Offset.Value, 0, buffer.Length);
                break;

            case MacroActionType.Find:
                // Find is informational during playback.  The host should
                // listen for ActionExecuted and use the SearchEngine to
                // locate the next match.
                break;

            case MacroActionType.Replace:
                // Replace: extract search/replace text from parameters.
                if (action.Parameters is not null &&
                    action.Parameters.TryGetValue("SearchText", out string? searchText) &&
                    action.Parameters.TryGetValue("ReplaceText", out string? replaceText) &&
                    searchText is not null && replaceText is not null)
                {
                    string docText = buffer.Length > 0 ? buffer.GetText(0, buffer.Length) : "";
                    int idx = docText.IndexOf(searchText, (int)Math.Min(caret, int.MaxValue),
                        StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        buffer.Delete(idx, searchText.Length);
                        buffer.Insert(idx, replaceText);
                        caret = idx + replaceText.Length;
                    }
                }
                break;

            case MacroActionType.Command:
                // Named commands are host-specific.  The host should listen
                // for ActionExecuted and dispatch the command by name.
                break;
        }

        return caret;
    }

    /// <summary>
    /// Translates a navigation <see cref="Keys"/> value into a caret
    /// movement within the buffer.
    /// </summary>
    private static long ApplyMovementKey(Keys key, PieceTable buffer, long caret)
    {
        return key switch
        {
            Keys.Left => Math.Max(0, caret - 1),
            Keys.Right => Math.Min(buffer.Length, caret + 1),
            Keys.Home => 0,
            Keys.End => buffer.Length,
            Keys.Up => MoveUp(buffer, caret),
            Keys.Down => MoveDown(buffer, caret),
            _ => caret,
        };
    }

    /// <summary>Moves the caret up one line, preserving approximate column position.</summary>
    private static long MoveUp(PieceTable buffer, long caret)
    {
        // Find start of current line.
        long lineStart = caret;
        while (lineStart > 0 && buffer.GetCharAt(lineStart - 1) != '\n')
            lineStart--;

        if (lineStart == 0)
            return 0; // Already on the first line.

        long column = caret - lineStart;

        // Find start of previous line.
        long prevLineEnd = lineStart - 1; // the '\n' character
        long prevLineStart = prevLineEnd;
        while (prevLineStart > 0 && buffer.GetCharAt(prevLineStart - 1) != '\n')
            prevLineStart--;

        long prevLineLength = prevLineEnd - prevLineStart;
        return prevLineStart + Math.Min(column, prevLineLength);
    }

    /// <summary>Moves the caret down one line, preserving approximate column position.</summary>
    private static long MoveDown(PieceTable buffer, long caret)
    {
        // Find start of current line.
        long lineStart = caret;
        while (lineStart > 0 && buffer.GetCharAt(lineStart - 1) != '\n')
            lineStart--;

        long column = caret - lineStart;

        // Find end of current line ('\n' or end of buffer).
        long lineEnd = caret;
        while (lineEnd < buffer.Length && buffer.GetCharAt(lineEnd) != '\n')
            lineEnd++;

        if (lineEnd >= buffer.Length)
            return buffer.Length; // Already on the last line.

        long nextLineStart = lineEnd + 1;

        // Find end of next line.
        long nextLineEnd = nextLineStart;
        while (nextLineEnd < buffer.Length && buffer.GetCharAt(nextLineEnd) != '\n')
            nextLineEnd++;

        long nextLineLength = nextLineEnd - nextLineStart;
        return nextLineStart + Math.Min(column, nextLineLength);
    }
}
