using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using NetworkDiscoveryTool.UI.Services;
using NetworkDiscoveryTool.UI.ViewModels;

namespace NetworkDiscoveryTool.UI.Views.Login;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _vm = viewModel;

        viewModel.OnLoginSuccess = () =>
        {
            try
            {
                var currentUser = App.ServiceProvider.GetRequiredService<CurrentUserService>();

                if (Owner is MainWindow existingMain)
                {
                    existingMain.RestoreSession();
                    existingMain.Show();
                    DialogResult = true;
                }
                else
                {
                    var mainWindow = App.ServiceProvider.GetRequiredService<MainWindow>();
                    App.Current.MainWindow = mainWindow;
                    mainWindow.Show();
                }
                Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open main window: {ex.Message}\n\n{ex.InnerException?.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        Closed += (_, _) =>
        {
            if (Owner is MainWindow { IsVisible: false })
                System.Windows.Application.Current.Shutdown();
        };

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var fadeIn = (Storyboard)FindResource("CardFadeIn");
        fadeIn.Begin();

        var logoPulse = (Storyboard)FindResource("LogoPulse");
        logoPulse.Begin();

        var float1 = (Storyboard)FindResource("FloatBlob1");
        float1.Begin();
        var float2 = (Storyboard)FindResource("FloatBlob2");
        float2.Begin();
        var float3 = (Storyboard)FindResource("FloatBlob3");
        float3.Begin();

        await _vm.LoadRememberedProfileAsync();

        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LoginViewModel.HasError) && _vm.HasError)
            {
                var shake = (Storyboard)FindResource("ErrorShake");
                ErrorBorder.BeginStoryboard(shake);
            }
            if (args.PropertyName == nameof(LoginViewModel.IsLoading))
            {
                BtnText.Visibility = _vm.IsLoading ? Visibility.Collapsed : Visibility.Visible;
            }
        };

        await Dispatcher.InvokeAsync(() =>
        {
            if (!_vm.HasRememberedProfile || _vm.ShowManualLogin)
            {
                UsernameBox.Focus();
            }
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _vm.Password = PasswordBox.Password;
    }

    private void PasswordRevealBox_TextChanged(object sender, RoutedEventArgs e)
    {
        if (_vm.ShowPassword)
            _vm.Password = PasswordRevealBox.Text;
    }

    private void RevealBtn_Click(object sender, RoutedEventArgs e)
    {
        _vm.ShowPassword = !_vm.ShowPassword;

        if (_vm.ShowPassword)
        {
            PasswordRevealBox.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordRevealBox.Visibility = Visibility.Visible;
            PasswordRevealBox.Focus();
            PasswordRevealBox.CaretIndex = PasswordRevealBox.Text.Length;
        }
        else
        {
            PasswordBox.Password = PasswordRevealBox.Text;
            PasswordBox.Visibility = Visibility.Visible;
            PasswordRevealBox.Visibility = Visibility.Collapsed;
            PasswordBox.Focus();
        }
    }

    private void RegisterPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _vm.RegisterPassword = RegisterPasswordBox.Password;
    }

    private void RegisterConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _vm.RegisterConfirmPassword = RegisterConfirmPasswordBox.Password;
    }

    private void RememberMe_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _vm.RememberMe = !_vm.RememberMe;
        CheckMark.Visibility = _vm.RememberMe ? Visibility.Visible : Visibility.Collapsed;
    }

    private void WindowDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch { }
        }
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}