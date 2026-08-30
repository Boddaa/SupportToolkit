using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkDiscoveryTool.UI.Services;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly AuthService _auth;
    private readonly CurrentUserService _currentUser;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _rememberMe;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _registerEmail = string.Empty;

    [ObservableProperty]
    private string _registerUsername = string.Empty;

    [ObservableProperty]
    private string _registerPassword = string.Empty;

    [ObservableProperty]
    private string _registerConfirmPassword = string.Empty;

    [ObservableProperty]
    private string _registerMessage = string.Empty;

    [ObservableProperty]
    private bool _isRegisterSuccess;

    [ObservableProperty]
    private bool _showRegister;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _showPassword;

    public Action? OnLoginSuccess { get; set; }

    public LoginViewModel(AuthService auth, CurrentUserService currentUser)
    {
        _auth = auth;
        _currentUser = currentUser;
    }

    [ObservableProperty] private bool _isWaitingForApproval;
    [ObservableProperty] private string _pendingUsername = string.Empty;
    private string _pendingPassword = string.Empty;
    private System.Windows.Threading.DispatcherTimer? _pollTimer;

    [RelayCommand]
    private async Task Login()
    {
        HasError = false;
        IsLoading = true;

        try
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                ErrorMessage = "Username is required";
                HasError = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Password is required";
                HasError = true;
                return;
            }

            await Task.Delay(400);

            if (!_auth.ValidateUser(Username, Password, out var user, out var authError))
            {
                if (authError != null && authError.Contains("pending", StringComparison.OrdinalIgnoreCase))
                {
                    StartWaitingForApproval(Username, Password);
                    return;
                }

                ErrorMessage = authError ?? "Invalid username or password";
                HasError = true;
                return;
            }

            StopWaitingForApproval();
            _currentUser.SetUser(user!.Id, user.Username, user.Role);

            if (RememberMe)
                SaveCredentials();

            OnLoginSuccess?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Login error: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ToggleRegister()
    {
        ShowRegister = !ShowRegister;
        RegisterMessage = string.Empty;
        IsRegisterSuccess = false;
    }

    [RelayCommand]
    private void Register()
    {
        RegisterMessage = string.Empty;
        IsRegisterSuccess = false;

        if (string.IsNullOrWhiteSpace(RegisterUsername) || string.IsNullOrWhiteSpace(RegisterPassword))
        {
            RegisterMessage = "All fields are required";
            return;
        }

        if (RegisterPassword != RegisterConfirmPassword)
        {
            RegisterMessage = "Passwords do not match";
            return;
        }

        var (success, message) = _auth.Register(RegisterUsername, RegisterPassword, RegisterEmail);
        RegisterMessage = message;
        IsRegisterSuccess = success;

        if (success)
        {
            var regUser = RegisterUsername;
            var regPass = RegisterPassword;

            RegisterUsername = string.Empty;
            RegisterPassword = string.Empty;
            RegisterConfirmPassword = string.Empty;
            RegisterEmail = string.Empty;

            ShowRegister = false;
            StartWaitingForApproval(regUser, regPass);
        }
    }

    private void StartWaitingForApproval(string username, string password)
    {
        PendingUsername = username;
        _pendingPassword = password;
        IsWaitingForApproval = true;
        ErrorMessage = string.Empty;
        HasError = false;

        _pollTimer?.Stop();
        _pollTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3.5)
        };
        _pollTimer.Tick += async (_, _) => await CheckApprovalInternalAsync();
        _pollTimer.Start();
    }

    [RelayCommand]
    private async Task CheckApprovalNowAsync()
    {
        await CheckApprovalInternalAsync();
    }

    [RelayCommand]
    private void CancelWaiting()
    {
        StopWaitingForApproval();
    }

    private async Task CheckApprovalInternalAsync()
    {
        if (!IsWaitingForApproval || string.IsNullOrWhiteSpace(PendingUsername)) return;

        try
        {
            var status = await _auth.CheckRemoteTelegramApprovalAsync(PendingUsername);
            if (status == NetworkDiscoveryTool.Services.Services.RemoteApprovalStatus.Approved)
            {
                StopWaitingForApproval();
                Username = PendingUsername;
                Password = _pendingPassword;
                await Login();
            }
            else if (status == NetworkDiscoveryTool.Services.Services.RemoteApprovalStatus.Rejected)
            {
                StopWaitingForApproval();
                ErrorMessage = "Registration request was rejected by Administrator.";
                HasError = true;
            }
        }
        catch { }
    }

    private void StopWaitingForApproval()
    {
        IsWaitingForApproval = false;
        _pollTimer?.Stop();
        _pollTimer = null;
    }

    private void SaveCredentials()
    {
    }
}
