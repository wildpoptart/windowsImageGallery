using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Linq;
using System.Windows.Media;
using Application = System.Windows.Application;
using System.Windows.Controls.Primitives;
using System.Collections.Concurrent;
using System.Threading;
using System.ComponentModel;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace FastImageGallery
{
    public partial class MainWindow : Window
    {
        private readonly HashSet<string> _supportedExtensions = new()
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp"
        };
        private readonly ThumbnailCache _thumbnailCache = new();
        private readonly ObservableCollection<ImageItem> _images = new();
        private SettingsWindow? _settingsWindow;
        private CancellationTokenSource? _loadingCancellation;
        private readonly ObservableCollection<string> _watchedFolders = new();

        public MainWindow()
        {
            InitializeComponent();
            Logger.Log("Application starting");
            ImageListView.ItemsSource = _images;
            Logger.Log($"ListView bound to collection. ItemsSource set: {ImageListView.ItemsSource != null}");
            
            if (ImageListView.ItemsPanel != null)
            {
                Logger.Log($"ItemsPanel template type: {ImageListView.ItemsPanel.GetType().Name}");
                Logger.Log($"ItemsPanel visual tree: {ImageListView.ItemsPanel}");
            }
            else
            {
                Logger.Log("Warning: ItemsPanel template is null");
            }

            InitializeImagePreview();

            // Initialize the thumbnail size dropdown
            ThumbnailSizeComboBox.SelectedIndex = Settings.Current.PreferredThumbnailSize switch
            {
                ThumbnailSize.Small => 0,
                ThumbnailSize.Medium => 1,
                ThumbnailSize.Large => 2,
                _ => 1
            };

            // Load folders from settings but don't scan yet
            foreach (var folder in Settings.Current.WatchedFolders)
            {
                _watchedFolders.Add(folder);
            }
            Logger.Log($"Loaded {_watchedFolders.Count} watched folders from settings");

            // Subscribe to the Loaded event
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Start loading images after the window is shown
            LoadingProgress.Visibility = Visibility.Visible;
            foreach (var folder in _watchedFolders)
            {
                _ = ScanFolderForImages(folder, CancellationToken.None);
            }
        }

        private async void SelectFolders_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _images.Clear();
                LoadingProgress.Visibility = Visibility.Visible;
                
                _loadingCancellation?.Cancel();
                _loadingCancellation = new CancellationTokenSource();
                
                try 
                {
                    await ScanFolderForImages(dialog.SelectedPath, _loadingCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    // Ignore cancellation
                }
                finally
                {
                    LoadingProgress.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async Task ScanFolderForImages(string folderPath, CancellationToken cancellationToken)
        {
            List<string> imageFiles;
            try
            {
                imageFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                    .Where(file => _supportedExtensions.Contains(Path.GetExtension(file).ToLower()))
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error scanning folder: {folderPath}", ex);
                return;
            }

            if (imageFiles.Count == 0)
            {
                Logger.Log($"No supported images found in folder: {folderPath}");
                return;
            }

            LoadingProgress.Maximum = imageFiles.Count;
            LoadingProgress.Value = 0;

            var batchSize = 10;
            try
            {
                for (int i = 0; i < imageFiles.Count; i += batchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var batch = imageFiles.Skip(i).Take(batchSize);
                    var tasks = batch.Select(path => AddImageToGallery(path, cancellationToken));
                    await Task.WhenAll(tasks);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError($"Error processing images in folder: {folderPath}", ex);
            }
        }

        private async Task AddImageToGallery(string imagePath, CancellationToken cancellationToken)
        {
            try
            {
                Logger.Log($"Starting to load image: {imagePath}");
                
                // Fix the size determination logic
                var size = (int)Settings.Current.PreferredThumbnailSize;
                
                // Load thumbnail on background thread
                var bitmap = await Task.Run(() => LoadThumbnail(imagePath, size), cancellationToken);
                Logger.Log($"Thumbnail loaded successfully for: {imagePath}");

                // Create a frozen (thread-safe) copy of the bitmap
                BitmapSource frozenBitmap = await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    bitmap.Freeze(); // Make the bitmap thread-safe
                    return bitmap;
                });
                
                // Now use the frozen bitmap in UI operations
                await Dispatcher.InvokeAsync(() =>
                {
                    try 
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            Logger.Log($"Current image count before adding: {_images.Count}");
                            
                            var imageItem = new ImageItem 
                            { 
                                FilePath = imagePath,
                                Thumbnail = frozenBitmap,
                                Width = size,
                                Height = size
                            };
                            _images.Add(imageItem);
                            LoadingProgress.Value++;
                            Logger.Log($"Added image to gallery. FilePath: {imageItem.FilePath}");
                            Logger.Log($"Current image count after adding: {_images.Count}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to add image to UI collection: {imagePath}", ex);
                    }
                }, DispatcherPriority.Background, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError($"Error in AddImageToGallery for {imagePath}", ex);
            }
        }

        private BitmapSource LoadThumbnail(string imagePath, int size)
        {
            try
            {
                if (_thumbnailCache.TryGetCachedThumbnail(imagePath, size, out var cached) && cached != null)
                {
                    cached.Freeze();
                    return cached;
                }

                using var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                
                BitmapSource thumbnail = Application.Current.Dispatcher.Invoke(() =>
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
                    if (decoder.Frames.Count == 0)
                        throw new InvalidOperationException("Image file contains no frames.");

                    var frame = decoder.Frames[0].Thumbnail ?? decoder.Frames[0];
                    frame.Freeze();
                    return frame;
                });

                var transformed = Application.Current.Dispatcher.Invoke(() =>
                {
                    var result = new TransformedBitmap(thumbnail, new ScaleTransform(
                        size / (double)thumbnail.PixelWidth,
                        size / (double)thumbnail.PixelHeight));
                    result.Freeze();
                    return result;
                });

                try
                {
                    _thumbnailCache.SaveThumbnail(transformed, _thumbnailCache.GetCachePath(imagePath, size));
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to cache thumbnail: {imagePath}", ex);
                }
                
                return transformed;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error loading thumbnail for {imagePath}", ex);
                throw;
            }
        }

        private void Image_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement element && 
                element.DataContext is ImageItem imageItem)
            {
                ShowPreview(imageItem);
                e.Handled = true;
            }
        }

        private void ShowPreview(ImageItem imageItem)
        {
            // Create BitmapImage with better memory handling
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(imageItem.FilePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            
            PreviewImage.Source = bitmap;
            PreviewImage.Opacity = 0; // Start fully transparent
            
            // Show the modal container
            ModalContainer.Visibility = Visibility.Visible;

            // Create fade in animations
            var overlayFadeIn = new DoubleAnimation(0, 0.7, TimeSpan.FromMilliseconds(200));
            var imageFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));

            // Start both animations
            DarkOverlay.BeginAnimation(OpacityProperty, overlayFadeIn);
            PreviewImage.BeginAnimation(OpacityProperty, imageFadeIn);
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow
                {
                    Owner = this
                };
                
                // Initialize the settings window with current folders
                foreach (var folder in _watchedFolders)
                {
                    _settingsWindow.Folders.Add(folder);
                }
                
                _settingsWindow.FolderAdded += OnFolderAdded;
                _settingsWindow.FolderRemoved += OnFolderRemoved;
                _settingsWindow.Closed += SettingsWindow_Closed;
            }
            _settingsWindow.Show();
        }

        private void SettingsWindow_Closed(object? sender, EventArgs e)
        {
            if (_settingsWindow != null)
            {
                _loadingCancellation?.Cancel();
                _loadingCancellation?.Dispose();
                _loadingCancellation = null;

                _settingsWindow.FolderAdded -= OnFolderAdded;
                _settingsWindow.FolderRemoved -= OnFolderRemoved;
                _settingsWindow.Closed -= SettingsWindow_Closed;
                _settingsWindow = null;
            }
        }

        private async void OnFolderAdded(string folderPath)
        {
            if (!ValidateFolder(folderPath))
            {
                Logger.Log($"Cannot access folder: {folderPath}");
                return;
            }

            try
            {
                _loadingCancellation?.Cancel();
                _loadingCancellation = new CancellationTokenSource();
                var token = _loadingCancellation.Token;

                LoadingProgress.Visibility = Visibility.Visible;
                await ScanFolderForImages(folderPath, token);
            }
            catch (OperationCanceledException)
            {
                Logger.Log($"Loading cancelled for folder: {folderPath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error loading images from folder: {folderPath}", ex);
            }
            finally
            {
                LoadingProgress.Visibility = Visibility.Collapsed;
            }

            _watchedFolders.Add(folderPath);
            Settings.Current.WatchedFolders = _watchedFolders.ToList();
            Settings.Save();
        }

        private bool ValidateFolder(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    Logger.Log($"Folder does not exist: {folderPath}");
                    return false;
                }

                Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Folder validation failed: {folderPath}", ex);
                return false;
            }
        }

        private void OnFolderRemoved(string folderPath)
        {
            var imagesToRemove = _images
                .Where(img => img.FilePath.StartsWith(folderPath))
                .ToList();

            foreach (var image in imagesToRemove)
            {
                _images.Remove(image);
            }

            _watchedFolders.Remove(folderPath);
            Settings.Current.WatchedFolders = _watchedFolders.ToList();
            Settings.Save();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _loadingCancellation?.Cancel();
            _loadingCancellation?.Dispose();
            base.OnClosing(e);
        }

        private void InitializeImagePreview()
        {
            // Instead of using MainImage, we'll use the image from the clicked item
            DarkOverlay.MouseLeftButtonDown += (s, e) => ClosePreview();
            PreviewImage.MouseLeftButtonDown += (s, e) => ClosePreview();
        }

        private void ClosePreview()
        {
            // Create fade out animation
            var fadeOut = new DoubleAnimation(0.7, 0, TimeSpan.FromMilliseconds(200));
            var imageFadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            
            // When both animations complete, collapse the container
            var animationsCompleted = 0;
            EventHandler onAnimationComplete = null;
            onAnimationComplete = (s, e) =>
            {
                animationsCompleted++;
                if (animationsCompleted >= 2) // Both animations finished
                {
                    ModalContainer.Visibility = Visibility.Collapsed;
                    // Remove the event handler to prevent memory leaks
                    fadeOut.Completed -= onAnimationComplete;
                    imageFadeOut.Completed -= onAnimationComplete;
                }
            };

            fadeOut.Completed += onAnimationComplete;
            imageFadeOut.Completed += onAnimationComplete;

            // Start both animations
            DarkOverlay.BeginAnimation(OpacityProperty, fadeOut);
            PreviewImage.BeginAnimation(OpacityProperty, imageFadeOut);
        }

        // Add this to handle ESC key to close the preview
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            
            if (e.Key == Key.Escape && ModalContainer.Visibility == Visibility.Visible)
            {
                ClosePreview();
            }
        }

        public void RefreshThumbnails()
        {
            _images.Clear();
            LoadingProgress.Visibility = Visibility.Visible;
            
            foreach (var folder in _watchedFolders)
            {
                _ = ScanFolderForImages(folder, CancellationToken.None);
            }
        }

        private void ThumbnailSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThumbnailSizeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                Settings.Current.PreferredThumbnailSize = Enum.Parse<ThumbnailSize>(selectedItem.Tag.ToString());
                Settings.Save();
                
                // Clear the thumbnail cache to regenerate thumbnails at the new size
                ThumbnailCache.Clear();
                
                // Refresh thumbnails
                RefreshThumbnails();
            }
        }
    }

    public class ImageItem
    {
        public required string FilePath { get; set; }
        public required BitmapSource Thumbnail { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
} 