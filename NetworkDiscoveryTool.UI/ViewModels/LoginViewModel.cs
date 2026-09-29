using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkDiscoveryTool.UI.Services;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly AuthService _auth;
    private readonly CurrentUserService _currentUser;
    private readonly ISettingsService _settings;

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

    // === Remembered Workstation Profile Properties ===
    [ObservableProperty]
    private bool _hasRememberedProfile;

    [ObservableProperty]
    private bool _showManualLogin;

    [ObservableProperty]
    private string _rememberedUsername = string.Empty;

    [ObservableProperty]
    private string _rememberedRole = string.Empty;

    [ObservableProperty]
    private string _rememberedWorkstation = string.Empty;

    [ObservableProperty]
    private string _rememberedAvatarLetter = "U";

    [ObservableProperty]
    private string _rememberedLastActive = string.Empty;

    public Action? OnLoginSuccess { get; set; }

    public LoginViewModel(AuthService auth, CurrentUserService currentUser, ISettingsService settings)
    {
        _auth = auth;
        _currentUser = currentUser;
        _settings = settings;

        _ = LoadRememberedProfileAsync();
    }

    [ObservableProperty] private bool _isWaitingForApproval;
    [ObservableProperty] private string _pendingUsername = string.Empty;
    private string _pendingPassword = string.Empty;
    private System.Windows.Threading.DispatcherTimer? _pollTimer;

    public async Task LoadRememberedProfileAsync()
    {
        try
        {
            var isActive = await _settings.GetAsync("Remembered_IsActive");
            if (isActive == "true")
            {
                var username = await _settings.GetAsync("Remembered_Username");
                var role = await _settings.GetAsync("Remembered_Role") ?? "User";
                var workstation = await _settings.GetAsync("Remembered_Workstation") ?? Environment.MachineName;
                var lastActive = await _settings.GetAsync("Remembered_LastActive");

                if (!string.IsNullOrWhiteSpace(username))
                {
                    if (_auth.ValidateRememberedUser(username, out var user, out _))
                    {
                        RememberedUsername = user!.Username;
                        RememberedRole = user.Role;
                        RememberedWorkstation = workstation;
                        RememberedAvatarLetter = string.IsNullOrWhiteSpace(user.Username)
                            ? "U"
                            : user.Username[0].ToString().ToUpperInvariant();

                        if (DateTime.TryParse(lastActive, out var dt))
                        {
                            RememberedLastActive = $"Previous session: {dt:MMM dd, yyyy h:mm tt}";
                        }
                        else
                        {
                            RememberedLastActive = "Workstation Profile Saved";
                        }

                        HasRememberedProfile = true;
                        ShowManualLogin = false;
                        RememberMe = true;
                        Username = username;
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to load remembered profile.");
        }

        HasRememberedProfile = false;
        ShowManualLogin = true;
    }

    [RelayCommand]
    private async Task QuickLoginAsync()
    {
        HasError = false;
        IsLoading = true;

        try
        {
            await Task.Delay(250);

            if (!_auth.ValidateRememberedUser(RememberedUsername, out var user, out var authError))
            {
                ErrorMessage = authError ?? "Workstation verification failed. Please enter credentials.";
                HasError = true;
                ShowManualLogin = true;
                return;
            }

            _currentUser.SetUser(user!.Id, user.Username, user.Role);

            // Update last active timestamp
            try
            {
                await _settings.SetAsync("Remembered_LastActive", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch { }

            // Ensure last theme is applied
            try
            {
                var theme = await _settings.GetAsync("Theme");
                if (!string.IsNullOrEmpty(theme))
                {
                    SettingsViewModel.ApplyTheme(theme);
                }
            }
            catch { }

            OnLoginSuccess?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unlock error: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SwitchAccount()
    {
        ShowManualLogin = true;
        ShowRegister = false;
        Password = string.Empty;
        HasError = false;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void BackToProfile()
    {
        if (HasRememberedProfile)
        {
            ShowManualLogin = false;
            ShowRegister = false;
            HasError = false;
            ErrorMessage = string.Empty;
        }
    }

    [RelayCommand]
    private async Task ForgetWorkstationAsync()
    {
        await ClearSavedCredentialsAsync();
        HasRememberedProfile = false;
        ShowManualLogin = true;
        RememberMe = false;
        Username = string.Empty;
        Password = string.Empty;
    }

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

            await Task.Delay(350);

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
            {
                await SaveCredentialsAsync(user);
            }
            else
            {
                await ClearSavedCredentialsAsync();
            }

            // Ensure last theme is applied
            try
            {
                var theme = await _settings.GetAsync("Theme");
                if (!string.IsNullOrEmpty(theme))
                {
                    SettingsViewModel.ApplyTheme(theme);
                }
            }
            catch { }

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

    private async Task SaveCredentialsAsync(AuthService.AppUser user)
    {
        try
        {
            await _settings.SetAsync("Remembered_IsActive", "true");
            await _settings.SetAsync("Remembered_Username", user.Username);
            await _settings.SetAsync("Remembered_UserId", user.Id.ToString());
            await _settings.SetAsync("Remembered_Role", user.Role);
            await _settings.SetAsync("Remembered_Workstation", Environment.MachineName);
            await _settings.SetAsync("Remembered_LastActive", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to save remembered workstation credentials.");
        }
    }

    private async Task ClearSavedCredentialsAsync()
    {
        try
        {
            await _settings.SetAsync("Remembered_IsActive", "false");
            await _settings.SetAsync("Remembered_Username", string.Empty);
            await _settings.SetAsync("Remembered_UserId", string.Empty);
            await _settings.SetAsync("Remembered_Role", string.Empty);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to clear remembered workstation credentials.");
        }
    }
}
