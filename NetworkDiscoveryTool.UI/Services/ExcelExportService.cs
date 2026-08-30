using ClosedXML.Excel;
using NetworkDiscoveryTool.Core.Models;

namespace NetworkDiscoveryTool.UI.Services;

public static class ExcelExportService
{
    public static async Task ExportScanAsync(List<Device> devices, string filePath)
    {
        await Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Scan Results");

            // Headers
            ws.Cell(1, 1).Value = "IP";
            ws.Cell(1, 2).Value = "Status";
            ws.Cell(1, 3).Value = "Latency (ms)";
            ws.Cell(1, 4).Value = "MAC Address";
            ws.Cell(1, 5).Value = "Hostname";
            ws.Cell(1, 6).Value = "Device Type";
            ws.Cell(1, 7).Value = "Vendor";
            ws.Cell(1, 8).Value = "Open Ports";

            var headerRange = ws.Range(1, 1, 1, 8);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(37, 99, 235);
            headerRange.Style.Font.FontColor = XLColor.White;

            // Data
            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                int row = i + 2;
                ws.Cell(row, 1).Value = d.IP;
                ws.Cell(row, 2).Value = d.Status;
                ws.Cell(row, 3).Value = d.LatencyMs;
                ws.Cell(row, 4).Value = d.MAC ?? "";
                ws.Cell(row, 5).Value = d.Hostname ?? "";
                ws.Cell(row, 6).Value = d.DeviceType ?? "";
                ws.Cell(row, 7).Value = d.Vendor ?? "";
                ws.Cell(row, 8).Value = string.Join(", ", d.Ports?.Where(p => p.State == "Open").Select(p => $"{p.PortNumber}/{p.Service}") ?? []);
            }

            // Summary sheet
            var summary = workbook.Worksheets.Add("Summary");
            summary.Cell(1, 1).Value = "Metric";
            summary.Cell(1, 2).Value = "Value";
            summary.Range(1, 1, 1, 2).Style.Font.Bold = true;
            summary.Cell(2, 1).Value = "Total Devices";
            summary.Cell(2, 2).Value = devices.Count;
            summary.Cell(3, 1).Value = "Online";
            summary.Cell(3, 2).Value = devices.Count(d => d.Status == "Online");
            summary.Cell(4, 1).Value = "Offline";
            summary.Cell(4, 2).Value = devices.Count(d => d.Status == "Offline");
            summary.Cell(5, 1).Value = "Export Date";
            summary.Cell(5, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            ws.Columns().AdjustToContents();
            summary.Columns().AdjustToContents();

            workbook.SaveAs(filePath);
        });
    }
}
