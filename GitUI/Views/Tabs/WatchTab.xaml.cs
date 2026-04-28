using System.Windows;
using System.Windows.Controls;
using GitUI.ViewModels.Tabs;

namespace GitUI.Views.Tabs;

public partial class WatchTab : UserControl
{
    public WatchTab()
    {
        InitializeComponent();
        Loaded += WatchTab_Loaded;
    }

    private void WatchTab_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is WatchTabViewModel vm)
            vm.VerifyFolderHealth();
    }
}
