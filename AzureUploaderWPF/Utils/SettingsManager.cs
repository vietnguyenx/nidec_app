using AzureUploaderWPF.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AzureUploaderWPF.Utils
{
    public class SettingsManager
    {
        private const string SETTINGS_FILE = "AzureUploaderSettings.ini";
        private const string UPLOADED_FILES_LOG = "uploaded_files.log";
        
        private static readonly SettingsManager _instance = new SettingsManager();
        
        public static SettingsManager Instance => _instance;
        
        public AzureStorageSettings StorageSettings { get; private set; } = new AzureStorageSettings();
        
        public ConcurrentDictionary<string, DateTime> UploadedFiles { get; private set; } = new ConcurrentDictionary<string, DateTime>();
        
        private SettingsManager()
        {
            LoadSavedSettings();
            LoadUploadedFilesLog();
        }
        
        public void SaveSettings()
        {
            try
            {
                List<string> lines = new List<string>
                {
                    $"ConnectionString={StorageSettings.ConnectionString}",
                    $"ContainerName={StorageSettings.ContainerName}",
                    $"IsAutoUpload={StorageSettings.IsAutoUploadEnabled}",
                    $"MonitorFolder={StorageSettings.MonitorFolderPath}",
                    $"CheckInterval={StorageSettings.CheckIntervalMinutes}"
                };
                
                if (StorageSettings.AutoStopTime.HasValue)
                {
                    lines.Add($"AutoStopTime={StorageSettings.AutoStopTime.Value:yyyy-MM-dd HH:mm:ss}");
                }
                
                File.WriteAllLines(SETTINGS_FILE, lines);
            }
            catch (Exception ex)
            {
                // Xử lý ngoại lệ khi lưu cấu hình
                Console.WriteLine($"Lỗi khi lưu cấu hình: {ex.Message}");
            }
        }
        
        public void LoadSavedSettings()
        {
            try
            {
                if (File.Exists(SETTINGS_FILE))
                {
                    var lines = File.ReadAllLines(SETTINGS_FILE);
                    
                    foreach (var line in lines)
                    {
                        var parts = line.Split(new[] { '=' }, 2);
                        if (parts.Length == 2)
                        {
                            string key = parts[0].Trim();
                            string value = parts[1].Trim();
                            
                            switch (key)
                            {
                                case "ConnectionString":
                                    StorageSettings.ConnectionString = value;
                                    break;
                                case "ContainerName":
                                    StorageSettings.ContainerName = value;
                                    break;
                                case "IsAutoUpload":
                                    StorageSettings.IsAutoUploadEnabled = bool.TryParse(value, out bool isAutoUpload) && isAutoUpload;
                                    break;
                                case "MonitorFolder":
                                    StorageSettings.MonitorFolderPath = value;
                                    break;
                                case "CheckInterval":
                                    StorageSettings.CheckIntervalMinutes = int.TryParse(value, out int interval) ? interval : 5;
                                    break;
                                case "AutoStopTime":
                                    if (DateTime.TryParse(value, out DateTime stopTime))
                                    {
                                        StorageSettings.AutoStopTime = stopTime;
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Xử lý ngoại lệ khi đọc cấu hình
                Console.WriteLine($"Lỗi khi đọc cấu hình: {ex.Message}");
            }
        }

        public void SaveUploadedFilesLog()
        {
            try
            {
                var entries = UploadedFiles.Select(pair => $"{pair.Key}|{pair.Value:yyyy-MM-dd HH:mm:ss}");
                File.WriteAllLines(UPLOADED_FILES_LOG, entries);
            }
            catch (Exception ex)
            {
                // Xử lý ngoại lệ khi lưu danh sách
                Console.WriteLine($"Lỗi khi lưu danh sách file đã upload: {ex.Message}");
            }
        }

        public void LoadUploadedFilesLog()
        {
            try
            {
                if (File.Exists(UPLOADED_FILES_LOG))
                {
                    UploadedFiles.Clear();
                    var lines = File.ReadAllLines(UPLOADED_FILES_LOG);
                    
                    foreach (var line in lines)
                    {
                        var parts = line.Split('|');
                        if (parts.Length == 2 && DateTime.TryParse(parts[1], out DateTime uploadTime))
                        {
                            UploadedFiles[parts[0]] = uploadTime;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Xử lý ngoại lệ khi đọc danh sách
                Console.WriteLine($"Lỗi khi đọc danh sách file đã upload: {ex.Message}");
            }
        }
    }
} 