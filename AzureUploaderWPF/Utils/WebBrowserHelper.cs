using Microsoft.Win32;
using System;
using System.Reflection;
using System.Security.Permissions;

namespace AzureUploaderWPF.Utils
{
    public static class WebBrowserHelper
    {
        public static void SetWebBrowserFeatures()
        {
            // Đặt FEATURE_BROWSER_EMULATION để sử dụng phiên bản IE mới nhất
            SetBrowserFeatureControlKey("FEATURE_BROWSER_EMULATION", GetBrowserEmulationMode());

            // Tắt các popup và thông báo lỗi
            SetBrowserFeatureControlKey("FEATURE_ENABLE_CLIPCHILDREN_OPTIMIZATION", 1);
            SetBrowserFeatureControlKey("FEATURE_AJAX_CONNECTIONEVENTS", 1);
            SetBrowserFeatureControlKey("FEATURE_ENABLE_SCRIPT_PASTE_URLACTION_IF_PROMPT", 1);
            SetBrowserFeatureControlKey("FEATURE_SCRIPTURL_MITIGATION", 1);
            SetBrowserFeatureControlKey("FEATURE_SPELLCHECKING", 0);
            SetBrowserFeatureControlKey("FEATURE_STATUS_BAR_THROTTLING", 1);
            SetBrowserFeatureControlKey("FEATURE_TABBED_BROWSING", 1);
            SetBrowserFeatureControlKey("FEATURE_VALIDATE_NAVIGATE_URL", 1);
            SetBrowserFeatureControlKey("FEATURE_WEBOC_DOCUMENT_ZOOM", 1);
            SetBrowserFeatureControlKey("FEATURE_DISABLE_LEGACY_COMPRESSION", 1);
        }
        private static UInt32 GetBrowserEmulationMode()
        {
            int browserVersion = 0;
            using (var ieKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Internet Explorer",
                RegistryKeyPermissionCheck.ReadSubTree,
                System.Security.AccessControl.RegistryRights.QueryValues))
            {
                var version = ieKey?.GetValue("svcVersion", null) ?? ieKey?.GetValue("Version", null);
                if (version != null)
                {
                    int.TryParse(version.ToString().Split('.')[0], out browserVersion);
                }
            }

            // Dựa vào phiên bản IE để chọn mã emulation phù hợp
            // https://msdn.microsoft.com/en-us/library/ee330730(v=vs.85).aspx
            switch (browserVersion)
            {
                case 11:
                    return 11001; // Internet Explorer 11. Webpages are displayed in IE11 edge mode, regardless of doctype
                case 10:
                    return 10001; // Internet Explorer 10
                case 9:
                    return 9999;  // Internet Explorer 9
                case 8:
                    return 8888;  // Internet Explorer 8
                default:
                    return 11001; // Default: IE11
            }
        }

        private static void SetBrowserFeatureControlKey(string feature, UInt32 value)
        {
            // Lấy tên tệp thực thi hiện tại
            string appName = System.IO.Path.GetFileName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName);
            
            // Thiết lập registry
            using (var key = Registry.CurrentUser.CreateSubKey(
                String.Concat(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\", feature),
                RegistryKeyPermissionCheck.ReadWriteSubTree))
            {
                key?.SetValue(appName, value, RegistryValueKind.DWord);
            }
        }
    }
} 