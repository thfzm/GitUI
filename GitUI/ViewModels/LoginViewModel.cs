using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUI.Services;

namespace GitUI.ViewModels;

public enum LoginPanel { Choose, DeviceFlowPending, WebFlowPending, ConfigureOAuth }

public partial class LoginViewModel : ObservableObject
{
    private readonly Func<string, AuthMethod, Task> _onAuthenticated;
    private CancellationTokenSource? _pollCts;

    [ObservableProperty] private string _patInput = "";
    [ObservableProperty] private string _clientIdInput = "";
    [ObservableProperty] private string _clientSecretInput = "";
    [ObservableProperty] private LoginPanel _panel = LoginPanel.Choose;
    [ObservableProperty] private string? _userCode;
    [ObservableProperty] private string? _verificationUri;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasDeviceFlow;
    [ObservableProperty] private bool _hasWebFlow;

    public string RedirectUri => OAuthConfig.RedirectUri;

    public LoginViewModel(Func<string, AuthMethod, Task> onAuthenticated)
    {
        _onAuthenticated = onAuthenticated;
        var settings = OAuthConfig.Load();
        ClientIdInput = settings.ClientId ?? "";
        ClientSecretInput = settings.ClientSecret ?? "";
        HasDeviceFlow = settings.HasDeviceFlow;
        HasWebFlow = settings.HasWebFlow;
    }

    // ---- PAT ----------------------------------------------------------------

    [RelayCommand]
    private async Task LoginWithPatAsync()
    {
        if (string.IsNullOrWhiteSpace(PatInput))
        {
            StatusMessage = "토큰을 입력하세요.";
            return;
        }
        IsBusy = true;
        StatusMessage = "확인 중...";
        try
        {
            await _onAuthenticated(PatInput.Trim(), AuthMethod.Pat);
        }
        catch (Exception ex)
        {
            StatusMessage = "오류: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- OAuth Web Flow -----------------------------------------------------

    [RelayCommand]
    private async Task LoginWithGithubWebAsync()
    {
        var settings = OAuthConfig.Load();
        if (!settings.HasWebFlow)
        {
            Panel = LoginPanel.ConfigureOAuth;
            StatusMessage = "Client ID와 Client Secret이 모두 필요합니다.";
            return;
        }

        Panel = LoginPanel.WebFlowPending;
        IsBusy = true;
        StatusMessage = null;
        try
        {
            var auth = new OAuthWebFlowAuthenticator(settings.ClientId!, settings.ClientSecret!);
            _pollCts = new CancellationTokenSource();
            var token = await auth.AuthenticateAsync(ct: _pollCts.Token);
            if (string.IsNullOrEmpty(token))
            {
                StatusMessage = "인증이 취소되었거나 실패했습니다.";
                Panel = LoginPanel.Choose;
                return;
            }
            await _onAuthenticated(token, AuthMethod.OAuthWebFlow);
        }
        catch (Exception ex)
        {
            StatusMessage = "오류: " + ex.Message;
            Panel = LoginPanel.Choose;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- OAuth Device Flow --------------------------------------------------

    [RelayCommand]
    private async Task LoginWithGithubDeviceAsync()
    {
        var settings = OAuthConfig.Load();
        if (!settings.HasDeviceFlow)
        {
            Panel = LoginPanel.ConfigureOAuth;
            StatusMessage = "Client ID가 필요합니다.";
            return;
        }

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var auth = new DeviceFlowAuthenticator(settings.ClientId!);
            var code = await auth.RequestDeviceCodeAsync();
            UserCode = code.UserCode;
            VerificationUri = code.VerificationUri;
            Panel = LoginPanel.DeviceFlowPending;

            try
            {
                Process.Start(new ProcessStartInfo { FileName = code.VerificationUri, UseShellExecute = true });
            }
            catch { }

            _pollCts = new CancellationTokenSource();
            var token = await auth.PollForTokenAsync(code, _pollCts.Token);
            if (token == null)
            {
                StatusMessage = "인증이 취소되었거나 만료되었습니다.";
                Panel = LoginPanel.Choose;
                return;
            }
            await _onAuthenticated(token, AuthMethod.OAuthDeviceFlow);
        }
        catch (Exception ex)
        {
            StatusMessage = "오류: " + ex.Message;
            Panel = LoginPanel.Choose;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelPending()
    {
        _pollCts?.Cancel();
        Panel = LoginPanel.Choose;
        StatusMessage = null;
    }

    [RelayCommand]
    private void OpenVerificationUrl()
    {
        if (string.IsNullOrEmpty(VerificationUri)) return;
        try { Process.Start(new ProcessStartInfo { FileName = VerificationUri, UseShellExecute = true }); }
        catch { }
    }

    [RelayCommand]
    private void CopyUserCode()
    {
        if (string.IsNullOrEmpty(UserCode)) return;
        try { Clipboard.SetText(UserCode); StatusMessage = "코드가 클립보드에 복사되었습니다."; }
        catch { }
    }

    // ---- OAuth Config -------------------------------------------------------

    [RelayCommand]
    private void ShowConfigureOAuth() => Panel = LoginPanel.ConfigureOAuth;

    [RelayCommand]
    private void BackToChoose()
    {
        Panel = LoginPanel.Choose;
        StatusMessage = null;
    }

    [RelayCommand]
    private void SaveOAuthConfig()
    {
        if (string.IsNullOrWhiteSpace(ClientIdInput))
        {
            StatusMessage = "Client ID를 입력하세요.";
            return;
        }
        var newSettings = new OAuthSettings(
            ClientIdInput.Trim(),
            string.IsNullOrWhiteSpace(ClientSecretInput) ? null : ClientSecretInput.Trim());
        OAuthConfig.Save(newSettings);
        HasDeviceFlow = newSettings.HasDeviceFlow;
        HasWebFlow = newSettings.HasWebFlow;
        StatusMessage = "저장되었습니다.";
        Panel = LoginPanel.Choose;
    }

    [RelayCommand]
    private void CopyRedirectUri()
    {
        try { Clipboard.SetText(RedirectUri); StatusMessage = "Callback URL이 복사되었습니다."; }
        catch { }
    }
}
