using AzureUploaderWPF.Models;
using AzureUploaderWPF.Utils;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AzureUploaderWPF.Services
{
    public delegate void FileUploadEventHandler(string filePath, bool success, string message);
    
    public delegate void MonitoringStatusChangedEventHandler(bool isMonitoring);
    
    public class AutoUploadService
    {
        private readonly AzureStorageSettings _storageSettings;
        private readonly AzureBlobService _blobService;
        private readonly CsvService _csvService;
        
        private System.Threading.Timer _autoUploadTimer;
        private System.Threading.Timer _autoStopTimer;
        private bool _isMonitoring;
        
        public event FileUploadEventHandler FileUploaded;

        public event MonitoringStatusChangedEventHandler MonitoringStatusChanged;
        
        public ConcurrentDictionary<string, DateTime> UploadedFiles => SettingsManager.Instance.UploadedFiles;

        public bool IsMonitoring
        {
            get => _isMonitoring;
            private set
            {
                if (_isMonitoring != value)
                {
                    _isMonitoring = value;
                    MonitoringStatusChanged?.Invoke(_isMonitoring);
                }
            }
        }
        public AutoUploadService(AzureStorageSettings storageSettings)
        {
            _storageSettings = storageSettings ?? throw new ArgumentNullException(nameof(storageSettings));
            _blobService = new AzureBlobService(_storageSettings);
            _csvService = new CsvService();
        }
        
        public void StartMonitoring()
        {
            if (IsMonitoring)
                return;
                
            if (string.IsNullOrEmpty(_storageSettings.MonitorFolderPath) || !Directory.Exists(_storageSettings.MonitorFolderPath))
            {
                throw new DirectoryNotFoundException("Thư mục giám sát không hợp lệ");
            }
            
            // Tạo timer cho việc tự động upload
            int intervalMilliseconds = _storageSettings.CheckIntervalMinutes * 60 * 1000;
            _autoUploadTimer = new System.Threading.Timer(AutoUploadTimerCallback, null, 0, intervalMilliseconds);
            
            // Thiết lập timer tự động dừng nếu có
            SetupAutoStop();
            
            IsMonitoring = true;
        }
        
        public void StopMonitoring()
        {
            if (!IsMonitoring)
                return;
                
            // Hủy các timer
            _autoUploadTimer?.Dispose();
            _autoUploadTimer = null;
            
            _autoStopTimer?.Dispose();
            _autoStopTimer = null;
            
            IsMonitoring = false;
        }
        
        private void SetupAutoStop()
        {
            _autoStopTimer?.Dispose();
            _autoStopTimer = null;
            
            if (_storageSettings.AutoStopTime.HasValue)
            {
                DateTime stopTime = _storageSettings.AutoStopTime.Value;
                if (stopTime > DateTime.Now)
                {
                    TimeSpan delay = stopTime - DateTime.Now;
                    _autoStopTimer = new System.Threading.Timer(AutoStopTimerCallback, null, (int)delay.TotalMilliseconds, Timeout.Infinite);
                }
            }
        }
        
        private void AutoStopTimerCallback(object state)
        {
            StopMonitoring();
        }

        private async void AutoUploadTimerCallback(object state)
        {
            if (!IsMonitoring || string.IsNullOrEmpty(_storageSettings.MonitorFolderPath))
                return;
                
            try
            {
                // Kiểm tra xem thư mục có tồn tại không
                if (!Directory.Exists(_storageSettings.MonitorFolderPath))
                {
                    FileUploaded?.Invoke(_storageSettings.MonitorFolderPath, false, "Thư mục giám sát không tồn tại");
                    return;
                }
                
                // Tìm tất cả các file CSV trong thư mục
                string[] csvFiles = Directory.GetFiles(_storageSettings.MonitorFolderPath, "*.csv", SearchOption.AllDirectories);
                
                if (csvFiles.Length == 0)
                {
                    FileUploaded?.Invoke(_storageSettings.MonitorFolderPath, true, "Không tìm thấy file CSV mới nào");
                    return;
                }
                
                // Lọc các file chưa upload
                var newFiles = csvFiles.Where(file => !UploadedFiles.ContainsKey(file)).ToList();
                
                if (newFiles.Count == 0)
                {
                    FileUploaded?.Invoke(_storageSettings.MonitorFolderPath, true, "Không tìm thấy file CSV mới nào");
                    return;
                }
                
                // Upload từng file mới
                foreach (var filePath in newFiles)
                {
                    try
                    {
                        var result = await _blobService.UploadFileAsync(filePath);
                        
                        if (result.Success)
                        {
                            // Thêm vào danh sách đã upload
                            UploadedFiles[filePath] = DateTime.Now;
                            SettingsManager.Instance.SaveUploadedFilesLog();
                        }
                        
                        FileUploaded?.Invoke(filePath, result.Success, result.Message);
                    }
                    catch (Exception ex)
                    {
                        FileUploaded?.Invoke(filePath, false, $"Lỗi upload: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                FileUploaded?.Invoke(_storageSettings.MonitorFolderPath, false, $"Lỗi giám sát: {ex.Message}");
            }
        }
    }
} 