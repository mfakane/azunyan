using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Azunote;

public sealed partial class MainWindow
{
    private const string CommandPalettePrefix = ">";
    private const int MaxCommandPaletteResults = 100;
    private const int MaxRecentPaletteCommands = 10;

    // Shared by every window, so a command run in one is offered first in all.
    private static readonly List<string> RecentPaletteCommands = [];

    // The candidates are collected once when the palette opens.
    private CommandPaletteItem[] _paletteCommands = [];
    private List<CommandPaletteItem> _paletteDocuments = [];
    private CommandPaletteItem? _openRecentPaletteItem;
    private CommandPaletteMatch[] _paletteMatches = [];
    private string? _paletteFilteredText;
    private bool _paletteRowsShown;

    private bool IsCommandPaletteOpen => CommandPalettePanel.Visibility == Visibility.Visible;

    void IMainWindowActions.ShowCommandPalette() => ShowCommandPalette(CommandPalettePrefix);

    private void ShowCommandPalette(string text)
    {
        if (!IsCommandPaletteOpen)
        {
            CommandPaletteList.ItemContainerTransitions = CommandPaletteRowTransitions;
            _paletteRowsShown = false;
        }

        _runtime.RefreshExternalToolsMenuNow();
        _paletteCommands = OrderByRecentUse(CollectMenuCommands());
        _paletteDocuments = CollectPaletteDocuments();
        _paletteFilteredText = null;
        CommandPalettePanel.Visibility = Visibility.Visible;
        CommandPaletteTextBox.Text = text;
        CommandPaletteTextBox.SelectionStart = text.Length;
        RefreshCommandPalette();
        CommandPaletteTextBox.Focus(FocusState.Programmatic);
    }

    private void CloseCommandPalette(bool focusEditor)
    {
        if (!IsCommandPaletteOpen)
        {
            return;
        }

        // The editor takes the focus before the palette hides, so the focus
        // does not pass through the first control of the window on its way.
        if (focusEditor)
        {
            Focus();
        }

        CommandPalettePanel.Visibility = Visibility.Collapsed;
        CommandPaletteList.Items.Clear();
        _paletteMatches = [];
        _paletteCommands = [];
        _paletteDocuments = [];
    }

    /// <summary>
    /// Filters the candidates by the current text. The rows are rebuilt only
    /// when the results change, so typing that narrows nothing keeps them and
    /// the selection as they are.
    /// </summary>
    private void RefreshCommandPalette()
    {
        var text = CommandPaletteTextBox.Text;
        if (string.Equals(text, _paletteFilteredText, StringComparison.Ordinal))
        {
            return;
        }

        _paletteFilteredText = text;
        var isCommandMode = text.StartsWith(CommandPalettePrefix, StringComparison.Ordinal);
        IReadOnlyList<CommandPaletteItem> items = isCommandMode ? _paletteCommands : _paletteDocuments;
        var query = (isCommandMode ? text[CommandPalettePrefix.Length..] : text).Trim();
        var matches = CommandPaletteSearch.Filter(query, items)
            .Take(MaxCommandPaletteResults)
            .ToArray();
        if (matches.Length == _paletteMatches.Length
            && matches.Zip(_paletteMatches).All(pair =>
                ReferenceEquals(pair.First.Item, pair.Second.Item)
                && pair.First.TitlePositions.SequenceEqual(pair.Second.TitlePositions)))
        {
            return;
        }

        _paletteMatches = matches;
        if (_paletteRowsShown)
        {
            CommandPaletteList.ItemContainerTransitions = [];
        }

        _paletteRowsShown |= matches.Length > 0;
        CommandPaletteList.Items.Clear();
        foreach (var match in _paletteMatches)
        {
            CommandPaletteList.Items.Add(CreateCommandPaletteRow(match));
        }

        SelectCommandPaletteRow(0);
    }

    private void SelectCommandPaletteRow(int index)
    {
        if (_paletteMatches.Length == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, _paletteMatches.Length - 1);
        CommandPaletteList.SelectedIndex = index;
        CommandPaletteList.ScrollIntoView(CommandPaletteList.Items[index]);
    }

    private void RunCommandPaletteRow(int index)
    {
        if (index < 0 || index >= _paletteMatches.Length)
        {
            return;
        }

        var item = _paletteMatches[index].Item;
        if (!item.IsEnabled)
        {
            return;
        }

        if (CommandPaletteTextBox.Text.StartsWith(CommandPalettePrefix, StringComparison.Ordinal))
        {
            var key = GetPaletteCommandKey(item);
            RecentPaletteCommands.Remove(key);
            RecentPaletteCommands.Insert(0, key);
            if (RecentPaletteCommands.Count > MaxRecentPaletteCommands)
            {
                RecentPaletteCommands.RemoveAt(RecentPaletteCommands.Count - 1);
            }
        }

        if (ReferenceEquals(item, _openRecentPaletteItem))
        {
            item.Invoke();
            return;
        }

        // The editor takes the focus back first, so commands that act on the
        // focused text, such as Select All, reach the document.
        CloseCommandPalette(focusEditor: true);
        item.Invoke();
    }

    private static string GetPaletteCommandKey(CommandPaletteItem item) =>
        $"{item.Detail} › {item.Title}";

    private static CommandPaletteItem[] OrderByRecentUse(
        IReadOnlyList<CommandPaletteItem> commands) =>
        commands
            .OrderBy(command => RecentPaletteCommands.IndexOf(GetPaletteCommandKey(command)) is var index
                && index >= 0
                    ? index
                    : int.MaxValue)
            .ToArray();

    /// <summary>
    /// Reads the commands from the menu bar, so every menu item is offered
    /// without a second list to keep in step. Open Recent is offered as one
    /// command that lists the recent files, and the open windows are left to
    /// the document search.
    /// </summary>
    private List<CommandPaletteItem> CollectMenuCommands()
    {
        var commands = new List<CommandPaletteItem>();
        foreach (var menu in MainMenuBar.Items)
        {
            CollectMenuCommands(menu.Items, menu.Title, isEnabled: true, commands);
        }

        return commands;
    }

    private void CollectMenuCommands(
        IList<MenuFlyoutItemBase> items,
        string path,
        bool isEnabled,
        List<CommandPaletteItem> commands)
    {
        foreach (var item in items)
        {
            if (ReferenceEquals(item, OpenRecentMenuItem))
            {
                // Lists the recent files in the palette itself.
                _openRecentPaletteItem = new CommandPaletteItem(
                    OpenRecentMenuItem.Text,
                    path,
                    Shortcut: null,
                    isEnabled,
                    IsChecked: false,
                    () => CommandPaletteTextBox.Text = string.Empty);
                commands.Add(_openRecentPaletteItem);
                continue;
            }

            switch (item)
            {
                case MenuFlyoutSubItem subItem:
                    CollectMenuCommands(
                        subItem.Items,
                        $"{path} › {subItem.Text}",
                        isEnabled && subItem.IsEnabled,
                        commands);
                    break;
                case MenuFlyoutItem menuItem when !_windowMenuItems.Contains(menuItem):
                    commands.Add(new CommandPaletteItem(
                        menuItem.Text,
                        path,
                        GetShortcutText(menuItem),
                        isEnabled && menuItem.IsEnabled,
                        menuItem is ToggleMenuFlyoutItem { IsChecked: true }
                            or RadioMenuFlyoutItem { IsChecked: true },
                        () => menuItem.Command?.Execute(menuItem.CommandParameter)));
                    break;
            }
        }
    }

    /// <summary>
    /// Lists the documents of the other windows, then the recent files that
    /// no window shows.
    /// </summary>
    private List<CommandPaletteItem> CollectPaletteDocuments()
    {
        var documents = new List<CommandPaletteItem>();
        var openPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in _application.Windows)
        {
            var path = window.Runtime.Session.State.FilePath;
            if (path is not null)
            {
                openPaths.Add(path);
            }

            if (!ReferenceEquals(window, this))
            {
                documents.Add(new CommandPaletteItem(
                    window.DocumentName,
                    Path.GetDirectoryName(path) ?? string.Empty,
                    "Open",
                    IsEnabled: true,
                    IsChecked: false,
                    window.ActivateWindow));
            }
        }

        foreach (var recent in _viewModel.RecentFileItems)
        {
            if (!openPaths.Contains(recent.Path))
            {
                documents.Add(new CommandPaletteItem(
                    recent.Text,
                    Path.GetDirectoryName(recent.Path) ?? string.Empty,
                    Shortcut: null,
                    IsEnabled: true,
                    IsChecked: false,
                    () => recent.OpenCommand.Execute(null)));
            }
        }

        return documents;
    }

    private static string? GetShortcutText(MenuFlyoutItem item)
    {
        if (!string.IsNullOrEmpty(item.KeyboardAcceleratorTextOverride))
        {
            return item.KeyboardAcceleratorTextOverride;
        }

        if (item.KeyboardAccelerators.FirstOrDefault() is not { } accelerator)
        {
            return null;
        }

        var parts = new List<string>();
        if (accelerator.Modifiers.HasFlag(VirtualKeyModifiers.Control)) parts.Add("Ctrl");
        if (accelerator.Modifiers.HasFlag(VirtualKeyModifiers.Shift)) parts.Add("Shift");
        if (accelerator.Modifiers.HasFlag(VirtualKeyModifiers.Menu)) parts.Add("Alt");
        parts.Add(accelerator.Key is >= VirtualKey.Number0 and <= VirtualKey.Number9
            ? ((int)accelerator.Key - (int)VirtualKey.Number0).ToString(CultureInfo.InvariantCulture)
            : accelerator.Key.ToString());
        return string.Join('+', parts);
    }

    private static ListViewItem CreateCommandPaletteRow(CommandPaletteMatch match)
    {
        var item = match.Item;
        var resources = Application.Current.Resources;
        var secondaryBrush = (Brush)resources["TextFillColorSecondaryBrush"];

        var title = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        AppendHighlightedText(title.Inlines, item.Title, match.TitlePositions);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        if (item.Detail.Length > 0)
        {
            text.Children.Add(new TextBlock
            {
                Text = item.Detail,
                Style = (Style)resources["CaptionTextBlockStyle"],
                Foreground = secondaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        var check = new FontIcon
        {
            Glyph = "",
            FontSize = 12,
            Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed
        };
        var shortcut = new TextBlock
        {
            Text = item.Shortcut ?? string.Empty,
            Foreground = secondaryBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        var row = new Grid
        {
            ColumnSpacing = 8,
            Padding = new Thickness(0, 4, 0, 4),
            Opacity = item.IsEnabled ? 1 : 0.4,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(16) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(shortcut, 2);
        row.Children.Add(check);
        row.Children.Add(text);
        row.Children.Add(shortcut);

        var listItem = new ListViewItem
        {
            Content = row,
            AllowFocusOnInteraction = false
        };
        AutomationProperties.SetName(listItem, item.Title);
        AutomationProperties.SetHelpText(listItem, item.Detail);
        return listItem;
    }

    private static void AppendHighlightedText(
        InlineCollection inlines,
        string text,
        IReadOnlyList<int> positions)
    {
        var start = 0;
        while (start < text.Length)
        {
            var highlighted = positions.Contains(start);
            var end = start + 1;
            while (end < text.Length && positions.Contains(end) == highlighted)
            {
                end++;
            }

            var run = new Run { Text = text[start..end] };
            if (highlighted)
            {
                run.FontWeight = FontWeights.Bold;
            }

            inlines.Add(run);
            start = end;
        }
    }

    private void CommandPaletteTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsCommandPaletteOpen)
        {
            RefreshCommandPalette();
        }
    }

    private void CommandPaletteTextBox_BeforeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // TextChanged arrives after the text changes, so a key pressed right
        // after typing could otherwise act on the results of the older text.
        RefreshCommandPalette();

        switch (e.Key)
        {
            case VirtualKey.Down:
                SelectCommandPaletteRow(CommandPaletteList.SelectedIndex + 1);
                break;
            case VirtualKey.Up:
                SelectCommandPaletteRow(CommandPaletteList.SelectedIndex - 1);
                break;
            case VirtualKey.Enter:
                RunCommandPaletteRow(CommandPaletteList.SelectedIndex);
                break;
            case VirtualKey.Escape:
                CloseCommandPalette(focusEditor: true);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    // Clicking a result leaves the focus in the text box, so losing it means
    // the user went elsewhere. The event arrives late, possibly after the
    // palette was reopened and focused again.
    private void CommandPaletteTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (CommandPaletteTextBox.FocusState == FocusState.Unfocused)
        {
            CloseCommandPalette(focusEditor: false);
        }
    }

    private void CommandPaletteList_ItemClick(object sender, ItemClickEventArgs e)
    {
        for (var index = 0; index < CommandPaletteList.Items.Count; index++)
        {
            if (CommandPaletteList.Items[index] is ListViewItem listItem
                && (ReferenceEquals(listItem, e.ClickedItem)
                    || ReferenceEquals(listItem.Content, e.ClickedItem)))
            {
                RunCommandPaletteRow(index);
                return;
            }
        }
    }
}
