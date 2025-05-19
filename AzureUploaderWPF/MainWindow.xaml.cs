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

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
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

        #endregion

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
                    }
                }
            }
            catch (Exception ex)
            {
                AddLogMsg($"Error loading settings: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                if (RememberSettingsCheckBox.IsChecked == true)
                {
                    string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SETTINGS_FILE);
                    File.WriteAllLines(settingsPath, new string[]
                    {
                        ConnectionStringTextBox.Text,
                        ContainerNameTextBox.Text
                    });
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
            LogListBox.Items.Add($"[{DateTime.Now.ToString("HH:mm:ss")}] {message}");
            LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
        }

        private async void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ConnectionStringTextBox.Text) || string.IsNullOrWhiteSpace(ContainerNameTextBox.Text))
                {
                    MessageBox.Show("Vui lòng nhập đầy đủ thông tin kết nối Azure Storage!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Lưu cấu hình nếu được chọn
                SaveSettings();

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
                    BlobContainerClient containerClient = new BlobContainerClient(ConnectionStringTextBox.Text, ContainerNameTextBox.Text);
                    await containerClient.CreateIfNotExistsAsync();

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
                MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show("Azure CSV Uploader v1.0\nDeveloped by FPTU HCM", "About");
        }

        private void DocsMenu_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Documentation is not available yet.", "Documentation");
        }

        private void ThemeMenu_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Theme selection is not implemented yet.", "Theme");
        }

        private void ExitMenu_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
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
                foreach (RadioButton rb in NavigationPanel.Children.OfType<RadioButton>())
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
                    rb.HorizontalContentAlignment = HorizontalAlignment.Center;
                    rb.Padding = new Thickness(0);
                }
            };
            
            // Áp dụng animation fade-out cho các thành phần
            ManagementLabel.BeginAnimation(OpacityProperty, fadeOut);
            SettingsLabel.BeginAnimation(OpacityProperty, fadeOut);
            FooterPanel.BeginAnimation(OpacityProperty, fadeOut);
            LogoPanel.BeginAnimation(OpacityProperty, fadeOut);
            
            // Fade-out nội dung text trong các RadioButton
            foreach (RadioButton rb in NavigationPanel.Children.OfType<RadioButton>())
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
            foreach (RadioButton rb in NavigationPanel.Children.OfType<RadioButton>())
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
                rb.HorizontalContentAlignment = HorizontalAlignment.Left;
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
                foreach (RadioButton rb in NavigationPanel.Children.OfType<RadioButton>())
                {
                    rb.BeginAnimation(OpacityProperty, fadeIn);
                }
            };
            
            // Thực hiện animation thay đổi kích thước
            SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, animation);
        }
    }
}
