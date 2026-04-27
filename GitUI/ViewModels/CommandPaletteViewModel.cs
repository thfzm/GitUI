using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GitUI.ViewModels;

public record CommandItem(string Icon, string Title, string? Subtitle, string? Hint, Action Execute);

public partial class CommandPaletteViewModel : ObservableObject
{
    private readonly List<CommandItem> _all;
    private readonly Action _onClose;

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private int _selectedIndex;

    public ObservableCollection<CommandItem> Results { get; } = new();

    public CommandPaletteViewModel(IEnumerable<CommandItem> items, Action onClose)
    {
        _all = items.ToList();
        _onClose = onClose;
        RebuildResults();
    }

    partial void OnQueryChanged(string value)
    {
        RebuildResults();
        SelectedIndex = 0;
    }

    private void RebuildResults()
    {
        Results.Clear();
        var q = Query.Trim().ToLowerInvariant();
        foreach (var c in _all)
        {
            if (string.IsNullOrEmpty(q)
                || c.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (c.Subtitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                Results.Add(c);
            }
        }
    }

    [RelayCommand]
    private void ExecuteSelected()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count) return;
        var item = Results[SelectedIndex];
        Close();
        try { item.Execute(); } catch { }
    }

    public void Close() => _onClose();
}
