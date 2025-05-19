using System;

namespace AzureUploaderWPF.Models
{
    public class AzureStorageSettings
    {
        public string ConnectionString { get; set; } = string.Empty;

        public string ContainerName { get; set; } = string.Empty;
        
        /// <summary>
        /// Trạng thái Auto Upload
        /// </summary>
        public bool IsAutoUploadEnabled { get; set; }
        
        /// <summary>
        /// Đường dẫn thư mục giám sát
        /// </summary>
        public string MonitorFolderPath { get; set; } = string.Empty;
        
        /// <summary>
        /// Khoảng thời gian kiểm tra (phút)
        /// </summary>
        public int CheckIntervalMinutes { get; set; } = 5;
        
        /// <summary>
        /// Thời gian tự động dừng giám sát
        /// </summary>
        public DateTime? AutoStopTime { get; set; }
    }
} 