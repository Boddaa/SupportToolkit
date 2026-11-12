using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace NetworkDiscoveryTool.Services.Services;

public sealed class AppUpdateService
{
    public const string CurrentVersion = "2.5.0";

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    public sealed record UpdateCheckResult(
        bool HasUpdate,
        string LatestVersion,
        string CurrentVersion,
        string DownloadUrl,
        string ReleaseNotes,
        bool IsMandatory,
        long FileSize,
        string PublishedAt
    );

    public async Task<UpdateCheckResult?> CheckForUpdateAsync(string serverBaseUrl)
    {
        try
        {
            var cleanUrl = serverBaseUrl.TrimEnd('/');
            var url = $"{cleanUrl}/api/app/check-update?currentVersion={CurrentVersion}";

            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
            var response = await HttpClient.GetAsync(url, cts.Token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cts.Token).ConfigureAwait(false);

                return new UpdateCheckResult(
                    HasUpdate: doc.TryGetProperty("hasUpdate", out var hu) && hu.ValueKind == JsonValueKind.True,
                    LatestVersion: doc.TryGetProperty("latestVersion", out var lv) && lv.ValueKind == JsonValueKind.String ? lv.GetString() ?? CurrentVersion : CurrentVersion,
                    CurrentVersion: CurrentVersion,
                    DownloadUrl: doc.TryGetProperty("downloadUrl", out var du) && du.ValueKind == JsonValueKind.String ? du.GetString() ?? "" : "",
                    ReleaseNotes: doc.TryGetProperty("releaseNotes", out var rn) && rn.ValueKind == JsonValueKind.String ? rn.GetString() ?? "" : "",
                    IsMandatory: doc.TryGetProperty("isMandatory", out var im) && im.ValueKind == JsonValueKind.True,
                    FileSize: doc.TryGetProperty("fileSize", out var fs) && fs.ValueKind == JsonValueKind.Number && fs.TryGetInt64(out var s) ? s : 0,
                    PublishedAt: doc.TryGetProperty("publishedAt", out var pa) && pa.ValueKind == JsonValueKind.String ? pa.GetString() ?? "" : ""
                );
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to check for updates: {ex.Message}");
        }

        return null;
    }

    public async Task DownloadAndApplyUpdateAsync(string downloadUrl, IProgress<double>? progress = null)
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
        {
            throw new InvalidOperationException("Could not determine current executable path.");
        }

        var tempDir = Path.GetTempPath();
        var tempNewExe = Path.Combine(tempDir, $"SupportToolKit_update_{Guid.NewGuid():N}.exe");

        // 1. Download file with progress tracking
        using (var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            await using var fileStream = new FileStream(tempNewExe, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(false);
                totalRead += bytesRead;

                if (totalBytes > 0 && progress != null)
                {
                    progress.Report((double)totalRead / totalBytes * 100.0);
                }
            }
        }

        // Verify file was written
        var downloadedInfo = new FileInfo(tempNewExe);
        if (!downloadedInfo.Exists || downloadedInfo.Length < 100_000)
        {
            throw new InvalidOperationException("Downloaded file appears corrupt or incomplete.");
        }

        // 2. Generate and trigger silent replacement script
        var currentPid = Environment.ProcessId;
        var updaterScript = Path.Combine(tempDir, $"stk_updater_{Guid.NewGuid():N}.bat");

        var scriptContent = $@"@echo off
set OLD_PID={currentPid}
set TARGET=""{currentExe}""
set NEW=""{tempNewExe}""

:: 1. Wait for parent process to exit
:loop
tasklist /fi ""pid eq %OLD_PID%"" 2>nul | find ""%OLD_PID%"" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto loop
)

:: 2. Overwrite target executable
timeout /t 1 /nobreak >nul
copy /y %NEW% %TARGET% >nul
if errorlevel 1 (
    timeout /t 2 /nobreak >nul
    copy /y %NEW% %TARGET% >nul
)

:: 3. Delete downloaded temporary file
del /f /q %NEW% >nul 2>nul

:: 4. Relaunch updated application
start """" %TARGET%

:: 5. Self delete this updater script
(goto) 2>nul & del ""%~f0""
";

        await File.WriteAllTextAsync(updaterScript, scriptContent).ConfigureAwait(false);

        // 3. Launch the updater script in detached hidden mode
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{updaterScript}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process.Start(startInfo);

        // 4. Clean exit
        System.Diagnostics.Debug.WriteLine("Update launched successfully. Exiting application to apply update.");
        await Task.Delay(200).ConfigureAwait(false);
        Environment.Exit(0);
    }
}
