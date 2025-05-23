using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AzureUploaderWPF.Services
{
    public class CsvService
    {
        private string GetUniqueColumnName(string baseName, HashSet<string> existingNames)
        {
            string uniqueName = baseName;
            int counter = 1;
            
            while (existingNames.Contains(uniqueName))
            {
                uniqueName = $"{baseName}_{counter}";
                counter++;
            }
            
            return uniqueName;
        }

        private string SanitizeColumnName(string columnName)
        {
            // Loại bỏ các ký tự không hợp lệ cho PropertyPath
            string sanitized = Regex.Replace(columnName, @"[^\w\d]", "_");
            
            // Loại bỏ các dấu gạch dưới liên tiếp
            sanitized = Regex.Replace(sanitized, @"_{2,}", "_");
            
            // Loại bỏ dấu gạch dưới ở đầu và cuối
            sanitized = sanitized.Trim('_');
            
            // Đảm bảo tên cột không rỗng và không bắt đầu bằng số
            if (string.IsNullOrEmpty(sanitized) || char.IsDigit(sanitized[0]))
            {
                sanitized = "Col_" + sanitized;
            }
            
            return sanitized;
        }

        public DataTable ReadCsvToDataTable(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Không tìm thấy file: {filePath}");
                
            DataTable dataTable = new DataTable();
            var columnNames = new HashSet<string>(); // Để theo dõi tên cột đã sử dụng
            
            try
            {
                // Sử dụng Encoding.UTF8 để hỗ trợ tiếng Nhật
                using (var reader = new StreamReader(filePath, Encoding.UTF8))
                using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    MissingFieldFound = null,
                    BadDataFound = null,
                    Encoding = Encoding.UTF8
                }))
                {
                    // Đọc header
                    csv.Read();
                    csv.ReadHeader();
                    
                    // Tạo các cột cho DataTable với tên đã được làm sạch
                    foreach (string header in csv.HeaderRecord)
                    {
                        string sanitizedHeader = SanitizeColumnName(header);
                        
                        // Đảm bảo tên cột là duy nhất
                        string uniqueColumnName = GetUniqueColumnName(sanitizedHeader, columnNames);
                        columnNames.Add(uniqueColumnName);
                        
                        dataTable.Columns.Add(uniqueColumnName);
                        
                        // Lưu tên gốc để hiển thị
                        dataTable.Columns[uniqueColumnName].Caption = header;
                    }
                    
                    // Đọc các dòng dữ liệu
                    while (csv.Read())
                    {
                        DataRow row = dataTable.NewRow();
                        foreach (DataColumn column in dataTable.Columns)
                        {
                            // Sử dụng tên gốc để lấy dữ liệu
                            string originalHeader = column.Caption;
                            row[column.ColumnName] = csv.GetField(originalHeader);
                        }
                        dataTable.Rows.Add(row);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Lỗi khi đọc file CSV: {ex.Message}", ex);
            }
            
            return dataTable;
        }

        public bool IsCsvFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;
                
            string extension = Path.GetExtension(filePath).ToLower();
            return extension == ".csv";
        }
    }
} 