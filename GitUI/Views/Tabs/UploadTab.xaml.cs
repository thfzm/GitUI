using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using GitUI.ViewModels.Tabs;

namespace GitUI.Views.Tabs;

public partial class UploadTab : UserControl
{
    public UploadTab()
    {
        InitializeComponent();
    }

    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        if (e.Effects == DragDropEffects.Copy)
            DropZone.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#33FFFFFF")!;
        e.Handled = true;
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
        => DropZone.ClearValue(Border.BackgroundProperty);

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        DropZone.ClearValue(Border.BackgroundProperty);
        if (DataContext is not UploadTabViewModel vm) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        await vm.UploadDroppedAsync(paths);
    }

    private async void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (DataContext is not UploadTabViewModel vm) return;
            if (!Clipboard.ContainsImage()) return;
            try
            {
                var src = Clipboard.GetImage();
                if (src == null) return;
                using var ms = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(src));
                encoder.Save(ms);
                await vm.UploadClipboardImageAsync(ms.ToArray());
                e.Handled = true;
            }
            catch { }
        }
    }
}
