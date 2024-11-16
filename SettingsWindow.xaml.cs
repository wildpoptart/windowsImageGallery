using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System;
using CommunityToolkit.Mvvm.Messaging;
using FastImageGallery.Messages;

namespace FastImageGallery
{
    public partial class SettingsWindow : Window
    {
        public ObservableCollection<string> Folders { get; } = new();
        public event Action<string>? FolderAdded;
        public event Action<string>? FolderRemoved;

        public SettingsWindow()
        {
            InitializeComponent();
            FoldersList.ItemsSource = Folders;
            PreserveAspectRatioCheckbox.IsChecked = Properties.Settings.Default.PreserveAspectRatio;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Hide instead of close if this is a user closing action
            if (e.Cancel == false)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnClosing(e);
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (!Folders.Contains(dialog.SelectedPath))
                {
                    Folders.Add(dialog.SelectedPath);
                    FolderAdded?.Invoke(dialog.SelectedPath);
                }
            }
        }

        private void RemoveFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && 
                button.DataContext is string folder)
            {
                Folders.Remove(folder);
                FolderRemoved?.Invoke(folder);
            }
        }

        private void PreserveAspectRatio_Changed(object sender, RoutedEventArgs e)
        {
            bool newValue = PreserveAspectRatioCheckbox.IsChecked ?? false;
            
            // Only update if the value actually changed
            if (newValue != Properties.Settings.Default.PreserveAspectRatio)
            {
                Properties.Settings.Default.PreserveAspectRatio = newValue;
                Properties.Settings.Default.Save();
                
                // Only send regenerate message if the value changed
                WeakReferenceMessenger.Default.Send(new RegenerateThumbnailsMessage());
            }
        }
    }
} 