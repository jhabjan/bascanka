using System.Text.Json;

namespace Bascanka.App;

/// <summary>
/// Saves and restores the editor session (open tabs, per-tab state, window geometry)
/// across application launches. Session data is persisted as structured JSON in
/// <c>%AppData%\Bascanka\session.json</c>.
/// </summary>
public sealed class SessionManager
{
    /// <summary>
    /// Saves the current session state from the main form.
    /// Records window geometry, all open file-backed tabs, and per-tab
    /// zoom/scroll/caret state.
    /// </summary>
    public void SaveSession(MainForm form)
    {
        try
        {
            // ── Window geometry ──────────────────────────────────────
            bool maximized = form.WindowState == FormWindowState.Maximized;
            var bounds = maximized ? form.RestoreBounds : form.Bounds;

            // ── Tabs ─────────────────────────────────────────────────
            var tabs = new List<Dictionary<string, object>>();
            int activeTabIndex = form.ActiveTabIndex;
            int savedActiveIndex = -1;

            foreach (var tab in form.Tabs)
            {
                // Only persist file-backed tabs (untitled documents are not saved).
                if (tab.FilePath is null) continue;

                var tabData = new Dictionary<string, object>
                {
                    ["Path"] = tab.FilePath,
                };

                if (tab.IsDeferredLoad)
                {
                    // Tab was never activated — preserve the pending state.
                    tabData["Caret"] = (int)Math.Min(tab.PendingCaret, int.MaxValue);
                    tabData["Scroll"] = tab.PendingScroll;
                    tabData["Zoom"] = tab.PendingZoom;
                    if (tab.PendingWordWrap == true)
                        tabData["WordWrap"] = 1;
                    if (tab.PendingLanguage is not null)
                        tabData["Language"] = tab.PendingLanguage;
                    if (tab.PendingCustomProfileName is not null)
                        tabData["CustomProfileName"] = tab.PendingCustomProfileName;
                }
                else
                {
                    tabData["Caret"] = (int)Math.Min(tab.Editor.CaretOffset, int.MaxValue);
                    tabData["Scroll"] = (int)tab.Editor.ScrollMgr.FirstVisibleLine;
                    tabData["Zoom"] = tab.Editor.ZoomLevel;
                    if (tab.Editor.WordWrap)
                        tabData["WordWrap"] = 1;

                    string lang = tab.Editor.Language;
                    if (!string.IsNullOrEmpty(lang))
                        tabData["Language"] = lang;

                    string? customProfile = tab.Editor.CustomProfileName;
                    if (customProfile is not null)
                        tabData["CustomProfileName"] = customProfile;
                }

                int originalIndex = ((IList<Editor.Tabs.TabInfo>)form.Tabs).IndexOf(tab);
                if (originalIndex == activeTabIndex)
                    savedActiveIndex = tabs.Count;

                tabs.Add(tabData);
            }

            // ── Build session object ──────────────────────────────────
            var session = new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["WindowHeight"] = bounds.Height,
                ["WindowMaximized"] = maximized ? 1 : 0,
                ["WindowWidth"] = bounds.Width,
                ["WindowX"] = bounds.X,
                ["WindowY"] = bounds.Y,
            };

            if (tabs.Count > 0)
            {
                session["ActiveTab"] = savedActiveIndex;
                session["Tabs"] = tabs;
            }

            var history = Bascanka.Editor.Panels.FindReplacePanel.GetSearchHistory();
            if (history.Count > 0)
                session["SearchHistory"] = history.ToArray();

            SettingsManager.SaveStructuredSession(session);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save session: {ex.Message}");
        }
    }

    /// <summary>
    /// Restores the previous session by opening all saved file-backed tabs
    /// and applying per-tab zoom/scroll/caret state.
    /// Returns true if at least one file was restored.
    /// </summary>
    public bool RestoreSession(MainForm form)
    {
        try
        {
            var root = SettingsManager.ReadSessionRoot();
            if (root is null) return false;
            var el = root.Value;

            // ── Search history (restore regardless of tabs) ───────
            RestoreSearchHistory(el);

            // ── Tabs (new structured format) ──────────────────────
            if (el.TryGetProperty("Tabs", out var tabsEl) && tabsEl.ValueKind == JsonValueKind.Array)
                return RestoreTabsFromArray(form, el, tabsEl);

            // ── Tabs (legacy flat format: Tab0_Path, Tab0_Zoom, ...) ──
            return RestoreTabsLegacy(form, el);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to restore session: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Restores window geometry from a previous session. Should be called
    /// during form construction, before <see cref="Form.OnLoad"/>.
    /// </summary>
    public void RestoreWindowState(MainForm form)
    {
        try
        {
            var root = SettingsManager.ReadSessionRoot();
            if (root is null) return;
            var el = root.Value;

            int x = GetIntProp(el, "WindowX", int.MinValue);
            int y = GetIntProp(el, "WindowY", int.MinValue);
            int w = GetIntProp(el, "WindowWidth", 0);
            int h = GetIntProp(el, "WindowHeight", 0);
            bool maximized = GetIntProp(el, "WindowMaximized", 0) != 0;

            if (w <= 0 || h <= 0) return;

            // Validate that the saved position is at least partially on-screen.
            var savedRect = new System.Drawing.Rectangle(x, y, w, h);
            bool onScreen = false;
            foreach (var screen in Screen.AllScreens)
            {
                if (screen.WorkingArea.IntersectsWith(savedRect))
                {
                    onScreen = true;
                    break;
                }
            }

            if (!onScreen) return; // Saved position is off-screen — keep defaults.

            form.StartPosition = FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(x, y);
            form.Size = new System.Drawing.Size(w, h);

            if (maximized)
                form.WindowState = FormWindowState.Maximized;
        }
        catch
        {
            // Silently ignore — keep default window position.
        }
    }

    /// <summary>
    /// Clears all session state.
    /// </summary>
    public void ClearSession()
    {
        SettingsManager.ClearSessionState();
    }

    // ── Private helpers ─────────────────────────────────────────────

    private static void RestoreSearchHistory(JsonElement root)
    {
        if (!root.TryGetProperty("SearchHistory", out var historyEl)) return;

        if (historyEl.ValueKind == JsonValueKind.Array)
        {
            var items = new List<string>();
            foreach (var item in historyEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    items.Add(item.GetString()!);
            }
            if (items.Count > 0)
                Bascanka.Editor.Panels.FindReplacePanel.SetSearchHistory(items);
        }
        else if (historyEl.ValueKind == JsonValueKind.String)
        {
            // Backward compat: old pipe-separated format.
            string historyStr = historyEl.GetString() ?? "";
            if (!string.IsNullOrEmpty(historyStr))
            {
                var items = historyStr.Split('|', StringSplitOptions.RemoveEmptyEntries);
                Bascanka.Editor.Panels.FindReplacePanel.SetSearchHistory(items);
            }
        }
    }

    private static bool RestoreTabsFromArray(MainForm form, JsonElement root, JsonElement tabsEl)
    {
        int tabCount = tabsEl.GetArrayLength();
        if (tabCount <= 0) return false;

        int activeIndex = GetIntProp(root, "ActiveTab", 0);
        if (activeIndex < 0 || activeIndex >= tabCount)
            activeIndex = 0;

        int actualActiveIndex = -1;

        for (int i = 0; i < tabCount; i++)
        {
            var tabEl = tabsEl[i];

            string? path = GetStringProp(tabEl, "Path");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                continue;

            int zoom = GetIntProp(tabEl, "Zoom", 0);
            int scroll = GetIntProp(tabEl, "Scroll", 0);
            int caret = GetIntProp(tabEl, "Caret", 0);
            bool wordWrap = GetIntProp(tabEl, "WordWrap", 0) != 0;
            string? language = GetStringProp(tabEl, "Language");
            string? customProfile = GetStringProp(tabEl, "CustomProfileName");

            if (i == activeIndex)
            {
                // Eagerly load the active tab.
                form.OpenFile(path);
                actualActiveIndex = form.Tabs.Count - 1;

                // Store state as pending — applied in ActivateTab.
                var tab = form.Tabs[actualActiveIndex];
                if (string.Equals(tab.FilePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    tab.PendingZoom = zoom;
                    tab.PendingScroll = scroll;
                    tab.PendingCaret = caret;
                    tab.PendingWordWrap = wordWrap ? true : null;
                    tab.PendingLanguage = language;
                    tab.PendingCustomProfileName = customProfile;
                }
            }
            else
            {
                // Defer loading for inactive tabs.
                form.AddDeferredTab(path, zoom, scroll, caret, wordWrap);

                // Set pending language/custom profile on the deferred tab.
                var tab = form.Tabs[form.Tabs.Count - 1];
                tab.PendingLanguage = language;
                tab.PendingCustomProfileName = customProfile;
            }
        }

        // Activate the previously active tab (applies pending state).
        if (actualActiveIndex >= 0)
            form.ActivateTab(actualActiveIndex);
        else if (form.Tabs.Count > 0)
            form.ActivateTab(0);

        return form.Tabs.Count > 0;
    }

    /// <summary>
    /// Backward compat: reads the old flat Tab0_Path / Tab0_Zoom / ... format.
    /// </summary>
    private static bool RestoreTabsLegacy(MainForm form, JsonElement root)
    {
        int tabCount = GetIntProp(root, "TabCount", 0);
        if (tabCount <= 0) return false;

        int activeIndex = GetIntProp(root, "ActiveTab", 0);
        if (activeIndex < 0 || activeIndex >= tabCount)
            activeIndex = 0;

        int actualActiveIndex = -1;

        for (int i = 0; i < tabCount; i++)
        {
            string? path = GetStringProp(root, $"Tab{i}_Path");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                continue;

            int zoom = GetIntProp(root, $"Tab{i}_Zoom", 0);
            int scroll = GetIntProp(root, $"Tab{i}_Scroll", 0);
            int caret = GetIntProp(root, $"Tab{i}_Caret", 0);

            if (i == activeIndex)
            {
                form.OpenFile(path);
                actualActiveIndex = form.Tabs.Count - 1;

                var tab = form.Tabs[actualActiveIndex];
                if (string.Equals(tab.FilePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    tab.PendingZoom = zoom;
                    tab.PendingScroll = scroll;
                    tab.PendingCaret = caret;
                }
            }
            else
            {
                form.AddDeferredTab(path, zoom, scroll, caret);
            }
        }

        if (actualActiveIndex >= 0)
            form.ActivateTab(actualActiveIndex);
        else if (form.Tabs.Count > 0)
            form.ActivateTab(0);

        return form.Tabs.Count > 0;
    }

    private static int GetIntProp(JsonElement el, string name, int defaultValue = 0)
    {
        if (el.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int val))
                return val;
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out int parsed))
                return parsed;
        }
        return defaultValue;
    }

    private static string? GetStringProp(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }
}
