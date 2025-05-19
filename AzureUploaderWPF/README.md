# Azure CSV Uploader WPF

Ứng dụng WPF để upload file CSV lên Azure Blob Storage với các tính năng:
- Upload file CSV đơn lẻ hoặc thư mục
- Giám sát tự động thư mục và upload file mới
- Giao diện người dùng hiện đại

## Cấu trúc dự án

Dự án được tổ chức theo mô hình MVVM (Model-View-ViewModel) với các thành phần chính sau:

### Models
- `AzureStorageSettings.cs`: Lưu trữ cấu hình kết nối Azure Storage
- `UploadedFile.cs`: Mô hình thông tin file đã upload

### ViewModels
- `MainViewModel.cs`: ViewModel cho giao diện chính của ứng dụng

### Views
- `MainWindow.xaml`: Giao diện chính của ứng dụng
- `ProgressWindow.xaml`: Cửa sổ hiển thị tiến trình upload

### Services
- `AzureBlobService.cs`: Dịch vụ làm việc với Azure Blob Storage
- `CsvService.cs`: Dịch vụ xử lý tệp CSV
- `AutoUploadService.cs`: Dịch vụ tự động upload file

### Utils
- `GridLengthAnimation.cs`: Class tùy chỉnh để hỗ trợ animation cho GridLength
- `WebBrowserHelper.cs`: Hỗ trợ cấu hình WebBrowser
- `SettingsManager.cs`: Quản lý cấu hình ứng dụng

### Controls
- Các điều khiển tùy chỉnh (nếu cần)

## Quy trình tái cấu trúc

1. **Tách code theo thành phần**:
   - Di chuyển code từ các file chính sang các thành phần riêng biệt
   - Tách biệt logic nghiệp vụ khỏi giao diện người dùng

2. **Áp dụng mô hình MVVM**:
   - Tạo các ViewModel để quản lý trạng thái và logic
   - Binding dữ liệu giữa ViewModel và View

3. **Tối ưu hóa code**:
   - Áp dụng các nguyên tắc SOLID
   - Tách biệt các chức năng thành các dịch vụ độc lập

## Kế hoạch hoàn thành tái cấu trúc

1. **Hoàn thành MainWindow và ViewModel**:
   - Tạo View mới cho MainWindow
   - Kết nối MainWindow với MainViewModel

2. **Sử dụng bộ container DI**:
   - Thêm Microsoft.Extensions.DependencyInjection
   - Cấu hình DI trong App.xaml.cs

3. **Cập nhật App.xaml.cs**:
   - Khởi tạo các dịch vụ và ViewModel
   - Đặt DataContext cho MainWindow

4. **Testing và hoàn thiện**:
   - Kiểm tra lại tất cả các chức năng sau khi tái cấu trúc
   - Cải thiện UX nếu cần

## Lợi ích của cấu trúc mới

1. **Khả năng bảo trì tốt hơn**: Code được tổ chức thành các module nhỏ dễ quản lý
2. **Testability**: Các thành phần độc lập có thể được kiểm thử riêng
3. **Extensibility**: Dễ dàng thêm tính năng mới mà không ảnh hưởng đến code hiện tại
4. **Separation of Concerns**: Tách biệt rõ ràng trách nhiệm của từng thành phần
5. **Reusability**: Các dịch vụ và thành phần có thể được tái sử dụng trong các dự án khác