using System;

namespace AzureUploaderWPF.Models
{
    public class UploadedFile
    {
        /// <summary>
        /// Đường dẫn đầy đủ của file
        /// </summary>
        public string FilePath { get; set; } = string.Empty;
        
        /// <summary>
        /// Tên file 
        /// </summary>
        public string FileName => System.IO.Path.GetFileName(FilePath);
        
        /// <summary>
        /// Thời gian upload
        /// </summary>
        public DateTime UploadTime { get; set; }
        
        /// <summary>
        /// Kích thước file
        /// </summary>
        public long FileSize { get; set; }
        
        /// <summary>
        /// Trạng thái upload
        /// </summary>
        public UploadStatus Status { get; set; }
    }
    
    public enum UploadStatus
    {
        Success,
        Failed,
        InProgress
    }
} 