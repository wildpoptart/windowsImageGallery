using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System;

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
    }
} 