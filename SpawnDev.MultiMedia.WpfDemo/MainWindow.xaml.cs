using System.Windows;

namespace SpawnDev.MultiMedia.WpfDemo
{
    public partial class MainWindow : Window
    {
        private IMediaStream? _stream;
        private WpfVideoRenderer? _renderer;
        private int _frameCount;
        private DateTime _fpsStart;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void StartCamera_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "Starting camera...";
                StatusText.Visibility = Visibility.Visible;

                _stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Video = true });
                var tracks = _stream.GetVideoTracks();
                if (tracks.Length == 0)
                {
                    StatusText.Text = "No video tracks returned.";
                    return;
                }

                var track = tracks[0];
                var settings = track.GetSettings();

                if (track is IVideoTrack videoTrack)
                {
                    _renderer = new WpfVideoRenderer();
                    _renderer.OnFrameRendered += OnFrameRendered;
                    _renderer.Attach(videoTrack);

                    // Bind the WriteableBitmap to the Image control
                    // It will be created on first frame
                    _frameCount = 0;
                    _fpsStart = DateTime.UtcNow;

                    StatusText.Text = $"Capturing: {track.Label}\n{settings.Width}x{settings.Height} @ {settings.FrameRate:F0}fps ({settings.PixelFormat})";
                }
                else
                {
                    StatusText.Text = $"Camera found but no frame capture support: {track.Label}";
                }

                StartCameraButton.IsEnabled = false;
                StopCameraButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void OnFrameRendered()
        {
            // Update the Image source from the renderer's bitmap
            if (_renderer?.Bitmap != null && CameraPreview.Source != _renderer.Bitmap)
            {
                CameraPreview.Source = _renderer.Bitmap;
                StatusText.Visibility = Visibility.Collapsed;
            }

            // FPS counter
            _frameCount++;
            var elapsed = (DateTime.UtcNow - _fpsStart).TotalSeconds;
            if (elapsed >= 2.0)
            {
                var fps = _frameCount / elapsed;
                Title = $"SpawnDev.MultiMedia - {fps:F0} FPS";
                _frameCount = 0;
                _fpsStart = DateTime.UtcNow;
            }
        }

        private void StopCamera_Click(object sender, RoutedEventArgs e)
        {
            _renderer?.Dispose();
            _renderer = null;

            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }

            CameraPreview.Source = null;
            StatusText.Text = "Camera stopped.";
            StatusText.Visibility = Visibility.Visible;
            Title = "SpawnDev.MultiMedia - Camera Preview";
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
                    StatusText.Text = "No devices found.";
                }
                else
                {
                    var lines = devices.Select(d => $"[{d.Kind}] {d.Label}");
                    StatusText.Text = $"Found {devices.Length} device(s):\n" + string.Join("\n", lines);
                    StatusText.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }
    }
}
