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
using Microsoft.Web.WebView2.Wpf;

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
            InitializeComponent();
            
            // Mặc định hiển thị tab Upload
            ShowUploadTab();
            
            // Tải cấu hình lưu trữ (nếu có)
            LoadSavedSettings();
            
            // Đăng ký sự kiện KeyDown để xử lý phím Esc
            this.KeyDown += MainWindow_KeyDown;
            
            // Tải danh sách các file đã upload trước đó (nếu có)
            LoadUploadedFilesLog();

            // Khởi tạo WebView2
            InitializeWebView2();
        }

        private async void InitializeWebView2()
        {
            try
            {
                await MonitorContent.EnsureCoreWebView2Async();
                
                // Cấu hình WebView2
                MonitorContent.CoreWebView2.Settings.IsScriptEnabled = true;
                MonitorContent.CoreWebView2.Settings.AreDevToolsEnabled = true;
                MonitorContent.CoreWebView2.Settings.IsWebMessageEnabled = true;
                
                // Đăng ký sự kiện
                MonitorContent.NavigationCompleted += WebView2_NavigationCompleted;
                
                // Tải trang web
                MonitorContent.Source = new Uri("https://nidec-board-fthccdafgja6fveq.eastus-01.azurewebsites.net/Dashboard");
            }
            catch (Exception ex)
            {
                AddLogMsg($"Lỗi khởi tạo WebView2: {ex.Message}");
            }
        }

        #region WebBrowser Configuration

        /// <summary>
        /// Cấu hình WebView2
        /// </summary>
        private void ConfigureWebBrowser()
        {
            // Đăng ký sự kiện cho WebView2
            MonitorContent.NavigationCompleted += WebView2_NavigationCompleted;
        }

        private async void WebView2_NavigationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            try
            {
                // Thêm xử lý sự kiện window.onerror để chặn lỗi script nếu cần
                await MonitorContent.CoreWebView2.ExecuteScriptAsync(
                    "window.onerror = function(message, url, line) { return true; };"
                );
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
                key?.SetValue(Path.GetFileName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName), value, RegistryValueKind.DWord);
            }
        }

        #endregion

        private void MainWindow_KeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (ConnectionStringHelpPopup.Visibility == Visibility.Visible || 
                    ContainerNameHelpPopup.Visibility == Visibility.Visible)
                {
                    CloseHelpPopup_Click(null, null);
                    e.Handled = true; 
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
                    ShowConnectionError("Connection string cannot be empty!");
                    return;
                }
                
                if (string.IsNullOrWhiteSpace(ContainerNameTextBox.Text))
                {
                    ShowConnectionError("Container name cannot be empty!");
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
                            ShowConnectionSuccess($"Connection successful to container '{ContainerNameTextBox.Text}'.");
                            AddLogMsg("Test connection successful.");
                        }
                        catch (Exception ex)
                        {
                            // Có lỗi khi truy cập properties, có thể là vấn đề quyền
                            ShowConnectionError($"Container exists but no access: {ex.Message}");
                        }
                    }
                    else
                    {
                        // Container không tồn tại, báo lỗi
                        ShowConnectionError($"Container '{ContainerNameTextBox.Text}' does not exist. Please check the container name.");
                        AddLogMsg($"Container '{ContainerNameTextBox.Text}' does not exist.");
                    }
                }
                catch (Exception ex)
                {
                    // Lỗi xác thực hoặc kết nối
                    ShowConnectionError($"Connection error: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                ShowConnectionError($"An error occurred: {ex.Message}");
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
            
            TestConnectionStatusMessage.Text = "Checking connection...";
            TestConnectionStatusMessage.Foreground = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#2C3E50"));
            
            TestConnectionDetails.Text = "Please wait for a moment...";
            
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
            
            TestConnectionStatusMessage.Text = "Connection successful!";
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
            
            TestConnectionStatusMessage.Text = "Connection failed!";
            TestConnectionStatusMessage.Foreground = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#C62828"));
            
            TestConnectionDetails.Text = errorMessage;
            
            // Ẩn thanh tiến trình
            TestConnectionProgress.IsIndeterminate = false;
            TestConnectionProgress.Visibility = Visibility.Collapsed;
            
            // Thêm log
            AddLogMsg("Test connection failed: " + errorMessage);
        }

        #endregion

        #region Auto Upload Feature

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
                    AddLogMsg($"Loaded {uploadedFiles.Count} files from upload history.");
                }
            }
            catch (Exception ex)
            {
                AddLogMsg($"Error loading upload history: {ex.Message}");
            }
        }

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
                AddLogMsg($"Error saving upload history: {ex.Message}");
            }
        }

        private void ShowAutoUploadConfig_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ConnectionStringTextBox.Text) || string.IsNullOrWhiteSpace(ContainerNameTextBox.Text))
            {
                WpfMessageBox.Show("Please enter complete Azure Storage connection information!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            AutoUploadConfigPopup.Visibility = Visibility.Visible;
            
            if (isMonitoring)
            {
                UpdateAutoUploadConfigUI();
            }
        }

        private void UpdateAutoUploadConfigUI()
        {
            FolderPathTextBox.Text = monitorFolderPath;
            
            TimeIntervalTextBox.Text = checkIntervalMinutes.ToString();
            
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
            
            if (isMonitoring)
            {
                MonitoringStatusText.Text = "Monitoring";
                MonitoringStatusText.Foreground = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#4CAF50"));
                StartMonitoringButton.Visibility = Visibility.Collapsed;
                StopMonitoringButton.Visibility = Visibility.Visible;
                
                var statusDot = ((Grid)MonitoringStatusText.Parent).Children[0] as Border;
                if (statusDot != null)
                {
                    statusDot.Background = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#4CAF50"));
                }
                
                if (autoStopTime.HasValue)
                {
                    TimeSpan remainingTime = autoStopTime.Value - DateTime.Now;
                    if (remainingTime.TotalMinutes > 0)
                    {
                        MonitoringStatusText.Text = $"Monitoring (remain {FormatTimeSpan(remainingTime)})";
                    }
                }
            }
            else
            {
                MonitoringStatusText.Text = "Stopped";
                MonitoringStatusText.Foreground = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#C62828"));
                StartMonitoringButton.Visibility = Visibility.Visible;
                StopMonitoringButton.Visibility = Visibility.Collapsed;
                
                var statusDot = ((Grid)MonitoringStatusText.Parent).Children[0] as Border;
                if (statusDot != null)
                {
                    statusDot.Background = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#C62828"));
                }
            }
        }

        private string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
            {
                return $"{Math.Floor(ts.TotalHours):0} hours {ts.Minutes} minutes";
            }
            else
            {
                return $"{ts.Minutes} minutes";
            }
        }

        private void CloseAutoUploadConfigPopup_Click(object sender, RoutedEventArgs e)
        {
            AutoUploadConfigPopup.Visibility = Visibility.Collapsed;
        }

        private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folderBrowserDialog = new WinForms.FolderBrowserDialog
                {
                    Description = "Select folder containing CSV files",
                    ShowNewFolderButton = false
                };

                if (folderBrowserDialog.ShowDialog() == WinForms.DialogResult.OK)
                {
                    string selectedFolder = folderBrowserDialog.SelectedPath;
                    FolderPathTextBox.Text = selectedFolder;
                    monitorFolderPath = selectedFolder;
                    
                    string[] csvFiles = Directory.GetFiles(selectedFolder, "*.csv");
                    AddLogMsg($"Selected monitoring folder: {selectedFolder}. Found {csvFiles.Length} CSV files.");
                }
            }
            catch (Exception ex)
            {
                AddLogMsg($"Error selecting folder: {ex.Message}");
                WpfMessageBox.Show($"Error selecting folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StartMonitoringButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(FolderPathTextBox.Text))
                {
                    WpfMessageBox.Show("Please select the folder to monitor!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!int.TryParse(TimeIntervalTextBox.Text, out int interval) || interval < 1)
                {
                    WpfMessageBox.Show("The test interval must be greater than or equal to 1 minute!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                checkIntervalMinutes = interval;
                monitorFolderPath = FolderPathTextBox.Text;

                StartMonitoring();

                StartMonitoringButton.Visibility = Visibility.Collapsed;
                StopMonitoringButton.Visibility = Visibility.Visible;
                MonitoringStatusText.Text = "Monitoring";
                MonitoringStatusText.Foreground = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#4CAF50"));
                
                var statusDot = ((Grid)MonitoringStatusText.Parent).Children[0] as Border;
                if (statusDot != null)
                {
                    statusDot.Background = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#4CAF50"));
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Error starting monitoring: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopMonitoringButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StopMonitoring();

                StartMonitoringButton.Visibility = Visibility.Visible;
                StopMonitoringButton.Visibility = Visibility.Collapsed;
                MonitoringStatusText.Text = "Đã dừng";
                MonitoringStatusText.Foreground = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#C62828"));
                
                var statusDot = ((Grid)MonitoringStatusText.Parent).Children[0] as Border;
                if (statusDot != null)
                {
                    statusDot.Background = new SolidColorBrush((MediaColor)WpfColorConverter.ConvertFromString("#C62828"));
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Error when stopping monitoring: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StartMonitoring()
        {
            if (isMonitoring) return;

            isMonitoring = true;
            autoUploadTimer = new System.Threading.Timer(AutoUploadTimerCallback, null, 0, checkIntervalMinutes * 60 * 1000);

            if (!string.IsNullOrEmpty(AutoStopDurationTextBox.Text) && 
                int.TryParse(AutoStopDurationTextBox.Text, out int hours) && 
                hours > 0)
            {
                SetupAutoStop();
            }

            AddLogMsg($"Start folder monitoring: {monitorFolderPath}");
            AddLogMsg($"Test period: {checkIntervalMinutes} minutes");
        }

        private void StopMonitoring()
        {
            if (!isMonitoring) return;

            isMonitoring = false;
            autoUploadTimer?.Dispose();
            autoUploadTimer = null;

            autoStopTimer?.Dispose();
            autoStopTimer = null;
            autoStopTime = null;

            AddLogMsg("Monitoring stopped");
        }

        private void SetupAutoStop()
        {
            if (autoStopTimer != null)
            {
                autoStopTimer.Dispose();
                autoStopTimer = null;
            }
            
            autoStopTime = null;
            
            int autoStopHours;
            if (!string.IsNullOrWhiteSpace(AutoStopDurationTextBox.Text) && 
                int.TryParse(AutoStopDurationTextBox.Text, out autoStopHours) && 
                autoStopHours > 0)
            {
                autoStopTime = DateTime.Now.AddHours(autoStopHours);
                
                long delayMs = (long)autoStopHours * 60 * 60 * 1000;
                
                autoStopTimer = new System.Threading.Timer(AutoStopTimerCallback, null, delayMs, Timeout.Infinite);
                
                AddLogMsg($"Monitoring will automatically stop after {autoStopHours} hours (at {autoStopTime.Value.ToString("HH:mm:ss dd/MM/yyyy")})");
            }
            else
            {
                AddLogMsg("Monitoring will continue until manual stop.");
            }
        }
        
        private void AutoStopTimerCallback(object state)
        {
            try
            {
                Dispatcher.InvokeAsync(() => 
                {
                    if (isMonitoring)
                    {
                        AddLogMsg($"Auto monitoring stopped due to scheduled time.");
                        StopMonitoring();
                        UpdateAutoUploadConfigUI();
                        
                        WpfMessageBox.Show("Auto monitoring stopped due to scheduled time.", "Notification", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.InvokeAsync(() => 
                {
                    AddLogMsg($"Error during auto stop: {ex.Message}");
                });
            }
            finally
            {
                if (autoStopTimer != null)
                {
                    autoStopTimer.Dispose();
                    autoStopTimer = null;
                }
            }
        }

        private async void AutoUploadTimerCallback(object state)
        {
            try
            {
                if (!Directory.Exists(monitorFolderPath))
                {
                    await Dispatcher.InvokeAsync(() => 
                    {
                        AddLogMsg($"Monitoring folder no longer exists: {monitorFolderPath}");
                    });
                    
                    StopMonitoring();
                    
                    await Dispatcher.InvokeAsync(() => 
                    {
                        UpdateAutoUploadConfigUI();
                        WpfMessageBox.Show("Monitoring folder no longer exists. Auto monitoring stopped!", "Notification", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                    
                    return;
                }
                
                string[] csvFiles = Directory.GetFiles(monitorFolderPath, "*.csv");
                
                if (csvFiles.Length == 0)
                {
                    await Dispatcher.InvokeAsync(() => 
                    {
                        AddLogMsg("Auto check: No CSV files found.");
                    });
                    return;
                }
                
                await Dispatcher.InvokeAsync(() => 
                {
                    AddLogMsg($"Auto check: Found {csvFiles.Length} CSV files in folder.");
                });
                
                List<string> newFiles = new List<string>();
                foreach (string filePath in csvFiles)
                {
                    if (!uploadedFiles.ContainsKey(filePath))
                    {
                        newFiles.Add(filePath);
                    }
                }
                
                if (newFiles.Count == 0)
                {
                    await Dispatcher.InvokeAsync(() => 
                    {
                        AddLogMsg("Auto check: No new CSV files found.");
                    });
                    return;
                }
                
                await Dispatcher.InvokeAsync(() => 
                {
                    AddLogMsg($"Auto check: Found {newFiles.Count} new CSV files. Starting auto upload...");
                });
                
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
                
                int successCount = 0;
                int failCount = 0;
                
                foreach (string filePath in newFiles)
                {
                    try
                    {
                        await UploadFileAsync(containerClient, filePath);
                        
                        uploadedFiles.TryAdd(filePath, DateTime.Now);
                        
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        await Dispatcher.InvokeAsync(() => 
                        {
                            AddLogMsg($"Error auto uploading file {Path.GetFileName(filePath)}: {ex.Message}");
                        });
                        failCount++;
                    }
                }
                
                await Dispatcher.InvokeAsync(() => 
                {
                    AddLogMsg($"Auto upload completed. Success: {successCount}, Failed: {failCount}");
                });
                
                SaveUploadedFilesLog();
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => 
                {
                    AddLogMsg($"Error during auto upload: {ex.Message}");
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
                        
                        if (lines.Length >= 5)
                        {
                            monitorFolderPath = lines[2];
                            if (int.TryParse(lines[3], out int interval))
                            {
                                checkIntervalMinutes = interval;
                            }
                            
                            if (lines.Length >= 6 && !string.IsNullOrEmpty(lines[5]))
                            {
                                try
                                {
                                    autoStopTime = DateTime.Parse(lines[5]);
                                    
                                    TimeSpan remainingTime = autoStopTime.Value - DateTime.Now;
                                    if (remainingTime.TotalMinutes > 0)
                                    {
                                        int remainingHours = (int)Math.Ceiling(remainingTime.TotalHours);
                                        AutoStopDurationTextBox.Text = remainingHours.ToString();
                                    }
                                    else
                                    {
                                        autoStopTime = null;
                                        AutoStopDurationTextBox.Text = "24"; 
                                    }
                                }
                                catch
                                {
                                    autoStopTime = null;
                                    AutoStopDurationTextBox.Text = "24"; 
                                }
                            }
                            
                            if (bool.TryParse(lines[4], out bool monitoring) && monitoring)
                            {
                                if (Directory.Exists(monitorFolderPath) && 
                                    (!autoStopTime.HasValue || autoStopTime.Value > DateTime.Now))
                                {
                                    StartMonitoring();
                                    AddLogMsg($"Auto monitoring started for folder: {monitorFolderPath}");
                                    
                                    if (autoStopTime.HasValue)
                                    {
                                        TimeSpan remainingTime = autoStopTime.Value - DateTime.Now;
                                        AddLogMsg($"Monitoring will automatically stop after {FormatTimeSpan(remainingTime)} (at {autoStopTime.Value.ToString("HH:mm:ss dd/MM/yyyy")})");
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
            
            ConnectionStringHelpPopup.Visibility = Visibility.Collapsed;
            ContainerNameHelpPopup.Visibility = Visibility.Collapsed;
        }

        private void ShowMonitorTab()
        {
            UploadContent.Visibility = Visibility.Collapsed;
            MonitorContent.Visibility = Visibility.Visible;
            UploadTabButton.IsChecked = false;
            MonitorTabButton.IsChecked = true;
            
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

        private void ConfigurationTabButton_Click(object sender, RoutedEventArgs e)
        {
            WpfMessageBox.Show("Configuration function is under development. Please come back later!", "Notification", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UserProfileTabButton_Click(object sender, RoutedEventArgs e)
        {
            WpfMessageBox.Show("User Profile function is under development. Please come back later!", "Notification", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AddLogMsg(string message)
        {
            if (Dispatcher.CheckAccess())
            {
                LogListBox.Items.Add($"[{DateTime.Now.ToString("HH:mm:ss")}] {message}");
                LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
            }
            else
            {
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
                    WpfMessageBox.Show("Please enter complete Azure Storage connection information!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SaveSettings();

                BlobContainerClient containerClient = new BlobContainerClient(ConnectionStringTextBox.Text, ContainerNameTextBox.Text);
                
                bool containerExists = await containerClient.ExistsAsync();
                if (!containerExists)
                {
                    WpfMessageBox.Show($"Container '{ContainerNameTextBox.Text}' does not exist. Please check the container name!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    AddLogMsg($"Upload failed: Container '{ContainerNameTextBox.Text}' does not exist.");
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

                    DataTable table = ReadCsvToDataTable(filePath);
                    PreviewDataGrid.ItemsSource = table.DefaultView;

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
                WpfMessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void UploadFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ConnectionStringTextBox.Text) || string.IsNullOrWhiteSpace(ContainerNameTextBox.Text))
                {
                    WpfMessageBox.Show("Please enter complete Azure Storage connection information!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SaveSettings();

                BlobContainerClient containerClient = new BlobContainerClient(ConnectionStringTextBox.Text, ContainerNameTextBox.Text);
                
                bool containerExists = await containerClient.ExistsAsync();
                if (!containerExists)
                {
                    WpfMessageBox.Show($"Container '{ContainerNameTextBox.Text}' does not exist. Please check the container name!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    AddLogMsg($"Upload failed: Container '{ContainerNameTextBox.Text}' does not exist.");
                    return;
                }

                var folderBrowserDialog = new WinForms.FolderBrowserDialog
                {
                    Description = "Select folder containing CSV files",
                    ShowNewFolderButton = false
                };

                if (folderBrowserDialog.ShowDialog() == WinForms.DialogResult.OK)
                {
                    string selectedFolder = folderBrowserDialog.SelectedPath;
                    AddLogMsg($"Selected folder: {selectedFolder}");

                    string[] csvFiles = Directory.GetFiles(selectedFolder, "*.csv");
                    
                    if (csvFiles.Length == 0)
                    {
                        WpfMessageBox.Show("No CSV files found in this folder!", "Notification", MessageBoxButton.OK, MessageBoxImage.Information);
                        AddLogMsg("No CSV files found in the selected folder.");
                        return;
                    }

                    AddLogMsg($"Found {csvFiles.Length} CSV files in folder.");
                    
                    MessageBoxResult result = WpfMessageBox.Show(
                        $"Found {csvFiles.Length} CSV files. Do you want to upload all?", 
                        "Confirm upload", 
                        MessageBoxButton.YesNo, 
                        MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        if (csvFiles.Length > 0)
                        {
                            DataTable table = ReadCsvToDataTable(csvFiles[0]);
                            PreviewDataGrid.ItemsSource = table.DefaultView;
                        }

                        AddLogMsg("STARTING BATCH UPLOAD...");
                        int successCount = 0;
                        int failCount = 0;

                        var progressWindow = new ProgressWindow(csvFiles.Length);
                        progressWindow.Owner = this;
                        progressWindow.Show();

                        for (int i = 0; i < csvFiles.Length; i++)
                        {
                            try
                            {
                                string filePath = csvFiles[i];
                                string fileName = Path.GetFileName(filePath);
                                
                                progressWindow.UpdateProgress(i + 1, fileName);
                                
                                await UploadFileAsync(containerClient, filePath);
                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                AddLogMsg($"Error uploading file {Path.GetFileName(csvFiles[i])}: {ex.Message}");
                                failCount++;
                            }
                        }

                        progressWindow.Close();

                        AddLogMsg($"BATCH UPLOAD COMPLETED. Success: {successCount}, Failed: {failCount}");
                        WpfMessageBox.Show(
                            $"Upload completed.\nSuccess: {successCount} files\nFailed: {failCount} files", 
                            "Upload results",
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
                WpfMessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            if (isSidebarExpanded)
            {
                CollapseSidebar();
            }
            else
            {
                ExpandSidebar();
            }
        }

        private void CollapseSidebar()
        {
            var animation = new GridLengthAnimation
            {
                From = new GridLength(EXPANDED_WIDTH),
                To = new GridLength(COLLAPSED_WIDTH),
                Duration = TimeSpan.FromSeconds(0.3),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            
            isSidebarExpanded = false;
            
            var rotateAnimation = new DoubleAnimation
            {
                From = 0,
                To = 180,
                Duration = TimeSpan.FromSeconds(0.3),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            
            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromSeconds(0.15),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            
            fadeOut.Completed += (s, e) =>
            {
                ManagementLabel.Visibility = Visibility.Collapsed;
                SettingsLabel.Visibility = Visibility.Collapsed;
                FooterPanel.Visibility = Visibility.Collapsed;
                LogoPanel.Visibility = Visibility.Collapsed;

                ((TextBlock)ToggleSidebarButton.Content).Text = "\uE701";
                
                foreach (WpfRadioButton rb in NavigationPanel.Children.OfType<WpfRadioButton>())
                {

                    if (rb.Tag != null && rb.Tag.ToString().Length == 1)
                    {
                    }
                    else
                    {
                        rb.Content = "";
                    }
                    
                    rb.HorizontalContentAlignment = WpfHorizontalAlignment.Center;
                    rb.Padding = new Thickness(0);
                }
            };
            
            ManagementLabel.BeginAnimation(OpacityProperty, fadeOut);
            SettingsLabel.BeginAnimation(OpacityProperty, fadeOut);
            FooterPanel.BeginAnimation(OpacityProperty, fadeOut);
            LogoPanel.BeginAnimation(OpacityProperty, fadeOut);
            
            foreach (WpfRadioButton rb in NavigationPanel.Children.OfType<WpfRadioButton>())
            {
                if (rb.Content != null && rb.Content.ToString() != "")
                {
                    if (rb.Tag != null && rb.Tag.ToString().Length > 1)
                    {
                        rb.BeginAnimation(OpacityProperty, fadeOut);
                    }
                }
            }
            
            SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, animation);
        }

        private void ExpandSidebar()
        {
            var animation = new GridLengthAnimation
            {
                From = new GridLength(COLLAPSED_WIDTH),
                To = new GridLength(EXPANDED_WIDTH),
                Duration = TimeSpan.FromSeconds(0.3),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            
            isSidebarExpanded = true;
            
            ((TextBlock)ToggleSidebarButton.Content).Text = "\uE700";
            
            UploadTabButton.Content = "Upload File";
            UploadTabButton.Opacity = 0;
            
            MonitorTabButton.Content = "Error Monitor";
            MonitorTabButton.Opacity = 0;
            
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
                
                rb.HorizontalContentAlignment = WpfHorizontalAlignment.Left;
                rb.Padding = new Thickness(20, 0, 20, 0);
            }
            
            ManagementLabel.Visibility = Visibility.Visible;
            ManagementLabel.Opacity = 0;
            
            SettingsLabel.Visibility = Visibility.Visible;
            SettingsLabel.Opacity = 0;
            
            FooterPanel.Visibility = Visibility.Visible;
            FooterPanel.Opacity = 0;
            
            LogoPanel.Visibility = Visibility.Visible;
            LogoPanel.Opacity = 0;
            
            animation.Completed += (s, e) =>
            {
                var fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromSeconds(0.2),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                
                ManagementLabel.BeginAnimation(OpacityProperty, fadeIn);
                SettingsLabel.BeginAnimation(OpacityProperty, fadeIn);
                FooterPanel.BeginAnimation(OpacityProperty, fadeIn);
                LogoPanel.BeginAnimation(OpacityProperty, fadeIn);
                
                foreach (WpfRadioButton rb in NavigationPanel.Children.OfType<WpfRadioButton>())
                {
                    rb.BeginAnimation(OpacityProperty, fadeIn);
                }
            };
            
            SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, animation);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (isMonitoring)
            {
                StopMonitoring();
                AddLogMsg("Auto monitoring stopped due to application closure.");
            }
            
            SaveSettings();
            
            base.OnClosing(e);
        }
    }
}
