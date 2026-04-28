using System.Windows.Controls;
using GitUI.ViewModels.Tabs;

namespace GitUI.Views.Tabs;

public partial class FilesTab : UserControl
{
    public FilesTab() { InitializeComponent(); }

    private void FileTree_SelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is FilesTabViewModel vm)
            vm.SelectedNode = e.NewValue as FileTreeNode;
    }
}
