using System.Windows;

namespace SpawnDev.MultiMedia.WpfDemo
{
    public partial class MainWindow : Window
    {
        private IMediaStream? _stream;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void StartCamera_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "Starting camera...";
                _stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Video = true });
                var tracks = _stream.GetVideoTracks();
                if (tracks.Length > 0)
                {
                    var settings = tracks[0].GetSettings();
                    StatusText.Text = $"Camera active: {settings.Width}x{settings.Height} @ {settings.FrameRate}fps\nLabel: {tracks[0].Label}";
                }
                StartCameraButton.IsEnabled = false;
                StopCameraButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void StopCamera_Click(object sender, RoutedEventArgs e)
        {
            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }
            StatusText.Text = "Camera stopped.";
            StartCameraButton.IsEnabled = true;
            StopCameraButton.IsEnabled = false;
        }

        private async void EnumerateDevices_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var devices = await MediaDevices.EnumerateDevices();
                if (devices.Length == 0)
                {
                    StatusText.Text = "No devices found (device enumeration not yet implemented for Windows).";
                }
                else
                {
                    var lines = devices.Select(d => $"[{d.Kind}] {d.Label} ({d.DeviceId})");
                    StatusText.Text = $"Found {devices.Length} device(s):\n" + string.Join("\n", lines);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }
    }
}
