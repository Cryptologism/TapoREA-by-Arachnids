using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using LibVLCSharp.Shared;
using TapoDesktop.Services;

namespace TapoDesktop;

public partial class MainWindow : Window
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;
    private readonly FfmpegRecorder _recorder = new();
    private readonly SemaphoreSlim _ptzGate = new(1, 1);
    private OnvifPtzClient? _ptz;
    private Uri? _streamUri;
    private bool _closePrepared;

    public MainWindow()
    {
        InitializeComponent();
        // Native libvlc files are brought in through VideoLAN.LibVLC.Windows NuGet.
        Core.Initialize();
        _libVlc = new LibVLC("--no-video-title-show");
        _player = new MediaPlayer(_libVlc);
        VideoView.Loaded += (_, _) => VideoView.MediaPlayer = _player;
        _player.Playing += (_, _) => Dispatcher.BeginInvoke(new Action(() => Status("Video stream opened.")));
        _player.EncounteredError += (_, _) => Dispatcher.BeginInvoke(new Action(() => Status("Video playback failed. Verify the camera IP, Camera Account, and stream with VLC.")));
    }

    private void Status(string message) => StatusText.Text = message;

    private bool TryCameraSettings(out string address, out string user, out string password)
    {
        address = IpText.Text.Trim();
        user = UserText.Text.Trim();
        password = PasswordInput.Password;
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            Status("Enter the camera IPv4 address, for example 192.168.1.50.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
        {
            Status("Enter your Tapo Camera Account credentials (not your Tapo cloud login).");
            return false;
        }
        return true;
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_recorder.IsRecording)
        {
            Status("Stop the current recording before changing the camera connection.");
            return;
        }
        if (!TryCameraSettings(out var host, out var user, out var password)) return;
        try
        {
            _player.Stop();
            _ptz?.Dispose();
            _ptz = null;
            string path = (StreamPicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "stream1";
            // Escape account credentials because ':' and '@' can appear in passwords.
            _streamUri = new Uri($"rtsp://{Uri.EscapeDataString(user)}:{Uri.EscapeDataString(password)}@{host}:554/{path}");
            using var media = new Media(_libVlc, _streamUri);
            media.AddOption(":rtsp-tcp");
            media.AddOption(":network-caching=400");
            bool accepted = _player.Play(media);
            Status(accepted ? "Connecting to RTSP video... (this may take a few seconds)" : "LibVLC could not start playback.");
        }
        catch (Exception ex)
        {
            // Exception messages are deliberately not shown: native errors may contain stream URLs/passwords.
            Status($"Connection failed ({ex.GetType().Name}). Check the IP address and permissions.");
        }
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_recorder.IsRecording)
        {
            Status("Stop recording before disconnecting.");
            return;
        }
        _player.Stop();
        _ptz?.Dispose();
        _ptz = null;
        _streamUri = null;
        Status("Disconnected.");
    }

    private async void PtzInitialize_Click(object sender, RoutedEventArgs e)
    {
        if (!TryCameraSettings(out var host, out var user, out var password)) return;
        _ptz?.Dispose();
        _ptz = new OnvifPtzClient(host, 2020, user, password);
        try
        {
            Status("Connecting to ONVIF and finding a PTZ-enabled profile...");
            await _ptz.InitializeAsync();
            Status("ONVIF PTZ ready. Use arrow buttons for short moves.");
        }
        catch (Exception ex)
        {
            _ptz.Dispose();
            _ptz = null;
            Status($"ONVIF setup failed: {ex.Message}");
        }
    }

    private async void PtzMove_Click(object sender, RoutedEventArgs e)
    {
        if (_ptz is null)
        {
            Status("Initialize ONVIF PTZ first.");
            return;
        }
        if (!await _ptzGate.WaitAsync(0)) return;
        try
        {
            string direction = (sender as Button)?.Tag?.ToString() ?? "";
            (double x, double y) = direction switch
            {
                "up" => (0.0, 0.25),
                "down" => (0.0, -0.25),
                "left" => (-0.25, 0.0),
                "right" => (0.25, 0.0),
                _ => (0.0, 0.0)
            };
            if (x == 0 && y == 0) return;
            await _ptz.PulseMoveAsync(x, y, TimeSpan.FromMilliseconds(350));
            Status($"PTZ move: {direction}.");
        }
        catch (Exception ex)
        {
            Status($"PTZ move failed: {ex.Message}");
        }
        finally
        {
            _ptzGate.Release();
        }
    }

    private async void PtzStop_Click(object sender, RoutedEventArgs e)
    {
        if (_ptz is null) return;
        try { await _ptz.StopAsync(); Status("PTZ stopped."); }
        catch (Exception ex) { Status($"PTZ stop failed: {ex.Message}"); }
    }

    private void RecordStart_Click(object sender, RoutedEventArgs e)
    {
        if (_streamUri is null)
        {
            Status("Connect to a camera before recording.");
            return;
        }
        try
        {
            string path = _recorder.Start(_streamUri);
            Status($"Recording started: {Path.GetFileName(path)}. Use Stop recording to finalize it.");
        }
        catch (Exception ex)
        {
            Status($"Cannot start recorder: {ex.Message}");
        }
    }

    private async void RecordStop_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _recorder.StopAsync();
            Status("Recording stopped and file finalized. Open the recording folder to view it.");
        }
        catch (Exception ex)
        {
            Status($"Recording shutdown error: {ex.Message}");
        }
    }

    private void OpenRecordings_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(FfmpegRecorder.RecordingsFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", FfmpegRecorder.RecordingsFolder) { UseShellExecute = true });
    }

    private async void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!_closePrepared)
        {
            e.Cancel = true;
            try { await _recorder.StopAsync(); }
            catch { /* best effort cleanup */ }
            _closePrepared = true;
            Close();
            return;
        }
        _ptz?.Dispose();
        _player.Stop();
        VideoView.MediaPlayer = null;
        VideoView.Dispose();
        _player.Dispose();
        _libVlc.Dispose();
        _ptzGate.Dispose();
    }
}
