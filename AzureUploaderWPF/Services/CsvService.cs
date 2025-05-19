using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Data;
using System.Globalization;
using System.IO;

namespace AzureUploaderWPF.Services
{
    public class CsvService
    {
        public DataTable ReadCsvToDataTable(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Không tìm thấy file: {filePath}");
                
            DataTable dataTable = new DataTable();
            
            try
            {
                using (var reader = new StreamReader(filePath))
                using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)))
                {
                    // Đọc header
                    csv.Read();
                    csv.ReadHeader();
                    
                    // Tạo các cột cho DataTable
                    foreach (string header in csv.HeaderRecord)
                    {
                        dataTable.Columns.Add(header);
                    }
                    
                    // Đọc các dòng dữ liệu
                    while (csv.Read())
                    {
                        DataRow row = dataTable.NewRow();
                        foreach (DataColumn column in dataTable.Columns)
                        {
                            row[column.ColumnName] = csv.GetField(column.ColumnName);
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