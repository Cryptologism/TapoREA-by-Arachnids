using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace TapoDesktop.Services;

/// <summary>
/// Separate FFmpeg process records the same RTSP stream to Matroska without re-encoding.
/// FFmpeg executable is supplied by the end user and not bundled with the starter project.
/// </summary>
public sealed class FfmpegRecorder
{
    private Process? _process;

    public static string RecordingsFolder
    {
        get
        {
            string baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrWhiteSpace(baseFolder))
                baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(baseFolder, "TapoDesktop", "Recordings");
        }
    }

    public bool IsRecording => _process is { HasExited: false };

    public string Start(Uri rtspStream)
    {
        if (IsRecording) throw new InvalidOperationException("A recording is already in progress.");
        _process?.Dispose();
        _process = null;
        Directory.CreateDirectory(RecordingsFolder);
        string file = Path.Combine(RecordingsFolder, $"TC70_{DateTime.Now:yyyyMMdd_HHmmss}.mkv");
        string localFfmpeg = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        string binary = File.Exists(localFfmpeg) ? localFfmpeg : "ffmpeg.exe";
        var psi = new ProcessStartInfo(binary)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true
        };
        // Use ArgumentList, not shell interpolation. User-supplied strings are not executed by a shell.
        foreach (string value in new[]
        {
            "-hide_banner", "-loglevel", "error", "-y", "-rtsp_transport", "tcp",
            "-i", rtspStream.AbsoluteUri,
            "-map", "0:v:0", "-map", "0:a?", "-c", "copy", "-f", "matroska", file
        }) psi.ArgumentList.Add(value);
        try
        {
            _process = Process.Start(psi) ?? throw new InvalidOperationException("FFmpeg did not start.");
            // Drain stderr to prevent subprocess deadlock. Never persist/print it: it can contain a camera URL.
            _process.ErrorDataReceived += (_, _) => { };
            _process.BeginErrorReadLine();
            return file;
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("ffmpeg.exe was not found. Put it beside TapoDesktop.exe or add it to PATH.", ex);
        }
    }

    public async Task StopAsync()
    {
        var proc = _process;
        if (proc is null) return;
        try
        {
            if (!proc.HasExited)
            {
                try
                {
                    await proc.StandardInput.WriteLineAsync("q");
                    await proc.StandardInput.FlushAsync();
                }
                catch (IOException) { /* already exited */ }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                try { await proc.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    if (!proc.HasExited) proc.Kill(entireProcessTree: true);
                    await proc.WaitForExitAsync();
                }
            }
        }
        finally
        {
            proc.Dispose();
            _process = null;
        }
    }
}
