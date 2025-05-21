using System;
using System.Windows;

namespace AzureUploaderWPF.Views
{
    /// <summary>
    /// Interaction logic for ProgressWindow.xaml
    /// </summary>
    public partial class ProgressWindow : Window
    {
        private int totalFiles;

        public ProgressWindow(int totalFiles)
        {
            InitializeComponent();
            this.totalFiles = totalFiles;

            // Configure initial values
            TotalFilesText.Text = totalFiles.ToString();
            CurrentFileText.Text = "0";
            PercentageText.Text = "0%";
            ProgressBar.Value = 0;
            ProgressBar.Maximum = totalFiles;
            CurrentFileNameText.Text = "Preparing...";
        }

        /// <param name="currentFile">Số thứ tự file đang xử lý</param>
        /// <param name="fileName">Tên file đang xử lý</param>
        public void UpdateProgress(int currentFile, string fileName)
        {
            this.Dispatcher.Invoke(() =>
            {
                CurrentFileText.Text = currentFile.ToString();
                ProgressBar.Value = currentFile;
                double percentage = Math.Round((double)currentFile / totalFiles * 100);
                PercentageText.Text = $"{percentage}%";
                CurrentFileNameText.Text = fileName;
                
                this.Title = $"Uploading... {percentage}% complete";
            });
        }
    }
} 