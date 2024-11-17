using FFmpeg.AutoGen;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace FastImageGallery
{
    public class VideoPlayer : Image
    {
        private unsafe class FFmpegContext
        {
            public AVFormatContext* FormatContext;
            public AVCodecContext* CodecContext;
            public AVFrame* Frame;
            public AVPacket* Packet;
            public int VideoStreamIndex = -1;

            public void Initialize(string filePath)
            {
                FormatContext = ffmpeg.avformat_alloc_context();
                var pathBytes = System.Text.Encoding.UTF8.GetBytes(filePath + "\0");
                
                fixed (byte* pathPtr = pathBytes)
                {
                    var path = Marshal.PtrToStringAnsi((IntPtr)pathPtr);
                    AVFormatContext* ctx = FormatContext;
                    ffmpeg.avformat_open_input(&ctx, path, null, null).ThrowExceptionIfError();
                    FormatContext = ctx;
                }

                ffmpeg.avformat_find_stream_info(FormatContext, null).ThrowExceptionIfError();

                // Find video stream
                for (var i = 0; i < FormatContext->nb_streams; i++)
                {
                    if (FormatContext->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                    {
                        VideoStreamIndex = i;
                        break;
                    }
                }

                if (VideoStreamIndex < 0)
                    throw new Exception("No video stream found");

                var stream = FormatContext->streams[VideoStreamIndex];
                var codecParams = stream->codecpar;
                var codec = ffmpeg.avcodec_find_decoder(codecParams->codec_id);
                CodecContext = ffmpeg.avcodec_alloc_context3(codec);
                ffmpeg.avcodec_parameters_to_context(CodecContext, codecParams).ThrowExceptionIfError();
                ffmpeg.avcodec_open2(CodecContext, codec, null).ThrowExceptionIfError();

                Frame = ffmpeg.av_frame_alloc();
                Packet = ffmpeg.av_packet_alloc();
            }

            public void Cleanup()
            {
                if (Packet != null)
                {
                    AVPacket* pkt = Packet;
                    ffmpeg.av_packet_free(&pkt);
                    Packet = null;
                }

                if (Frame != null)
                {
                    AVFrame* f = Frame;
                    ffmpeg.av_frame_free(&f);
                    Frame = null;
                }

                if (CodecContext != null)
                {
                    AVCodecContext* ctx = CodecContext;
                    ffmpeg.avcodec_free_context(&ctx);
                    CodecContext = null;
                }

                if (FormatContext != null)
                {
                    AVFormatContext* fmt = FormatContext;
                    ffmpeg.avformat_close_input(&fmt);
                    FormatContext = null;
                }
            }
        }

        private FFmpegContext? _context;
        private CancellationTokenSource? _playbackCancellation;
        private WriteableBitmap? _writeableBitmap;
        private Task? _playbackTask;
        private bool _isPlaying;

        static VideoPlayer()
        {
            string ffmpegPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "ffmpeg"
            );
            ffmpeg.RootPath = ffmpegPath;
        }

        public async Task LoadVideo(string filePath)
        {
            _context?.Cleanup();
            _context = new FFmpegContext();

            await Task.Run(() =>
            {
                _context.Initialize(filePath);
            });

            Application.Current.Dispatcher.Invoke(() =>
            {
                unsafe
                {
                    _writeableBitmap = new WriteableBitmap(
                        _context.CodecContext->width,
                        _context.CodecContext->height,
                        96,
                        96,
                        PixelFormats.Bgra32,
                        null
                    );
                    Source = _writeableBitmap;
                }
            });
        }

        public void Play()
        {
            if (_isPlaying) return;
            _isPlaying = true;
            _playbackCancellation = new CancellationTokenSource();
            _playbackTask = Task.Run(PlaybackLoop);
        }

        public void Pause()
        {
            _isPlaying = false;
            _playbackCancellation?.Cancel();
        }

        private async Task PlaybackLoop()
        {
            try
            {
                while (_isPlaying)
                {
                    await Task.Run(DecodeNextFrame);
                    await Task.Delay(33); // ~30fps
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
        }

        private unsafe void DecodeNextFrame()
        {
            if (_context == null) return;

            // Create local variables for the pointers
            AVPacket* packet = _context.Packet;
            AVFormatContext* format = _context.FormatContext;
            AVCodecContext* codec = _context.CodecContext;
            AVFrame* frame = _context.Frame;

            // Now use the local variables directly
            if (ffmpeg.av_read_frame(format, packet) >= 0)
            {
                if (packet->stream_index == _context.VideoStreamIndex)
                {
                    ffmpeg.avcodec_send_packet(codec, packet).ThrowExceptionIfError();
                    var response = ffmpeg.avcodec_receive_frame(codec, frame);

                    if (response == 0)
                    {
                        RenderFrame();
                    }
                }
                ffmpeg.av_packet_unref(packet);
            }
        }

        private unsafe void RenderFrame()
        {
            if (_context == null) return;

            // Get local references to the pointers
            AVFrame* frame = _context.Frame;
            AVCodecContext* codec = _context.CodecContext;

            var frameData = new byte[codec->width * codec->height * 4];
            fixed (byte* ptr = frameData)
            {
                // Copy frame data to managed array
                Buffer.MemoryCopy(frame->data[0], ptr, frameData.Length, frameData.Length);
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                _writeableBitmap?.Lock();
                try
                {
                    Marshal.Copy(frameData, 0, _writeableBitmap.BackBuffer, frameData.Length);
                    _writeableBitmap.AddDirtyRect(
                        new Int32Rect(0, 0, _writeableBitmap.PixelWidth, _writeableBitmap.PixelHeight));
                }
                finally
                {
                    _writeableBitmap?.Unlock();
                }
            });
        }
    }
} 