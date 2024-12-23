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
using System.Diagnostics;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using CommunityToolkit.Mvvm.Messaging;
using FastImageGallery.Messages;
using PhotoOrganizer.Controls;
using PhotoOrganizer.Models;
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
               InitializeImagePreview();

               // Set default sort to "By Date" descending
               _currentSortOption = "By Date";
               _isAscending = false;
               SortingComboBox.SelectedIndex = 1; // Select "By Date"
               SortDirectionIcon.Text = "↓";

               // Load saved settings
               foreach (var folder in Settings.Current.WatchedFolders)
               {
                    _watchedFolders.Add(folder);
                    _ = ScanFolderForImages(folder, CancellationToken.None);
               }
          }
          private async Task AddImageToGallery(string imagePath, CancellationToken cancellationToken)
          {
               try
               {
                    Logger.Log($"Starting to load image: {imagePath}");
                    var size = (int)Settings.Current.PreferredThumbnailSize;
                    
                    // Load thumbnail in background thread
                    var bitmap = await Task.Run(() => LoadThumbnail(imagePath, size), cancellationToken);
                    
                    // Create ImageItem
                    var newItem = new ImageItem
                    {
                         FilePath = imagePath,
                         Thumbnail = bitmap,
                         Width = bitmap.PixelWidth,
                         Height = bitmap.PixelHeight
                    };

                    // Update UI on dispatcher thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                         _images.Add(newItem);
                         TotalImages++;

                         // Create image element
                         var image = CreateImageElement(newItem);
                         _imageElements[imagePath] = image;

                         // Apply current sorting
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

                         // Clear and rebuild gallery with sorted items
                         _images.Clear();
                         GalleryContainer.Children.Clear();

                         foreach (var item in sortedItems)
                         {
                              _images.Add(item);
                              var element = _imageElements[item.FilePath];
                              GalleryContainer.Children.Add(element);
                         }

                         UpdateTotalImagesText();
                    }, DispatcherPriority.Background);
               }
               catch (Exception ex)
               {
                    Logger.LogError($"Error adding image to gallery: {imagePath}", ex);
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
                         .OrderByDescending(file => new FileInfo(file).LastWriteTime)
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

                    // Process files in smaller batches with UI updates between each batch
                    var batchSize = 20;  // Increased batch size for better performance
                    var totalBatches = (imageFiles.Count + batchSize - 1) / batchSize;
                    var currentBatch = 0;

                    for (int i = 0; i < imageFiles.Count; i += batchSize)
                    {
                         cancellationToken.ThrowIfCancellationRequested();
                         currentBatch++;

                         var batch = imageFiles.Skip(i).Take(batchSize);
                         var tasks = batch.Select(path => AddImageToGallery(path, cancellationToken));

                         // Process batch
                         await Task.WhenAll(tasks);

                         // Update progress
                         LoadingProgress.Value = i + batchSize;

                         // Allow UI to update and process user input
                         if (currentBatch % 5 == 0)  // Every 5 batches
                         {
                              await Task.Delay(50, cancellationToken);  // Longer delay to ensure UI responsiveness
                              await Dispatcher.Yield();  // Allow UI thread to process other work
                         }
                         else
                         {
                              await Task.Delay(1, cancellationToken);  // Minimal delay between other batches
                         }
                    }

                    // Generate video thumbnails after images are loaded
                    await ThumbnailCache.GenerateVideoThumbnailsAsync(_images);
               }
               catch (Exception ex) when (ex is not OperationCanceledException)
               {
                    Logger.LogError($"Error processing images in folder: {folderPath}", ex);
               }
               finally
               {
                    IsLoading = false;
               }
          }
          private int FindInsertIndex(ImageItem newItem)
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
                         string cachePath = ThumbnailCache.GetCachePath(imagePath, size, preserveAspectRatio);
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
               // Clear both collections
               _images.Clear();
               _imageElements.Clear();
               GalleryContainer.Children.Clear();  // Clear the UI container
               TotalImages = 0;

               LoadingProgress.Visibility = Visibility.Visible;
               
               // Store current sort settings
               var currentSortOption = _currentSortOption;
               var isAscending = _isAscending;

               // Load all images
               foreach (var folder in _watchedFolders)
               {
                    _ = ScanFolderForImages(folder, CancellationToken.None);
               }

               // Restore sort settings and apply sorting
               _currentSortOption = currentSortOption;
               _isAscending = isAscending;
               ApplySorting();
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
                         RefreshThumbnails();  // This will now properly clear everything
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

               // Clear and rebuild both collections
               _images.Clear();
               _imageElements.Clear();
               GalleryContainer.Children.Clear();

               foreach (var item in sortedItems)
               {
                    _images.Add(item);
                    var imageElement = CreateImageElement(item);
                    _imageElements[item.FilePath] = imageElement;
                    GalleryContainer.Children.Add(imageElement);
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
               
               // Add mouse handlers
               image.MouseDown += Image_MouseDown;
               
               // Add context menu
               image.ContextMenu = new ContextMenu
               {
                    Items = 
                    {
                         new MenuItem { Header = "Show in Explorer", Command = new RelayCommand(() => ShowInExplorer_Click(item)) },
                         new MenuItem { Header = "Copy", Command = new RelayCommand(() => CopyImage_Click(item)) },
                         new MenuItem { Header = "Delete", Command = new RelayCommand(() => DeleteImage_Click(item)) }
                    }
               };
               
               image.DataContext = item;
               
               return image;
          }
          private void EnableLoggingCheckbox_Checked(object sender, RoutedEventArgs e)
          {
               Logger.IsEnabled = EnableLoggingCheckbox.IsChecked ?? false;
          }
          private void OrganizeByComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
          {
               if (OrganizeByComboBox.SelectedItem is ComboBoxItem selectedItem)
               {
                    var organizationType = selectedItem.Content.ToString() switch
                    {
                         "Date" => OrganizationType.ByDate,
                         _ => OrganizationType.None
                    };
                    
                    if (currentOrganization != organizationType)
                    {
                         currentOrganization = organizationType;
                         OrganizeGallery();
                    }
               }
          }
          private void OrganizeGallery()
          {
               if (_images.Count == 0) return;

               var items = _images.ToList();
               
               // First sort the items
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

               // Clear existing items
               _images.Clear();
               _imageElements.Clear();
               GalleryContainer.Children.Clear();

               // Create a WrapPanel for the default view
               var mainPanel = new WrapPanel();
               GalleryContainer.Children.Add(mainPanel);

               if (currentOrganization == OrganizationType.None)
               {
                    // No organization, just add all items to main WrapPanel
                    foreach (var item in sortedItems)
                    {
                         _images.Add(item);
                         var imageElement = CreateImageElement(item);
                         _imageElements[item.FilePath] = imageElement;
                         mainPanel.Children.Add(imageElement);
                    }
               }
               else
               {
                    // Remove the default WrapPanel for organized view
                    GalleryContainer.Children.Clear();
                    
                    // Group by year and month
                    var yearGroups = sortedItems
                         .GroupBy(img => new FileInfo(img.FilePath).LastWriteTime.Year)
                         .OrderByDescending(g => g.Key);

                    foreach (var yearGroup in yearGroups)
                    {
                         var yearPanel = new StackPanel();

                         // Create collapsible group for each year
                         var yearCollapsibleGroup = new CollapsibleGroup
                         {
                              Header = new TextBlock
                              {
                                   Text = yearGroup.Key.ToString(),
                                   FontWeight = FontWeights.Bold,
                                   FontSize = 20,
                                   Margin = new Thickness(5, 5, 5, 5)
                              },
                              Content = yearPanel,
                              Margin = new Thickness(5, 5, 5, 5)
                         };

                         var monthGroups = yearGroup
                              .GroupBy(img => new FileInfo(img.FilePath).LastWriteTime.Month)
                              .OrderByDescending(m => m.Key);

                         foreach (var monthGroup in monthGroups)
                         {
                              // Create collapsible group for each month
                              var monthPanel = new WrapPanel
                              {
                                   Margin = new Thickness(20, 5, 5, 15)
                              };

                              foreach (var item in monthGroup)
                              {
                                   _images.Add(item);
                                   var imageElement = CreateImageElement(item);
                                   _imageElements[item.FilePath] = imageElement;
                                   monthPanel.Children.Add(imageElement);
                              }

                              var monthName = new DateTime(yearGroup.Key, monthGroup.Key, 1).ToString("MMMM");
                              var monthCollapsibleGroup = new CollapsibleGroup
                              {
                                   Header = new TextBlock
                                   {
                                        Text = monthName,
                                        FontWeight = FontWeights.SemiBold,
                                        FontSize = 16,
                                        Margin = new Thickness(5, 5, 5, 5)
                                   },
                                   Content = monthPanel,
                                   Margin = new Thickness(15, 0, 0, 0)
                              };

                              yearPanel.Children.Add(monthCollapsibleGroup);
                         }

                         GalleryContainer.Children.Add(yearCollapsibleGroup);
                    }
               }
          }
          private void RegenerateThumbnails()
          {
               RefreshThumbnails();
          }
          private void ShowInExplorer_Click(ImageItem item)
          {
               var path = item.FilePath;
               if (File.Exists(path))
               {
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
               }
          }
          private void CopyImage_Click(ImageItem item)
          {
               try
               {
                    Clipboard.SetText(item.FilePath);
                    MessageBox.Show("File path copied to clipboard", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
               }
               catch (Exception ex)
               {
                    Logger.LogError("Error copying file path", ex);
                    MessageBox.Show("Error copying file path", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
               }
          }
          private void DeleteImage_Click(ImageItem item)
          {
               var result = MessageBox.Show(
                    "Are you sure you want to delete this file?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
               );

               if (result == MessageBoxResult.Yes)
               {
                    try
                    {
                         File.Delete(item.FilePath);
                         _images.Remove(item);
                         if (_imageElements.ContainsKey(item.FilePath))
                         {
                              GalleryContainer.Children.Remove(_imageElements[item.FilePath]);
                              _imageElements.Remove(item.FilePath);
                         }
                         TotalImages--;
                         
                         if (item == _currentPreviewItem)
                         {
                              ClosePreview();
                         }
                    }
                    catch (Exception ex)
                    {
                         Logger.LogError("Error deleting file", ex);
                         MessageBox.Show("Error deleting file", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
               }
          }
          private class RelayCommand : ICommand
          {
               private readonly Action _execute;
               
               public RelayCommand(Action execute) => _execute = execute;
               
               public event EventHandler? CanExecuteChanged;
               public bool CanExecute(object? parameter) => true;
               public void Execute(object? parameter) => _execute();
          }
          private void ShowInExplorer_Click_Handler(object sender, RoutedEventArgs e)
          {
               if (sender is MenuItem menuItem && 
                   menuItem.DataContext is ImageItem item)
               {
                    ShowInExplorer_Click(item);
               }
               else if (_currentPreviewItem != null)
               {
                    ShowInExplorer_Click(_currentPreviewItem);
               }
          }
          private void CopyImage_Click_Handler(object sender, RoutedEventArgs e)
          {
               if (sender is MenuItem menuItem && 
                   menuItem.DataContext is ImageItem item)
               {
                    CopyImage_Click(item);
               }
               else if (_currentPreviewItem != null)
               {
                    CopyImage_Click(_currentPreviewItem);
               }
          }
          private void DeleteImage_Click_Handler(object sender, RoutedEventArgs e)
          {
               if (sender is MenuItem menuItem && 
                   menuItem.DataContext is ImageItem item)
               {
                    DeleteImage_Click(item);
               }
               else if (_currentPreviewItem != null)
               {
                    DeleteImage_Click(_currentPreviewItem);
               }
          }
     }
}
