using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opaline.Core.Auth;

namespace Opaline.App.ViewModels;

public partial class AuthViewModel : ObservableObject
{
    private readonly OAuthClient _oauth;
    private CancellationTokenSource? _pollCts;

    [ObservableProperty] private bool isSignedIn;
    [ObservableProperty] private bool isWaiting;
    [ObservableProperty] private string? userCode;
    [ObservableProperty] private string? verificationUrl;
    [ObservableProperty] private string? statusMessage;

    public AuthViewModel(OAuthClient oauth)
    {
        _oauth = oauth;
        IsSignedIn = oauth.IsSignedIn;
    }

    [RelayCommand]
    public async Task StartSignInAsync()
    {
        IsWaiting = true;
        StatusMessage = "Requesting device code…";
        try
        {
            var device = await _oauth.RequestDeviceCodeAsync();
            UserCode = device.UserCode;
            VerificationUrl = device.VerificationUrl;
            StatusMessage = $"Go to {device.VerificationUrl} and enter code: {device.UserCode}";

            _pollCts?.Cancel();
            _pollCts = new CancellationTokenSource();
            var tokens = await _oauth.PollForTokenAsync(device, _pollCts.Token);
            if (tokens is not null)
            {
                IsSignedIn = true;
                StatusMessage = "Signed in successfully.";
                UserCode = null;
            }
            else
            {
                StatusMessage = "Sign-in cancelled or expired.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sign-in failed: {ex.Message}";
        }
        finally
        {
            IsWaiting = false;
        }
    }

    [RelayCommand]
    public void CancelSignIn()
    {
        _pollCts?.Cancel();
        IsWaiting = false;
        StatusMessage = "Cancelled.";
    }

    [RelayCommand]
    public void SignOut()
    {
        _oauth.SignOut();
        IsSignedIn = false;
        StatusMessage = "Signed out.";
        UserCode = null;
    }
}
