using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace FastImageGallery
{
    public class ThumbnailCache
    {
        private static readonly string _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FastImageGallery", "ThumbnailCache");

        public ThumbnailCache()
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

        public string GetCachePath(string imagePath, int size, bool preserveAspectRatio)
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
    }
} 