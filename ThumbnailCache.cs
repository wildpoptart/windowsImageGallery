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
        }

        public string GetCachePath(string imagePath, int size)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(imagePath + size));
            var hashString = BitConverter.ToString(hash).Replace("-", "").ToLower();
            return Path.Combine(_cacheDirectory, $"{hashString}.jpg");
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

        public static void Clear()
        {
            if (Directory.Exists(_cacheDirectory))
            {
                try
                {
                    Directory.Delete(_cacheDirectory, true);
                    Directory.CreateDirectory(_cacheDirectory);
                }
                catch (Exception ex)
                {
                    Logger.LogError("Error clearing thumbnail cache", ex);
                }
            }
        }
    }
} 