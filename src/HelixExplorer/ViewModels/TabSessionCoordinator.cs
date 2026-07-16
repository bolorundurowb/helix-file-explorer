using System.Collections.ObjectModel;
using HelixExplorer.Core.Session;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels;

/// <summary>
/// Helpers for tab create/close/cycle and session document save/restore.
/// Event wiring and default layout stay with <see cref="MainWindowViewModel"/>.
/// </summary>
public sealed class TabSessionCoordinator(ISessionStore sessionStore, ILogger<TabSessionCoordinator> logger)
{
    public void Restore(
        IList<string> recentPaths,
        Func<TabViewModel> createBrowserTab,
        Func<TabViewModel> createDefaultTab,
        Action<TabViewModel> addTab,
        Action<TabViewModel> selectTab,
        IList<TabViewModel> tabs)
    {
        var session = sessionStore.Load();

        if (session.RecentPaths.Count > 0)
        {
            recentPaths.Clear();
            foreach (var path in session.RecentPaths)
                recentPaths.Add(path);
        }

        if (session.Tabs.Count == 0)
        {
            addTab(createDefaultTab());
        }
        else
        {
            foreach (var snapshot in session.Tabs)
            {
                var tab = createBrowserTab();
                addTab(tab);
                tab.RestoreFrom(snapshot);
            }
        }

        if (tabs.Count == 0)
            return;

        var index = Math.Clamp(session.ActiveTabIndex, 0, tabs.Count - 1);
        selectTab(tabs[index]);
    }

    public void Save(
        IList<TabViewModel> tabs,
        TabViewModel? selectedTab,
        IEnumerable<string> recentPaths)
    {
        var document = new SessionDocument
        {
            ActiveTabIndex = selectedTab is null ? 0 : Math.Max(0, tabs.IndexOf(selectedTab)),
            RecentPaths = recentPaths.Take(12).ToList()
        };

        foreach (var tab in tabs.Where(t => t.IsBrowserTab))
            document.Tabs.Add(tab.CreateSnapshot());

        try
        {
            sessionStore.Save(document);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to persist session document");
        }
    }

    public TabViewModel? CloseTab(
        ObservableCollection<TabViewModel> tabs,
        TabViewModel? tab,
        TabViewModel? selectedTab,
        Action<TabViewModel> detach,
        Func<TabViewModel> createDefaultTab,
        Action<TabViewModel> addTab)
    {
        if (tab is null)
            return selectedTab;

        var index = tabs.IndexOf(tab);
        if (index < 0)
            return selectedTab;

        var wasSelected = ReferenceEquals(tab, selectedTab);

        tabs.Remove(tab);
        detach(tab);
        tab.Dispose();

        if (tabs.Count == 0)
        {
            var replacement = createDefaultTab();
            addTab(replacement);
            return replacement;
        }

        if (wasSelected)
            return tabs[Math.Clamp(index, 0, tabs.Count - 1)];

        return selectedTab;
    }

    public TabViewModel? CycleSelectedTab(
        ObservableCollection<TabViewModel> tabs,
        TabViewModel? selectedTab,
        int delta)
    {
        if (tabs.Count < 2 || selectedTab is null)
            return selectedTab;

        var current = tabs.IndexOf(selectedTab);
        var next = ((current + delta) % tabs.Count + tabs.Count) % tabs.Count;
        return tabs[next];
    }
}
