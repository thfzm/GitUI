using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GitUI.ViewModels;

namespace GitUI.Views;

public partial class CommandPalette : UserControl
{
    public CommandPalette()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            QueryBox.Focus();
            Keyboard.Focus(QueryBox);
        };
    }

    private void QueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel vm) return;
        switch (e.Key)
        {
            case Key.Down:
                if (vm.SelectedIndex < vm.Results.Count - 1) vm.SelectedIndex++;
                e.Handled = true;
                break;
            case Key.Up:
                if (vm.SelectedIndex > 0) vm.SelectedIndex--;
                e.Handled = true;
                break;
            case Key.Enter:
                vm.ExecuteSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                vm.Close();
                e.Handled = true;
                break;
        }
    }

    private void Background_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CommandPaletteViewModel vm) vm.Close();
    }

    private void Card_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
