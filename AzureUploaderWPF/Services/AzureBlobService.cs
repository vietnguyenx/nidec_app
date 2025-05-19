using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using AzureUploaderWPF.Models;
using System;
using System.IO;
using System.Threading.Tasks;

namespace AzureUploaderWPF.Services
{
    public class AzureBlobService
    {
        private readonly AzureStorageSettings _settings;
        
        public AzureBlobService(AzureStorageSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }
        
        public async Task<(bool Success, string Message)> TestConnectionAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_settings.ConnectionString))
                    return (false, "Connection string không được để trống");
                
                if (string.IsNullOrWhiteSpace(_settings.ContainerName))
                    return (false, "Tên container không được để trống");
                
                // Tạo client để kết nối Azure Blob
                BlobServiceClient blobServiceClient = new BlobServiceClient(_settings.ConnectionString);
                
                // Lấy client cho container
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_settings.ContainerName);
                
                // Kiểm tra container có tồn tại không
                bool containerExists = await containerClient.ExistsAsync();
                
                if (!containerExists)
                {
                    // Tạo container nếu chưa tồn tại
                    await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);
                    return (true, $"Kết nối thành công. Container '{_settings.ContainerName}' đã được tạo mới.");
                }
                
                return (true, $"Kết nối thành công. Container '{_settings.ContainerName}' đã tồn tại.");
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi kết nối: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> UploadFileAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return (false, $"File không tồn tại: {filePath}");
                
                // Tạo client để kết nối Azure Blob
                BlobServiceClient blobServiceClient = new BlobServiceClient(_settings.ConnectionString);
                
                // Lấy client cho container
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_settings.ContainerName);
                
                // Đảm bảo container tồn tại
                await containerClient.CreateIfNotExistsAsync();
                
                // Lấy tên file
                string fileName = Path.GetFileName(filePath);
                
                // Lấy client cho blob
                BlobClient blobClient = containerClient.GetBlobClient(fileName);
                
                // Upload file
                using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    await blobClient.UploadAsync(fileStream, true);
                }
                
                return (true, $"Upload thành công: {fileName}");
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi upload: {ex.Message}");
            }
        }

        public BlobContainerClient GetContainerClient()
        {
            if (string.IsNullOrWhiteSpace(_settings.ConnectionString) || string.IsNullOrWhiteSpace(_settings.ContainerName))
                throw new InvalidOperationException("Connection string và tên container không được để trống");
                
            BlobServiceClient blobServiceClient = new BlobServiceClient(_settings.ConnectionString);
            return blobServiceClient.GetBlobContainerClient(_settings.ContainerName);
        }
    }
} 