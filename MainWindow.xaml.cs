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
using CommunityToolkit.Mvvm.Messaging;
using FastImageGallery.Messages;
using PhotoOrganizer.Controls;
using PhotoOrganizer.Models;
using MessageBox = System.Windows.MessageBox;
namespace FastImageGallery
{
     public static class MediaElementExtensions
     {
          public static bool IsPlaying(this MediaElement media)
          {
               try
               {
                    return media.NaturalDuration.HasTimeSpan && 
                           media.Position < media.NaturalDuration.TimeSpan && 
                           !(media.LoadedBehavior == MediaState.Pause);
               }
               catch
               {
                    return false;
               }
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
          private bool _isAscending = false;
          private string _currentSortOption = "By Date";
          private ImageItem? _currentPreviewItem;
          private OrganizationType currentOrganization = OrganizationType.None;
          private readonly Dictionary<string, Image> _imageElements = new();
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
               SortingComboBox.SelectedIndex = 1;
               
               WeakReferenceMessenger.Default.Register<RegenerateThumbnailsMessage>(this, (r, m) =>
               {
                    RegenerateThumbnails();
               });
               
               // Initialize with no organization
               OrganizeGallery();
               
               // Add this line to initialize the preview handlers
               InitializeImagePreview();
               // Disable logging by default
               Logger.IsEnabled = false;
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
                    var size = (int)Settings.Current.PreferredThumbnailSize;
                    var bitmap = await Task.Run(() => LoadThumbnail(imagePath, size), cancellationToken);
                    Logger.Log($"Thumbnail loaded successfully for: {imagePath}");

                    BitmapSource frozenBitmap = await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                         bitmap.Freeze();
                         return bitmap;
                    });

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

                                   int insertIndex = FindSortedInsertIndex(imageItem);
                                   _images.Insert(insertIndex, imageItem);
                                   TotalImages = _images.Count;
                                   LoadingProgress.Value++;
                                   
                                   // Create the image element once and store it
                                   var image = CreateImageElement(imageItem);
                                   _imageElements[imagePath] = image;
                                   
                                   // Only reorganize gallery periodically or when batch is complete
                                   if (_images.Count % 20 == 0 || LoadingProgress.Value == LoadingProgress.Maximum)
                                   {
                                        OrganizeGallery();
                                   }
                                   
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
          private int FindSortedInsertIndex(ImageItem newItem)
          {
               if (_images.Count == 0) return 0;

               var comparer = _currentSortOption switch
               {
                    "By Date" => _isAscending
                         ? (Func<ImageItem, ImageItem, int>)((a, b) => 
                             new FileInfo(a.FilePath).LastWriteTime.CompareTo(new FileInfo(b.FilePath).LastWriteTime))
                         : (a, b) => 
                             new FileInfo(b.FilePath).LastWriteTime.CompareTo(new FileInfo(a.FilePath).LastWriteTime),
                             
                    "By Name" or _ => _isAscending
                         ? (a, b) => 
                             string.Compare(Path.GetFileName(a.FilePath), Path.GetFileName(b.FilePath))
                         : (a, b) => 
                             string.Compare(Path.GetFileName(b.FilePath), Path.GetFileName(a.FilePath))
               };

               for (int i = 0; i < _images.Count; i++)
               {
                    if (comparer(newItem, _images[i]) < 0)
                    {
                         return i;
                    }
               }

               return _images.Count;
          }
          private BitmapSource LoadThumbnail(string imagePath, int size)
          {
               try
               {
                    bool preserveAspectRatio = Properties.Settings.Default.PreserveAspectRatio;
                    
                    // First try to get from cache
                    BitmapSource? cached;
                    if (_thumbnailCache.TryGetCachedThumbnail(imagePath, size, preserveAspectRatio, out cached) && cached != null)
                    {
                         Logger.Log($"Using cached thumbnail for {imagePath}");
                         cached.Freeze();
                         return cached;
                    }

                    Logger.Log($"Generating new thumbnail for {imagePath}");
                    
                    // If not in cache, generate new thumbnail
                    var thumbnail = ThumbnailCache.GenerateThumbnail(imagePath);
                    thumbnail.Freeze();

                    // Save to cache
                    try
                    {
                         string cachePath = _thumbnailCache.GetCachePath(imagePath, size, preserveAspectRatio);
                         Logger.Log($"Saving thumbnail to cache: {cachePath}");
                         _thumbnailCache.SaveThumbnail(thumbnail, cachePath);
                    }
                    catch (Exception ex)
                    {
                         Logger.LogError($"Failed to cache thumbnail: {imagePath}", ex);
                    }

                    return thumbnail;
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
          private async void ShowPreview(ImageItem imageItem)
          {
               _currentPreviewItem = imageItem;
               
               var extension = Path.GetExtension(imageItem.FilePath).ToLower();
               bool isVideo = extension == ".mp4" || extension == ".avi" || extension == ".mov" || extension == ".wmv";
               
               // Reset controls state
               PreviewImage.Visibility = Visibility.Collapsed;
               PreviewMedia.Visibility = Visibility.Collapsed;
               MediaControls.Visibility = Visibility.Collapsed;
               
               if (isVideo)
               {
                    try
                    {
                         PreviewMedia.Visibility = Visibility.Visible;
                         MediaControls.Visibility = Visibility.Visible;
                         
                         PreviewMedia.PlaybackEnded += async (s, e) => 
                         {
                              await Dispatcher.InvokeAsync(() =>
                              {
                                   PlayPauseButton.Content = "⏵";  // Reset to play button when video ends
                              });
                         };
                         
                         await PreviewMedia.LoadVideoAsync(imageItem.FilePath);
                         PlayPauseButton.Content = "⏵";
                         
                         PreviewMedia.Opacity = 0;
                         var mediaFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                         PreviewMedia.BeginAnimation(OpacityProperty, mediaFadeIn);
                         
                         Logger.Log($"Loading video: {imageItem.FilePath}");
                    }
                    catch (Exception ex)
                    {
                         Logger.LogError($"Error loading video: {imageItem.FilePath}", ex);
                         MessageBox.Show($"Error loading video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    
                    PreviewMedia.DataContext = imageItem;
               }
               else if (imageItem.IsGif)
               {
                    // For GIFs, use FFmpegPlayer
                    PreviewMedia.Visibility = Visibility.Visible;
                    await PreviewMedia.LoadVideoAsync(imageItem.FilePath);
                    PreviewMedia.Play();
                    PreviewMedia.Opacity = 0;
                    var mediaFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                    PreviewMedia.BeginAnimation(OpacityProperty, mediaFadeIn);
                    
                    PreviewMedia.DataContext = imageItem;
               }
               else
               {
                    // Regular images use Image control
                    PreviewImage.Visibility = Visibility.Visible;
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
                    
                    PreviewImage.DataContext = imageItem;
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
               Logger.Log("Media opened successfully");
               if (PreviewMedia is FFmpegPlayer player)
               {
                    Logger.Log($"Media duration: {player.Duration}");
                    // No need to set behaviors since FFmpegPlayer handles its own state
               }
          }
          private void PreviewMedia_MediaFailed(object sender, ExceptionRoutedEventArgs e)
          {
               if (e.ErrorException != null)
               {
                    Logger.LogError($"Media failed to play: {e.ErrorException.Message}", e.ErrorException);
                    MessageBox.Show($"Failed to play media: {e.ErrorException.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
               }
          }
          private void PreviewMedia_MediaEnded(object sender, RoutedEventArgs e)
          {
               PreviewMedia.Position = TimeSpan.Zero;
               PlayPauseButton.Content = "⏵";
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
                    _imageElements.Remove(image.FilePath);
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
               DarkOverlay.MouseLeftButtonDown += ClosePreview;
               PreviewImage.MouseLeftButtonDown += ClosePreview;
          }
          private void ClosePreview(object sender, MouseButtonEventArgs e)
          {
               ClosePreview();
               e.Handled = true;
          }
          private void ClosePreview()
          {
               try
               {
                    _currentPreviewItem = null;
                    
                    // Cleanup FFmpeg player
                    if (PreviewMedia.Visibility == Visibility.Visible)
                    {
                         PreviewMedia.Pause();
                         PlayPauseButton.Content = "⏵";
                    }

                    // Create fade out animations
                    var fadeOut = new DoubleAnimation(0.7, 0, TimeSpan.FromMilliseconds(200));
                    var contentFadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));

                    fadeOut.Completed += (s, args) => {
                         ModalContainer.Visibility = Visibility.Collapsed;
                         PreviewImage.Source = null;
                         PreviewImage.Visibility = Visibility.Collapsed;
                         PreviewMedia.Visibility = Visibility.Collapsed;
                         MediaControls.Visibility = Visibility.Collapsed;
                    };

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
               catch (Exception ex)
               {
                    Logger.LogError("Error closing preview", ex);
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
               if (ThumbnailSizeComboBox.SelectedItem is ComboBoxItem selectedItem && 
                   selectedItem.Tag?.ToString() is string tagValue)
               {
                    var newSize = Enum.Parse<ThumbnailSize>(tagValue);
                    // Only refresh if the size actually changed
                    if (newSize != Settings.Current.PreferredThumbnailSize)
                    {
                         Settings.Current.PreferredThumbnailSize = newSize;
                         Settings.Save();
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
               if (PreviewMedia.Visibility != Visibility.Visible) return;

               try
               {
                    if (PreviewMedia.IsPlaying)
                    {
                         PreviewMedia.Pause();
                         PlayPauseButton.Content = "⏵";
                    }
                    else
                    {
                         PreviewMedia.Play();
                         PlayPauseButton.Content = "⏸";
                    }

                    Logger.Log($"PlayPause clicked. IsPlaying: {PreviewMedia.IsPlaying}");
               }
               catch (Exception ex)
               {
                    Logger.LogError("Error during play/pause", ex);
                    MessageBox.Show("Error controlling playback", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
               }
          }
          private void Stop_Click(object sender, RoutedEventArgs e)
          {
               if (PreviewMedia.Visibility != Visibility.Visible) return;

               try
               {
                    PreviewMedia.Stop();
                    PreviewMedia.Position = TimeSpan.Zero;
                    PlayPauseButton.Content = "⏵";
               }
               catch (Exception ex)
               {
                    Logger.LogError("Error stopping media", ex);
                    MessageBox.Show("Error stopping playback", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
               }
          }
          private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
          {
               if (PreviewMedia.Visibility == Visibility.Visible)
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
               public double Width { get; set; }
               public double Height { get; set; }
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
                    var result = MessageBox.Show(
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
                              MessageBox.Show(
                                   "Failed to delete the file. Make sure you have the necessary permissions.",
                                   "Error",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Error);
                         }
                    }
               }
          }
          private async void RegenerateThumbnails()
          {
               IsLoading = true;
               try
               {
                    _images.Clear();
                    
                    foreach (var folder in _watchedFolders)
                    {
                         await ScanFolderForImages(folder, CancellationToken.None);
                    }
                    
                    // Reorganize after all thumbnails are regenerated
                    OrganizeGallery();
               }
               finally
               {
                    IsLoading = false;
               }
          }
          private void OrganizeByComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
          {
               var selectedIndex = OrganizeByComboBox.SelectedIndex;
               currentOrganization = (OrganizationType)selectedIndex;
               OrganizeGallery();
          }
          private void OrganizeGallery()
          {
               GalleryContainer.Children.Clear();

               if (currentOrganization == OrganizationType.None)
               {
                    var wrapPanel = new WrapPanel();
                    foreach (var item in _images)
                    {
                         // Reuse existing image element
                         if (_imageElements.TryGetValue(item.FilePath, out var image))
                         {
                              // Remove from old parent if needed
                              if (image.Parent is System.Windows.Controls.Panel oldParent)
                              {
                                   oldParent.Children.Remove(image);
                              }
                              wrapPanel.Children.Add(image);
                         }
                    }
                    GalleryContainer.Children.Add(wrapPanel);
                    return;
               }

               var groupedByYear = _images
                   .GroupBy(t => File.GetLastWriteTime(t.FilePath).Year)
                   .OrderByDescending(g => g.Key);

               foreach (var yearGroup in groupedByYear)
               {
                    var yearPanel = new CollapsibleGroup
                    {
                        Header = yearGroup.Key.ToString(),
                        Margin = new Thickness(0, 0, 0, 5)
                    };

                    var yearContent = new StackPanel();
                    
                    var monthGroups = yearGroup
                        .GroupBy(t => File.GetLastWriteTime(t.FilePath).Month)
                        .OrderByDescending(g => g.Key);

                    foreach (var monthGroup in monthGroups)
                    {
                        var monthPanel = new CollapsibleGroup
                        {
                            Header = new DateTime(yearGroup.Key, monthGroup.Key, 1).ToString("MMMM"),
                            Margin = new Thickness(0, 0, 0, 5)
                        };

                        var monthGallery = new WrapPanel
                        {
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                            Width = double.NaN  // Auto width
                        };

                        foreach (var item in monthGroup.OrderByDescending(t => File.GetLastWriteTime(t.FilePath)))
                        {
                            if (_imageElements.TryGetValue(item.FilePath, out var image))
                            {
                                if (image.Parent is System.Windows.Controls.Panel oldParent)
                                {
                                    oldParent.Children.Remove(image);
                                }
                                monthGallery.Children.Add(image);
                            }
                        }

                        monthPanel.Content = monthGallery;
                        yearContent.Children.Add(monthPanel);
                    }

                    yearPanel.Content = yearContent;
                    GalleryContainer.Children.Add(yearPanel);
               }
          }
          private Image CreateImageElement(ImageItem item)
          {
               var size = (int)Settings.Current.PreferredThumbnailSize;
               var image = new Image
               {
                    Source = item.Thumbnail,
                    Width = size,
                    Height = size,
                    Margin = new Thickness(2),
                    Stretch = Properties.Settings.Default.PreserveAspectRatio ? Stretch.Uniform : Stretch.Fill,
                    Style = (Style)FindResource("ImageStyle")  // Apply the hover style
               };
               
               image.MouseDown += Image_MouseDown;
               image.DataContext = item;
               
               return image;
          }
          private void EnableLoggingCheckbox_Checked(object sender, RoutedEventArgs e)
          {
               Logger.IsEnabled = EnableLoggingCheckbox.IsChecked ?? false;
          }
     }
     public class ImageItem
     {
          public required string FilePath { get; set; }
          public required BitmapSource Thumbnail { get; set; }
          public double Width { get; set; }
          public double Height { get; set; }
          public bool IsGif => Path.GetExtension(FilePath).ToLower() == ".gif";
     }
}
