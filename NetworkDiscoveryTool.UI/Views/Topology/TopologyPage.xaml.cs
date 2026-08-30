using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NetworkDiscoveryTool.UI.ViewModels;

namespace NetworkDiscoveryTool.UI.Views.Topology;

public partial class TopologyPage : Page
{
    public TopologyPage(TopologyViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.LoadTopologyDataAsync();

        vm.RequestVisualExport = (filePath) =>
        {
            try
            {
                var originalTransform = TopologySurface.LayoutTransform;
                TopologySurface.LayoutTransform = null;
                TopologySurface.UpdateLayout();

                double renderWidth = vm.CanvasWidth > 0 ? vm.CanvasWidth : TopologySurface.ActualWidth;
                double renderHeight = vm.CanvasHeight > 0 ? vm.CanvasHeight : TopologySurface.ActualHeight;

                if (renderWidth <= 0) renderWidth = 1400;
                if (renderHeight <= 0) renderHeight = 900;

                var size = new System.Windows.Size(renderWidth, renderHeight);
                TopologySurface.Measure(size);
                TopologySurface.Arrange(new System.Windows.Rect(size));
                TopologySurface.UpdateLayout();

                var rtb = new RenderTargetBitmap((int)renderWidth, (int)renderHeight, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(TopologySurface);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));

                using (var stream = File.Create(filePath))
                {
                    encoder.Save(stream);
                }

                TopologySurface.LayoutTransform = originalTransform;
                TopologySurface.UpdateLayout();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ExportTopology PNG] Error: {ex}");
                return false;
            }
        };
    }
}