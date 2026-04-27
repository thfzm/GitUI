using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;
using GitUI.ViewModels;

namespace GitUI.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        // Pre-fill the secret password box from the VM (e.g., when reopening config).
        if (DataContext is LoginViewModel vm)
        {
            ClientSecretBox.Password = vm.ClientSecretInput;
        }
    }

    private void PatBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm && sender is PasswordBox pb)
        {
            vm.PatInput = pb.Password;
        }
    }

    private void ClientSecretBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm && sender is PasswordBox pb)
        {
            vm.ClientSecretInput = pb.Password;
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.ToString(),
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch { }
    }
}
