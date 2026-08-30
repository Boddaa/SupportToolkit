using System.Windows.Media.Animation;
using NetworkDiscoveryTool.UI.ViewModels;

namespace NetworkDiscoveryTool.UI.Views.Settings;

public partial class SettingsPage
{
    private readonly SettingsViewModel _vm;

    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        _vm = vm;
        SqlPasswordBox.PasswordChanged += (s, e) => _vm.SqlPassword = SqlPasswordBox.Password;
    }

    private async void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            _vm.LoadAppInfo();
            await _vm.LoadSettingsAsync();
            _vm.LoadPendingUsers();
            _vm.LoadAllUsers();
            _vm.CompleteInitialization();

            if (SqlPasswordBox != null && !string.IsNullOrEmpty(_vm.SqlPassword))
            {
                SqlPasswordBox.Password = _vm.SqlPassword;
            }

            await System.Windows.Threading.Dispatcher.Yield(
                System.Windows.Threading.DispatcherPriority.Background);

            AnimateCards();
        }
        catch
        {
            // Suppress any page load exception safely
        }
    }

    private void AnimateCards()
    {
        var cards = new[]
        {
            AppearanceCard, NotificationsCard, DatabaseCard, LogsCard,
            UpdatesCard, AboutCard, UserManagementCard, SaveChangesCard
        };

        var delay = TimeSpan.Zero;
        var step = TimeSpan.FromSeconds(0.08);
        var duration = TimeSpan.FromSeconds(0.35);

        foreach (var card in cards)
        {
            if (card is null) continue;
            if (card.Visibility != System.Windows.Visibility.Visible) continue;

            card.Opacity = 0;
            var anim = new DoubleAnimation(0, 1, duration)
            {
                BeginTime = delay,
                FillBehavior = FillBehavior.HoldEnd
            };
            card.BeginAnimation(System.Windows.UIElement.OpacityProperty, anim);
            delay += step;
        }
    }

    private void TextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {

    }

    private void ComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {

    }
}