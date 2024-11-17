using System.Windows.Media.Imaging;
using System.IO;

namespace FastImageGallery
{
    public class ImageItem
    {
        public required string FilePath { get; set; }
        public required BitmapSource Thumbnail { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsGif => Path.GetExtension(FilePath).ToLower() == ".gif";
        public bool IsVideo => Path.GetExtension(FilePath).ToLower() is ".mp4" or ".avi" or ".mov" or ".wmv";
    }
} 