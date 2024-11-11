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
                Directory.CreateDirectory(GetSizeCacheDirectory(size));
            }
        }

        private static string GetSizeCacheDirectory(ThumbnailSize size)
        {
            return Path.Combine(_cacheDirectory, size.ToString());
        }

        public string GetCachePath(string imagePath, int size)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(imagePath));
            var hashString = BitConverter.ToString(hash).Replace("-", "").ToLower();
            var sizeDir = GetSizeCacheDirectory((ThumbnailSize)size);
            return Path.Combine(sizeDir, $"{hashString}.jpg");
        }

        public bool TryGetCachedThumbnail(string imagePath, int size, out BitmapSource? thumbnail)
        {
            thumbnail = null;
            var cachePath = GetCachePath(imagePath, size);

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
            string directory = Path.GetDirectoryName(cachePath);
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
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(imagePath);
            bitmap.DecodePixelWidth = size;
            bitmap.DecodePixelHeight = size;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        public static void Clear(ThumbnailSize size)
        {
            string sizeDirectory = GetSizeCacheDirectory(size);
            if (Directory.Exists(sizeDirectory))
            {
                try
                {
                    Directory.Delete(sizeDirectory, true);
                    Directory.CreateDirectory(sizeDirectory);
                    Logger.Log($"Cleared thumbnail cache for size: {size}");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error clearing thumbnail cache for size {size}", ex);
                }
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
                        Directory.CreateDirectory(GetSizeCacheDirectory(size));
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