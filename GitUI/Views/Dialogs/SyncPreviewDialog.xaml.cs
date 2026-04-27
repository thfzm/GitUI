using System.Collections.Generic;
using System.Windows;
using GitUI.Services;

namespace GitUI.Views.Dialogs;

public partial class SyncPreviewDialog : Window
{
    public IReadOnlyList<SyncFileEntry> Entries { get; }
    public int Added { get; }
    public int Modified { get; }
    public int Unchanged { get; }
    public int Skipped { get; }
    public string Message { get; }

    public SyncPreviewDialog(SyncPreviewResult result, string message)
    {
        Entries = result.Entries;
        Added = result.Added;
        Modified = result.Modified;
        Unchanged = result.Unchanged;
        Skipped = result.Skipped;
        Message = message;
        DataContext = this;
        InitializeComponent();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
