using System.Windows.Controls;
using System.Windows.Input;
using GitUI.ViewModels;

namespace GitUI.Views;

public partial class SearchView : UserControl
{
    public SearchView() { InitializeComponent(); }

    private void QueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SearchViewModel vm)
        {
            vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }
}
