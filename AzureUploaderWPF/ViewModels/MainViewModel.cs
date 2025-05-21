using AzureUploaderWPF.Models;
using AzureUploaderWPF.Services;
using AzureUploaderWPF.Utils;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;

namespace AzureUploaderWPF.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        #region Services
        private readonly AzureBlobService _blobService;
        private readonly CsvService _csvService;
        private readonly AutoUploadService _autoUploadService;
        #endregion

        #region Properties
        private AzureStorageSettings _storageSettings;
        private bool _isSidebarExpanded = true;
        private string _connectionTestMessage = string.Empty;
        private bool _isConnectionTesting = false;
        private bool _isConnectionSuccess = false;
        private bool _isConnectionFailed = false;

        public AzureStorageSettings StorageSettings 
        { 
            get => _storageSettings; 
            set 
            { 
                _storageSettings = value; 
                OnPropertyChanged(); 
            } 
        }

        public bool IsSidebarExpanded
        {
            get => _isSidebarExpanded;
            set
            {
                _isSidebarExpanded = value;
                OnPropertyChanged();
            }
        }

        public string ConnectionTestMessage
        {
            get => _connectionTestMessage;
            set
            {
                _connectionTestMessage = value;
                OnPropertyChanged();
            }
        }

        public bool IsConnectionTesting
        {
            get => _isConnectionTesting;
            set
            {
                _isConnectionTesting = value;
                OnPropertyChanged();
            }
        }

        public bool IsConnectionSuccess
        {
            get => _isConnectionSuccess;
            set
            {
                _isConnectionSuccess = value;
                OnPropertyChanged();
            }
        }

        public bool IsConnectionFailed
        {
            get => _isConnectionFailed;
            set
            {
                _isConnectionFailed = value;
                OnPropertyChanged();
            }
        }

        public bool IsAutoUploadRunning => _autoUploadService?.IsMonitoring ?? false;

        public ObservableCollection<string> LogMessages { get; private set; } = new ObservableCollection<string>();
        #endregion

        #region Commands
        public ICommand TestConnectionCommand { get; private set; }
        public ICommand UploadFileCommand { get; private set; }
        public ICommand UploadFolderCommand { get; private set; }
        public ICommand SelectFolderCommand { get; private set; }
        public ICommand StartMonitoringCommand { get; private set; }
        public ICommand StopMonitoringCommand { get; private set; }
        public ICommand ToggleSidebarCommand { get; private set; }
        #endregion

        public MainViewModel()
        {
            // Khởi tạo cấu hình từ SettingsManager
            _storageSettings = SettingsManager.Instance.StorageSettings;

            // Khởi tạo các dịch vụ
            _blobService = new AzureBlobService(_storageSettings);
            _csvService = new CsvService();
            _autoUploadService = new AutoUploadService(_storageSettings);

            // Đăng ký sự kiện
            _autoUploadService.FileUploaded += OnFileUploaded;
            _autoUploadService.MonitoringStatusChanged += OnMonitoringStatusChanged;

            // Khởi tạo các lệnh
            InitializeCommands();
        }

        private void InitializeCommands()
        {
            TestConnectionCommand = new RelayCommand(async _ => await TestConnection());
            UploadFileCommand = new RelayCommand(async _ => await UploadFile());
            UploadFolderCommand = new RelayCommand(async _ => await UploadFolder());
            SelectFolderCommand = new RelayCommand(_ => SelectMonitorFolder());
            StartMonitoringCommand = new RelayCommand(_ => StartMonitoring());
            StopMonitoringCommand = new RelayCommand(_ => StopMonitoring());
            ToggleSidebarCommand = new RelayCommand(_ => ToggleSidebar());
        }

        private async Task TestConnection()
        {
            try
            {
                ShowTestingConnectionState();
                var result = await _blobService.TestConnectionAsync();

                if (result.Success)
                {
                    ShowConnectionSuccess(result.Message);
                    AddLogMsg($"Connection successful: {result.Message}");
                }
                else
                {
                    ShowConnectionError(result.Message);
                    AddLogMsg($"Connection error: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                ShowConnectionError(ex.Message);
                AddLogMsg($"Connection error: {ex.Message}");
            }
        }

        private void ShowTestingConnectionState()
        {
            IsConnectionTesting = true;
            IsConnectionSuccess = false;
            IsConnectionFailed = false;
            ConnectionTestMessage = "Testing connection...";
        }

        private void ShowConnectionSuccess(string details)
        {
            IsConnectionTesting = false;
            IsConnectionSuccess = true;
            IsConnectionFailed = false;
            ConnectionTestMessage = details;

            // Lưu cấu hình
            SettingsManager.Instance.SaveSettings();
        }

        private void ShowConnectionError(string errorMessage)
        {
            IsConnectionTesting = false;
            IsConnectionSuccess = false;
            IsConnectionFailed = true;
            ConnectionTestMessage = errorMessage;
        }

        private async Task UploadFile()
        {
            try
            {
                Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
                openFileDialog.Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*";
                openFileDialog.Multiselect = true;
                
                if (openFileDialog.ShowDialog() == true)
                {
                    string[] selectedFiles = openFileDialog.FileNames;
                    
                    if (selectedFiles.Length == 0)
                    {
                        AddLogMsg("No files selected.");
                        return;
                    }

                    // Hiển thị cửa sổ tiến trình
                    var progressWindow = new Views.ProgressWindow(selectedFiles.Length);
                    progressWindow.Owner = System.Windows.Application.Current.MainWindow;
                    progressWindow.Show();

                    int currentFile = 0;
                    
                    foreach (string filePath in selectedFiles)
                    {
                        currentFile++;
                        string fileName = Path.GetFileName(filePath);
                        
                        progressWindow.UpdateProgress(currentFile, fileName);
                        AddLogMsg($"Processing: {fileName}");
                        
                        try
                        {
                            var result = await _blobService.UploadFileAsync(filePath);
                            AddLogMsg(result.Message);
                            
                            if (result.Success)
                            {
                                // Thêm vào danh sách đã upload
                                SettingsManager.Instance.UploadedFiles[filePath] = DateTime.Now;
                                SettingsManager.Instance.SaveUploadedFilesLog();
                            }
                        }
                        catch (Exception ex)
                        {
                            AddLogMsg($"Error uploading {fileName}: {ex.Message}");
                        }
                    }
                    
                    progressWindow.Close();
                    AddLogMsg($"Completed processing {selectedFiles.Length} files.");
                }
            }
            catch (Exception ex)
            {
                AddLogMsg($"Error: {ex.Message}");
            }
        }

        private async Task UploadFolder()
        {
            try
            {
                var folderDialog = new System.Windows.Forms.FolderBrowserDialog();
                folderDialog.Description = "Chọn thư mục chứa các file CSV cần upload";
                folderDialog.UseDescriptionForTitle = true;
                
                if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string folderPath = folderDialog.SelectedPath;
                    
                    if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                    {
                        AddLogMsg("Invalid folder.");
                        return;
                    }
                    
                    // Tìm tất cả các file CSV trong thư mục
                    string[] csvFiles = Directory.GetFiles(folderPath, "*.csv", SearchOption.AllDirectories);
                    
                    if (csvFiles.Length == 0)
                    {
                        AddLogMsg("Không tìm thấy file CSV nào trong thư mục.");
                        return;
                    }
                    
                    // Hiển thị cửa sổ tiến trình
                    var progressWindow = new Views.ProgressWindow(csvFiles.Length);
                    progressWindow.Owner = System.Windows.Application.Current.MainWindow;
                    progressWindow.Show();
                    
                    int currentFile = 0;
                    
                    foreach (string filePath in csvFiles)
                    {
                        currentFile++;
                        string fileName = Path.GetFileName(filePath);
                        
                        progressWindow.UpdateProgress(currentFile, fileName);
                        AddLogMsg($"Processing: {fileName}");
                        
                        try
                        {
                            var result = await _blobService.UploadFileAsync(filePath);
                            AddLogMsg(result.Message);
                            
                            if (result.Success)
                            {
                                // Thêm vào danh sách đã upload
                                SettingsManager.Instance.UploadedFiles[filePath] = DateTime.Now;
                                SettingsManager.Instance.SaveUploadedFilesLog();
                            }
                        }
                        catch (Exception ex)
                        {
                            AddLogMsg($"Error uploading {fileName}: {ex.Message}");
                        }
                    }
                    
                    progressWindow.Close();
                    AddLogMsg($"Completed processing {csvFiles.Length} files in folder.");
                }
            }
            catch (Exception ex)
            {
                AddLogMsg($"Error: {ex.Message}");
            }
        }

        private void SelectMonitorFolder()
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog();
            folderDialog.Description = "Chọn thư mục để giám sát";
            folderDialog.UseDescriptionForTitle = true;
            
            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string folderPath = folderDialog.SelectedPath;
                
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                {
                    AddLogMsg("Invalid folder.");
                    return;
                }
                
                StorageSettings.MonitorFolderPath = folderPath;
                SettingsManager.Instance.SaveSettings();
                AddLogMsg($"Selected monitoring folder: {folderPath}");
            }
        }

        private void StartMonitoring()
        {
            try
            {
                _autoUploadService.StartMonitoring();
                OnPropertyChanged(nameof(IsAutoUploadRunning));
                AddLogMsg("Auto monitoring started.");
            }
            catch (Exception ex)
            {
                AddLogMsg($"Error starting monitoring: {ex.Message}");
            }
        }

        private void StopMonitoring()
        {
            try
            {
                _autoUploadService.StopMonitoring();
                OnPropertyChanged(nameof(IsAutoUploadRunning));
                AddLogMsg("Auto monitoring stopped.");
            }
            catch (Exception ex)
            {
                AddLogMsg($"Error stopping monitoring: {ex.Message}");
            }
        }

        private void OnFileUploaded(string filePath, bool success, string message)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                AddLogMsg(message);
            });
        }

        private void OnMonitoringStatusChanged(bool isMonitoring)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(IsAutoUploadRunning));
            });
        }

        private void ToggleSidebar()
        {
            IsSidebarExpanded = !IsSidebarExpanded;
        }

        private void AddLogMsg(string message)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                LogMessages.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
                
                // Giới hạn số lượng log
                const int maxLogCount = 100;
                while (LogMessages.Count > maxLogCount)
                {
                    LogMessages.RemoveAt(LogMessages.Count - 1);
                }
            });
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Lớp RelayCommand đơn giản để hỗ trợ binding command
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}