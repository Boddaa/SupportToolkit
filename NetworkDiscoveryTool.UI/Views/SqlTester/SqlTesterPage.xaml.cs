using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using NetworkDiscoveryTool.UI.ViewModels;

namespace NetworkDiscoveryTool.UI.Views.SqlTester;

public partial class SqlTesterPage
{
    private readonly SqlTesterViewModel _vm;

    public SqlTesterPage(SqlTesterViewModel vm)
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            File.AppendAllText("crash.log", $"{DateTime.Now}: Page init error: {ex}\r\n");
            throw;
        }
        DataContext = vm;
        _vm = vm;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.InitializeCommand.Execute(null);

            if (string.IsNullOrWhiteSpace(_vm.ServerHost))
            {
                _vm.ServerHost = ".";
                _vm.Database = "master";
                _vm.Port = 1433;
                _vm.UseWindowsAuth = true;
            }
        }
        catch (Exception ex)
        {
            File.AppendAllText("crash.log", $"{DateTime.Now}: Page_Loaded error: {ex}\r\n");
        }
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.Password = PasswordBox.Password;
        }
        catch (Exception ex)
        {
            File.AppendAllText("crash.log", $"{DateTime.Now}: PasswordBox error: {ex}\r\n");
        }
    }
}
