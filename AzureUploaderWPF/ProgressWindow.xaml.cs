using System;
using System.Windows;

namespace AzureUploaderWPF
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
            
            // Cấu hình các giá trị ban đầu
            TotalFilesText.Text = totalFiles.ToString();
            CurrentFileText.Text = "0";
            PercentageText.Text = "0%";
            ProgressBar.Value = 0;
            ProgressBar.Maximum = totalFiles;
            CurrentFileNameText.Text = "Đang chuẩn bị...";
        }

        /// <summary>
        /// Cập nhật thông tin tiến trình
        /// </summary>
        /// <param name="currentFile">Số thứ tự file đang xử lý</param>
        /// <param name="fileName">Tên file đang xử lý</param>
        public void UpdateProgress(int currentFile, string fileName)
        {
            // Đảm bảo cập nhật UI từ thread chính
            this.Dispatcher.Invoke(() =>
            {
                CurrentFileText.Text = currentFile.ToString();
                ProgressBar.Value = currentFile;
                double percentage = Math.Round((double)currentFile / totalFiles * 100);
                PercentageText.Text = $"{percentage}%";
                CurrentFileNameText.Text = fileName;
                
                // Cập nhật tiêu đề cửa sổ
                this.Title = $"Đang upload... {percentage}% hoàn thành";
            });
        }
    }
} 