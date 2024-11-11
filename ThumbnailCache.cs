using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace FastImageGallery
{
    public class ThumbnailCache
    {
        private readonly string _cacheDirectory;

        public ThumbnailCache()
        {
            _cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FastImageGallery", "ThumbnailCache");
            
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
    }
} 