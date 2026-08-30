using System;
using System.IO;
using System.Windows;
using NetworkDiscoveryTool.UI.Models;
using NetworkDiscoveryTool.UI.ViewModels;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDataFormats = System.Windows.DataFormats;

namespace NetworkDiscoveryTool.UI.Views.LogCollector;

public partial class LogCollectorPage
{
    private readonly LogCollectorViewModel _vm;
    private static readonly string[] LogExtensions = [".log", ".txt", ".csv", ".evtx", ".xml"];

    public LogCollectorPage(LogCollectorViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        _vm = vm;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_vm.AppLogPath))
            _vm.SetAppLogPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs"));
        if (string.IsNullOrWhiteSpace(_vm.IisLogPath))
            _vm.SetIisLogPath(@"C:\inetpub\logs\LogFiles");
        if (string.IsNullOrWhiteSpace(_vm.CustomFolderPath))
            _vm.SetCustomFolderPath(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
    }

    private void DropZone_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(WpfDataFormats.FileDrop))
        {
            e.Effects = WpfDragDropEffects.Copy;
            DropZone.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");
            DropZone.Background = (System.Windows.Media.Brush)FindResource("CardBgAltBrush");
        }
        else
        {
            e.Effects = WpfDragDropEffects.None;
        }
        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        DropZone.BorderBrush = (System.Windows.Media.Brush)FindResource("CardBorderBrush");
        DropZone.Background = (System.Windows.Media.Brush)FindResource("CardBgBrush");
        e.Handled = true;
    }

    private void DropZone_Drop(object sender, System.Windows.DragEventArgs e)
    {
        DropZone.BorderBrush = (System.Windows.Media.Brush)FindResource("CardBorderBrush");
        DropZone.Background = (System.Windows.Media.Brush)FindResource("CardBgBrush");

        if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(WpfDataFormats.FileDrop);
        if (paths is null || paths.Length == 0) return;

        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                    AddDirectory(path);
                else if (File.Exists(path))
                    AddFile(path);
            }
            catch { /* skip inaccessible */ }
        }

        if (_vm.PreviewFiles.Count > 0)
        {
            _vm.UpdatePreviewStats();
            _vm.RefreshTimeline();
        }
    }

    private void BrowseFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = "Log files (*.log;*.txt;*.csv)|*.log;*.txt;*.csv|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
                AddFile(file);

            if (_vm.PreviewFiles.Count > 0)
            {
                _vm.UpdatePreviewStats();
                _vm.RefreshTimeline();
            }
        }
    }

    private void AddDirectory(string dirPath)
    {
        foreach (var ext in LogExtensions)
        {
            foreach (var file in Directory.GetFiles(dirPath, "*" + ext, SearchOption.AllDirectories))
                AddFile(file);
        }
    }

    private void AddFile(string filePath)
    {
        try
        {
            var fi = new FileInfo(filePath);
            _vm.AddDroppedFile(new LogFileEntry
            {
                FileName = fi.Name,
                FullPath = fi.FullName,
                SizeBytes = fi.Length,
                LastModified = fi.LastWriteTime,
            });
        }
        catch { /* skip locked files */ }
    }
}
