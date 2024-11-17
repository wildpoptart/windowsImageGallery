using FFmpeg.AutoGen;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace FastImageGallery
{
    public class VideoThumbnailGenerator
    {
        private static bool _ffmpegInitialized = false;
        private static readonly string FFmpegPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, 
            "ffmpeg"
        );

        public static void Initialize()
        {
            if (_ffmpegInitialized) return;
            ffmpeg.RootPath = FFmpegPath;
            _ffmpegInitialized = true;
        }

        public static BitmapSource GenerateVideoThumbnail(string videoPath, int width, int height)
        {
            // Create a MediaElement to load the video
            var mediaElement = new MediaElement
            {
                Source = new Uri(videoPath),
                LoadedBehavior = MediaState.Pause,
                UnloadedBehavior = MediaState.Manual,
                Width = width,
                Height = height,
                Stretch = Stretch.Uniform
            };

            // Create a window to host the MediaElement (required for rendering)
            var window = new Window
            {
                Content = mediaElement,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = null,
                Width = width,
                Height = height,
                ShowInTaskbar = false,
                Visibility = Visibility.Hidden
            };

            try
            {
                window.Show();
                mediaElement.Position = TimeSpan.Zero;
                mediaElement.Play();  // Need to play for at least one frame
                Thread.Sleep(100);    // Give it time to render the first frame
                mediaElement.Pause();

                // Render the MediaElement to a bitmap
                var rtb = new RenderTargetBitmap(
                    width, height, 
                    96, 96, 
                    PixelFormats.Pbgra32);
                rtb.Render(mediaElement);
                
                var frame = BitmapFrame.Create(rtb);
                frame.Freeze();
                return frame;
            }
            finally
            {
                window.Close();
            }
        }

        private static unsafe BitmapSource ConvertFrameToBitmap(AVFrame* frame, int targetWidth, int targetHeight)
        {
            // Calculate dimensions preserving aspect ratio
            double aspectRatio = frame->width / (double)frame->height;
            int width, height;
            
            if (aspectRatio > 1)
            {
                width = targetWidth;
                height = (int)(targetWidth / aspectRatio);
            }
            else
            {
                height = targetHeight;
                width = (int)(targetHeight * aspectRatio);
            }

            // Create software scaler context
            var swsContext = ffmpeg.sws_getContext(
                frame->width, frame->height, (AVPixelFormat)frame->format,
                width, height, AVPixelFormat.AV_PIX_FMT_BGRA,
                ffmpeg.SWS_BILINEAR, null, null, null);

            if (swsContext == null)
                throw new Exception("Could not initialize the conversion context");

            try
            {
                // Allocate destination buffer
                var dstData = new byte_ptrArray4();
                var dstLinesize = new int_array4();
                
                ffmpeg.av_image_alloc(ref dstData, ref dstLinesize, width, height, 
                    AVPixelFormat.AV_PIX_FMT_BGRA, 1).ThrowExceptionIfError();

                try
                {
                    // Convert frame
                    ffmpeg.sws_scale(swsContext,
                        frame->data, frame->linesize,
                        0, frame->height,
                        dstData, dstLinesize).ThrowExceptionIfError();

                    // Create WriteableBitmap
                    var bitmap = new WriteableBitmap(width, height, 96, 96, 
                        PixelFormats.Bgra32, null);

                    // Copy pixels
                    bitmap.Lock();
                    try
                    {
                        var backBuffer = bitmap.BackBuffer;
                        var stride = bitmap.BackBufferStride;
                        var bufferSize = height * stride;

                        for (int i = 0; i < height; i++)
                        {
                            var sourcePtr = (IntPtr)dstData[0] + i * dstLinesize[0];
                            var destPtr = backBuffer + i * stride;
                            Buffer.MemoryCopy((void*)sourcePtr, (void*)destPtr, stride, dstLinesize[0]);
                        }

                        bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                    }
                    finally
                    {
                        bitmap.Unlock();
                    }

                    bitmap.Freeze();
                    return bitmap;
                }
                finally
                {
                    // Free destination buffer
                    ffmpeg.av_free(dstData[0]);
                }
            }
            finally
            {
                ffmpeg.sws_freeContext(swsContext);
            }
        }

        private static unsafe void ThrowFFmpegError(int error)
        {
            const int bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
            var message = Marshal.PtrToStringAnsi((IntPtr)buffer);
            throw new ApplicationException($"FFmpeg error: {message}");
        }
    }

    internal static class FFmpegHelper
    {
        public static unsafe void ThrowExceptionIfError(this int error)
        {
            if (error < 0)
            {
                const int bufferSize = 1024;
                var buffer = stackalloc byte[bufferSize];
                ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
                var message = Marshal.PtrToStringAnsi((IntPtr)buffer);
                throw new ApplicationException($"FFmpeg error: {message}");
            }
        }
    }
} 