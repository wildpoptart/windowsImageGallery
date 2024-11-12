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
     public static class MediaElementExtensions
     {
          public static bool IsPlaying(this MediaElement media)
          {
               return media.Position < media.NaturalDuration.TimeSpan;
          }
     }
     public partial class MainWindow : Window, INotifyPropertyChanged
     {
          public event PropertyChangedEventHandler? PropertyChanged;
          protected virtual void OnPropertyChanged(string propertyName)
          {
               PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
          }
          private readonly HashSet<string> _supportedExtensions = new()
{
".jpg", ".jpeg", ".png", ".gif", ".bmp",
".mp4", ".wmv", ".avi", ".mov"
};
          private readonly ThumbnailCache _thumbnailCache = new();
          private readonly ObservableCollection<ImageItem> _images = new();
          private SettingsWindow? _settingsWindow;
          private CancellationTokenSource? _loadingCancellation;
          private readonly ObservableCollection<string> _watchedFolders = new();
          private bool _isLoading;
          private int _totalImages;
          private bool _isAscending = true;
          private string _currentSortOption = "By Name";
          public bool IsLoading
          {
               get => _isLoading;
               set
               {
                    _isLoading = value;
                    OnPropertyChanged(nameof(IsLoading));
               }
          }
          public int TotalImages
          {
               get => _totalImages;
               set
               {
                    _totalImages = value;
                    OnPropertyChanged(nameof(TotalImages));
                    UpdateTotalImagesText();
               }
          }
          public MainWindow()
          {
               InitializeComponent();
               DataContext = this;
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
               SortingComboBox.SelectionChanged += SortingComboBox_SelectionChanged;
               SortingComboBox.SelectedIndex = 0;
          }
          private void MainWindow_Loaded(object sender, RoutedEventArgs e)
          {
               // Start loading images after the window is shown
               IsLoading = true;
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
                    IsLoading = true;
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
                         IsLoading = false;
                    }
               }
          }
          private async Task ScanFolderForImages(string folderPath, CancellationToken cancellationToken)
          {
               IsLoading = true;
               try
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
               finally
               {
                    IsLoading = false;
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
                                   var imageItem = new ImageItem
                                   {
                                        FilePath = imagePath,
                                        Thumbnail = frozenBitmap,
                                        Width = size,
                                        Height = size
                                   };
                                   _images.Add(imageItem);
                                   TotalImages = _images.Count;
                                   LoadingProgress.Value++;
                                   Logger.Log($"Added image to gallery. FilePath: {imageItem.FilePath}");
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
               if (imageItem.IsGif)
               {
                    // For GIFs, use MediaElement to support animation
                    PreviewImage.Visibility = Visibility.Collapsed;
                    PreviewMedia.Visibility = Visibility.Visible;
                    PreviewMedia.Source = new Uri(imageItem.FilePath);
                    PreviewMedia.Play();
                    PreviewMedia.Opacity = 0;
                    var mediaFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                    PreviewMedia.BeginAnimation(OpacityProperty, mediaFadeIn);
               }
               else
               {
                    // Regular images use Image control
                    PreviewImage.Visibility = Visibility.Visible;
                    PreviewMedia.Visibility = Visibility.Collapsed;
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imageItem.FilePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    PreviewImage.Source = bitmap;
                    PreviewImage.Opacity = 0;
                    var imageFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                    PreviewImage.BeginAnimation(OpacityProperty, imageFadeIn);
               }
               // Show the modal container
               ModalContainer.Visibility = Visibility.Visible;
               // Fade in overlay
               var overlayFadeIn = new DoubleAnimation(0, 0.7, TimeSpan.FromMilliseconds(200));
               DarkOverlay.BeginAnimation(OpacityProperty, overlayFadeIn);
          }
          // Add these event handlers for MediaElement
          private void PreviewMedia_MediaOpened(object sender, RoutedEventArgs e)
          {
               // Nothing needed here for GIFs
          }
          private void PreviewMedia_MediaEnded(object sender, RoutedEventArgs e)
          {
               if (PreviewMedia.Source != null)
               {
                    // Loop GIFs
                    PreviewMedia.Position = TimeSpan.Zero;
                    PreviewMedia.Play();
               }
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
               TotalImages = _images.Count;
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
               // Stop any playing media
               PreviewMedia.Stop();
               PreviewMedia.Source = null;
               // Create fade out animations
               var fadeOut = new DoubleAnimation(0.7, 0, TimeSpan.FromMilliseconds(200));
               var contentFadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
               var animationsCompleted = 0;
               EventHandler onAnimationComplete = null;
               onAnimationComplete = (s, e) =>
               {
                    animationsCompleted++;
                    if (animationsCompleted >= 2)
                    {
                         ModalContainer.Visibility = Visibility.Collapsed;
                         fadeOut.Completed -= onAnimationComplete;
                         contentFadeOut.Completed -= onAnimationComplete;
                    }
               };
               fadeOut.Completed += onAnimationComplete;
               contentFadeOut.Completed += onAnimationComplete;
               DarkOverlay.BeginAnimation(OpacityProperty, fadeOut);
               if (PreviewImage.Visibility == Visibility.Visible)
               {
                    PreviewImage.BeginAnimation(OpacityProperty, contentFadeOut);
               }
               else
               {
                    PreviewMedia.BeginAnimation(OpacityProperty, contentFadeOut);
               }
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
               TotalImages = 0;
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
                    var newSize = Enum.Parse<ThumbnailSize>(selectedItem.Tag.ToString());
                    // Only refresh if the size actually changed
                    if (newSize != Settings.Current.PreferredThumbnailSize)
                    {
                         Settings.Current.PreferredThumbnailSize = newSize;
                         Settings.Save();
                         // Just refresh thumbnails without clearing any cache
                         RefreshThumbnails();
                    }
               }
          }
          private void UpdateTotalImagesText()
          {
               Dispatcher.InvokeAsync(() =>
               {
                    TotalImagesText.Text = $"Total Images: {TotalImages:N0}";
               });
          }
          private void PlayPause_Click(object sender, RoutedEventArgs e)
          {
               if (PreviewMedia.Source == null) return;
               if (PreviewMedia.Position >= PreviewMedia.NaturalDuration.TimeSpan)
               {
                    PreviewMedia.Position = TimeSpan.Zero;
               }
               if (PreviewMedia.CanPause && PreviewMedia.Position < PreviewMedia.NaturalDuration.TimeSpan)
               {
                    PreviewMedia.Pause();
                    PlayPauseButton.Content = "⏵";
               }
               else
               {
                    PreviewMedia.Play();
                    PlayPauseButton.Content = "⏸";
               }
          }
          private void Stop_Click(object sender, RoutedEventArgs e)
          {
               if (PreviewMedia.Source == null) return;
               PreviewMedia.Stop();
               PlayPauseButton.Content = "⏵";
          }
          private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
          {
               if (PreviewMedia != null)
               {
                    PreviewMedia.Volume = e.NewValue;
               }
          }
          private void SortDirectionButton_Click(object sender, RoutedEventArgs e)
          {
               _isAscending = !_isAscending;
               SortDirectionIcon.Text = _isAscending ? "↑" : "↓";
               ApplySorting();
          }
          private void SortingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
          {
               if (SortingComboBox.SelectedItem is ComboBoxItem selectedItem)
               {
                    _currentSortOption = selectedItem.Content.ToString() ?? "By Name";
                    ApplySorting();
               }
          }
          private void ApplySorting()
          {
               if (_images.Count == 0) return;

               var items = _images.ToList();
               
               IOrderedEnumerable<ImageItem> sortedItems;
               switch (_currentSortOption)
               {
                    case "By Date":
                         sortedItems = _isAscending 
                              ? items.OrderBy(img => new FileInfo(img.FilePath).LastWriteTime)
                              : items.OrderByDescending(img => new FileInfo(img.FilePath).LastWriteTime);
                         break;
                         
                    case "By Name":
                    default:
                         sortedItems = _isAscending 
                              ? items.OrderBy(img => Path.GetFileName(img.FilePath))
                              : items.OrderByDescending(img => Path.GetFileName(img.FilePath));
                         break;
               }

               _images.Clear();
               foreach (var item in sortedItems)
               {
                    _images.Add(item);
               }
          }
          public class ImageItem
          {
               public required string FilePath { get; set; }
               public required BitmapSource Thumbnail { get; set; }
               public int Width { get; set; }
               public int Height { get; set; }
               public bool IsGif => Path.GetExtension(FilePath).ToLower() == ".gif";
          }
          private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
          {
               if (sender is MenuItem menuItem && menuItem.DataContext is ImageItem imageItem)
               {
                    try
                    {
                         var argument = $"/select,\"{imageItem.FilePath}\"";
                         System.Diagnostics.Process.Start("explorer.exe", argument);
                    }
                    catch (Exception ex)
                    {
                         Logger.LogError($"Failed to open explorer for {imageItem.FilePath}", ex);
                    }
               }
          }
          private void CopyImage_Click(object sender, RoutedEventArgs e)
          {
               if (sender is MenuItem menuItem && menuItem.DataContext is ImageItem imageItem)
               {
                    try
                    {
                         var files = new string[] { imageItem.FilePath };
                         var dataObject = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, files);
                         System.Windows.Clipboard.SetDataObject(dataObject);
                    }
                    catch (Exception ex)
                    {
                         Logger.LogError($"Failed to copy {imageItem.FilePath}", ex);
                    }
               }
          }
          private void DeleteImage_Click(object sender, RoutedEventArgs e)
          {
               if (sender is MenuItem menuItem && menuItem.DataContext is ImageItem imageItem)
               {
                    var result = System.Windows.MessageBox.Show(
                         $"Are you sure you want to delete this file?\n{imageItem.FilePath}",
                         "Confirm Delete",
                         MessageBoxButton.YesNo,
                         MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                         try
                         {
                              File.Delete(imageItem.FilePath);
                              _images.Remove(imageItem);
                              TotalImages = _images.Count;
                              
                              // If we're in preview mode, close it
                              if (ModalContainer.Visibility == Visibility.Visible)
                              {
                                   ClosePreview();
                              }
                         }
                         catch (Exception ex)
                         {
                              Logger.LogError($"Failed to delete {imageItem.FilePath}", ex);
                              System.Windows.MessageBox.Show(
                                   "Failed to delete the file. Make sure you have the necessary permissions.",
                                   "Error",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Error);
                         }
                    }
               }
          }
     }
     public class ImageItem
     {
          public required string FilePath { get; set; }
          public required BitmapSource Thumbnail { get; set; }
          public int Width { get; set; }
          public int Height { get; set; }
          public bool IsGif => Path.GetExtension(FilePath).ToLower() == ".gif";
     }
}
