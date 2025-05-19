using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Azure.Storage.Blobs;
using CsvHelper.Configuration;
using CsvHelper;
using System.Configuration;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using Microsoft.Win32;
using System.Security.Permissions;
using System.Reflection;
using System.Windows.Media.Animation;
using WinForms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using DrawingColor = System.Drawing.Color;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;
using WpfRadioButton = System.Windows.Controls.RadioButton;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using System.Threading;
using System.Collections.Concurrent;
using AzureUploaderWPF.Views;

namespace AzureUploaderWPF
{
    // Class tùy chỉnh để hỗ trợ animation cho GridLength
    public class GridLengthAnimation : AnimationTimeline
    {
        private bool isCompleted;

        static GridLengthAnimation()
        {
            FromProperty = DependencyProperty.Register("From", typeof(GridLength),
                typeof(GridLengthAnimation));

            ToProperty = DependencyProperty.Register("To", typeof(GridLength),
                typeof(GridLengthAnimation));

            EasingFunctionProperty = DependencyProperty.Register("EasingFunction", 
                typeof(IEasingFunction), typeof(GridLengthAnimation));
        }

        public override Type TargetPropertyType => typeof(GridLength);

        protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

        public static readonly DependencyProperty FromProperty;
        public GridLength From
        {
            get => (GridLength)GetValue(FromProperty);
            set => SetValue(FromProperty, value);
        }

        public static readonly DependencyProperty ToProperty;
        public GridLength To
        {
            get => (GridLength)GetValue(ToProperty);
            set => SetValue(ToProperty, value);
        }

        public static readonly DependencyProperty EasingFunctionProperty;
        public IEasingFunction EasingFunction
        {
            get => (IEasingFunction)GetValue(EasingFunctionProperty);
            set => SetValue(EasingFunctionProperty, value);
        }

        public override object GetCurrentValue(object defaultOriginValue, 
                                              object defaultDestinationValue, 
                                              AnimationClock animationClock)
        {
            if (animationClock.CurrentProgress == null)
                return defaultOriginValue;

            double progress = animationClock.CurrentProgress.Value;
            
            if (EasingFunction != null)
                progress = EasingFunction.Ease(progress);

            var fromValue = From.IsAuto ? (GridLength)defaultOriginValue : From;
            var toValue = To.IsAuto ? (GridLength)defaultDestinationValue : To;

            if (fromValue.IsAuto)
                fromValue = new GridLength(0, toValue.IsStar ? GridUnitType.Star : GridUnitType.Pixel);
            
            if (toValue.IsAuto)
                toValue = new GridLength(0, fromValue.IsStar ? GridUnitType.Star : GridUnitType.Pixel);

            if (fromValue.GridUnitType != toValue.GridUnitType)
            {
                // Handle transition between different unit types if needed
                if (animationClock.CurrentProgress.Value >= 1)
                    return toValue;
                return fromValue;
            }

            var value = fromValue.Value + (toValue.Value - fromValue.Value) * progress;
            
            if (!isCompleted && progress >= 1.0)
            {
                isCompleted = true;
            }
            
            return new GridLength(value, fromValue.GridUnitType);
        }
    }

    public partial class MainWindow : Window
    {
        private bool isAutoUpload = false;
        private const string SETTINGS_FILE = "AzureUploaderSettings.ini";
        private bool isSidebarExpanded = true;
        private const double EXPANDED_WIDTH = 265;
        private const double COLLAPSED_WIDTH = 60;
        
        // Các biến cho tính năng tự động upload
        private string monitorFolderPath;
        private System.Threading.Timer autoUploadTimer;
        private ConcurrentDictionary<string, DateTime> uploadedFiles = new ConcurrentDictionary<string, DateTime>();
        private int checkIntervalMinutes = 5; // Mặc định kiểm tra mỗi 5 phút
        private bool isMonitoring = false;
        private const string UPLOADED_FILES_LOG = "uploaded_files.log";
        private DateTime? autoStopTime; // Thời điểm tự động dừng giám sát
        private System.Threading.Timer autoStopTimer; // Timer để tự động dừng giám sát

        public MainWindow()
        {
            // Cấu hình WebBrowser để sử dụng phiên bản IE mới nhất và tắt thông báo lỗi script
            SetWebBrowserFeatures();
            
            InitializeComponent();
            
            // Đăng ký sự kiện cho WebBrowser để tắt thông báo lỗi script
            ConfigureWebBrowser();
            
            // Mặc định hiển thị tab Upload
            ShowUploadTab();
            
            // Tải cấu hình lưu trữ (nếu có)
            LoadSavedSettings();
            
            // Đăng ký sự kiện KeyDown để xử lý phím Esc
            this.KeyDown += MainWindow_KeyDown;
            
            // Tải danh sách các file đã upload trước đó (nếu có)
            LoadUploadedFilesLog();
        }

        #region WebBrowser Configuration

        /// <summary>
        /// Tắt thông báo lỗi JavaScript và xử lý sự kiện cho WebBrowser
        /// </summary>
        private void ConfigureWebBrowser()
        {
            // Đăng ký sự kiện cho WebBrowser
            MonitorContent.Navigated += WebBrowser_Navigated;
            MonitorContent.LoadCompleted += WebBrowser_LoadCompleted;
        }

        private void WebBrowser_Navigated(object sender, NavigationEventArgs e)
        {
            // Truy cập vào đối tượng ActiveX và tắt các thông báo lỗi script
            dynamic activeX = MonitorContent.GetType().InvokeMember("ActiveXInstance",
                BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
                null, MonitorContent, null);

            if (activeX != null)
            {
                activeX.Silent = true;
            }
        }

        private void WebBrowser_LoadCompleted(object sender, NavigationEventArgs e)
        {
            // Truy cập document object để tùy chỉnh thêm nếu cần
            try
            {
                dynamic doc = MonitorContent.Document;
                if (doc != null)
                {
                    // Thêm xử lý sự kiện window.onerror để chặn lỗi script nếu cần
                    MonitorContent.InvokeScript("eval", new object[]
                    {
                        "window.onerror = function(message, url, line) { return true; };"
                    });
                }
            }
            catch { /* Bỏ qua lỗi nếu có */ }
        }

        /// <summary>
        /// Cấu hình Registry để WebBrowser sử dụng phiên bản IE mới nhất
        /// </summary>
        private static void SetWebBrowserFeatures()
        {
            // Không cần thay đổi nếu đang chạy ở chế độ 64-bit
            if (Environment.Is64BitProcess)
                SetBrowserFeatureControlKey("FEATURE_BROWSER_EMULATION", GetBrowserEmulationMode());
            else
                SetBrowserFeatureControlKey("FEATURE_BROWSER_EMULATION", GetBrowserEmulationMode());

            // Tắt các tính năng gây ra thông báo bảo mật/xác nhận
            SetBrowserFeatureControlKey("FEATURE_BLOCK_LMZ_SCRIPT", 0);
            SetBrowserFeatureControlKey("FEATURE_DISABLE_NAVIGATION_SOUNDS", 1);
            SetBrowserFeatureControlKey("FEATURE_SCRIPTURL_MITIGATION", 1);
            SetBrowserFeatureControlKey("FEATURE_SPELLCHECKING", 0);
            SetBrowserFeatureControlKey("FEATURE_STATUS_BAR_THROTTLING", 1);
            SetBrowserFeatureControlKey("FEATURE_WEBOC_DOCUMENT_ZOOM", 1);
            SetBrowserFeatureControlKey("FEATURE_LOCALMACHINE_LOCKDOWN", 0);
            SetBrowserFeatureControlKey("FEATURE_GPU_RENDERING", 1);
            SetBrowserFeatureControlKey("FEATURE_ADDON_MANAGEMENT", 0);
            SetBrowserFeatureControlKey("FEATURE_ENABLE_CLIPCHILDREN_OPTIMIZATION", 1);
        }

        private static UInt32 GetBrowserEmulationMode()
        {
            int browserVersion = 0;
            using (var ieKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Internet Explorer",
                RegistryKeyPermissionCheck.ReadSubTree,
                System.Security.AccessControl.RegistryRights.QueryValues))
            {
                var version = ieKey?.GetValue("svcVersion") ?? ieKey?.GetValue("Version") ?? "0.0.0.0";
                if (version != null)
                {
                    browserVersion = int.Parse(version.ToString().Split('.')[0]);
                }
            }

            // Giá trị Emulation cho IE từ 7-11
            switch (browserVersion)
            {
                case 7: return 7000;   // IE7
                case 8: return 8000;   // IE8
                case 9: return 9000;   // IE9
                case 10: return 10000; // IE10
                case 11: return 11001; // IE11 - Edge mode
                default: return 11001; // Mặc định là IE11 Edge mode
            }
        }

        private static void SetBrowserFeatureControlKey(string feature, UInt32 value)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Internet Explorer\Main\FeatureControl\" + feature,
                RegistryKeyPermissionCheck.ReadWriteSubTree))
            {
                // Thêm tên ứng dụng hiện tại vào registry
                key?.SetValue(Path.GetFileName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName), value, RegistryValueKind.DWord);
            }
        }

        #endregion

        private void MainWindow_KeyDown(object sender, WpfKeyEventArgs e)
        {
            // Kiểm tra nếu phím nhấn là Esc
            if (e.Key == Key.Escape)
            {
                // Kiểm tra nếu có popup nào đang hiển thị, thì đóng lại
                if (ConnectionStringHelpPopup.Visibility == Visibility.Visible || 
                    ContainerNameHelpPopup.Visibility == Visibility.Visible)
                {
                    CloseHelpPopup_Click(null, null);
                    e.Handled = true; // Đánh dấu sự kiện đã được xử lý
                }
            }
        }

        #region Help Popups

        private void ConnectionStringHelp_Click(object sender, RoutedEventArgs e)
        {
            // Hiển thị popup trợ giúp cho Connection String
            ConnectionStringHelpPopup.Visibility = Visibility.Visible;
            ContainerNameHelpPopup.Visibility = Visibility.Collapsed;
            
            // Đặt focus vào popup để có thể bắt được phím Esc
            ConnectionStringHelpPopup.Focus();
        }

        private void ContainerNameHelp_Click(object sender, RoutedEventArgs e)
        {
            // Hiển thị popup trợ giúp cho Container Name
            ContainerNameHelpPopup.Visibility = Visibility.Visible;
            ConnectionStringHelpPopup.Visibility = Visibility.Collapsed;
            
            // Đặt focus vào popup để có thể bắt được phím Esc
            ContainerNameHelpPopup.Focus();
        }

        private void CloseHelpPopup_Click(object sender, RoutedEventArgs e)
        {
            // Đóng tất cả các popup
            ConnectionStringHelpPopup.Visibility = Visibility.Collapsed;
            ContainerNameHelpPopup.Visibility = Visibility.Collapsed;
        }

        private void CloseContainerNameHelpPopup_Click(object sender, RoutedEventArgs e)
        {
            ContainerNameHelpPopup.Visibility = Visibility.Collapsed;
        }

        private void CloseTestConnectionPopup_Click(object sender, RoutedEventArgs e)
        {
            TestConnectionPopup.Visibility = Visibility.Collapsed;
        }
        
        /// <summary>
        /// Xử lý sự kiện khi nhấn nút Test Connection
        /// </summary>
        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            // Hiển thị popup kết quả kiểm tra
            ShowTestingConnectionState();
            
            try
            {
                // Kiểm tra các trường nhập liệu
                if (string.IsNullOrWhiteSpace(ConnectionStringTextBox.Text))
                {
                    ShowConnectionError("Connection string không được để trống!");
                    return;
                }
                
                if (string.IsNullOrWhiteSpace(ContainerNameTextBox.Text))
                {
                    ShowConnectionError("Container name không được để trống!");
                    return;
                }
                
                // Thử tạo kết nối đến Azure Storage và kiểm tra container
                try
                {
                    // Tạo BlobContainerClient từ connection string và container name
                    var containerClient = new BlobContainerClient(
                        ConnectionStringTextBox.Text, 
                        ContainerNameTextBox.Text);
                    
                    // Kiểm tra kết nối bằng cách gọi phương thức Exists
                    var exists = await containerClient.ExistsAsync();
                    
                    if (exists)
                    {
                        // Container tồn tại, tiếp tục kiểm tra quyền truy cập
                        try
                        {
                            // Lấy properties của container để kiểm tra quyền
                            await containerClient.GetPropertiesAsync();
                            
                            // Nếu không có lỗi, kết nối thành công
                            ShowConnectionSuccess($"Kết nối thành công đến container '{ContainerNameTextBox.Text}'.");
                            AddLogMsg("Test connection thành công.");
                        }
                        catch (Exception ex)
                        {
                            // Có lỗi khi truy cập properties, có thể là vấn đề quyền
                            ShowConnectionError($"Container tồn tại nhưng không có quyền truy cập: {ex.Message}");
                        }
                    }
                    else
                    {
                        // Container không tồn tại, báo lỗi
                        ShowConnectionError($"Container '{ContainerNameTextBox.Text}' không tồn tại. Vui lòng kiểm tra lại tên container.");
                        AddLogMsg($"Container '{ContainerNameTextBox.Text}' không tồn tại.");
                    }
                }
                catch (Exception ex)
                {
                    // Lỗi xác thực hoặc kết nối
                    ShowConnectionError($"Lỗi kết nối: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                ShowConnectionError($"Có lỗi xảy ra: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Hiển thị trạng thái đang kiểm tra kết nối
        /// </summary>
        private void ShowTestingConnectionState()
        {
            // Hiển thị popup
            TestConnectionPopup.Visibility = Visibility.Visible;
            
            // Cấu hình UI cho trạng thái đang kiểm tra
            StatusIcon.Text = "\uE10B"; // Biểu tượng đang xử lý
            StatusIconBorder.Background = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#E1F5FE"));
            StatusIcon.Foreground = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#03A9F4"));
            
            TestConnectionStatusMessage.Text = "Đang kiểm tra kết nối...";
            TestConnectionStatusMessage.Foreground = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#2C3E50"));
            
            TestConnectionDetails.Text = "Vui lòng đợi trong giây lát...";
            
            // Hiển thị thanh tiến trình
            TestConnectionProgress.IsIndeterminate = true;
            TestConnectionProgress.Visibility = Visibility.Visible;
        }
        
        /// <summary>
        /// Hiển thị kết quả kết nối thành công
        /// </summary>
        private void ShowConnectionSuccess(string details)
        {
            // Cấu hình UI cho trạng thái thành công
            StatusIcon.Text = "\uE73E"; // Biểu tượng tích
            StatusIconBorder.Background = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#EDF7ED"));
            StatusIcon.Foreground = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#4CAF50"));
            
            TestConnectionStatusMessage.Text = "Kết nối thành công!";
            TestConnectionStatusMessage.Foreground = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#2E7D32"));
            
            TestConnectionDetails.Text = details;
            
            // Ẩn thanh tiến trình
            TestConnectionProgress.IsIndeterminate = false;
            TestConnectionProgress.Visibility = Visibility.Collapsed;
        }
        
        /// <summary>
        /// Hiển thị lỗi kết nối
        /// </summary>
        private void ShowConnectionError(string errorMessage)
        {
            // Cấu hình UI cho trạng thái lỗi
            StatusIcon.Text = "\uE783"; // Biểu tượng lỗi
            StatusIconBorder.Background = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#FEECEB"));
            StatusIcon.Foreground = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#E53935"));
            
            TestConnectionStatusMessage.Text = "Kết nối thất bại!";
            TestConnectionStatusMessage.Foreground = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#C62828"));
            
            TestConnectionDetails.Text = errorMessage;
            
            // Ẩn thanh tiến trình
            TestConnectionProgress.IsIndeterminate = false;
            TestConnectionProgress.Visibility = Visibility.Collapsed;
            
            // Thêm log
            AddLogMsg("Test connection thất bại: " + errorMessage);
        }

        #endregion

        #region Auto Upload Feature

        /// <summary>
        /// Tải danh sách các file đã được upload từ log
        /// </summary>
        private void LoadUploadedFilesLog()
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UPLOADED_FILES_LOG);
                if (File.Exists(logPath))
                {
                    string[] lines = File.ReadAllLines(logPath);
                    foreach (string line in lines)
                    {
                        string[] parts = line.Split('|');
                        if (parts.Length >= 2)
                        {
                            string filePath = parts[0];
                            DateTime timestamp = DateTime.Parse(parts[1]);
                            uploadedFiles.TryAdd(filePath, timestamp);
                        }
                    }
                    AddLogMsg($"Đã tải {uploadedFiles.Count} file từ lịch sử upload.");
                }
            }
            catch (Exception ex)
            {
                AddLogMsg($"Lỗi khi tải lịch sử upload: {ex.Message}");
            }
        }

        /// <summary>
        /// Lưu danh sách các file đã được upload vào log
        /// </summary>
        private void SaveUploadedFilesLog()
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UPLOADED_FILES_LOG);
                List<string> lines = new List<string>();
                
                foreach (var pair in uploadedFiles)
                {
                    lines.Add($"{pair.Key}|{pair.Value}");
                }
                
                File.WriteAllLines(logPath, lines);
            }
            catch (Exception ex)
            {
                AddLogMsg($"Lỗi khi lưu lịch sử upload: {ex.Message}");
            }
        }

        /// <summary>
        /// Hiển thị cửa sổ cấu hình giám sát tự động
        /// </summary>
        private void ShowAutoUploadConfig_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ConnectionStringTextBox.Text) || string.IsNullOrWhiteSpace(ContainerNameTextBox.Text))
            {
                WpfMessageBox.Show("Vui lòng nhập đầy đủ thông tin kết nối Azure Storage trước khi cấu hình tự động upload!", 
                    "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            AutoUploadConfigPopup.Visibility = Visibility.Visible;
            
            // Nếu đang giám sát, cập nhật UI
            if (isMonitoring)
            {
                UpdateAutoUploadConfigUI();
            }
        }

        /// <summary>
        /// Cập nhật giao diện cửa sổ cấu hình giám sát tự động
        /// </summary>
        private void UpdateAutoUploadConfigUI()
        {
            // Cập nhật thông tin thư mục đang giám sát
            FolderPathTextBox.Text = monitorFolderPath;
            
            // Cập nhật interval
            TimeIntervalTextBox.Text = checkIntervalMinutes.ToString();
            
            // Cập nhật thời gian tự động dừng
            if (autoStopTime.HasValue && isMonitoring)
            {
                TimeSpan remainingTime = autoStopTime.Value - DateTime.Now;
                if (remainingTime.TotalMinutes > 0)
                {
                    int remainingHours = (int)Math.Ceiling(remainingTime.TotalHours);
                    AutoStopDurationTextBox.Text = remainingHours.ToString();
                }
                else
                {
                    AutoStopDurationTextBox.Text = "0";
                }
            }
            
            // Cập nhật trạng thái giám sát
            if (isMonitoring)
            {
                MonitoringStatusText.Text = "Đang giám sát";
                MonitoringStatusText.Foreground = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#2E7D32"));
                StartMonitoringButton.Content = "Dừng giám sát";
                
                // Hiển thị thời gian còn lại nếu có
                if (autoStopTime.HasValue)
                {
                    TimeSpan remainingTime = autoStopTime.Value - DateTime.Now;
                    if (remainingTime.TotalMinutes > 0)
                    {
                        MonitoringStatusText.Text = $"Đang giám sát (còn {FormatTimeSpan(remainingTime)})";
                    }
                }
            }
            else
            {
                MonitoringStatusText.Text = "Đã dừng";
                MonitoringStatusText.Foreground = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#C62828"));
                StartMonitoringButton.Content = "Bắt đầu giám sát";
            }
        }

        /// <summary>
        /// Định dạng TimeSpan thành chuỗi dễ đọc
        /// </summary>
        private string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
            {
                return $"{Math.Floor(ts.TotalHours):0} giờ {ts.Minutes} phút";
            }
            else
            {
                return $"{ts.Minutes} phút";
            }
        }

        /// <summary>
        /// Đóng cửa sổ cấu hình giám sát tự động
        /// </summary>
        private void CloseAutoUploadConfigPopup_Click(object sender, RoutedEventArgs e)
        {
            AutoUploadConfigPopup.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Xử lý sự kiện chọn thư mục để giám sát
        /// </summary>
        private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folderBrowserDialog = new WinForms.FolderBrowserDialog
                {
                    Description = "Chọn thư mục để giám sát các file CSV",
                    ShowNewFolderButton = false
                };

                if (folderBrowserDialog.ShowDialog() == WinForms.DialogResult.OK)
                {
                    string selectedFolder = folderBrowserDialog.SelectedPath;
                    FolderPathTextBox.Text = selectedFolder;
                    monitorFolderPath = selectedFolder;
                    
                    // Kiểm tra xem trong thư mục có file CSV không
                    string[] csvFiles = Directory.GetFiles(selectedFolder, "*.csv");
                    AddLogMsg($"Đã chọn thư mục giám sát: {selectedFolder}. Tìm thấy {csvFiles.Length} file CSV.");
                }
            }
            catch (Exception ex)
            {
                AddLogMsg($"Lỗi khi chọn thư mục: {ex.Message}");
                WpfMessageBox.Show($"Lỗi khi chọn thư mục: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Xử lý sự kiện bật/tắt giám sát tự động
        /// </summary>
        private async void StartMonitoringButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (isMonitoring)
                {
                    // Dừng giám sát
                    StopMonitoring();
                    AddLogMsg("Đã dừng giám sát tự động.");
                }
                else
                {
                    // Kiểm tra thông tin đầu vào
                    if (string.IsNullOrWhiteSpace(monitorFolderPath) || !Directory.Exists(monitorFolderPath))
                    {
                        WpfMessageBox.Show("Vui lòng chọn thư mục hợp lệ để giám sát!", 
                            "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    
                    // Kiểm tra khoảng thời gian
                    if (!int.TryParse(TimeIntervalTextBox.Text, out int minutes) || minutes < 1)
                    {
                        WpfMessageBox.Show("Khoảng thời gian kiểm tra phải là số phút hợp lệ (lớn hơn 0)!", 
                            "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    
                    // Kiểm tra kết nối đến Azure
                    try
                    {
                        var containerClient = new BlobContainerClient(
                            ConnectionStringTextBox.Text, 
                            ContainerNameTextBox.Text);
                        
                        bool exists = await containerClient.ExistsAsync();
                        if (!exists)
                        {
                            WpfMessageBox.Show($"Container '{ContainerNameTextBox.Text}' không tồn tại. Vui lòng kiểm tra lại!", 
                                "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        WpfMessageBox.Show($"Lỗi kết nối đến Azure: {ex.Message}", 
                            "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    
                    // Lưu cấu hình
                    checkIntervalMinutes = minutes;
                    
                    // Bắt đầu giám sát
                    StartMonitoring();
                    AddLogMsg($"Bắt đầu giám sát tự động thư mục: {monitorFolderPath}. Kiểm tra mỗi {checkIntervalMinutes} phút.");
                }
                
                // Cập nhật UI
                UpdateAutoUploadConfigUI();
            }
            catch (Exception ex)
            {
                AddLogMsg($"Lỗi: {ex.Message}");
                WpfMessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Bắt đầu giám sát tự động
        /// </summary>
        private void StartMonitoring()
        {
            // Khởi tạo timer
            int intervalMs = checkIntervalMinutes * 60 * 1000; // Chuyển phút sang mili giây
            autoUploadTimer = new System.Threading.Timer(AutoUploadTimerCallback, null, 0, intervalMs);
            
            // Thiết lập thời gian tự động dừng (nếu có)
            SetupAutoStop();
            
            // Cập nhật trạng thái
            isMonitoring = true;
            
            // Lưu cấu hình
            SaveSettings();
        }
        
        /// <summary>
        /// Thiết lập thời gian tự động dừng giám sát
        /// </summary>
        private void SetupAutoStop()
        {
            // Hủy timer cũ nếu có
            if (autoStopTimer != null)
            {
                autoStopTimer.Dispose();
                autoStopTimer = null;
            }
            
            // Reset thời gian tự động dừng
            autoStopTime = null;
            
            // Đọc giá trị từ textbox
            int autoStopHours;
            if (!string.IsNullOrWhiteSpace(AutoStopDurationTextBox.Text) && 
                int.TryParse(AutoStopDurationTextBox.Text, out autoStopHours) && 
                autoStopHours > 0)
            {
                // Tính thời điểm dừng
                autoStopTime = DateTime.Now.AddHours(autoStopHours);
                
                // Tạo timer để tự động dừng
                // Chuyển giờ sang milliseconds
                long delayMs = (long)autoStopHours * 60 * 60 * 1000;
                
                // Khởi tạo timer với callback một lần
                autoStopTimer = new System.Threading.Timer(AutoStopTimerCallback, null, delayMs, Timeout.Infinite);
                
                AddLogMsg($"Giám sát sẽ tự động dừng sau {autoStopHours} giờ (vào lúc {autoStopTime.Value.ToString("HH:mm:ss dd/MM/yyyy")})");
            }
            else
            {
                AddLogMsg("Giám sát sẽ tiếp tục cho đến khi dừng thủ công.");
            }
        }
        
        /// <summary>
        /// Callback khi timer tự động dừng kích hoạt
        /// </summary>
        private void AutoStopTimerCallback(object state)
        {
            try
            {
                // Dừng giám sát từ thread UI
                Dispatcher.InvokeAsync(() => 
                {
                    if (isMonitoring)
                    {
                        AddLogMsg($"Tự động dừng giám sát theo lịch đã cài đặt sau {AutoStopDurationTextBox.Text} giờ.");
                        StopMonitoring();
                        UpdateAutoUploadConfigUI();
                        
                        // Thông báo cho người dùng
                        WpfMessageBox.Show("Giám sát tự động đã dừng theo thời gian đã cài đặt!", 
                            "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.InvokeAsync(() => 
                {
                    AddLogMsg($"Lỗi khi dừng tự động: {ex.Message}");
                });
            }
            finally
            {
                // Đảm bảo timer được giải phóng
                if (autoStopTimer != null)
                {
                    autoStopTimer.Dispose();
                    autoStopTimer = null;
                }
            }
        }

        /// <summary>
        /// Dừng giám sát tự động
        /// </summary>
        private void StopMonitoring()
        {
            // Dừng timer
            if (autoUploadTimer != null)
            {
                autoUploadTimer.Change(Timeout.Infinite, Timeout.Infinite);
                autoUploadTimer.Dispose();
                autoUploadTimer = null;
            }
            
            // Dừng auto stop timer nếu có
            if (autoStopTimer != null)
            {
                autoStopTimer.Change(Timeout.Infinite, Timeout.Infinite);
                autoStopTimer.Dispose();
                autoStopTimer = null;
            }
            
            // Xóa thời gian dừng
            autoStopTime = null;
            
            // Cập nhật trạng thái
            isMonitoring = false;
            
            // Lưu danh sách các file đã upload
            SaveUploadedFilesLog();
        }

        /// <summary>
        /// Callback được gọi mỗi khi timer kích hoạt
        /// </summary>
        private async void AutoUploadTimerCallback(object state)
        {
            try
            {
                // Kiểm tra xem thư mục còn tồn tại không
                if (!Directory.Exists(monitorFolderPath))
                {
                    // Sử dụng Dispatcher để ghi log vì nó truy cập UI
                    await Dispatcher.InvokeAsync(() => 
                    {
                        AddLogMsg($"Thư mục giám sát không còn tồn tại: {monitorFolderPath}");
                    });
                    
                    StopMonitoring();
                    
                    // Cập nhật UI từ thread UI
                    await Dispatcher.InvokeAsync(() => 
                    {
                        UpdateAutoUploadConfigUI();
                        WpfMessageBox.Show("Thư mục giám sát không còn tồn tại. Đã dừng giám sát tự động!", 
                            "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                    
                    return;
                }
                
                // Tìm tất cả các file CSV trong thư mục
                string[] csvFiles = Directory.GetFiles(monitorFolderPath, "*.csv");
                
                if (csvFiles.Length == 0)
                {
                    await Dispatcher.InvokeAsync(() => 
                    {
                        AddLogMsg("Kiểm tra tự động: Không tìm thấy file CSV nào.");
                    });
                    return;
                }
                
                // Tìm các file mới (chưa được upload)
                List<string> newFiles = new List<string>();
                foreach (string filePath in csvFiles)
                {
                    // Kiểm tra xem file đã được upload chưa
                    if (!uploadedFiles.ContainsKey(filePath))
                    {
                        newFiles.Add(filePath);
                    }
                }
                
                if (newFiles.Count == 0)
                {
                    await Dispatcher.InvokeAsync(() => 
                    {
                        AddLogMsg("Kiểm tra tự động: Không có file CSV mới.");
                    });
                    return;
                }
                
                await Dispatcher.InvokeAsync(() => 
                {
                    AddLogMsg($"Tìm thấy {newFiles.Count} file CSV mới. Bắt đầu upload tự động...");
                });
                
                // Tạo container client với dữ liệu từ UI thread
                string connectionString = "";
                string containerName = "";
                
                await Dispatcher.InvokeAsync(() => 
                {
                    connectionString = ConnectionStringTextBox.Text;
                    containerName = ContainerNameTextBox.Text;
                });
                
                var containerClient = new BlobContainerClient(
                    connectionString, 
                    containerName);
                
                // Upload tất cả các file mới
                int successCount = 0;
                int failCount = 0;
                
                foreach (string filePath in newFiles)
                {
                    try
                    {
                        // Upload file
                        await UploadFileAsync(containerClient, filePath);
                        
                        // Đánh dấu file đã được upload
                        uploadedFiles.TryAdd(filePath, DateTime.Now);
                        
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        await Dispatcher.InvokeAsync(() => 
                        {
                            AddLogMsg($"Lỗi khi upload tự động file {Path.GetFileName(filePath)}: {ex.Message}");
                        });
                        failCount++;
                    }
                }
                
                await Dispatcher.InvokeAsync(() => 
                {
                    AddLogMsg($"Upload tự động hoàn tất. Thành công: {successCount}, Thất bại: {failCount}");
                });
                
                // Lưu danh sách các file đã upload
                SaveUploadedFilesLog();
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => 
                {
                    AddLogMsg($"Lỗi trong quá trình kiểm tra tự động: {ex.Message}");
                });
            }
        }

        #endregion

        private void SaveSettings()
        {
            try
            {
                if (RememberSettingsCheckBox.IsChecked == true)
                {
                    List<string> settings = new List<string>
                    {
                        ConnectionStringTextBox.Text,
                        ContainerNameTextBox.Text,
                        monitorFolderPath ?? "",
                        checkIntervalMinutes.ToString(),
                        isMonitoring.ToString(),
                        autoStopTime.HasValue ? autoStopTime.Value.ToString("o") : ""
                    };
                    
                    string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SETTINGS_FILE);
                    File.WriteAllLines(settingsPath, settings);
                    AddLogMsg("Connection settings saved for future use.");
                }
                else
                {
                    // Xóa file cấu hình nếu không chọn lưu
                    string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SETTINGS_FILE);
                    if (File.Exists(settingsPath))
                    {
                        File.Delete(settingsPath);
                        AddLogMsg("Saved connection settings have been removed.");
                    }
                }
            }
            catch (Exception ex)
            {
                AddLogMsg($"Error saving settings: {ex.Message}");
            }
        }

        private void LoadSavedSettings()
        {
            try
            {
                string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SETTINGS_FILE);
                if (File.Exists(settingsPath))
                {
                    string[] lines = File.ReadAllLines(settingsPath);
                    if (lines.Length >= 2)
                    {
                        ConnectionStringTextBox.Text = lines[0];
                        ContainerNameTextBox.Text = lines[1];
                        RememberSettingsCheckBox.IsChecked = true;
                        AddLogMsg("Loaded saved connection settings.");
                        
                        // Tải cấu hình giám sát tự động nếu có
                        if (lines.Length >= 5)
                        {
                            monitorFolderPath = lines[2];
                            if (int.TryParse(lines[3], out int interval))
                            {
                                checkIntervalMinutes = interval;
                            }
                            
                            // Thiết lập AutoStopDurationTextBox
                            if (lines.Length >= 6 && !string.IsNullOrEmpty(lines[5]))
                            {
                                try
                                {
                                    autoStopTime = DateTime.Parse(lines[5]);
                                    
                                    // Tính thời gian còn lại
                                    TimeSpan remainingTime = autoStopTime.Value - DateTime.Now;
                                    if (remainingTime.TotalMinutes > 0)
                                    {
                                        int remainingHours = (int)Math.Ceiling(remainingTime.TotalHours);
                                        AutoStopDurationTextBox.Text = remainingHours.ToString();
                                    }
                                    else
                                    {
                                        // Nếu đã hết thời gian, không khôi phục giám sát
                                        autoStopTime = null;
                                        AutoStopDurationTextBox.Text = "24"; // Mặc định
                                    }
                                }
                                catch
                                {
                                    autoStopTime = null;
                                    AutoStopDurationTextBox.Text = "24"; // Mặc định
                                }
                            }
                            
                            if (bool.TryParse(lines[4], out bool monitoring) && monitoring)
                            {
                                // Khởi động lại giám sát tự động nếu đã bật trước đó và chưa hết thời gian
                                if (Directory.Exists(monitorFolderPath) && 
                                    (!autoStopTime.HasValue || autoStopTime.Value > DateTime.Now))
                                {
                                    StartMonitoring();
                                    AddLogMsg($"Khởi động lại giám sát tự động cho thư mục: {monitorFolderPath}");
                                    
                                    if (autoStopTime.HasValue)
                                    {
                                        TimeSpan remainingTime = autoStopTime.Value - DateTime.Now;
                                        AddLogMsg($"Giám sát sẽ tự động dừng sau {FormatTimeSpan(remainingTime)} (vào lúc {autoStopTime.Value.ToString("HH:mm:ss dd/MM/yyyy")})");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddLogMsg($"Error loading settings: {ex.Message}");
            }
        }

        private void ShowUploadTab()
        {
            UploadContent.Visibility = Visibility.Visible;
            MonitorContent.Visibility = Visibility.Collapsed;
            UploadTabButton.IsChecked = true;
            MonitorTabButton.IsChecked = false;
            
            // Đảm bảo đóng tất cả các popup khi chuyển tab
            ConnectionStringHelpPopup.Visibility = Visibility.Collapsed;
            ContainerNameHelpPopup.Visibility = Visibility.Collapsed;
        }

        private void ShowMonitorTab()
        {
            UploadContent.Visibility = Visibility.Collapsed;
            MonitorContent.Visibility = Visibility.Visible;
            UploadTabButton.IsChecked = false;
            MonitorTabButton.IsChecked = true;
            
            // Đảm bảo đóng tất cả các popup khi chuyển tab
            ConnectionStringHelpPopup.Visibility = Visibility.Collapsed;
            ContainerNameHelpPopup.Visibility = Visibility.Collapsed;
        }

        private void UploadTabButton_Click(object sender, RoutedEventArgs e)
        {
            ShowUploadTab();
        }

        private void MonitorTabButton_Click(object sender, RoutedEventArgs e)
        {
            ShowMonitorTab();
        }

        private void AddLogMsg(string message)
        {
            // Kiểm tra xem phương thức này có được gọi từ UI thread không
            if (Dispatcher.CheckAccess())
            {
                LogListBox.Items.Add($"[{DateTime.Now.ToString("HH:mm:ss")}] {message}");
                LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
            }
            else
            {
                // Nếu không, sử dụng Dispatcher để chuyển sang UI thread
                Dispatcher.InvokeAsync(() =>
                {
                    LogListBox.Items.Add($"[{DateTime.Now.ToString("HH:mm:ss")}] {message}");
                    LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
                });
            }
        }

        private async void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ConnectionStringTextBox.Text) || string.IsNullOrWhiteSpace(ContainerNameTextBox.Text))
                {
                    WpfMessageBox.Show("Vui lòng nhập đầy đủ thông tin kết nối Azure Storage!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Lưu cấu hình nếu được chọn
                SaveSettings();

                // Tạo container client để kiểm tra xem container có tồn tại
                BlobContainerClient containerClient = new BlobContainerClient(ConnectionStringTextBox.Text, ContainerNameTextBox.Text);
                
                // Kiểm tra xem container có tồn tại không
                bool containerExists = await containerClient.ExistsAsync();
                if (!containerExists)
                {
                    WpfMessageBox.Show($"Container '{ContainerNameTextBox.Text}' không tồn tại. Vui lòng kiểm tra lại tên container!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    AddLogMsg($"Upload thất bại: Container '{ContainerNameTextBox.Text}' không tồn tại.");
                    return;
                }

                Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv",
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string filePath = openFileDialog.FileName;
                    AddLogMsg($"Selected file: {filePath}");

                    // 1. Hiển thị preview
                    DataTable table = ReadCsvToDataTable(filePath);
                    PreviewDataGrid.ItemsSource = table.DefaultView;

                    // 2. Upload
                    AddLogMsg("STARTING MANUAL UPLOAD...");
                    
                    await UploadFileAsync(containerClient, filePath);
                    AddLogMsg("MANUAL UPLOAD COMPLETED.");
                }
                else
                {
                    AddLogMsg("No file selected.");
                }
            }
            catch (Exception ex)
            {
                AddLogMsg($"Error: {ex.Message}");
                WpfMessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Xử lý sự kiện khi nhấn nút Upload Folder (tự động upload nhiều file CSV)
        /// </summary>
        private async void UploadFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ConnectionStringTextBox.Text) || string.IsNullOrWhiteSpace(ContainerNameTextBox.Text))
                {
                    WpfMessageBox.Show("Vui lòng nhập đầy đủ thông tin kết nối Azure Storage!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Lưu cấu hình nếu được chọn
                SaveSettings();

                // Tạo container client để kiểm tra xem container có tồn tại
                BlobContainerClient containerClient = new BlobContainerClient(ConnectionStringTextBox.Text, ContainerNameTextBox.Text);
                
                // Kiểm tra xem container có tồn tại không
                bool containerExists = await containerClient.ExistsAsync();
                if (!containerExists)
                {
                    WpfMessageBox.Show($"Container '{ContainerNameTextBox.Text}' không tồn tại. Vui lòng kiểm tra lại tên container!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    AddLogMsg($"Upload thất bại: Container '{ContainerNameTextBox.Text}' không tồn tại.");
                    return;
                }

                // Hiển thị dialog chọn thư mục
                var folderBrowserDialog = new WinForms.FolderBrowserDialog
                {
                    Description = "Chọn thư mục chứa các file CSV",
                    ShowNewFolderButton = false
                };

                if (folderBrowserDialog.ShowDialog() == WinForms.DialogResult.OK)
                {
                    string selectedFolder = folderBrowserDialog.SelectedPath;
                    AddLogMsg($"Selected folder: {selectedFolder}");

                    // Tìm tất cả các file CSV trong thư mục
                    string[] csvFiles = Directory.GetFiles(selectedFolder, "*.csv");
                    
                    if (csvFiles.Length == 0)
                    {
                        WpfMessageBox.Show("Không tìm thấy file CSV nào trong thư mục này!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                        AddLogMsg("No CSV files found in the selected folder.");
                        return;
                    }

                    AddLogMsg($"Found {csvFiles.Length} CSV files in folder.");
                    
                    // Confirm với người dùng
                    MessageBoxResult result = WpfMessageBox.Show(
                        $"Tìm thấy {csvFiles.Length} file CSV. Bạn có muốn upload tất cả?", 
                        "Xác nhận upload", 
                        MessageBoxButton.YesNo, 
                        MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        // Hiển thị preview file đầu tiên
                        if (csvFiles.Length > 0)
                        {
                            DataTable table = ReadCsvToDataTable(csvFiles[0]);
                            PreviewDataGrid.ItemsSource = table.DefaultView;
                        }

                        // Bắt đầu upload
                        AddLogMsg("STARTING BATCH UPLOAD...");
                        int successCount = 0;
                        int failCount = 0;

                        // Progress dialog
                        var progressWindow = new ProgressWindow(csvFiles.Length);
                        progressWindow.Owner = this;
                        progressWindow.Show();

                        // Upload tuần tự từng file
                        for (int i = 0; i < csvFiles.Length; i++)
                        {
                            try
                            {
                                string filePath = csvFiles[i];
                                string fileName = Path.GetFileName(filePath);
                                
                                // Cập nhật thông tin tiến trình
                                progressWindow.UpdateProgress(i + 1, fileName);
                                
                                // Upload file
                                await UploadFileAsync(containerClient, filePath);
                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                AddLogMsg($"Error uploading file {Path.GetFileName(csvFiles[i])}: {ex.Message}");
                                failCount++;
                            }
                        }

                        // Đóng cửa sổ tiến trình
                        progressWindow.Close();

                        // Hiển thị thông báo kết quả
                        AddLogMsg($"BATCH UPLOAD COMPLETED. Success: {successCount}, Failed: {failCount}");
                        WpfMessageBox.Show(
                            $"Upload hoàn tất.\nThành công: {successCount} file\nThất bại: {failCount} file", 
                            "Kết quả upload",
                            MessageBoxButton.OK, 
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        AddLogMsg("Batch upload canceled by user.");
                    }
                }
            }
            catch (Exception ex)
            {
                AddLogMsg($"Error: {ex.Message}");
                WpfMessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private DataTable ReadCsvToDataTable(string filePath)
        {
            var dt = new DataTable();
            using (var reader = new StreamReader(filePath))
            using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,
                BadDataFound = null
            }))
            {
                using (var dr = new CsvDataReader(csv))
                {
                    dt.Load(dr);
                }
            }
            return dt;
        }

        private async Task UploadFileAsync(BlobContainerClient containerClient, string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            BlobClient blobClient = containerClient.GetBlobClient(fileName);

            try
            {
                AddLogMsg($"Uploading {fileName}...");
                using FileStream fileStream = File.OpenRead(filePath);
                await blobClient.UploadAsync(fileStream, true);
                AddLogMsg($"{fileName} uploaded successfully.");
            }
            catch (Exception ex)
            {
                AddLogMsg($"Error uploading {fileName}: {ex.Message}");
                throw;
            }
        }

        private void AboutMenu_Click(object sender, RoutedEventArgs e)
        {
            WpfMessageBox.Show("Azure CSV Uploader v1.0\nDeveloped by FPTU HCM", "About");
        }

        private void DocsMenu_Click(object sender, RoutedEventArgs e)
        {
            WpfMessageBox.Show("Documentation is not available yet.", "Documentation");
        }

        private void ThemeMenu_Click(object sender, RoutedEventArgs e)
        {
            WpfMessageBox.Show("Theme selection is not implemented yet.", "Theme");
        }

        private void ExitMenu_Click(object sender, RoutedEventArgs e)
        {
            WpfApplication.Current.Shutdown();
        }

        /// <summary>
        /// Xử lý sự kiện khi nhấn nút toggle sidebar
        /// </summary>
        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            if (isSidebarExpanded)
            {
                // Thu nhỏ sidebar
                CollapseSidebar();
            }
            else
            {
                // Mở rộng sidebar
                ExpandSidebar();
            }
        }

        /// <summary>
        /// Thu nhỏ sidebar với animation mượt mà
        /// </summary>
        private void CollapseSidebar()
        {
            // Tạo animation mượt mà cho GridLength
            var animation = new GridLengthAnimation
            {
                From = new GridLength(EXPANDED_WIDTH),
                To = new GridLength(COLLAPSED_WIDTH),
                Duration = TimeSpan.FromSeconds(0.3),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            
            // Thay đổi trạng thái
            isSidebarExpanded = false;
            
            // Thay đổi nút toggle với animation
            var rotateAnimation = new DoubleAnimation
            {
                From = 0,
                To = 180,
                Duration = TimeSpan.FromSeconds(0.3),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            
            // Tạo animation fade-out cho các thành phần cần ẩn
            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromSeconds(0.15),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            
            fadeOut.Completed += (s, e) =>
            {
                // Ẩn các thành phần không cần thiết sau khi fade out
                ManagementLabel.Visibility = Visibility.Collapsed;
                SettingsLabel.Visibility = Visibility.Collapsed;
                FooterPanel.Visibility = Visibility.Collapsed;
                LogoPanel.Visibility = Visibility.Collapsed;
                
                // Thay đổi nút toggle
                ((TextBlock)ToggleSidebarButton.Content).Text = "\uE701";
                
                // Căn chỉnh lại các icon cho chế độ thu nhỏ
                foreach (WpfRadioButton rb in NavigationPanel.Children.OfType<WpfRadioButton>())
                {
                    // Lưu trữ nội dung gốc trong Tag
                    if (rb.Tag != null && rb.Tag.ToString().Length == 1)
                    {
                        // Đây là icon Unicode, không thay đổi
                    }
                    else
                    {
                        rb.Content = "";
                    }
                    
                    // Căn giữa các nút khi thu nhỏ
                    rb.HorizontalContentAlignment = WpfHorizontalAlignment.Center;
                    rb.Padding = new Thickness(0);
                }
            };
            
            // Áp dụng animation fade-out cho các thành phần
            ManagementLabel.BeginAnimation(OpacityProperty, fadeOut);
            SettingsLabel.BeginAnimation(OpacityProperty, fadeOut);
            FooterPanel.BeginAnimation(OpacityProperty, fadeOut);
            LogoPanel.BeginAnimation(OpacityProperty, fadeOut);
            
            // Fade-out nội dung text trong các RadioButton
            foreach (WpfRadioButton rb in NavigationPanel.Children.OfType<WpfRadioButton>())
            {
                if (rb.Content != null && rb.Content.ToString() != "")
                {
                    // Lưu trữ nội dung hiện tại để khôi phục sau này
                    if (rb.Tag != null && rb.Tag.ToString().Length > 1)
                    {
                        // Nếu có thêm thông tin trong Tag, chỉ áp dụng animation opacity
                        rb.BeginAnimation(OpacityProperty, fadeOut);
                    }
                }
            }
            
            // Thực hiện animation thay đổi kích thước
            SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, animation);
        }

        /// <summary>
        /// Mở rộng sidebar với animation mượt mà
        /// </summary>
        private void ExpandSidebar()
        {
            // Tạo animation mượt mà cho GridLength
            var animation = new GridLengthAnimation
            {
                From = new GridLength(COLLAPSED_WIDTH),
                To = new GridLength(EXPANDED_WIDTH),
                Duration = TimeSpan.FromSeconds(0.3),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            
            // Thay đổi trạng thái
            isSidebarExpanded = true;
            
            // Thay đổi nút toggle trong quá trình animation
            ((TextBlock)ToggleSidebarButton.Content).Text = "\uE700";
            
            // Đặt lại nội dung cho các nút điều hướng
            UploadTabButton.Content = "Upload File";
            UploadTabButton.Opacity = 0;
            
            MonitorTabButton.Content = "Error Monitor";
            MonitorTabButton.Opacity = 0;
            
            // Tìm và đặt lại nội dung cho các RadioButton khác
            foreach (WpfRadioButton rb in NavigationPanel.Children.OfType<WpfRadioButton>())
            {
                if (rb != UploadTabButton && rb != MonitorTabButton)
                {
                    if (rb.Tag.ToString() == "\uE713")
                        rb.Content = "Configuration";
                    else if (rb.Tag.ToString() == "\uE77B")
                        rb.Content = "User Profile";
                    rb.Opacity = 0;
                }
                
                // Phục hồi padding và căn lề ban đầu
                rb.HorizontalContentAlignment = WpfHorizontalAlignment.Left;
                rb.Padding = new Thickness(20, 0, 20, 0);
            }
            
            // Hiện lại các thành phần trước khi bắt đầu animation fade-in
            ManagementLabel.Visibility = Visibility.Visible;
            ManagementLabel.Opacity = 0;
            
            SettingsLabel.Visibility = Visibility.Visible;
            SettingsLabel.Opacity = 0;
            
            FooterPanel.Visibility = Visibility.Visible;
            FooterPanel.Opacity = 0;
            
            LogoPanel.Visibility = Visibility.Visible;
            LogoPanel.Opacity = 0;
            
            // Animation đã hoàn thành một nửa, thực hiện animation fade-in
            animation.Completed += (s, e) =>
            {
                // Tạo animation fade-in
                var fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromSeconds(0.2),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                
                // Áp dụng animation fade-in cho các thành phần
                ManagementLabel.BeginAnimation(OpacityProperty, fadeIn);
                SettingsLabel.BeginAnimation(OpacityProperty, fadeIn);
                FooterPanel.BeginAnimation(OpacityProperty, fadeIn);
                LogoPanel.BeginAnimation(OpacityProperty, fadeIn);
                
                // Fade-in nội dung text trong các RadioButton
                foreach (WpfRadioButton rb in NavigationPanel.Children.OfType<WpfRadioButton>())
                {
                    rb.BeginAnimation(OpacityProperty, fadeIn);
                }
            };
            
            // Thực hiện animation thay đổi kích thước
            SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, animation);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Dừng giám sát khi đóng ứng dụng
            if (isMonitoring)
            {
                StopMonitoring();
                AddLogMsg("Đã dừng giám sát tự động do đóng ứng dụng.");
            }
            
            // Lưu cấu hình
            SaveSettings();
            
            base.OnClosing(e);
        }
    }
}
