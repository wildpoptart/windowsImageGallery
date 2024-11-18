using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using NAudio.Wave;

namespace FastImageGallery
{
    public class FFmpegPlayer : Image, IDisposable
    {
        private Process? _ffmpegProcess;
        private CancellationTokenSource? _playbackCancellation;
        private WriteableBitmap? _writeableBitmap;
        private Task? _playbackTask;
        private bool _isPlaying;
        private double _volume = 1.0;
        private TimeSpan _position = TimeSpan.Zero;
        private TimeSpan _duration = TimeSpan.Zero;
        private int _width;
        private int _height;
        private Stream? _ffmpegInput;
        private double _frameRate = 30.0; // Default framerate
        private TimeSpan _frameDelay;

        private static readonly string FFmpegPath = Path.GetFullPath(
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "ffmpeg"
            )
        );

        private readonly object _lockObject = new object();
        private bool _isDisposed;

        private Process? _audioProcess;
        private WaveOut? _waveOut;
        private bool _isAudioStarted = false;

        public event EventHandler? MediaOpened;
        public event EventHandler? MediaEnded;
        public event EventHandler? PlaybackEnded;

        public bool IsPlaying => _isPlaying;
        public TimeSpan Position { get => _position; set => SeekTo(value); }
        public TimeSpan Duration => _duration;
        public double Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0, 1);
                if (_waveOut != null)
                {
                    _waveOut.Volume = (float)_volume;
                }
            }
        }

        public MediaState LoadedBehavior { get; set; } = MediaState.Manual;
        public MediaState UnloadedBehavior { get; set; } = MediaState.Stop;
        public Duration NaturalDuration => new Duration(Duration);

        public FFmpegPlayer()
        {
            _width = 1920;
            _height = 1080;
            Logger.Log($"FFmpeg path: {FFmpegPath}");
        }

        public async Task LoadVideoAsync(string filePath)
        {
            try
            {
                lock (_lockObject)
                {
                    if (_isDisposed) return;
                    
                    _playbackCancellation?.Cancel();
                    _playbackCancellation?.Dispose();
                    _playbackCancellation = new CancellationTokenSource();
                }

                await Task.Run(() => 
                {
                    CleanupFFmpeg();

                    var ffprobePath = Path.Combine(FFmpegPath, "ffprobe.exe");
                    var ffmpegExePath = Path.Combine(FFmpegPath, "ffmpeg.exe");

                    Logger.Log($"Using ffprobe: {ffprobePath}");
                    Logger.Log($"Using ffmpeg: {ffmpegExePath}");

                    // Get video info using ffprobe
                    var infoProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = ffprobePath,
                            Arguments = $"-v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate -of csv=s=x:p=0 \"{filePath}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };

                    infoProcess.Start();
                    var info = infoProcess.StandardOutput.ReadToEnd().Trim().Split('x');
                    infoProcess.WaitForExit();

                    if (info.Length >= 3 && 
                        int.TryParse(info[0], out int videoWidth) && 
                        int.TryParse(info[1], out int videoHeight))
                    {
                        _width = videoWidth;
                        _height = videoHeight;

                        // Parse frame rate (comes in form "num/den")
                        var fpsStr = info[2].Split('/');
                        if (fpsStr.Length == 2 && 
                            int.TryParse(fpsStr[0], out int num) && 
                            int.TryParse(fpsStr[1], out int den))
                        {
                            _frameRate = (double)num / den;
                            _frameDelay = TimeSpan.FromSeconds(1.0 / _frameRate);
                            Logger.Log($"Video info: {_width}x{_height} @ {_frameRate:F2} fps");
                        }
                    }

                    // FFmpeg input arguments for raw video with original quality
                    var inputArgs = $"-i \"{filePath}\" -f rawvideo -pix_fmt bgra -s {_width}x{_height} " +
                                  $"-vsync 0 -copyts -vf \"scale={_width}:{_height}:flags=bicubic\" " +
                                  $"-sws_flags bicubic -";

                    _ffmpegProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = ffmpegExePath,
                            Arguments = inputArgs,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        },
                        EnableRaisingEvents = true
                    };

                    _ffmpegProcess.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            Logger.Log($"FFmpeg: {e.Data}");
                    };

                    _ffmpegProcess.Start();
                    _ffmpegProcess.BeginErrorReadLine();

                    _ffmpegInput = _ffmpegProcess.StandardOutput.BaseStream;
                });

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_isDisposed) return;

                    _writeableBitmap = new WriteableBitmap(
                        _width,
                        _height,
                        96,
                        96,
                        PixelFormats.Bgra32,
                        null
                    );
                    Source = _writeableBitmap;
                    MediaOpened?.Invoke(this, EventArgs.Empty);
                });

                // Read and display the first frame immediately
                await ReadAndDisplayFrame();

                // Start a separate FFmpeg process for audio with proper sync parameters
                var audioArgs = $"-i \"{filePath}\" -vn -f wav -ar 44100 -ac 2 " +
                               $"-af aresample=async=1:min_hard_comp=0.100000:first_pts=0 " +
                               $"-acodec pcm_s16le -threads 0 -";
                
                _audioProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = Path.Combine(FFmpegPath, "ffmpeg.exe"),
                        Arguments = audioArgs,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    },
                    EnableRaisingEvents = true
                };

                _audioProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Logger.Log($"FFmpeg Audio: {e.Data}");
                };

                // Start the process first
                _audioProcess.Start();
                
                // Then set the priority
                _audioProcess.PriorityClass = ProcessPriorityClass.AboveNormal;
                
                _audioProcess.BeginErrorReadLine();

                // Don't start the audio process yet
                _isAudioStarted = false;
            }
            catch (Exception ex)
            {
                Logger.LogError("Error initializing FFmpeg", ex);
                CleanupFFmpeg();
                throw;
            }
        }

        private async Task<bool> ReadAndDisplayFrame()
        {
            var frameSize = _width * _height * 4;
            var buffer = new byte[frameSize];
            var readBuffer = new byte[32768];
            var currentPosition = 0;

            while (currentPosition < frameSize)
            {
                var bytesRead = await _ffmpegInput.ReadAsync(
                    readBuffer, 
                    0, 
                    Math.Min(readBuffer.Length, frameSize - currentPosition));

                if (bytesRead == 0)
                {
                    return false;
                }

                Buffer.BlockCopy(readBuffer, 0, buffer, currentPosition, bytesRead);
                currentPosition += bytesRead;
            }

            if (currentPosition >= frameSize)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        _writeableBitmap?.Lock();
                        
                        unsafe
                        {
                            fixed (byte* sourcePtr = buffer)
                            {
                                Buffer.MemoryCopy(
                                    sourcePtr,
                                    (void*)_writeableBitmap.BackBuffer,
                                    frameSize,
                                    frameSize);
                            }
                        }

                        _writeableBitmap?.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
                    }
                    finally
                    {
                        _writeableBitmap?.Unlock();
                    }
                }, DispatcherPriority.Render);

                return true;
            }

            return false;
        }

        public void Play()
        {
            if (_isPlaying) return;
            
            try
            {
                Logger.Log("Starting playback");
                _isPlaying = true;

                // Start audio if not already started
                if (!_isAudioStarted && _audioProcess != null)
                {
                    // Don't start the process again since it's already started in LoadVideoAsync
                    var waveStream = new FFmpegWaveStream(_audioProcess.StandardOutput.BaseStream);
                    _waveOut = new WaveOut
                    {
                        DesiredLatency = 100,  // Lower latency
                        NumberOfBuffers = 2,   // Fewer buffers for lower latency
                    };
                    _waveOut.Init(waveStream);
                    _waveOut.Volume = (float)_volume;
                    _waveOut.Play();
                    _isAudioStarted = true;
                }
                else if (_waveOut != null)
                {
                    _waveOut.Play();
                }

                _playbackCancellation = new CancellationTokenSource();
                _playbackTask = Task.Run(PlaybackLoop);
            }
            catch (Exception ex)
            {
                Logger.LogError("Error starting playback", ex);
                _isPlaying = false;
                throw;
            }
        }

        public void Pause()
        {
            if (!_isPlaying) return;
            
            Logger.Log("Pausing playback");
            _isPlaying = false;
            _waveOut?.Pause();
            _playbackCancellation?.Cancel();
            try
            {
                _playbackTask?.Wait(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                Logger.LogError("Error waiting for playback task to complete", ex);
            }
        }

        public void Stop()
        {
            Pause();
            SeekToStart();
        }

        private void SeekTo(TimeSpan position)
        {
            // Implement seeking by restarting FFmpeg process with -ss parameter
            if (_ffmpegProcess != null)
            {
                var currentFile = _ffmpegProcess.StartInfo.Arguments.Split(" -f")[0].Replace("-i ", "").Trim('"');
                LoadVideoAsync(currentFile).Wait();
            }
        }

        private void SeekToStart() => SeekTo(TimeSpan.Zero);

        private async Task PlaybackLoop()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                while (_isPlaying && _ffmpegInput != null)
                {
                    sw.Restart();

                    if (!await ReadAndDisplayFrame())
                    {
                        Logger.Log("End of stream reached");
                        _isPlaying = false;
                        
                        // Use Dispatcher for UI updates
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MediaEnded?.Invoke(this, EventArgs.Empty);
                            PlaybackEnded?.Invoke(this, EventArgs.Empty);
                        });
                        
                        SeekToStart();
                        break;
                    }

                    var elapsed = sw.Elapsed;
                    if (elapsed < _frameDelay)
                    {
                        await Task.Delay(_frameDelay - elapsed);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Log("Playback cancelled");
            }
            catch (Exception ex)
            {
                Logger.LogError("Error during playback", ex);
            }
        }

        private void CleanupFFmpeg()
        {
            lock (_lockObject)
            {
                _isPlaying = false;
                _isAudioStarted = false;

                if (_playbackCancellation != null)
                {
                    try
                    {
                        if (!_playbackCancellation.IsCancellationRequested)
                            _playbackCancellation.Cancel();
                    }
                    catch (ObjectDisposedException) { }
                    finally
                    {
                        _playbackCancellation.Dispose();
                        _playbackCancellation = null;
                    }
                }

                if (_playbackTask != null)
                {
                    try
                    {
                        _playbackTask.Wait(TimeSpan.FromSeconds(1));
                    }
                    catch { }
                    _playbackTask = null;
                }

                if (_ffmpegInput != null)
                {
                    try
                    {
                        _ffmpegInput.Dispose();
                    }
                    catch { }
                    _ffmpegInput = null;
                }

                if (_ffmpegProcess != null)
                {
                    try
                    {
                        if (!_ffmpegProcess.HasExited)
                            _ffmpegProcess.Kill();
                    }
                    catch { }
                    finally
                    {
                        _ffmpegProcess.Dispose();
                        _ffmpegProcess = null;
                    }
                }

                if (_waveOut != null)
                {
                    _waveOut.Stop();
                    _waveOut.Dispose();
                    _waveOut = null;
                }

                if (_audioProcess != null)
                {
                    try
                    {
                        if (!_audioProcess.HasExited)
                            _audioProcess.Kill();
                    }
                    catch { }
                    finally
                    {
                        _audioProcess.Dispose();
                        _audioProcess = null;
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_lockObject)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                CleanupFFmpeg();
                GC.SuppressFinalize(this);
            }
        }

        private class FFmpegWaveStream : WaveStream
        {
            private readonly Stream _stream;
            private readonly WaveFormat _waveFormat;
            private readonly byte[] _buffer;
            private int _bufferCount;
            private int _bufferOffset;
            private const int BufferSize = 4096 * 16;  // Larger buffer for smoother playback

            public FFmpegWaveStream(Stream stream)
            {
                _stream = stream;
                _waveFormat = new WaveFormat(44100, 16, 2);
                _buffer = new byte[BufferSize];
                _bufferCount = 0;
                _bufferOffset = 0;
            }

            public override WaveFormat WaveFormat => _waveFormat;

            public override long Length => _stream.Length;

            public override long Position
            {
                get => _stream.Position;
                set => _stream.Position = value;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int totalBytesRead = 0;

                while (totalBytesRead < count)
                {
                    if (_bufferCount == 0)
                    {
                        // Fill the buffer
                        _bufferCount = _stream.Read(_buffer, 0, BufferSize);
                        _bufferOffset = 0;

                        if (_bufferCount == 0)
                            break;
                    }

                    int toCopy = Math.Min(count - totalBytesRead, _bufferCount);
                    Buffer.BlockCopy(_buffer, _bufferOffset, buffer, offset + totalBytesRead, toCopy);

                    _bufferOffset += toCopy;
                    _bufferCount -= toCopy;
                    totalBytesRead += toCopy;
                }

                return totalBytesRead;
            }
        }
    }
} 