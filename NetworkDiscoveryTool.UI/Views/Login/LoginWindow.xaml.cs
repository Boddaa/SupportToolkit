using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using NetworkDiscoveryTool.UI.Services;
using NetworkDiscoveryTool.UI.ViewModels;

namespace NetworkDiscoveryTool.UI.Views.Login;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;
    private bool _loginSucceeded = false;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _vm = viewModel;

        viewModel.OnLoginSuccess = () =>
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    _loginSucceeded = true;
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
                    _loginSucceeded = false;
                    System.Windows.MessageBox.Show($"Failed to open main window: {ex.Message}\n\n{ex.InnerException?.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        };

        Closed += (_, _) =>
        {
            if (!_loginSucceeded)
            {
                if (System.Windows.Application.Current.MainWindow == null ||
                    !System.Windows.Application.Current.MainWindow.IsVisible ||
                    System.Windows.Application.Current.MainWindow == this)
                {
                    try { System.Windows.Application.Current.Shutdown(); } catch { }
                    Environment.Exit(0);
                }
            }
        };

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var fadeIn = (Storyboard)FindResource("CardFadeIn");
        fadeIn.Begin();

        await Dispatcher.InvokeAsync(() =>
        {
            if (!_vm.HasRememberedProfile || _vm.ShowManualLogin)
            {
                UsernameBox.Focus();
            }
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void WindowDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_vm.ShowPassword)
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
        }
        else
        {
            PasswordBox.Password = PasswordRevealBox.Text;
            PasswordRevealBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            PasswordBox.Focus();
        }
    }

    private void RememberMe_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _vm.RememberMe = !_vm.RememberMe;
    }

    private void RegisterPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            _vm.RegisterPassword = pb.Password;
    }

    private void RegisterConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            _vm.RegisterConfirmPassword = pb.Password;
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
