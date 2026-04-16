using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpawnDev.MultiMedia.WpfDemo
{
    /// <summary>
    /// WPF video renderer that converts VideoFrame data to WriteableBitmap.
    /// Bind the Bitmap property to a WPF Image.Source for live camera preview.
    /// Handles pixel format conversion (NV12/I420/YUY2 -> BGRA) automatically.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WpfVideoRenderer : IVideoRenderer
    {
        private IVideoTrack? _track;
        private WriteableBitmap? _bitmap;
        private bool _disposed;

        /// <summary>
        /// The WriteableBitmap that renders video frames.
        /// Bind this to a WPF Image.Source control.
        /// </summary>
        public WriteableBitmap? Bitmap => _bitmap;

        public bool IsAttached => _track != null;

        /// <summary>
        /// Fired on the UI thread after each frame is rendered to the bitmap.
        /// Useful for updating UI elements that depend on the frame.
        /// </summary>
        public event Action? OnFrameRendered;

        public void Attach(IVideoTrack track)
        {
            Detach();
            _track = track;
            _track.OnFrame += HandleFrame;
        }

        public void Detach()
        {
            if (_track != null)
            {
                _track.OnFrame -= HandleFrame;
                _track = null;
            }
        }

        private void HandleFrame(VideoFrame frame)
        {
            if (_disposed || frame.Width <= 0 || frame.Height <= 0) return;

            // Convert to BGRA if needed (WriteableBitmap requires BGRA/PBGRA)
            VideoFrame bgraFrame;
            if (frame.Format == VideoPixelFormat.BGRA)
                bgraFrame = frame;
            else
                bgraFrame = PixelFormatConverter.Convert(frame, VideoPixelFormat.BGRA);

            // Must update the bitmap on the UI thread
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                try
                {
                    if (_disposed) return;

                    // Create or resize bitmap if dimensions changed
                    if (_bitmap == null || _bitmap.PixelWidth != bgraFrame.Width || _bitmap.PixelHeight != bgraFrame.Height)
                    {
                        _bitmap = new WriteableBitmap(
                            bgraFrame.Width, bgraFrame.Height,
                            96, 96, PixelFormats.Bgra32, null);
                    }

                    // Write pixel data to the bitmap
                    _bitmap.Lock();
                    try
                    {
                        var data = bgraFrame.Data.Span;
                        int stride = bgraFrame.Width * 4;
                        unsafe
                        {
                            fixed (byte* src = data)
                            {
                                Buffer.MemoryCopy(src, _bitmap.BackBuffer.ToPointer(),
                                    _bitmap.BackBufferStride * _bitmap.PixelHeight,
                                    data.Length);
                            }
                        }
                        _bitmap.AddDirtyRect(new Int32Rect(0, 0, bgraFrame.Width, bgraFrame.Height));
                    }
                    finally
                    {
                        _bitmap.Unlock();
                    }

                    OnFrameRendered?.Invoke();
                }
                catch
                {
                    // Frame rendering failed - skip this frame
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Detach();
        }
    }
}
