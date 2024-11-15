using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using FastImageGallery.Messages;

namespace FastImageGallery.ViewModels
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool _preserveAspectRatio;
        public bool PreserveAspectRatio
        {
            get => _preserveAspectRatio;
            set
            {
                if (_preserveAspectRatio != value)
                {
                    _preserveAspectRatio = value;
                    OnPropertyChanged(nameof(PreserveAspectRatio));
                    SaveSettings();
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Application.Current.MainWindow?.InvalidateVisual();
                    });
                }
            }
        }

        public SettingsViewModel()
        {
            LoadSettings();
        }

        private void LoadSettings()
        {
            _preserveAspectRatio = Properties.Settings.Default.PreserveAspectRatio;
        }

        private void SaveSettings()
        {
            Properties.Settings.Default.PreserveAspectRatio = PreserveAspectRatio;
            Properties.Settings.Default.Save();
        }
    }
} 