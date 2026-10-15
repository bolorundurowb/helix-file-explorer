using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HelixExplorer.Core.Settings;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

/// <summary>
/// Fills menus from <see cref="CommandRegistry"/> and <see cref="FolderColorPalette"/> when they open.
/// Rebuilding on open reads checked state straight from the pane, so no per-menu binding can disagree with it.
/// </summary>
internal static class CommandMenuBuilder
{
    private static readonly IBrush[] SwatchBrushes = FolderColorPalette.All
        .Select(c => (IBrush)new ImmutableSolidColorBrush(Color.Parse(c.Hex)))
        .ToArray();

    /// <summary>
    /// Fills every direct submenu whose <c>Tag</c> is a registry menu or the folder colour list.
    /// </summary>
    public static void FillTaggedSubmenus(ItemCollection items, PaneViewModel pane)
    {
        foreach (var item in items)
        {
            if (item is MenuItem { Tag: IReadOnlyList<IReadOnlyList<CommandDefinition<PaneViewModel>>> groups } menuItem)
                Fill(menuItem.Items, groups, pane);
        }

        FillColorSubmenus(items, pane.SetFolderColorCommand);
    }

    /// <summary>
    /// Fills every direct submenu tagged with <see cref="FolderColorPalette.All"/>; <paramref name="command"/> receives the hex value.
    /// </summary>
    public static void FillColorSubmenus(ItemCollection items, ICommand command)
    {
        foreach (var item in items)
        {
            if (item is MenuItem { Tag: IReadOnlyList<FolderColorOption> } menuItem)
                FillColors(menuItem.Items, command);
        }
    }

    public static void Fill(
        ItemCollection items,
        IReadOnlyList<IReadOnlyList<CommandDefinition<PaneViewModel>>> groups,
        PaneViewModel pane)
    {
        items.Clear();
        for (var g = 0; g < groups.Count; g++)
        {
            if (g > 0)
                items.Add(new Separator());

            foreach (var definition in groups[g])
                items.Add(CreateItem(definition, pane));
        }
    }

    /// <summary>
    /// Puts the colour entries at the top of <paramref name="items"/>, leaving any static entries (such as Clear) after them.
    /// </summary>
    private static void FillColors(ItemCollection items, ICommand command)
    {
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] is MenuItem { Tag: FolderColorOption })
                items.RemoveAt(i);
        }

        var colors = FolderColorPalette.All;
        for (var i = 0; i < colors.Count; i++)
        {
            var color = colors[i];
            items.Insert(i, new MenuItem
            {
                Header = color.Name,
                Tag = color,
                Command = command,
                CommandParameter = color.Hex,
                Icon = new Ellipse { Width = 12, Height = 12, Fill = SwatchBrushes[i] },
            });
        }
    }

    private static MenuItem CreateItem(CommandDefinition<PaneViewModel> definition, PaneViewModel pane)
    {
        var item = new MenuItem
        {
            Header = definition.Label,
            IsEnabled = definition.CanExecute?.Invoke(pane) ?? true,
        };

        if (definition.IsChecked is { } isChecked)
        {
            item.ToggleType = MenuItemToggleType.Radio;
            item.IsChecked = isChecked(pane);
        }

        if (definition.Icon is { } key
            && Application.Current?.TryFindResource(key, out var resource) == true
            && resource is Geometry geometry)
        {
            item.Icon = new PathIcon { Data = geometry };
        }

        if (definition.Gesture.Length > 0)
            item.InputGesture = KeyGesture.Parse(definition.Gesture);

        item.Click += (_, _) => definition.Execute(pane);
        return item;
    }
}
