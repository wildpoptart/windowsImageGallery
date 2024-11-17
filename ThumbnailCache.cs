using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using FastImageGallery;

namespace FastImageGallery
{
    public class ThumbnailCache
    {
        private static readonly string _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FastImageGallery", "ThumbnailCache");

        public ThumbnailCache()
        {
            EnsureDirectoriesExist();
        }

        private static void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(_cacheDirectory);
            foreach (ThumbnailSize size in Enum.GetValues(typeof(ThumbnailSize)))
            {
                Directory.CreateDirectory(GetSizeCacheDirectory(size, false));
                Directory.CreateDirectory(GetSizeCacheDirectory(size, true));
            }
        }

        private static string GetSizeCacheDirectory(ThumbnailSize size, bool preserveAspectRatio)
        {
            string aspectFolder = preserveAspectRatio ? "AspectRatio" : "Square";
            return Path.Combine(_cacheDirectory, size.ToString(), aspectFolder);
        }

        public static string GetCachePath(string imagePath, int size, bool preserveAspectRatio)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(imagePath));
            var hashString = BitConverter.ToString(hash).Replace("-", "").ToLower();
            var sizeDir = GetSizeCacheDirectory((ThumbnailSize)size, preserveAspectRatio);
            return Path.Combine(sizeDir, $"{hashString}.jpg");
        }

        public bool TryGetCachedThumbnail(string imagePath, int size, bool preserveAspectRatio, out BitmapSource? thumbnail)
        {
            thumbnail = null;
            var cachePath = GetCachePath(imagePath, size, preserveAspectRatio);

            if (!File.Exists(cachePath)) return false;
            if (File.GetLastWriteTime(cachePath) < File.GetLastWriteTime(imagePath)) return false;

            try
            {
                thumbnail = new BitmapImage(new Uri(cachePath));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void SaveThumbnail(BitmapSource thumbnail, string cachePath)
        {
            string? directory = Path.GetDirectoryName(cachePath);
            if (!Directory.Exists(directory) && directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            using var fileStream = File.Create(cachePath);
            var encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(thumbnail));
            encoder.Save(fileStream);
        }

        public static BitmapSource GenerateThumbnail(string imagePath)
        {
            int size = (int)Settings.Current.PreferredThumbnailSize;
            bool preserveAspectRatio = Properties.Settings.Default.PreserveAspectRatio;
            
            // Check if it's a video file
            var extension = Path.GetExtension(imagePath).ToLower();
            if (extension == ".mp4" || extension == ".avi" || extension == ".mov" || extension == ".wmv")
            {
                try
                {
                    return VideoThumbnailGenerator.GenerateVideoThumbnail(imagePath, size, size);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to generate video thumbnail: {imagePath}", ex);
                    // Return a default video thumbnail
                    return CreateDefaultVideoThumbnail(size);
                }
            }

            // Existing image thumbnail code...
            using var stream = File.OpenRead(imagePath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(imagePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            
            if (preserveAspectRatio)
            {
                // Calculate dimensions while preserving aspect ratio
                double aspectRatio = frame.PixelWidth / (double)frame.PixelHeight;
                if (aspectRatio > 1) // Wider than tall
                {
                    bitmap.DecodePixelWidth = size;
                    bitmap.DecodePixelHeight = (int)(size / aspectRatio);
                }
                else // Taller than wide
                {
                    bitmap.DecodePixelHeight = size;
                    bitmap.DecodePixelWidth = (int)(size * aspectRatio);
                }
            }
            else
            {
                // Square thumbnails
                bitmap.DecodePixelWidth = size;
                bitmap.DecodePixelHeight = size;
            }
            
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private static BitmapSource CreateDefaultVideoThumbnail(int size)
        {
            // Create a simple default thumbnail for videos when generation fails
            var drawingVisual = new DrawingVisual();
            using (var drawingContext = drawingVisual.RenderOpen())
            {
                // Draw a black background
                drawingContext.DrawRectangle(
                    Brushes.Black,
                    null,
                    new Rect(0, 0, size, size));

                // Draw a play button icon
                var playIcon = new PathGeometry();
                // ... define play button triangle path ...
                drawingContext.DrawGeometry(Brushes.White, null, playIcon);
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(drawingVisual);
            bitmap.Freeze();
            return bitmap;
        }

        public static void Clear(ThumbnailSize size)
        {
            string squareDirectory = GetSizeCacheDirectory(size, false);
            string aspectDirectory = GetSizeCacheDirectory(size, true);
            
            try
            {
                if (Directory.Exists(squareDirectory))
                {
                    Directory.Delete(squareDirectory, true);
                    Directory.CreateDirectory(squareDirectory);
                }
                if (Directory.Exists(aspectDirectory))
                {
                    Directory.Delete(aspectDirectory, true);
                    Directory.CreateDirectory(aspectDirectory);
                }
                Logger.Log($"Cleared thumbnail cache for size: {size}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error clearing thumbnail cache for size {size}", ex);
            }
        }

        public static void ClearAll()
        {
            if (Directory.Exists(_cacheDirectory))
            {
                try
                {
                    Directory.Delete(_cacheDirectory, true);
                    Directory.CreateDirectory(_cacheDirectory);
                    foreach (ThumbnailSize size in Enum.GetValues(typeof(ThumbnailSize)))
                    {
                        Directory.CreateDirectory(GetSizeCacheDirectory(size, false));
                        Directory.CreateDirectory(GetSizeCacheDirectory(size, true));
                    }
                    Logger.Log("Cleared all thumbnail caches");
                }
                catch (Exception ex)
                {
                    Logger.LogError("Error clearing all thumbnail caches", ex);
                }
            }
        }

        public static async Task GenerateVideoThumbnailsAsync(ObservableCollection<ImageItem> images)
        {
            var videoItems = images.Where(img => 
                {
                    var ext = Path.GetExtension(img.FilePath).ToLower();
                    return ext == ".mp4" || ext == ".avi" || ext == ".mov" || ext == ".wmv";
                }).ToList();

            Logger.Log($"Starting to generate {videoItems.Count} video thumbnails...");

            foreach (var videoItem in videoItems)
            {
                try
                {
                    int size = (int)Settings.Current.PreferredThumbnailSize;
                    bool preserveAspectRatio = Properties.Settings.Default.PreserveAspectRatio;
                    string cachePath = GetCachePath(videoItem.FilePath, size, preserveAspectRatio);

                    // Skip if we already have a cached thumbnail
                    if (File.Exists(cachePath) && 
                        File.GetLastWriteTime(cachePath) >= File.GetLastWriteTime(videoItem.FilePath))
                    {
                        continue;
                    }

                    // Generate thumbnail using FFmpegPlayer
                    var player = new FFmpegPlayer();
                    await player.LoadVideoAsync(videoItem.FilePath);
                    
                    // Save the first frame as thumbnail
                    if (player.Source is WriteableBitmap writeableBitmap)
                    {
                        var encoder = new JpegBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(writeableBitmap));
                        
                        using var fileStream = File.Create(cachePath);
                        encoder.Save(fileStream);
                        
                        // Update the thumbnail in the UI
                        var newThumbnail = new BitmapImage(new Uri(cachePath));
                        newThumbnail.Freeze();
                        videoItem.Thumbnail = newThumbnail;
                    }

                    player.Dispose();
                    Logger.Log($"Generated thumbnail for video: {videoItem.FilePath}");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to generate video thumbnail: {videoItem.FilePath}", ex);
                }
            }

            Logger.Log("Finished generating video thumbnails");
        }
    }
} 