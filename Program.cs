using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

[assembly: System.Reflection.AssemblyTitle("名钻指纹助手")]
[assembly: System.Reflection.AssemblyProduct("名钻指纹助手")]
[assembly: System.Reflection.AssemblyDescription("GTA Online 纯视觉指纹识别辅助工具")]
[assembly: System.Reflection.AssemblyCompany("lsf")]
[assembly: System.Reflection.AssemblyVersion("2026.9.15.2")]
[assembly: System.Reflection.AssemblyFileVersion("2026.9.15.2")]
[assembly: System.Reflection.AssemblyInformationalVersion("2026.09.15.2")]

namespace GtaCasinoAssistant
{
    public static class AppLog
    {
        private static readonly object Sync = new object();
        private static string _lastSignature = "";
        private static DateTime _lastWriteUtc = DateTime.MinValue;
        private static string _lastErrorSummary = "无";

        public static string LogDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LSF", "FingerprintAssistant", "logs");
            }
        }

        public static string LastErrorSummary
        {
            get { lock (Sync) return _lastErrorSummary; }
        }

        public static void Write(string context, Exception error)
        {
            Append(context, error);
        }

        public static void WriteThrottled(string context, Exception error)
        {
            string signature = context + ":" + error.GetType().FullName + ":" + error.Message;
            lock (Sync)
            {
                DateTime now = DateTime.UtcNow;
                if (signature == _lastSignature && (now - _lastWriteUtc).TotalSeconds < 30) return;
                _lastSignature = signature;
                _lastWriteUtc = now;
                AppendUnsafe(context, error, now);
            }
        }

        private static void Append(string context, Exception error)
        {
            lock (Sync) AppendUnsafe(context, error, DateTime.UtcNow);
        }

        private static void AppendUnsafe(string context, Exception error, DateTime nowUtc)
        {
            _lastErrorSummary = context + "：" + error.Message;
            try
            {
                string directory = LogDirectory;
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "assistant-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                File.AppendAllText(path,
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] " + context + Environment.NewLine
                    + error + Environment.NewLine + Environment.NewLine,
                    Encoding.UTF8);

                foreach (string oldLog in Directory.GetFiles(directory, "assistant-*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(oldLog) < nowUtc.AddDays(-14)) File.Delete(oldLog);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    public sealed class FeedbackPreferences
    {
        public bool ConsentAsked { get; set; }
        public bool Joined { get; set; }
        public bool AutoUpload { get; set; }
        public string ConsentVersion { get; set; }
        public string FeedbackId { get; set; }
        public string DailyDate { get; set; }
        public int DailyQueued { get; set; }
    }

    public static class RecognitionFeedback
    {
        private const string CurrentConsentVersion = "2026-09-09";
        private const string UploadUrl = "https://gtacn.org/api/fingerprint-feedback";
        private static readonly object Sync = new object();
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LSF", "FingerprintAssistant", "feedback-settings.json");
        private static FeedbackPreferences _preferences;
        private static int _uploadBusy;
        private static DateTime _lastQueuedUtc = DateTime.MinValue;

        public static bool AutoUploadEnabled
        {
            get
            {
                FeedbackPreferences settings = Load();
                return settings.Joined && settings.AutoUpload;
            }
        }

        public static void PromptInitialConsent(Form owner)
        {
            FeedbackPreferences settings = Load();
            if (settings.ConsentAsked && settings.ConsentVersion == CurrentConsentVersion) return;
            ShowSettings(owner, true);
        }

        public static void ShowSettings(Form owner, bool initial)
        {
            FeedbackPreferences current = Load();
            using (Form form = new Form())
            using (CheckBox joined = new CheckBox())
            using (CheckBox automatic = new CheckBox())
            using (Label explanation = new Label())
            using (Button save = new Button())
            using (Button cancel = new Button())
            {
                form.Text = "识别改进计划";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.ClientSize = new Size(500, 245);
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ShowInTaskbar = false;
                form.BackColor = Color.FromArgb(15, 23, 42);
                form.ForeColor = Color.FromArgb(226, 232, 240);
                form.TopMost = true;

                explanation.Text = "可自愿提供识别失败样本，帮助适配更多分辨率。\n只上传 8 个指纹识别小区域的灰度图，不上传完整屏幕、账号、文件路径或设备名称。上传在独立后台线程进行，不会等待上传，也不会影响识别和按键。";
                explanation.Location = new Point(22, 18);
                explanation.Size = new Size(456, 76);
                explanation.Font = new Font("Microsoft YaHei", 9f);

                joined.Text = "加入识别改进计划";
                joined.Location = new Point(25, 103);
                joined.Size = new Size(300, 28);
                joined.Checked = current.Joined;
                joined.Font = new Font("Microsoft YaHei", 9f, FontStyle.Bold);

                automatic.Text = "识别界面连续失败时自动上传脱敏样本（每天最多 3 份）";
                automatic.Location = new Point(47, 135);
                automatic.Size = new Size(420, 28);
                automatic.Checked = current.Joined && current.AutoUpload;
                automatic.Enabled = joined.Checked;
                automatic.Font = new Font("Microsoft YaHei", 8.5f);
                joined.CheckedChanged += delegate
                {
                    automatic.Enabled = joined.Checked;
                    if (!joined.Checked) automatic.Checked = false;
                };

                save.Text = "保存选择";
                save.Location = new Point(270, 188);
                save.Size = new Size(100, 34);
                save.DialogResult = DialogResult.OK;
                save.BackColor = Color.FromArgb(8, 145, 178);
                save.ForeColor = Color.White;
                save.FlatStyle = FlatStyle.Flat;

                cancel.Text = initial ? "暂不参加" : "取消";
                cancel.Location = new Point(378, 188);
                cancel.Size = new Size(100, 34);
                cancel.DialogResult = DialogResult.Cancel;
                cancel.BackColor = Color.FromArgb(30, 41, 59);
                cancel.ForeColor = Color.White;
                cancel.FlatStyle = FlatStyle.Flat;

                form.Controls.Add(explanation);
                form.Controls.Add(joined);
                form.Controls.Add(automatic);
                form.Controls.Add(save);
                form.Controls.Add(cancel);
                form.AcceptButton = save;
                form.CancelButton = cancel;

                DialogResult result = form.ShowDialog(owner);
                if (result == DialogResult.OK)
                {
                    current.Joined = joined.Checked;
                    current.AutoUpload = joined.Checked && automatic.Checked;
                }
                else if (initial)
                {
                    current.Joined = false;
                    current.AutoUpload = false;
                }
                if (initial || result == DialogResult.OK)
                {
                    current.ConsentAsked = true;
                    current.ConsentVersion = CurrentConsentVersion;
                    Save(current);
                }
            }
        }

        public static bool TryQueue(string mode, Bitmap frame, Rectangle[] regions, string reason, string appVersion)
        {
            FeedbackPreferences settings = Load();
            if (!settings.Joined || !settings.AutoUpload || frame == null || regions == null || regions.Length == 0) return false;
            if (Interlocked.CompareExchange(ref _uploadBusy, 1, 0) != 0) return false;

            DateTime now = DateTime.UtcNow;
            lock (Sync)
            {
                string today = now.ToString("yyyy-MM-dd");
                if (settings.DailyDate != today) { settings.DailyDate = today; settings.DailyQueued = 0; }
                if (settings.DailyQueued >= 3 || (now - _lastQueuedUtc).TotalMinutes < 10)
                {
                    Interlocked.Exchange(ref _uploadBusy, 0);
                    Save(settings);
                    return false;
                }
                settings.DailyQueued++;
                _lastQueuedUtc = now;
                Save(settings);
            }

            List<Bitmap> crops = new List<Bitmap>();
            int frameWidth = frame.Width;
            int frameHeight = frame.Height;
            try
            {
                foreach (Rectangle sourceRegion in regions)
                {
                    Rectangle region = Rectangle.Intersect(new Rectangle(0, 0, frame.Width, frame.Height), sourceRegion);
                    if (region.Width < 8 || region.Height < 8) continue;
                    crops.Add(frame.Clone(region, PixelFormat.Format24bppRgb));
                    if (crops.Count == 8) break;
                }
                if (crops.Count != 8) throw new InvalidOperationException("识别区域不完整，已取消反馈");
            }
            catch (Exception ex)
            {
                foreach (Bitmap crop in crops) crop.Dispose();
                Interlocked.Exchange(ref _uploadBusy, 0);
                AppLog.WriteThrottled("FeedbackCrop", ex);
                return false;
            }

            Thread worker = new Thread(delegate()
            {
                try { Upload(settings.FeedbackId, mode, reason, appVersion, frameWidth, frameHeight, crops); }
                catch (Exception ex) { AppLog.WriteThrottled("FeedbackUpload", ex); }
                finally
                {
                    foreach (Bitmap crop in crops) crop.Dispose();
                    Interlocked.Exchange(ref _uploadBusy, 0);
                }
            });
            worker.IsBackground = true;
            worker.Name = "RecognitionFeedbackUpload";
            worker.Start();
            return true;
        }

        private static void Upload(string feedbackId, string mode, string reason, string appVersion, int width, int height, List<Bitmap> crops)
        {
            List<object> images = new List<object>();
            for (int i = 0; i < crops.Count; i++)
            {
                using (Bitmap normalized = new Bitmap(96, 96, PixelFormat.Format24bppRgb))
                {
                    using (Graphics graphics = Graphics.FromImage(normalized))
                    {
                        graphics.Clear(Color.Black);
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(crops[i], new Rectangle(0, 0, 96, 96));
                    }
                    for (int y = 0; y < normalized.Height; y++)
                        for (int x = 0; x < normalized.Width; x++)
                        {
                            Color pixel = normalized.GetPixel(x, y);
                            int gray = (pixel.R * 30 + pixel.G * 59 + pixel.B * 11) / 100;
                            normalized.SetPixel(x, y, Color.FromArgb(gray, gray, gray));
                        }
                    using (MemoryStream stream = new MemoryStream())
                    {
                        normalized.Save(stream, ImageFormat.Png);
                        images.Add(new Dictionary<string, object> {
                            { "name", "region-" + (i + 1).ToString("00") + ".png" },
                            { "data", Convert.ToBase64String(stream.ToArray()) }
                        });
                    }
                }
            }

            Dictionary<string, object> payload = new Dictionary<string, object> {
                { "feedbackId", feedbackId }, { "mode", mode }, { "reason", Limit(reason, 240) },
                { "appVersion", appVersion }, { "frameWidth", width }, { "frameHeight", height },
                { "consentVersion", CurrentConsentVersion }, { "images", images }
            };
            byte[] body = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(payload));
            if (body.Length > 2 * 1024 * 1024) throw new InvalidOperationException("脱敏样本超过大小限制");
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(UploadUrl);
            request.Method = "POST";
            request.ContentType = "application/json; charset=utf-8";
            request.UserAgent = "Mingzuan-Fingerprint-Feedback/1.0";
            request.Timeout = 8000;
            request.ReadWriteTimeout = 8000;
            request.ContentLength = body.Length;
            using (Stream output = request.GetRequestStream()) output.Write(body, 0, body.Length);
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
                    throw new WebException("服务器返回 " + (int)response.StatusCode);
            }
        }

        private static string Limit(string value, int length)
        {
            if (String.IsNullOrEmpty(value)) return "recognition-failed";
            return value.Length <= length ? value : value.Substring(0, length);
        }

        private static FeedbackPreferences Load()
        {
            lock (Sync)
            {
                if (_preferences != null) return _preferences;
                try
                {
                    if (File.Exists(SettingsPath))
                        _preferences = new JavaScriptSerializer().Deserialize<FeedbackPreferences>(File.ReadAllText(SettingsPath, Encoding.UTF8));
                }
                catch (Exception ex) { AppLog.WriteThrottled("FeedbackSettingsLoad", ex); }
                if (_preferences == null) _preferences = new FeedbackPreferences();
                Guid parsed;
                if (!Guid.TryParse(_preferences.FeedbackId, out parsed)) _preferences.FeedbackId = Guid.NewGuid().ToString("D");
                return _preferences;
            }
        }

        private static void Save(FeedbackPreferences settings)
        {
            lock (Sync)
            {
                try
                {
                    _preferences = settings;
                    Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                    string temp = SettingsPath + ".tmp";
                    File.WriteAllText(temp, new JavaScriptSerializer().Serialize(settings), Encoding.UTF8);
                    if (File.Exists(SettingsPath)) File.Replace(temp, SettingsPath, null);
                    else File.Move(temp, SettingsPath);
                }
                catch (Exception ex) { AppLog.WriteThrottled("FeedbackSettingsSave", ex); }
            }
        }
    }

#if !MICROSOFT_STORE
    public static class UpdateClient
    {
        private const string ReleaseMetadataUrl = "https://gtacn.org/downloads/gta-casino-fingerprint-assistant-release.json";
        private const string SiteOrigin = "https://gtacn.org";

        private sealed class UpdateWebClient : WebClient
        {
            public UpdateWebClient()
            {
                Headers[HttpRequestHeader.UserAgent] = "Mingzuan-Fingerprint-Assistant-Updater/1.0";
                Headers[HttpRequestHeader.Accept] = "*/*";
            }

            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                request.Timeout = 30000;
                HttpWebRequest http = request as HttpWebRequest;
                if (http != null) http.ReadWriteTimeout = 120000;
                return request;
            }
        }

        public sealed class UpdateInfo
        {
            public string Version;
            public string DownloadUrl;
            public string Sha256;
        }

        public static void BeginCheck(Form owner, string currentVersion, Action<UpdateInfo> notifyAvailable)
        {
            Thread worker = new Thread(delegate()
            {
                try
                {
                    UpdateInfo update = ReadAvailableUpdate(currentVersion);
                    if (update == null || owner.IsDisposed) return;
                    owner.BeginInvoke(new Action(delegate()
                    {
                        if (owner.IsDisposed) return;
                        notifyAvailable(update);
                    }));
                }
                catch (Exception ex)
                {
                    // Update checks are best-effort and must never block normal use.
                    AppLog.WriteThrottled("UpdateCheck", ex);
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private static UpdateInfo ReadAvailableUpdate(string currentVersion)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string raw;
            using (UpdateWebClient client = new UpdateWebClient())
            {
                client.Encoding = Encoding.UTF8;
                client.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                raw = client.DownloadString(ReleaseMetadataUrl + "?t=" + DateTime.UtcNow.Ticks);
            }
            Dictionary<string, object> data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
            if (data == null) return null;
            object versionValue, urlValue, hashValue;
            if (!data.TryGetValue("version", out versionValue)
                || !data.TryGetValue("downloadUrl", out urlValue)
                || !data.TryGetValue("sha256", out hashValue)) return null;

            Version available, current;
            if (!Version.TryParse(Convert.ToString(versionValue), out available)
                || !Version.TryParse(currentVersion, out current)
                || available <= current) return null;
            string relativeOrAbsolute = Convert.ToString(urlValue);
            string absolute = relativeOrAbsolute.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? relativeOrAbsolute : SiteOrigin + (relativeOrAbsolute.StartsWith("/") ? "" : "/") + relativeOrAbsolute;
            return new UpdateInfo
            {
                Version = available.ToString(),
                DownloadUrl = absolute,
                Sha256 = Convert.ToString(hashValue).Trim().ToLowerInvariant()
            };
        }

        public static void StartDownload(Form owner, UpdateInfo update, Action<string> reportStatus,
            Action<int, double, long, long> reportProgress, Action<bool, string> completed)
        {
            Thread worker = new Thread(delegate()
            {
                string installerPath = Path.Combine(Path.GetTempPath(), "LSF-FingerprintAssistant-Setup-" + update.Version + ".exe");
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(update.DownloadUrl);
                    request.UserAgent = "Mingzuan-Fingerprint-Assistant-Updater/1.0";
                    request.Accept = "*/*";
                    request.Timeout = 30000;
                    request.ReadWriteTimeout = 120000;
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (Stream input = response.GetResponseStream())
                    using (FileStream output = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        long total = response.ContentLength;
                        long received = 0;
                        byte[] buffer = new byte[65536];
                        Stopwatch timer = Stopwatch.StartNew();
                        long lastReportMs = -250;
                        int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, count);
                            received += count;
                            if (timer.ElapsedMilliseconds - lastReportMs >= 250 || (total > 0 && received >= total))
                            {
                                lastReportMs = timer.ElapsedMilliseconds;
                                int percent = total > 0 ? (int)Math.Min(100, received * 100L / total) : -1;
                                double speed = timer.Elapsed.TotalSeconds > 0
                                    ? received / 1048576d / timer.Elapsed.TotalSeconds : 0;
                                ReportProgress(owner, reportProgress, percent, speed, received, total);
                            }
                        }
                    }
                    ReportStatus(owner, reportStatus, "新版已下载，正在校验安装包...");
                    string actualHash;
                    using (SHA256 sha = SHA256.Create())
                    using (FileStream stream = File.OpenRead(installerPath))
                    {
                        byte[] digest = sha.ComputeHash(stream);
                        StringBuilder builder = new StringBuilder(digest.Length * 2);
                        foreach (byte value in digest) builder.Append(value.ToString("x2"));
                        actualHash = builder.ToString();
                    }
                    if (!String.Equals(actualHash, update.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("新版安装包 SHA-256 校验失败，已停止升级");

                    if (owner.IsDisposed) return;
                    ReportStatus(owner, reportStatus, "校验通过，正在安装并重新启动...");
                    owner.BeginInvoke(new Action(delegate()
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo(installerPath,
                                "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /AUTOUPDATE=1")
                            { UseShellExecute = true });
                            Application.Exit();
                        }
                        catch (Exception ex) { completed(false, "无法启动升级程序：" + ex.Message); }
                    }));
                }
                catch (Exception ex)
                {
                    AppLog.Write("UpdateDownload", ex);
                    try { if (File.Exists(installerPath)) File.Delete(installerPath); } catch { }
                    if (!owner.IsDisposed)
                    {
                        try
                        {
                            owner.BeginInvoke(new Action(delegate()
                            {
                                if (!owner.IsDisposed) completed(false, "自动升级失败：" + ex.Message);
                            }));
                        }
                        catch { }
                    }
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private static void ReportStatus(Form owner, Action<string> reportStatus, string message)
        {
            if (owner.IsDisposed) return;
            try
            {
                owner.BeginInvoke(new Action(delegate()
                {
                    if (!owner.IsDisposed) reportStatus(message);
                }));
            }
            catch { }
        }

        private static void ReportProgress(Form owner, Action<int, double, long, long> reportProgress,
            int percent, double megabytesPerSecond, long received, long total)
        {
            if (owner.IsDisposed || reportProgress == null) return;
            try
            {
                owner.BeginInvoke(new Action(delegate()
                {
                    if (!owner.IsDisposed) reportProgress(percent, megabytesPerSecond, received, total);
                }));
            }
            catch { }
        }
    }
#endif

    public static class LicenseClient
    {
        public sealed class AccountProfile
        {
            public string Nickname = "已登录用户";
            public string Avatar = "";
            public DateTime ExpiresAtUtc = DateTime.MinValue;
        }

        internal sealed class LicenseAccessRejectedException : Exception
        {
            public LicenseAccessRejectedException(string message) : base(message) { }
        }

        internal sealed class LicenseServiceUnavailableException : Exception
        {
            public LicenseServiceUnavailableException(string message) : base(message) { }
        }

        private sealed class TimeoutWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                request.Timeout = 10000;
                HttpWebRequest http = request as HttpWebRequest;
                if (http != null)
                {
                    http.ReadWriteTimeout = 15000;
                    http.KeepAlive = true;
                }
                return request;
            }
        }

        private const string ApiBase = "https://gtacn.org/api/tool-license";
        private const string SiteOrigin = "https://gtacn.org";
        private const string Product = "fingerprint-assistant";
        private const string PublicModulus = "sB99j9QQbnHT7hxI76x3jU31Uv2Rk1c2VXqWqPAijWryTh2_dlbSdDTy5VYDhtAGtDmI0O-ch-OQbHFrtkXEXL0iYFSbbaBgbdtSt2nHUOhbQ_wUs6C0Wp_xe7ntt1mKedNMW72CxwbXhTwh4LtE5rc_FDnKTmq1CN50B_uOPer0EqKuR8f0sl9XCd9eHlpmjwRkiQsAsANT7MjxUDLqI3crfTb2rRW2uD5eQWaRcH5F3tSdr54txJHf9aiRVCFaZ71b19joRgHKJQGYMsH6KwyCw7SPoqgy-cG2hXmsrAoHQY322QX5JFHAZjgukQdTXp8kccBMl1j51TK2rlYsWw";
        private const string PublicExponent = "AQAB";
        private static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LSF", "FingerprintAssistant");
        private static readonly string DevicePath = Path.Combine(DataDirectory, "device.id");
        private static readonly string LicensePath = Path.Combine(DataDirectory, "license.token");
        private static AccountProfile _currentProfile;

        public static AccountProfile CurrentProfile { get { return _currentProfile; } }

        public static bool TryLoadLocalAccess(out AccountProfile profile)
        {
            profile = null;
            Directory.CreateDirectory(DataDirectory);
            string deviceId = GetDeviceId();
            string token = LoadToken();
            DateTime expiry;
            if (!ValidateToken(token, deviceId, out expiry))
            {
                DeleteToken();
                _currentProfile = null;
                return false;
            }
            _currentProfile = ReadProfile(token, expiry);
            profile = _currentProfile;
            return true;
        }

        public static bool ResolveAccountAccess(string appVersion)
        {
            Directory.CreateDirectory(DataDirectory);
            string deviceId = GetDeviceId();
            string token = LoadToken();
            DateTime expiry;
            if (ValidateToken(token, deviceId, out expiry))
            {
                bool serverReached;
                if (RevalidateAccountAccess(appVersion, out serverReached)) return true;
            }

            using (AccountLoginForm form = new AccountLoginForm(deviceId, appVersion))
            {
                return form.ShowDialog() == DialogResult.OK;
            }
        }

        public static bool ValidateTokenForTest(string token, string deviceId)
        {
            DateTime expiry;
            return ValidateToken(token, deviceId, out expiry);
        }

        public static string GetDeviceId()
        {
            try
            {
                if (File.Exists(DevicePath))
                {
                    string existing = File.ReadAllText(DevicePath).Trim();
                    Guid parsed;
                    if (Guid.TryParse(existing, out parsed)) return parsed.ToString("D");
                }
            }
            catch { }
            string created = Guid.NewGuid().ToString("D");
            File.WriteAllText(DevicePath, created, Encoding.UTF8);
            return created;
        }

        public static Dictionary<string, object> StartWebsiteLogin(string deviceId, string appVersion)
        {
            return Post("/device/start", new Dictionary<string, object> {
                { "deviceId", deviceId }, { "deviceLabel", Environment.MachineName }, { "appVersion", appVersion }
            });
        }

        public static Dictionary<string, object> PollWebsiteLogin(string deviceCode, string deviceId)
        {
            return Post("/device/status", new Dictionary<string, object> { { "deviceCode", deviceCode }, { "deviceId", deviceId } });
        }

        public static Dictionary<string, object> SendEmailLoginCode(string email)
        {
            return Post("/email/start", new Dictionary<string, object> {
                { "email", email }
            });
        }

        public static Dictionary<string, object> CompleteEmailLogin(string email, string code, string deviceId, string appVersion)
        {
            return Post("/email/complete", new Dictionary<string, object> {
                { "email", email }, { "code", code }, { "deviceId", deviceId },
                { "deviceLabel", Environment.MachineName }, { "appVersion", appVersion }
            });
        }

        public static bool AcceptServerToken(Dictionary<string, object> response, string deviceId)
        {
            object tokenValue;
            if (!response.TryGetValue("token", out tokenValue)) return false;
            string token = Convert.ToString(tokenValue);
            DateTime expiry;
            if (!ValidateToken(token, deviceId, out expiry)) return false;
            SaveToken(token);
            _currentProfile = ReadProfile(token, expiry);
            return true;
        }

        public static void BeginRevalidate(Form owner, string appVersion, Action<bool, bool, AccountProfile, string> completed)
        {
            Thread worker = new Thread(delegate()
            {
                bool access = true;
                bool online = false;
                string message = "离线授权有效";
                try
                {
                    string deviceId = GetDeviceId();
                    string token = LoadToken();
                    Dictionary<string, object> response = Post("/refresh", new Dictionary<string, object> {
                        { "token", token }, { "deviceId", deviceId },
                        { "deviceLabel", Environment.MachineName }, { "appVersion", appVersion }
                    });
                    online = true;
                    access = AcceptServerToken(response, deviceId);
                    message = access ? "账号资料已同步" : "许可证签名校验失败";
                    if (!access) DeleteToken();
                }
                catch (LicenseAccessRejectedException ex)
                {
                    online = true;
                    access = false;
                    message = ex.Message;
                    DeleteToken();
                    _currentProfile = null;
                }
                catch (Exception ex)
                {
                    AppLog.WriteThrottled("LicenseBackgroundRefresh", ex);
                    access = true;
                    online = false;
                    message = "网络不可用，正在使用本地离线授权";
                }
                if (owner.IsDisposed) return;
                try
                {
                    owner.BeginInvoke(new Action(delegate()
                    {
                        if (!owner.IsDisposed) completed(access, online, _currentProfile, message);
                    }));
                }
                catch { }
            });
            worker.IsBackground = true;
            worker.Name = "AccountProfileRefresh";
            worker.Start();
        }

        public static void SendPresence(string appVersion, string mode, bool autoEnabled, string speedMode, string state, string summary)
        {
            string deviceId = GetDeviceId();
            string token = LoadToken();
            DateTime expiry;
            if (!ValidateToken(token, deviceId, out expiry)) return;
            Post("/presence", new Dictionary<string, object> {
                { "token", token }, { "deviceId", deviceId }, { "appVersion", appVersion },
                { "mode", mode }, { "autoEnabled", autoEnabled }, { "speedMode", speedMode },
                { "state", state }, { "summary", summary ?? "" }
            });
        }

        public static void LogoutLocal()
        {
            DeleteToken();
            _currentProfile = null;
        }

        public static bool RevalidateAccountAccess(string appVersion, out bool serverReached)
        {
            serverReached = false;
            string deviceId = GetDeviceId();
            string token = LoadToken();
            DateTime expiry;
            if (!ValidateToken(token, deviceId, out expiry))
            {
                serverReached = true;
                DeleteToken();
                return false;
            }
            try
            {
                Dictionary<string, object> response = Post("/refresh", new Dictionary<string, object> {
                    { "token", token }, { "deviceId", deviceId },
                    { "deviceLabel", Environment.MachineName }, { "appVersion", appVersion }
                });
                serverReached = true;
                if (!AcceptServerToken(response, deviceId))
                {
                    DeleteToken();
                    return false;
                }
                return true;
            }
            catch (LicenseAccessRejectedException)
            {
                serverReached = true;
                DeleteToken();
                return false;
            }
            catch (LicenseServiceUnavailableException)
            {
                // A valid signed lease remains usable only when the server truly
                // cannot be reached. An explicit 4xx rejection never gets grace.
                return true;
            }
            catch
            {
                return true;
            }
        }

        private static string LoadToken()
        {
            try
            {
                if (!File.Exists(LicensePath)) return "";
                string stored = File.ReadAllText(LicensePath).Trim();
                const string prefix = "dpapi-v1:";
                if (!stored.StartsWith(prefix, StringComparison.Ordinal))
                {
                    // Transparently migrate old plaintext credentials after the
                    // first successful read. Never include the token in logs.
                    SaveToken(stored);
                    return stored;
                }
                byte[] encrypted = Convert.FromBase64String(stored.Substring(prefix.Length));
                byte[] clear = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(clear);
            }
            catch (Exception ex)
            {
                AppLog.WriteThrottled("CredentialLoad", ex);
                return "";
            }
        }

        private static void SaveToken(string token)
        {
            Directory.CreateDirectory(DataDirectory);
            byte[] clear = Encoding.UTF8.GetBytes(token ?? "");
            byte[] encrypted = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
            File.WriteAllText(LicensePath, "dpapi-v1:" + Convert.ToBase64String(encrypted), Encoding.UTF8);
        }

        private static void DeleteToken()
        {
            try { if (File.Exists(LicensePath)) File.Delete(LicensePath); }
            catch { }
        }

        private static AccountProfile ReadProfile(string token, DateTime expiry)
        {
            AccountProfile profile = new AccountProfile { ExpiresAtUtc = expiry };
            try
            {
                string[] parts = token.Split('.');
                if (parts.Length != 2) return profile;
                string json = Encoding.UTF8.GetString(FromBase64Url(parts[0]));
                Dictionary<string, object> payload = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                object profileValue;
                if (payload != null && payload.TryGetValue("profile", out profileValue))
                {
                    Dictionary<string, object> data = profileValue as Dictionary<string, object>;
                    if (data != null)
                    {
                        object nickname, avatar;
                        if (data.TryGetValue("nickname", out nickname) && !String.IsNullOrWhiteSpace(Convert.ToString(nickname)))
                            profile.Nickname = Convert.ToString(nickname).Trim();
                        if (data.TryGetValue("avatar", out avatar)) profile.Avatar = Convert.ToString(avatar).Trim();
                    }
                }
            }
            catch { }
            return profile;
        }

        private static Dictionary<string, object> Post(string route, Dictionary<string, object> payload)
        {
            return PostUrl(ApiBase + route, payload);
        }

        private static Dictionary<string, object> PostUrl(string url, Dictionary<string, object> payload)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string body = new JavaScriptSerializer().Serialize(payload);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                using (WebClient client = new TimeoutWebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers[HttpRequestHeader.ContentType] = "application/json";
                    string raw;
                    try { raw = client.UploadString(url, "POST", body); }
                    catch (WebException ex)
                    {
                        if (ex.Response != null)
                        {
                            HttpWebResponse httpResponse = ex.Response as HttpWebResponse;
                            try
                            {
                                using (StreamReader reader = new StreamReader(ex.Response.GetResponseStream())) raw = reader.ReadToEnd();
                                Dictionary<string, object> error = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                                object message;
                                string detail = error != null && error.TryGetValue("error", out message) ? Convert.ToString(message) : "授权服务器拒绝了请求";
                                if (httpResponse != null && (int)httpResponse.StatusCode >= 400 && (int)httpResponse.StatusCode < 500)
                                    throw new LicenseAccessRejectedException(detail);
                                throw new LicenseServiceUnavailableException(detail);
                            }
                            finally { ex.Response.Close(); }
                        }
                        if (attempt == 0)
                        {
                            Thread.Sleep(350);
                            continue;
                        }
                        throw new LicenseServiceUnavailableException("无法连接授权服务器，请检查网络");
                    }
                    Dictionary<string, object> result = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                    object ok;
                    if (result == null || !result.TryGetValue("ok", out ok) || !Convert.ToBoolean(ok))
                    {
                        object message;
                        throw new LicenseAccessRejectedException(result != null && result.TryGetValue("error", out message) ? Convert.ToString(message) : "授权服务器返回异常");
                    }
                    return result;
                }
            }
            throw new LicenseServiceUnavailableException("无法连接授权服务器，请检查网络");
        }

        private static bool ValidateToken(string token, string deviceId, out DateTime expiry)
        {
            expiry = DateTime.MinValue;
            try
            {
                string[] parts = token.Split('.');
                if (parts.Length != 2) return false;
                byte[] data = Encoding.UTF8.GetBytes(parts[0]);
                byte[] signature = FromBase64Url(parts[1]);
                using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
                {
                    rsa.ImportParameters(new RSAParameters { Modulus = FromBase64Url(PublicModulus), Exponent = FromBase64Url(PublicExponent) });
                    if (!rsa.VerifyData(data, CryptoConfig.MapNameToOID("SHA256"), signature)) return false;
                }
                string json = Encoding.UTF8.GetString(FromBase64Url(parts[0]));
                Dictionary<string, object> payload = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (Convert.ToString(payload["product"]) != Product) return false;
                object accountValue;
                if (!payload.TryGetValue("accountId", out accountValue) || String.IsNullOrWhiteSpace(Convert.ToString(accountValue))) return false;
                if (!String.Equals(Convert.ToString(payload["deviceHash"]), Sha256(deviceId.Trim()), StringComparison.OrdinalIgnoreCase)) return false;
                expiry = DateTime.Parse(Convert.ToString(payload["expiresAt"]), null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
                return expiry > DateTime.UtcNow;
            }
            catch { return false; }
        }

        private static string Sha256(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                StringBuilder builder = new StringBuilder(digest.Length * 2);
                foreach (byte item in digest) builder.Append(item.ToString("x2"));
                return builder.ToString();
            }
        }

        private static byte[] FromBase64Url(string value)
        {
            string normalized = value.Replace('-', '+').Replace('_', '/');
            while (normalized.Length % 4 != 0) normalized += "=";
            return Convert.FromBase64String(normalized);
        }
    }

    public sealed class AccountLoginForm : Form
    {
        private readonly string _deviceId;
        private readonly string _appVersion;
        private readonly Label _status = new Label();
        private readonly Button _website = new Button();
        private readonly Button _guest = new Button();
        private readonly TextBox _email = new TextBox();
        private readonly TextBox _emailCode = new TextBox();
        private readonly Button _sendEmailCode = new Button();
        private readonly Button _emailLogin = new Button();
        private readonly LinkLabel _emailToggle = new LinkLabel();
        private readonly Panel _emailPanel = new Panel();
        private readonly LinkLabel _register = new LinkLabel();
        private bool _emailExpanded;

        public AccountLoginForm(string deviceId, string appVersion)
        {
            _deviceId = deviceId; _appVersion = appVersion;
            Text = "名钻指纹助手 · 账号登录";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(430, 365);
            ShowInTaskbar = true;
            TopMost = true;
            BackColor = Color.FromArgb(10, 17, 29);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Shown += delegate { SetAttention(true); };

            Label title = new Label { Text = "登录解锁自动模式", Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold), Location = new Point(24, 22), AutoSize = true };
            Label note = new Label { Text = "推荐使用浏览器授权，无需在软件内输入邮箱或密码。\n在网站确认后，软件会自动完成登录。", ForeColor = Color.FromArgb(160, 174, 192), Location = new Point(26, 60), Size = new Size(378, 46) };
            _website.Text = "使用浏览器授权登录（推荐）"; _website.Location = new Point(27, 112); _website.Size = new Size(376, 46); _website.Click += WebsiteClicked;
            _website.BackColor = Color.FromArgb(6, 182, 212); _website.ForeColor = Color.FromArgb(3, 15, 25); _website.FlatStyle = FlatStyle.Flat;
            _website.FlatAppearance.BorderSize = 0; _website.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            Label browserHint = new Label { Text = "将打开默认浏览器 · 登录并确认此设备即可", ForeColor = Color.FromArgb(148, 163, 184), Location = new Point(27, 167), Size = new Size(376, 22), TextAlign = ContentAlignment.MiddleCenter };

            _emailToggle.Text = "▸ 其他方式：使用邮箱验证码登录"; _emailToggle.Location = new Point(27, 202); _emailToggle.Size = new Size(376, 24);
            _emailToggle.LinkColor = Color.FromArgb(125, 211, 252); _emailToggle.ActiveLinkColor = Color.FromArgb(34, 211, 238);
            _emailToggle.LinkClicked += delegate { SetEmailExpanded(!_emailExpanded); };

            _emailPanel.Location = new Point(27, 232); _emailPanel.Size = new Size(376, 160); _emailPanel.Visible = false;
            Label emailLabel = new Label { Text = "邮箱", ForeColor = Color.FromArgb(160, 174, 192), Location = new Point(0, 4), Size = new Size(50, 18) };
            _email.Location = new Point(53, 1); _email.Size = new Size(323, 26); _email.BackColor = Color.FromArgb(23, 32, 48); _email.ForeColor = Color.White; _email.BorderStyle = BorderStyle.FixedSingle;
            Label codeLabel = new Label { Text = "验证码", ForeColor = Color.FromArgb(160, 174, 192), Location = new Point(0, 43), Size = new Size(50, 18) };
            _emailCode.Location = new Point(53, 40); _emailCode.Size = new Size(185, 26); _emailCode.BackColor = Color.FromArgb(23, 32, 48); _emailCode.ForeColor = Color.White; _emailCode.BorderStyle = BorderStyle.FixedSingle; _emailCode.MaxLength = 6;
            _sendEmailCode.Text = "发送验证码"; _sendEmailCode.Location = new Point(250, 37); _sendEmailCode.Size = new Size(126, 32); _sendEmailCode.Click += SendEmailCodeClicked;
            _emailLogin.Text = "邮箱验证并登录"; _emailLogin.Location = new Point(0, 86); _emailLogin.Size = new Size(376, 38); _emailLogin.Click += EmailLoginClicked;
            _emailPanel.Controls.AddRange(new Control[] { emailLabel, _email, codeLabel, _emailCode, _sendEmailCode, _emailLogin });

            _register.Text = "没有账号？前往网站注册"; _register.Location = new Point(27, 236); _register.Size = new Size(376, 20);
            _register.LinkColor = Color.FromArgb(56, 189, 248); _register.ActiveLinkColor = Color.FromArgb(125, 211, 252);
            _register.LinkClicked += delegate
            {
                try { Process.Start(new ProcessStartInfo("https://gtacn.org/") { UseShellExecute = true }); }
                catch (Exception ex) { _status.Text = "无法打开注册网站：" + ex.Message; }
            };
            _guest.Text = "暂不登录，继续游客模式"; _guest.Location = new Point(27, 264); _guest.Size = new Size(376, 34); _guest.Click += (sender, args) => { DialogResult = DialogResult.Ignore; Close(); };
            _guest.BackColor = Color.FromArgb(15, 23, 42); _guest.ForeColor = Color.FromArgb(148, 163, 184); _guest.FlatStyle = FlatStyle.Flat;
            _guest.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
            _status.Location = new Point(27, 310); _status.Size = new Size(376, 42); _status.ForeColor = Color.FromArgb(126, 231, 135); _status.Text = "账号登录状态可离线保留 7 天，到期前后台自动续期。";
            Controls.AddRange(new Control[] { title, note, _website, browserHint, _emailToggle, _emailPanel, _register, _guest, _status });
        }

        private void SetEmailExpanded(bool expanded)
        {
            _emailExpanded = expanded;
            _emailPanel.Visible = expanded;
            _emailToggle.Text = expanded ? "▾ 收起邮箱验证码登录" : "▸ 其他方式：使用邮箱验证码登录";
            _register.Top = expanded ? 402 : 236;
            _guest.Top = expanded ? 430 : 264;
            _status.Top = expanded ? 476 : 310;
            ClientSize = new Size(430, expanded ? 530 : 365);
        }

        private void SetAttention(bool attention)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { Invoke(new Action<bool>(SetAttention), attention); return; }
            TopMost = attention;
            if (!attention) return;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            BringToFront();
            Activate();
        }

        private void SetBusy(bool busy, string status)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { BeginInvoke(new Action<bool, string>(SetBusy), busy, status); return; }
            _website.Enabled = !busy; _guest.Enabled = !busy; _email.Enabled = !busy;
            _emailCode.Enabled = !busy; _sendEmailCode.Enabled = !busy; _emailLogin.Enabled = !busy;
            _emailToggle.Enabled = !busy; _register.Enabled = !busy;
            _status.Text = status;
        }

        private void Complete(Dictionary<string, object> response)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<Dictionary<string, object>>(Complete), response); return; }
            if (!LicenseClient.AcceptServerToken(response, _deviceId)) { SetBusy(false, "服务器许可证签名校验失败"); return; }
            DialogResult = DialogResult.OK; Close();
        }

        private void WebsiteClicked(object sender, EventArgs e)
        {
            SetBusy(true, "正在创建账号登录请求…");
            new Thread(() => {
                try
                {
                    Dictionary<string, object> started = LicenseClient.StartWebsiteLogin(_deviceId, _appVersion);
                    string deviceCode = Convert.ToString(started["deviceCode"]);
                    string uri = Convert.ToString(started["verificationUri"]);
                    int expiresIn = started.ContainsKey("expiresIn") ? Convert.ToInt32(started["expiresIn"]) : 600;
                    int pollInterval = started.ContainsKey("pollInterval") ? Math.Max(2, Convert.ToInt32(started["pollInterval"])) : 3;
                    SetAttention(false);
                    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                    SetBusy(true, "等待浏览器确认账号登录…");
                    DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn));
                    int transientFailures = 0;
                    while (DateTime.UtcNow < deadline && !IsDisposed)
                    {
                        Thread.Sleep(pollInterval * 1000);
                        try
                        {
                            Dictionary<string, object> polled = LicenseClient.PollWebsiteLogin(deviceCode, _deviceId);
                            transientFailures = 0;
                            string status = Convert.ToString(polled["status"]);
                            if (status == "approved") { Complete(polled); return; }
                            if (status == "expired" || status == "consumed") throw new InvalidOperationException("账号登录请求已过期，请重新操作");
                        }
                        catch (LicenseClient.LicenseServiceUnavailableException)
                        {
                            transientFailures++;
                            SetBusy(true, transientFailures < 3
                                ? "网络波动，正在继续等待浏览器确认…"
                                : "授权服务器响应较慢，仍在自动重试…");
                        }
                    }
                    if (IsDisposed) return;
                    throw new InvalidOperationException("网站登录等待超时");
                }
                catch (Exception ex) { SetBusy(false, ex.Message); SetAttention(true); }
            }) { IsBackground = true }.Start();
        }

        private void SendEmailCodeClicked(object sender, EventArgs e)
        {
            string email = _email.Text.Trim().ToLowerInvariant();
            if (email.Length < 5 || !email.Contains("@")) { _status.Text = "请输入有效邮箱地址"; return; }
            SetBusy(true, "正在发送邮箱验证码…");
            new Thread(delegate()
            {
                try
                {
                    Dictionary<string, object> response = LicenseClient.SendEmailLoginCode(email);
                    object message;
                    SetBusy(false, response.TryGetValue("message", out message)
                        ? Convert.ToString(message) : "验证码已发送，请检查邮箱");
                }
                catch (Exception ex) { SetBusy(false, ex.Message); }
            }) { IsBackground = true }.Start();
        }

        private void EmailLoginClicked(object sender, EventArgs e)
        {
            string email = _email.Text.Trim().ToLowerInvariant();
            string code = _emailCode.Text.Trim();
            if (email.Length < 5 || !email.Contains("@")) { _status.Text = "请输入有效邮箱地址"; return; }
            if (code.Length != 6) { _status.Text = "请输入 6 位邮箱验证码"; return; }
            SetBusy(true, "正在验证邮箱并登录…");
            new Thread(delegate()
            {
                try
                {
                    Dictionary<string, object> response = LicenseClient.CompleteEmailLogin(email, code, _deviceId, _appVersion);
                    Complete(response);
                }
                catch (Exception ex) { SetBusy(false, ex.Message); }
            }) { IsBackground = true }.Start();
        }
    }

    public static class TextBoxCompatibility
    {
        public static void PlaceholderTextCompat(this TextBox box, string value)
        {
            box.Text = value; box.ForeColor = Color.Gray;
            box.GotFocus += (sender, args) => { if (box.ForeColor == Color.Gray) { box.Text = ""; box.ForeColor = Color.Black; } };
            box.LostFocus += (sender, args) => { if (String.IsNullOrWhiteSpace(box.Text)) { box.Text = value; box.ForeColor = Color.Gray; } };
        }
    }

    public class TemplateData
    {
        public byte[,] Pixels;
        public double Mean;
        public double Deviation;
    }

    public class FingerprintSlice
    {
        public int Index;
        public string Name;
        public string FileName;
        public Bitmap Image;
        public TemplateData CachedTemplate;
    }

    public class FingerprintTarget
    {
        public int Id;
        public string Title;
        public string TitleEn;
        public string Feature;
        public string Mnemonic;
        public string MasterFileName;
        public Bitmap MasterImage;
        public List<FingerprintSlice> Slices = new List<FingerprintSlice>();
        public List<int> DefaultSlots = new List<int>();
    }

    public class FingerprintDatabase
    {
        public List<FingerprintTarget> Targets = new List<FingerprintTarget>();

        public void Load(string templatesDir)
        {
            var t1 = new FingerprintTarget
            {
                Id = 1,
                Title = "指纹 1：左上同心环（经典单核心）",
                TitleEn = "Top-Left Loop Core",
                Feature = "母本正中顶端偏向左上，右侧有向内凹折",
                Mnemonic = "口诀：认准左上单核心小圆圈 (切片特征: 顶部小帽 + 左折角 + U型托 + 底部密纹)",
                MasterFileName = "target_1_master.png",
                DefaultSlots = new List<int> { 1, 2, 4, 6 }
            };
            t1.Slices.Add(new FingerprintSlice { Index = 1, Name = "① 顶部小帽", FileName = "target_1_slice_1.png" });
            t1.Slices.Add(new FingerprintSlice { Index = 2, Name = "② 左上折角", FileName = "target_1_slice_2.png" });
            t1.Slices.Add(new FingerprintSlice { Index = 3, Name = "③ 中下U型托", FileName = "target_1_slice_3.png" });
            t1.Slices.Add(new FingerprintSlice { Index = 4, Name = "④ 底部密纹", FileName = "target_1_slice_4.png" });

            var t2 = new FingerprintTarget
            {
                Id = 2,
                Title = "指纹 2：正中狭长环（高耸核心）",
                TitleEn = "Center Oval Loop",
                Feature = "狭长瘦高同心椭圆，中间有明显的竖向直裂缝",
                Mnemonic = "口诀：认准正中央竖向平直长裂缝 (切片特征: 尖顶 + 右斜切 + 左直纹 + 底部大弧)",
                MasterFileName = "target_2_master.png",
                DefaultSlots = new List<int> { 1, 3, 5, 7 }
            };
            t2.Slices.Add(new FingerprintSlice { Index = 1, Name = "① 顶部尖拱", FileName = "target_2_slice_1.png" });
            t2.Slices.Add(new FingerprintSlice { Index = 2, Name = "② 右上斜切", FileName = "target_2_slice_2.png" });
            t2.Slices.Add(new FingerprintSlice { Index = 3, Name = "③ 左下直纹", FileName = "target_2_slice_3.png" });
            t2.Slices.Add(new FingerprintSlice { Index = 4, Name = "④ 底部大弧", FileName = "target_2_slice_4.png" });

            var t3 = new FingerprintTarget
            {
                Id = 3,
                Title = "指纹 3：右倾旋涡环（向右倾斜）",
                TitleEn = "Right Leaning Arch",
                Feature = "纹路整体向右上方倾斜滑出，左侧弧度较陡",
                Mnemonic = "口诀：纹理整体向右上倾斜 (切片特征: 右斜波 + 三角尖 + 宽斜弧 + 发散横纹)",
                MasterFileName = "target_3_master.png",
                DefaultSlots = new List<int> { 1, 2, 5, 7 }
            };
            t3.Slices.Add(new FingerprintSlice { Index = 1, Name = "① 顶部右斜波", FileName = "target_3_slice_1.png" });
            t3.Slices.Add(new FingerprintSlice { Index = 2, Name = "② 中间三角尖", FileName = "target_3_slice_2.png" });
            t3.Slices.Add(new FingerprintSlice { Index = 3, Name = "③ 左侧宽斜弧", FileName = "target_3_slice_3.png" });
            t3.Slices.Add(new FingerprintSlice { Index = 4, Name = "④ 右下发散纹", FileName = "target_3_slice_4.png" });

            var t4 = new FingerprintTarget
            {
                Id = 4,
                Title = "指纹 4：双核心圆涡（两点交汇）",
                TitleEn = "Double Whorl Core",
                Feature = "正下方有明显的双核心交织涡旋（两点交汇）",
                Mnemonic = "口诀：母本正下方有双核心圆涡 (切片特征: 拱形盖 + 三角汇聚 + 双核圆涡 + 深U)",
                MasterFileName = "target_4_master.png",
                DefaultSlots = new List<int> { 2, 4, 5, 6 }
            };
            t4.Slices.Add(new FingerprintSlice { Index = 1, Name = "① 顶部拱形盖", FileName = "target_4_slice_1.png" });
            t4.Slices.Add(new FingerprintSlice { Index = 2, Name = "② 中间三角汇聚", FileName = "target_4_slice_2.png" });
            t4.Slices.Add(new FingerprintSlice { Index = 3, Name = "③ 双核圆涡", FileName = "target_4_slice_3.png" });
            t4.Slices.Add(new FingerprintSlice { Index = 4, Name = "④ 底部深U收口", FileName = "target_4_slice_4.png" });

            Targets.Add(t1);
            Targets.Add(t2);
            Targets.Add(t3);
            Targets.Add(t4);

            foreach (var t in Targets)
            {
                string mPath = Path.Combine(templatesDir, t.MasterFileName);
                if (File.Exists(mPath))
                {
                    try { using (var s = Image.FromFile(mPath)) t.MasterImage = new Bitmap(s); } catch { }
                }

                foreach (var sl in t.Slices)
                {
                    string sPath = Path.Combine(templatesDir, sl.FileName);
                    if (File.Exists(sPath))
                    {
                        try
                        {
                            using (var s = Image.FromFile(sPath))
                            {
                                sl.Image = new Bitmap(s);
                                sl.CachedTemplate = ReliableAutoScanner.PrepareTemplate(sl.Image);
                            }
                        }
                        catch { }
                    }
                }
            }
        }
    }

    public class ReliableAutoScanner
    {
        private const double MinimumAverageScore = 0.35;
        private const double MinimumTargetMargin = 0.04;
        private static int _preferredLayout = -1;
        private static int _preferredLayoutWidth;
        private static int _preferredLayoutHeight;

        public sealed class AssignmentResult
        {
            public FingerprintTarget Target;
            public int[] SliceSlots;
            public double AverageScore;
        }

        public static bool ScanBitmap(FingerprintDatabase db, Bitmap screenshot, out FingerprintTarget matchedTarget, out List<int> detectedSlots, out double confidence)
        {
            matchedTarget = null;
            detectedSlots = new List<int>();
            confidence = 0;
            if (db == null || screenshot == null || db.Targets.Count == 0) return false;

            List<Rectangle[]> layouts = GetSlotRectangleCandidates(screenshot);
            if (_preferredLayoutWidth == screenshot.Width && _preferredLayoutHeight == screenshot.Height
                && _preferredLayout >= 0 && _preferredLayout < layouts.Count
                && TryMatchLayout(db, screenshot, layouts[_preferredLayout], out matchedTarget, out detectedSlots, out confidence))
                return true;

            for (int index = 0; index < layouts.Count; index++)
            {
                FingerprintTarget target;
                List<int> slots;
                double score;
                if (!TryMatchLayout(db, screenshot, layouts[index], out target, out slots, out score)) continue;
                matchedTarget = target;
                detectedSlots = slots;
                confidence = score;
                _preferredLayout = index;
                _preferredLayoutWidth = screenshot.Width;
                _preferredLayoutHeight = screenshot.Height;
                return true;
            }
            return false;
        }

        private static bool TryMatchLayout(FingerprintDatabase db, Bitmap screenshot, Rectangle[] rectangles,
            out FingerprintTarget matchedTarget, out List<int> detectedSlots, out double confidence)
        {
            matchedTarget = null;
            detectedSlots = new List<int>();
            confidence = 0;
            List<byte[,]> slots = ExtractSlots(screenshot, rectangles);
            if (slots.Count != 8) return false;

            List<AssignmentResult> results = new List<AssignmentResult>();
            foreach (FingerprintTarget target in db.Targets)
            {
                if (target.Slices.Count < 4) continue;
                double[,] scores = new double[4, 8];
                bool ok = true;

                for (int sIdx = 0; sIdx < 4; sIdx++)
                {
                    TemplateData tpl = target.Slices[sIdx].CachedTemplate;
                    if (tpl == null) { ok = false; break; }

                    for (int slotIdx = 0; slotIdx < 8; slotIdx++)
                    {
                        scores[sIdx, slotIdx] = FindBestCorrelation(slots[slotIdx], tpl);
                    }
                }

                if (ok)
                {
                    results.Add(FindBestAssignment(target, scores));
                }
            }

            if (results.Count == 0) return false;
            results.Sort((a, b) => b.AverageScore.CompareTo(a.AverageScore));
            AssignmentResult winner = results[0];
            double runnerUp = results.Count > 1 ? results[1].AverageScore : -1;
            double margin = winner.AverageScore - runnerUp;

            if (winner.AverageScore < MinimumAverageScore || margin < MinimumTargetMargin)
            {
                return false;
            }

            matchedTarget = winner.Target;
            detectedSlots = new List<int>();
            foreach (int s in winner.SliceSlots) detectedSlots.Add(s + 1);
            detectedSlots.Sort();
            confidence = winner.AverageScore;
            return true;
        }

        private static List<Rectangle[]> GetSlotRectangleCandidates(Bitmap screenshot)
        {
            List<Rectangle[]> result = new List<Rectangle[]>();
            AddRectangleCandidate(result, GetSlotRectangles(screenshot));
            foreach (CanonicalScreenTransform transform in CanonicalScreenLayouts.Create(screenshot))
            {
                Rectangle[] rectangles = new Rectangle[8];
                int index = 0;
                for (int row = 0; row < 4; row++)
                    for (int col = 0; col < 2; col++)
                        rectangles[index++] = transform.Map(new Rectangle(
                            (int)Math.Round(456.0 + col * 150.2),
                            (int)Math.Round(270.0 + row * 145.2),
                            137, 132));
                AddRectangleCandidate(result, rectangles);
            }
            return result;
        }

        private static void AddRectangleCandidate(List<Rectangle[]> candidates, Rectangle[] value)
        {
            if (value == null || value.Length != 8) return;
            foreach (Rectangle[] existing in candidates)
            {
                bool same = true;
                for (int index = 0; index < value.Length; index++)
                {
                    if (Math.Abs(existing[index].X - value[index].X) > 1
                        || Math.Abs(existing[index].Y - value[index].Y) > 1
                        || Math.Abs(existing[index].Width - value[index].Width) > 1
                        || Math.Abs(existing[index].Height - value[index].Height) > 1) { same = false; break; }
                }
                if (same) return;
            }
            candidates.Add(value);
        }

        public static Rectangle[] GetSlotRectangles(Bitmap screenshot)
        {
            if (screenshot == null) return new Rectangle[0];
            double scale, offsetX, offsetY, slotLeft, slotTop, columnStep, rowStep, slotWidth, slotHeight;
            double aspectRatio = screenshot.Width / (double)screenshot.Height;

            if (aspectRatio >= 1.70)
            {
                const double safeWidth = 1920.0;
                const double safeHeight = 1080.0;
                scale = Math.Min(screenshot.Width / safeWidth, screenshot.Height / safeHeight);
                offsetX = (screenshot.Width - safeWidth * scale) / 2.0;
                offsetY = (screenshot.Height - safeHeight * scale) / 2.0;
                slotLeft = 456.0;
                slotTop = 270.0;
                columnStep = 150.2;
                rowStep = 145.2;
                slotWidth = 136.8;
                slotHeight = 132.3;
            }
            else
            {
                const double refWidth = 952.0;
                const double refHeight = 592.0;
                scale = Math.Min(screenshot.Width / refWidth, screenshot.Height / refHeight);
                offsetX = (screenshot.Width - refWidth * scale) / 2.0;
                offsetY = (screenshot.Height - refHeight * scale) / 2.0;
                slotLeft = 200.0;
                slotTop = 148.0;
                columnStep = 82.35;
                rowStep = 79.6;
                slotWidth = 75.0;
                slotHeight = 72.5;
            }

            Rectangle[] rectangles = new Rectangle[8];
            int index = 0;
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 2; col++)
                {
                    int x = (int)Math.Round(offsetX + (slotLeft + col * columnStep) * scale);
                    int y = (int)Math.Round(offsetY + (slotTop + row * rowStep) * scale);
                    int width = Math.Max(8, (int)Math.Round(slotWidth * scale));
                    int height = Math.Max(8, (int)Math.Round(slotHeight * scale));
                    rectangles[index++] = new Rectangle(x, y, width, height);
                }
            }
            return rectangles;
        }

        public static int DetectCursorSlot(Bitmap screenshot)
        {
            if (screenshot == null) return 0;
            int winner = 0;
            double winnerStrength = 0;
            foreach (Rectangle[] slots in GetCursorSlotRectangleCandidates(screenshot))
            {
                double strength;
                int current = DetectCursorSlotInLayout(screenshot, slots, out strength);
                if (current != 0 && strength > winnerStrength)
                {
                    winner = current;
                    winnerStrength = strength;
                }
            }
            return winner;
        }

        private static int DetectCursorSlotInLayout(Bitmap screenshot, Rectangle[] slots, out double strength)
        {
            strength = 0;
            if (slots.Length != 8) return 0;
            double best = -1, runnerUp = -1;
            int bestSlot = 0;

            for (int index = 0; index < slots.Length; index++)
            {
                Rectangle slot = slots[index];
                // The white corner bracket sits roughly 10-12 px outside a
                // 120 px tile in the 1920x1080 layout.
                int expand = Math.Max(5, (int)Math.Round(slot.Width * 0.13));
                Rectangle outer = Rectangle.FromLTRB(
                    Math.Max(0, slot.Left - expand),
                    Math.Max(0, slot.Top - expand),
                    Math.Min(screenshot.Width, slot.Right + expand),
                    Math.Min(screenshot.Height, slot.Bottom + expand));
                if (outer.Width < 10 || outer.Height < 10) continue;

                int light = 0, sampled = 0;
                for (int y = outer.Top; y < outer.Bottom; y++)
                {
                    for (int x = outer.Left; x < outer.Right; x++)
                    {
                        // Sample only outside the slot image. Including even one
                        // row of the fingerprint itself makes bright answer tiles
                        // look like the white active-cursor bracket at 720p.
                        if (x >= slot.Left && x < slot.Right && y >= slot.Top && y < slot.Bottom) continue;
                        Color pixel = screenshot.GetPixel(x, y);
                        sampled++;
                        int max = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
                        int min = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                        if (pixel.R > 180 && pixel.G > 180 && pixel.B > 180 && max - min < 45) light++;
                    }
                }
                double score = sampled > 0 ? light / (double)sampled : 0;
                if (score > best)
                {
                    runnerUp = best;
                    best = score;
                    bestSlot = index + 1;
                }
                else if (score > runnerUp) runnerUp = score;
            }

            // At 720p the one-pixel active bracket occupies a smaller portion of
            // the sampled band. Keep a stricter threshold at 1080p+ and use a
            // scale-aware floor at lower resolutions. An uncertain result still
            // returns 0 and therefore never overrides the last stable cursor.
            double minimumScore = screenshot.Width < 1600 ? 0.012 : 0.020;
            double minimumLead = screenshot.Width < 1600 ? 0.003 : 0.004;
            double lead = best - runnerUp;
            if (best < minimumScore || lead < minimumLead) return 0;
            strength = best + lead;
            return bestSlot;
        }

        private static List<Rectangle[]> GetCursorSlotRectangleCandidates(Bitmap screenshot)
        {
            List<Rectangle[]> result = new List<Rectangle[]>();
            AddRectangleCandidate(result, GetCursorSlotRectangles(screenshot));
            foreach (CanonicalScreenTransform transform in CanonicalScreenLayouts.Create(screenshot))
            {
                Rectangle[] rectangles = new Rectangle[8];
                int index = 0;
                for (int row = 0; row < 4; row++)
                    for (int col = 0; col < 2; col++)
                        rectangles[index++] = transform.Map(new Rectangle(
                            474 + col * 145, 270 + row * 145, 120, 121));
                AddRectangleCandidate(result, rectangles);
            }
            return result;
        }

        private static Rectangle[] GetCursorSlotRectangles(Bitmap screenshot)
        {
            // The recognition crop deliberately contains a generous margin,
            // while cursor detection must follow the visible 2x4 tile edge.
            // Keep a separate geometry model so bright fingerprint ridges are
            // never sampled as if they were the active white bracket.
            double aspect = screenshot.Height > 0 ? screenshot.Width / (double)screenshot.Height : 0;
            if (aspect < 1.70)
            {
                Rectangle[] recognition = GetSlotRectangles(screenshot);
                for (int i = 0; i < recognition.Length; i++)
                {
                    Rectangle item = recognition[i];
                    int insetX = Math.Max(2, (int)Math.Round(item.Width * 0.10));
                    int insetY = Math.Max(1, (int)Math.Round(item.Height * 0.025));
                    recognition[i] = Rectangle.Inflate(item, -insetX, -insetY);
                }
                return recognition;
            }

            const double refWidth = 1920.0;
            const double refHeight = 1080.0;
            double scale = Math.Min(screenshot.Width / refWidth, screenshot.Height / refHeight);
            double offsetX = (screenshot.Width - refWidth * scale) / 2.0;
            double offsetY = (screenshot.Height - refHeight * scale) / 2.0;
            Rectangle[] result = new Rectangle[8];
            int index = 0;
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 2; col++)
                {
                    result[index++] = new Rectangle(
                        (int)Math.Round(offsetX + (474.0 + col * 145.0) * scale),
                        (int)Math.Round(offsetY + (270.0 + row * 145.0) * scale),
                        Math.Max(8, (int)Math.Round(120.0 * scale)),
                        Math.Max(8, (int)Math.Round(121.0 * scale)));
                }
            }
            return result;
        }

        public static bool DetectSignalError(Bitmap screenshot)
        {
            if (screenshot == null) return false;
            Rectangle area = new Rectangle(
                (int)(screenshot.Width * 0.34),
                (int)(screenshot.Height * 0.40),
                (int)(screenshot.Width * 0.32),
                (int)(screenshot.Height * 0.20));
            int red = 0, sampled = 0;
            int step = Math.Max(1, screenshot.Width / 960);
            for (int y = area.Top; y < area.Bottom; y += step)
            {
                for (int x = area.Left; x < area.Right; x += step)
                {
                    Color pixel = screenshot.GetPixel(x, y);
                    sampled++;
                    if (pixel.R > 155 && pixel.R > pixel.G * 1.7 && pixel.R > pixel.B * 1.45) red++;
                }
            }
            return sampled > 0 && red / (double)sampled >= 0.055;
        }

        private static List<byte[,]> ExtractSlots(Bitmap screenshot, Rectangle[] rectangles)
        {
            List<byte[,]> result = new List<byte[,]>();
            if (rectangles == null || rectangles.Length != 8) return result;
            foreach (Rectangle rectangle in rectangles)
            {
                using (Bitmap crop = Crop(screenshot, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height))
                {
                    if (crop == null) { result.Clear(); return result; }
                    result.Add(ToGray(crop, 40, 39));
                }
            }
            return result;
        }

        public static TemplateData PrepareTemplate(Bitmap image)
        {
            if (image == null) return null;
            byte[,] pixels = ToGray(image, 31, 33);
            double mean = 0;
            int h = pixels.GetLength(0);
            int w = pixels.GetLength(1);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    mean += pixels[y, x];
            mean /= (w * h);

            double variance = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double diff = pixels[y, x] - mean;
                    variance += diff * diff;
                }
            if (variance < 1e-5) return null;
            return new TemplateData { Pixels = pixels, Mean = mean, Deviation = Math.Sqrt(variance) };
        }

        private static double FindBestCorrelation(byte[,] source, TemplateData template)
        {
            int sh = source.GetLength(0);
            int sw = source.GetLength(1);
            int th = template.Pixels.GetLength(0);
            int tw = template.Pixels.GetLength(1);
            if (tw > sw || th > sh) return -1;

            double best = -1;
            for (int offsetY = 0; offsetY <= sh - th; offsetY++)
            {
                for (int offsetX = 0; offsetX <= sw - tw; offsetX++)
                {
                    double patchMean = 0;
                    for (int y = 0; y < th; y++)
                        for (int x = 0; x < tw; x++)
                            patchMean += source[offsetY + y, offsetX + x];
                    patchMean /= (tw * th);

                    double num = 0, pVar = 0;
                    for (int y = 0; y < th; y++)
                    {
                        for (int x = 0; x < tw; x++)
                        {
                            double td = template.Pixels[y, x] - template.Mean;
                            double pd = source[offsetY + y, offsetX + x] - patchMean;
                            num += td * pd;
                            pVar += pd * pd;
                        }
                    }
                    if (pVar > 1e-5)
                    {
                        double score = num / (template.Deviation * Math.Sqrt(pVar));
                        if (score > best) best = score;
                    }
                }
            }
            return best;
        }

        private static AssignmentResult FindBestAssignment(FingerprintTarget target, double[,] scores)
        {
            AssignmentResult best = null;
            for (int a = 0; a < 8; a++)
            for (int b = 0; b < 8; b++)
            {
                if (b == a) continue;
                for (int c = 0; c < 8; c++)
                {
                    if (c == a || c == b) continue;
                    for (int d = 0; d < 8; d++)
                    {
                        if (d == a || d == b || d == c) continue;
                        double avg = (scores[0, a] + scores[1, b] + scores[2, c] + scores[3, d]) / 4.0;
                        if (best == null || avg > best.AverageScore)
                        {
                            best = new AssignmentResult
                            {
                                Target = target,
                                SliceSlots = new int[] { a, b, c, d },
                                AverageScore = avg
                            };
                        }
                    }
                }
            }
            return best;
        }

        private static Bitmap Crop(Bitmap src, int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0 || x < 0 || y < 0 || x + w > src.Width || y + h > src.Height) return null;
            Bitmap res = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(res))
            {
                g.DrawImage(src, new Rectangle(0, 0, w, h), new Rectangle(x, y, w, h), GraphicsUnit.Pixel);
            }
            return res;
        }

        private static byte[,] ToGray(Bitmap bmp, int w, int h)
        {
            byte[,] res = new byte[h, w];
            using (Bitmap scaled = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.DrawImage(bmp, 0, 0, w, h);
                }
                BitmapData bd = scaled.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                unsafe
                {
                    byte* p = (byte*)bd.Scan0;
                    int stride = bd.Stride;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int idx = y * stride + x * 3;
                            res[y, x] = (byte)((p[idx + 2] * 30 + p[idx + 1] * 59 + p[idx] * 11) / 100);
                        }
                }
                scaled.UnlockBits(bd);
            }
            return res;
        }
    }

    internal sealed class CanonicalScreenTransform
    {
        public string Name;
        public double ScaleX;
        public double ScaleY;
        public double OffsetX;
        public double OffsetY;

        public double RadiusScale { get { return Math.Min(Math.Abs(ScaleX), Math.Abs(ScaleY)); } }
        public int X(double canonicalX) { return (int)Math.Round(OffsetX + canonicalX * ScaleX); }
        public int Y(double canonicalY) { return (int)Math.Round(OffsetY + canonicalY * ScaleY); }

        public Rectangle Map(Rectangle canonical)
        {
            return new Rectangle(
                X(canonical.X), Y(canonical.Y),
                Math.Max(1, (int)Math.Round(canonical.Width * ScaleX)),
                Math.Max(1, (int)Math.Round(canonical.Height * ScaleY)));
        }
    }

    internal static class CanonicalScreenLayouts
    {
        private const double ReferenceWidth = 1920.0;
        private const double ReferenceHeight = 1080.0;

        public static List<CanonicalScreenTransform> Create(Bitmap image)
        {
            List<CanonicalScreenTransform> result = new List<CanonicalScreenTransform>();
            if (image == null || image.Width <= 0 || image.Height <= 0) return result;

            double sx = image.Width / ReferenceWidth;
            double sy = image.Height / ReferenceHeight;
            Add(result, "stretch", sx, sy, 0, 0);

            double fit = Math.Min(sx, sy);
            double fitX = (image.Width - ReferenceWidth * fit) / 2.0;
            double fitY = (image.Height - ReferenceHeight * fit) / 2.0;
            Add(result, "fit-center", fit, fit, fitX, fitY);
            Add(result, "fit-top", fit, fit, fitX, 0);

            // GTA's 16:10 HUD commonly keeps the 16:9 reference width and
            // anchors it near the top instead of stretching it vertically.
            Add(result, "width-top", sx, sx, 0, 0);
            Add(result, "width-center", sx, sx, 0, (image.Height - ReferenceHeight * sx) / 2.0);

            // Safe-zone settings can move the whole hacking panel slightly
            // toward or away from the centre. Try a small bounded family and
            // require the scanner's structural checks to accept the result.
            foreach (double factor in new double[] { 0.96, 1.04 })
            {
                double scale = fit * factor;
                Add(result, "safe-" + factor.ToString("F2"), scale, scale,
                    (image.Width - ReferenceWidth * scale) / 2.0,
                    (image.Height - ReferenceHeight * scale) / 2.0);
            }
            return result;
        }

        private static void Add(List<CanonicalScreenTransform> result, string name,
            double scaleX, double scaleY, double offsetX, double offsetY)
        {
            if (scaleX <= 0 || scaleY <= 0) return;
            foreach (CanonicalScreenTransform existing in result)
            {
                if (Math.Abs(existing.ScaleX - scaleX) < 0.0001
                    && Math.Abs(existing.ScaleY - scaleY) < 0.0001
                    && Math.Abs(existing.OffsetX - offsetX) < 0.5
                    && Math.Abs(existing.OffsetY - offsetY) < 0.5) return;
            }
            result.Add(new CanonicalScreenTransform
            {
                Name = name,
                ScaleX = scaleX,
                ScaleY = scaleY,
                OffsetX = offsetX,
                OffsetY = offsetY
            });
        }
    }

    public sealed class CayoVoltageResult
    {
        public int Target;
        public int[] LeftNumbers;
        public int[] RightMultipliers;
        public int[] Assignment;
        public double Confidence;

        public string DetectionKey
        {
            get { return "voltage:" + Target + ":" + string.Join(",", LeftNumbers) + ":" + string.Join(",", RightMultipliers) + ":" + string.Join(",", Assignment); }
        }
    }

    // Reads the real VOLTlab screen after normalising the captured GTA client to
    // the game's 1920x1080 reference layout. It only accepts a frame when every
    // seven-segment digit is valid, the right side contains exactly x1/x2/x10,
    // and the target has at least one mathematically valid wiring permutation.
    public static class CayoVoltageScanner
    {
        public static string LastDiagnostic { get; private set; }
        private static int _preferredTransform = -1;
        private static int _preferredWidth;
        private static int _preferredHeight;
        private static int _lastThreshold = 100;
        private static readonly int[][] DigitPatterns = new int[][]
        {
            new int[] { 1, 1, 1, 0, 1, 1, 1 },
            new int[] { 0, 0, 1, 0, 0, 1, 0 },
            new int[] { 1, 0, 1, 1, 1, 0, 1 },
            new int[] { 1, 0, 1, 1, 0, 1, 1 },
            new int[] { 0, 1, 1, 1, 0, 1, 0 },
            new int[] { 1, 1, 0, 1, 0, 1, 1 },
            new int[] { 1, 1, 0, 1, 1, 1, 1 },
            new int[] { 1, 0, 1, 0, 0, 1, 0 },
            new int[] { 1, 1, 1, 1, 1, 1, 1 },
            new int[] { 1, 1, 1, 1, 0, 1, 1 }
        };

        private static readonly int[] TargetY = new int[] { 123, 137, 137, 154, 173, 173, 195 };
        private static readonly int[][] TargetX = new int[][]
        {
            new int[] { 865, 849, 881, 865, 849, 881, 865 },
            new int[] { 955, 939, 971, 955, 939, 971, 955 },
            new int[] { 1043, 1029, 1061, 1043, 1029, 1061, 1043 }
        };
        private static readonly int[] LeftX = new int[] { 509, 495, 527, 509, 495, 527, 509 };
        private static readonly int[][] LeftY = new int[][]
        {
            new int[] { 271, 287, 287, 303, 323, 323, 343 },
            new int[] { 507, 522, 522, 540, 557, 557, 579 },
            new int[] { 741, 755, 755, 773, 791, 791, 813 }
        };

        public static bool ScanBitmap(Bitmap screenshot, out CayoVoltageResult result)
        {
            result = null;
            if (screenshot == null || screenshot.Width < 960 || screenshot.Height < 540) return false;
            List<CanonicalScreenTransform> transforms = CanonicalScreenLayouts.Create(screenshot);
            if (_preferredWidth == screenshot.Width && _preferredHeight == screenshot.Height
                && _preferredTransform >= 0 && _preferredTransform < transforms.Count)
            {
                CayoVoltageResult preferred;
                if (TryScanTransform(screenshot, transforms[_preferredTransform], out preferred))
                {
                    result = preferred;
                    LastDiagnostic = "ok " + transforms[_preferredTransform].Name + " threshold=" + _lastThreshold + " cached";
                    return true;
                }
            }

            for (int index = 0; index < transforms.Count; index++)
            {
                CayoVoltageResult current;
                if (!TryScanTransform(screenshot, transforms[index], out current)) continue;
                result = current;
                _preferredTransform = index;
                _preferredWidth = screenshot.Width;
                _preferredHeight = screenshot.Height;
                LastDiagnostic = "ok " + transforms[index].Name + " threshold=" + _lastThreshold;
                return true;
            }
            LastDiagnostic = "no valid layout " + screenshot.Width + "x" + screenshot.Height;
            return false;
        }

        private static bool TryScanTransform(Bitmap screenshot, CanonicalScreenTransform transform, out CayoVoltageResult result)
        {
            result = null;
            foreach (int threshold in new int[] { 100, 70, 130, 160 })
            {
                CayoVoltageResult current;
                if (!TryScanTransformAtThreshold(screenshot, transform, threshold, out current)) continue;
                _lastThreshold = threshold;
                result = current;
                return true;
            }
            return false;
        }

        private static bool TryScanTransformAtThreshold(Bitmap screenshot, CanonicalScreenTransform transform,
            int threshold, out CayoVoltageResult result)
        {
            result = null;
            int[] targetDigits = new int[3];
            int[] left = new int[3];
            int[] right = new int[3];
            for (int index = 0; index < 3; index++)
            {
                targetDigits[index] = ReadDigit(screenshot, TargetX[index], TargetY, transform, threshold);
                left[index] = ReadDigit(screenshot, LeftX, LeftY[index], transform, threshold);
                right[index] = ReadMultiplier(screenshot, index, transform, threshold);
                if (targetDigits[index] < 0 || left[index] < 0 || right[index] < 0) return false;
            }
            int[] sortedRight = (int[])right.Clone();
            Array.Sort(sortedRight);
            if (sortedRight[0] != 1 || sortedRight[1] != 2 || sortedRight[2] != 10) return false;

            int target = targetDigits[0] * 100 + targetDigits[1] * 10 + targetDigits[2];
            List<int[]> solutions = Solve(target, left, right);
            if (solutions.Count == 0) return false;
            result = new CayoVoltageResult
            {
                Target = target,
                LeftNumbers = left,
                RightMultipliers = right,
                Assignment = solutions[0],
                Confidence = solutions.Count == 1 ? 0.97 : 0.93
            };
            return true;
        }

        public static List<int[]> Solve(int target, int[] left, int[] right)
        {
            List<int[]> matches = new List<int[]>();
            int[][] permutations = new int[][]
            {
                new int[] { 0, 1, 2 }, new int[] { 0, 2, 1 }, new int[] { 1, 0, 2 },
                new int[] { 1, 2, 0 }, new int[] { 2, 0, 1 }, new int[] { 2, 1, 0 }
            };
            if (left == null || right == null || left.Length != 3 || right.Length != 3) return matches;
            foreach (int[] p in permutations)
                if (left[0] * right[p[0]] + left[1] * right[p[1]] + left[2] * right[p[2]] == target)
                    matches.Add((int[])p.Clone());
            return matches;
        }

        private static int ReadDigit(Bitmap image, int[] xs, int[] ys, CanonicalScreenTransform transform, int threshold)
        {
            int[] observed = new int[7];
            for (int i = 0; i < 7; i++) observed[i] = IsBright(image, xs[i], ys[i], threshold, transform) ? 1 : 0;
            for (int digit = 0; digit < DigitPatterns.Length; digit++)
            {
                bool same = true;
                for (int i = 0; i < 7; i++) if (observed[i] != DigitPatterns[digit][i]) { same = false; break; }
                if (same) return digit;
            }
            return -1;
        }

        private static int ReadMultiplier(Bitmap image, int row, CanonicalScreenTransform transform, int threshold)
        {
            int baseY = row == 0 ? 0 : row == 1 ? 236 : 470;
            bool lower = IsBright(image, 1351, 305 + baseY, threshold, transform);
            bool upper = IsBright(image, 1349, 277 + baseY, threshold, transform);
            if (!lower && upper) return 10;
            if (lower && !upper) return 2;
            if (!lower && !upper) return 1;
            return -1;
        }

        private static bool IsBright(Bitmap image, int referenceX, int referenceY, int threshold,
            CanonicalScreenTransform transform)
        {
            int x = Math.Max(0, Math.Min(image.Width - 1, transform.X(referenceX)));
            int y = Math.Max(0, Math.Min(image.Height - 1, transform.Y(referenceY)));
            int radius = Math.Max(1, (int)Math.Round(transform.RadiusScale * 1.5));
            int maximum = 0;
            for (int yy = Math.Max(0, y - radius); yy <= Math.Min(image.Height - 1, y + radius); yy++)
                for (int xx = Math.Max(0, x - radius); xx <= Math.Min(image.Width - 1, x + radius); xx++)
                {
                    Color c = image.GetPixel(xx, yy);
                    maximum = Math.Max(maximum, (c.R * 30 + c.G * 59 + c.B * 11) / 100);
                }
            return maximum > threshold;
        }
    }

    public sealed class CasinoKeypadResult
    {
        public int[] Rows;
        public double Confidence;
        public double MinimumColumnConfidence;
        public string DetectionKey { get { return "keypad:" + string.Join(",", Rows); } }
    }

    // Captures the six cyan columns while the real casino keypad pattern is
    // visible. Each column must contain exactly one illuminated row.
    public static class CasinoKeypadScanner
    {
        public static string LastRingDiagnostic { get; private set; }
        public static string LastPatternDiagnostic { get; private set; }
        private static int _preferredTransform = -1;
        private static int _preferredWidth;
        private static int _preferredHeight;

        public static bool ScanPattern(Bitmap screenshot, out CasinoKeypadResult result)
        {
            result = null;
            if (screenshot == null || screenshot.Width < 960 || screenshot.Height < 540) return false;
            List<CanonicalScreenTransform> transforms = CanonicalScreenLayouts.Create(screenshot);
            CasinoKeypadResult winner = null;
            int winnerIndex = -1;
            for (int index = 0; index < transforms.Count; index++)
            {
                CasinoKeypadResult current;
                if (!TryScanPatternTransform(screenshot, transforms[index], out current)) continue;
                double currentScore = current.MinimumColumnConfidence * 0.70 + current.Confidence * 0.30;
                double winnerScore = winner == null ? -1.0
                    : winner.MinimumColumnConfidence * 0.70 + winner.Confidence * 0.30;
                bool cachedTie = Math.Abs(currentScore - winnerScore) < 0.015
                    && _preferredWidth == screenshot.Width && _preferredHeight == screenshot.Height
                    && index == _preferredTransform;
                if (winner == null || currentScore > winnerScore || cachedTie)
                {
                    winner = current;
                    winnerIndex = index;
                }
            }
            if (winner == null)
            {
                LastPatternDiagnostic = "no valid layout " + screenshot.Width + "x" + screenshot.Height;
                return false;
            }
            result = winner;
            _preferredTransform = winnerIndex;
            _preferredWidth = screenshot.Width;
            _preferredHeight = screenshot.Height;
            LastPatternDiagnostic = "ok " + transforms[winnerIndex].Name;
            return true;
        }

        private static bool TryScanPatternTransform(Bitmap screenshot, CanonicalScreenTransform transform,
            out CasinoKeypadResult result)
        {
            result = null;
            int[] rows = new int[6];
            double confidenceTotal = 0;
            double minimumConfidence = 1.0;
            for (int column = 0; column < rows.Length; column++)
            {
                int bestRow = -1, bestCount = 0, secondCount = 0;
                for (int row = 0; row < 5; row++)
                {
                    int count = CountCyan(screenshot, column, row, transform);
                    if (count > bestCount) { secondCount = bestCount; bestCount = count; bestRow = row; }
                    else if (count > secondCount) secondCount = count;
                }
                // Sampling stride scales with the layout, so the observed dot
                // count stays roughly constant from 720p through 4K.
                int minimum = 8;
                double columnConfidence = (bestCount - secondCount) / (double)Math.Max(minimum, bestCount);
                if (bestRow < 0 || bestCount < minimum || bestCount < secondCount * 1.40
                    || columnConfidence < 0.28) return false;
                rows[column] = bestRow + 1;
                columnConfidence = Math.Min(1.0, columnConfidence);
                confidenceTotal += columnConfidence;
                minimumConfidence = Math.Min(minimumConfidence, columnConfidence);
            }
            result = new CasinoKeypadResult
            {
                Rows = rows,
                Confidence = confidenceTotal / rows.Length,
                MinimumColumnConfidence = minimumConfidence
            };
            return true;
        }

        public static bool IsInputStage(Bitmap screenshot)
        {
            if (screenshot == null) return false;
            int row;
            for (int column = 1; column <= 6; column++)
                if (TryDetectRingRow(screenshot, column, out row)) return true;
            return false;
        }

        // During input GTA draws a thick white ring around the currently
        // selected node. Read that ring for the requested column instead of
        // assuming the cursor stays on the previous row while GTA validates a
        // choice and advances to the next column.
        public static bool TryDetectRingRow(Bitmap screenshot, int column, out int row)
        {
            row = 0;
            if (screenshot == null || column < 1 || column > 6 || screenshot.Width < 960 || screenshot.Height < 540)
                return false;

            List<CanonicalScreenTransform> transforms = CanonicalScreenLayouts.Create(screenshot);
            if (_preferredWidth == screenshot.Width && _preferredHeight == screenshot.Height
                && _preferredTransform >= 0 && _preferredTransform < transforms.Count)
            {
                string preferredDiagnostic;
                if (TryDetectRingRowTransform(screenshot, column, transforms[_preferredTransform], out row, out preferredDiagnostic))
                {
                    LastRingDiagnostic = preferredDiagnostic + " layout=" + transforms[_preferredTransform].Name;
                    return true;
                }
            }

            for (int index = 0; index < transforms.Count; index++)
            {
                string diagnostic;
                if (!TryDetectRingRowTransform(screenshot, column, transforms[index], out row, out diagnostic)) continue;
                _preferredTransform = index;
                _preferredWidth = screenshot.Width;
                _preferredHeight = screenshot.Height;
                LastRingDiagnostic = diagnostic + " layout=" + transforms[index].Name;
                return true;
            }
            LastRingDiagnostic = "miss col=" + column + " layouts=" + transforms.Count;
            return false;
        }

        private static bool TryDetectRingRowTransform(Bitmap screenshot, int column,
            CanonicalScreenTransform transform, out int row, out string diagnostic)
        {
            row = 0;
            diagnostic = "";

            double radiusScale = transform.RadiusScale;
            double innerRadius = Math.Max(18.0, 43.0 * radiusScale);
            double outerRadius = Math.Max(innerRadius + 5.0, 56.0 * radiusScale);
            double innerSquared = innerRadius * innerRadius;
            double outerSquared = outerRadius * outerRadius;
            int centerX = transform.X(499 + (column - 1) * 108);
            int bestRow = 0;
            double bestRatio = 0, secondRatio = 0;

            for (int candidate = 1; candidate <= 5; candidate++)
            {
                int centerY = transform.Y(343 + (candidate - 1) * 107);
                int radius = (int)Math.Ceiling(outerRadius);
                int bright = 0, samples = 0;
                int step = Math.Max(1, (int)Math.Floor(radiusScale));
                for (int y = Math.Max(0, centerY - radius); y <= Math.Min(screenshot.Height - 1, centerY + radius); y += step)
                    for (int x = Math.Max(0, centerX - radius); x <= Math.Min(screenshot.Width - 1, centerX + radius); x += step)
                    {
                        double dx = x - centerX, dy = y - centerY;
                        double distanceSquared = dx * dx + dy * dy;
                        if (distanceSquared < innerSquared || distanceSquared > outerSquared) continue;
                        Color color = screenshot.GetPixel(x, y);
                        int gray = (color.R * 30 + color.G * 59 + color.B * 11) / 100;
                        int maximum = Math.Max(color.R, Math.Max(color.G, color.B));
                        int minimum = Math.Min(color.R, Math.Min(color.G, color.B));
                        if (gray >= 170 && maximum - minimum <= 80) bright++;
                        samples++;
                    }
                double ratio = samples == 0 ? 0 : bright / (double)samples;
                if (ratio > bestRatio)
                {
                    secondRatio = bestRatio;
                    bestRatio = ratio;
                    bestRow = candidate;
                }
                else if (ratio > secondRatio) secondRatio = ratio;
            }

            // Inactive outlines are thin (normally below 5% of the annulus),
            // while the active double ring occupies roughly a quarter of it.
            if (bestRow != 0 && bestRatio >= 0.10 && bestRatio >= secondRatio * 1.55)
            {
                row = bestRow;
                diagnostic = "annulus col=" + column + " row=" + row + " score=" + bestRatio.ToString("F3");
                return true;
            }

            // Safe-zone and window decorations can shift the ring a few pixels
            // relative to the 1920x1080 reference center. Fall back to comparing
            // the whole cell band, where the thick active ring still contains
            // far more bright pixels than the five inactive outlines.
            int fallbackRow;
            double fallbackBest, fallbackSecond;
            if (TryDetectRingByCellBrightness(screenshot, column, transform, out fallbackRow, out fallbackBest, out fallbackSecond))
            {
                row = fallbackRow;
                diagnostic = "cell col=" + column + " row=" + row + " score=" + fallbackBest.ToString("F3");
                return true;
            }
            diagnostic = "miss col=" + column + " annulus=" + bestRatio.ToString("F3")
                + "/" + secondRatio.ToString("F3") + " cell=" + fallbackBest.ToString("F3")
                + "/" + fallbackSecond.ToString("F3");
            return false;
        }

        private static bool TryDetectRingByCellBrightness(Bitmap image, int column,
            CanonicalScreenTransform transform, out int row, out double best, out double second)
        {
            row = 0; best = 0; second = 0;
            int x1 = transform.X(440 + (column - 1) * 108);
            int x2 = transform.X(558 + (column - 1) * 108);
            int step = Math.Max(1, (int)Math.Floor(transform.RadiusScale * 2));
            x1 = Math.Max(0, Math.Min(image.Width - 1, x1));
            x2 = Math.Max(x1 + 1, Math.Min(image.Width, x2));
            for (int candidate = 1; candidate <= 5; candidate++)
            {
                int y1 = transform.Y(289 + (candidate - 1) * 107);
                int y2 = transform.Y(396 + (candidate - 1) * 107);
                y1 = Math.Max(0, Math.Min(image.Height - 1, y1));
                y2 = Math.Max(y1 + 1, Math.Min(image.Height, y2));
                int bright = 0, samples = 0;
                for (int y = y1; y < y2; y += step)
                    for (int x = x1; x < x2; x += step)
                    {
                        Color color = image.GetPixel(x, y);
                        int gray = (color.R * 30 + color.G * 59 + color.B * 11) / 100;
                        if (gray >= 145) bright++;
                        samples++;
                    }
                double ratio = samples == 0 ? 0 : bright / (double)samples;
                if (ratio > best) { second = best; best = ratio; row = candidate; }
                else if (ratio > second) second = ratio;
            }
            return row != 0 && best >= 0.065 && best >= second * 1.8;
        }

        private static Color Sample(Bitmap image, int referenceX, int referenceY)
        {
            int x = Math.Max(0, Math.Min(image.Width - 1, (int)Math.Round(referenceX * image.Width / 1920.0)));
            int y = Math.Max(0, Math.Min(image.Height - 1, (int)Math.Round(referenceY * image.Height / 1080.0)));
            int radius = Math.Max(1, (int)Math.Round(Math.Min(image.Width / 1920.0, image.Height / 1080.0)));
            int r = 0, g = 0, b = 0, count = 0;
            for (int yy = Math.Max(0, y - radius); yy <= Math.Min(image.Height - 1, y + radius); yy++)
                for (int xx = Math.Max(0, x - radius); xx <= Math.Min(image.Width - 1, x + radius); xx++)
                {
                    Color c = image.GetPixel(xx, yy); r += c.R; g += c.G; b += c.B; count++;
                }
            return count == 0 ? Color.Black : Color.FromArgb(r / count, g / count, b / count);
        }

        private static int CountCyan(Bitmap image, int column, int row, CanonicalScreenTransform transform)
        {
            int x1 = transform.X(456 + column * 108);
            int x2 = transform.X(557 + column * 108);
            double rowHeight = (831 - 297) / 5.0;
            int y1 = transform.Y(297 + row * rowHeight);
            int y2 = transform.Y(297 + (row + 1) * rowHeight);
            x1 = Math.Max(0, Math.Min(image.Width - 1, x1)); x2 = Math.Max(x1 + 1, Math.Min(image.Width, x2));
            y1 = Math.Max(0, Math.Min(image.Height - 1, y1)); y2 = Math.Max(y1 + 1, Math.Min(image.Height, y2));
            int step = Math.Max(1, (int)Math.Round(transform.RadiusScale * 2));
            int count = 0;
            for (int y = y1; y < y2; y += step)
                for (int x = x1; x < x2; x += step)
                    if (IsCyan(image.GetPixel(x, y))) count++;
            return count;
        }

        private static bool IsCyan(Color color)
        {
            int max = Math.Max(color.R, Math.Max(color.G, color.B));
            int min = Math.Min(color.R, Math.Min(color.G, color.B));
            return max >= 90 && max - min >= 28
                && color.G > color.R + 14 && color.B > color.R + 12
                && Math.Abs(color.G - color.B) <= 82;
        }
    }

    // Cayo Perico fingerprint cloner. The current rows are matched against the
    // eight bands of the target fingerprint from the same frame, so no Rockstar
    // artwork or web screenshots are shipped as templates.
    // Coordinate strategy adapted from infpdev/gtao-heist-toolkit (license and
    // attribution: https://github.com/infpdev/gtao-heist-toolkit).
    public sealed class CayoFingerprintResult
    {
        public int[] Clicks;
        public int CursorRow;
        public double Confidence;
        public string TargetSignature;

        public string DetectionKey
        {
            get { return "cayo:" + CursorRow + ":" + string.Join(",", Clicks); }
        }
    }

    public static class CayoFingerprintScanner
    {
        public static string LastDiagnostic { get; private set; }
        private static int _preferredTransform = -1;
        private static int _preferredWidth;
        private static int _preferredHeight;
        private const double MinimumAssignmentAverage = 0.34;
        private const double MinimumAssignedRowScore = 0.24;
        private const double MinimumAssignmentMargin = 0.002;
        private static readonly Rectangle[] TargetRects = new Rectangle[]
        {
            new Rectangle(907, 331, 655, 100), new Rectangle(907, 404, 655, 100),
            new Rectangle(907, 500, 655, 100), new Rectangle(907, 560, 655, 100),
            new Rectangle(907, 627, 655, 100), new Rectangle(907, 697, 655, 112),
            new Rectangle(907, 780, 655, 103), new Rectangle(907, 863, 655, 112)
        };
        private static readonly Rectangle[] ScanRects = new Rectangle[]
        {
            new Rectangle(424, 360, 386, 55), new Rectangle(424, 436, 386, 55),
            new Rectangle(424, 512, 386, 55), new Rectangle(424, 588, 386, 55),
            new Rectangle(424, 664, 386, 55), new Rectangle(424, 740, 386, 55),
            new Rectangle(424, 816, 386, 55), new Rectangle(424, 892, 386, 55)
        };

        private sealed class ScreenTransform
        {
            public double Scale;
            public double OffsetX;
            public double OffsetY;
        }

        public static bool ScanBitmap(Bitmap screenshot, out CayoFingerprintResult result)
        {
            result = null;
            if (screenshot == null || screenshot.Width < 960 || screenshot.Height < 540) return false;

            double aspect = screenshot.Width / (double)screenshot.Height;
            // GTA uses several safe-zone layouts (keyboard/controller, 16:9/16:10
            // and different HUD safe-zone settings). Test a small bounded set of
            // known transforms and only accept a complete eight-row solution.
            double baseScale = aspect < 1.70
                ? screenshot.Width / 1920.0
                : Math.Min(screenshot.Width / 1920.0, screenshot.Height / 1080.0);
            double centerX = aspect < 1.70 ? 0 : (screenshot.Width - 1920.0 * baseScale) / 2.0;
            double centerY = aspect < 1.70 ? 0 : (screenshot.Height - 1080.0 * baseScale) / 2.0;
            ScreenTransform[] candidates = new ScreenTransform[]
            {
                new ScreenTransform { Scale = baseScale, OffsetX = centerX, OffsetY = centerY },
                new ScreenTransform { Scale = baseScale * 1.04, OffsetX = centerX - 40 * baseScale, OffsetY = centerY + 40 * baseScale },
                new ScreenTransform { Scale = baseScale * 0.98, OffsetX = centerX + 45 * baseScale, OffsetY = centerY - 60 * baseScale },
                new ScreenTransform { Scale = baseScale, OffsetX = centerX, OffsetY = centerY + 40 * baseScale }
            };

            if (_preferredWidth == screenshot.Width && _preferredHeight == screenshot.Height
                && _preferredTransform >= 0 && _preferredTransform < candidates.Length)
            {
                ScreenTransform preferred = candidates[_preferredTransform];
                CayoFingerprintResult preferredResult;
                string preferredDiagnostic;
                if (TryScanTransform(screenshot, preferred.Scale, preferred.OffsetX, preferred.OffsetY, out preferredResult, out preferredDiagnostic))
                {
                    result = preferredResult;
                    LastDiagnostic = "ok " + result.Confidence.ToString("F3") + " cached=" + (_preferredTransform + 1);
                    return true;
                }
            }

            CayoFingerprintResult winner = null;
            int winnerIndex = -1;
            List<string> diagnostics = new List<string>();
            int candidateIndex = 0;
            foreach (ScreenTransform candidate in candidates)
            {
                candidateIndex++;
                CayoFingerprintResult current;
                string diagnostic;
                if (TryScanTransform(screenshot, candidate.Scale, candidate.OffsetX, candidate.OffsetY, out current, out diagnostic))
                {
                    if (winner == null || current.Confidence > winner.Confidence) { winner = current; winnerIndex = candidateIndex - 1; }
                }
                if (!String.IsNullOrEmpty(diagnostic)) diagnostics.Add(candidateIndex + ":" + diagnostic);
            }
            result = winner;
            if (winnerIndex >= 0)
            {
                _preferredTransform = winnerIndex;
                _preferredWidth = screenshot.Width;
                _preferredHeight = screenshot.Height;
            }
            LastDiagnostic = result != null
                ? "ok " + result.Confidence.ToString("F3") + " layout=" + (winnerIndex + 1)
                : String.Join("; ", diagnostics.ToArray());
            return result != null;
        }

        public static int DetectCursorRow(Bitmap screenshot)
        {
            if (screenshot == null || screenshot.Width < 960 || screenshot.Height < 540) return -1;
            double aspect = screenshot.Width / (double)screenshot.Height;
            double baseScale = aspect < 1.70
                ? screenshot.Width / 1920.0
                : Math.Min(screenshot.Width / 1920.0, screenshot.Height / 1080.0);
            double centerX = aspect < 1.70 ? 0 : (screenshot.Width - 1920.0 * baseScale) / 2.0;
            double centerY = aspect < 1.70 ? 0 : (screenshot.Height - 1080.0 * baseScale) / 2.0;
            ScreenTransform[] candidates = new ScreenTransform[]
            {
                new ScreenTransform { Scale = baseScale, OffsetX = centerX, OffsetY = centerY },
                new ScreenTransform { Scale = baseScale * 1.04, OffsetX = centerX - 40 * baseScale, OffsetY = centerY + 40 * baseScale },
                new ScreenTransform { Scale = baseScale * 0.98, OffsetX = centerX + 45 * baseScale, OffsetY = centerY - 60 * baseScale },
                new ScreenTransform { Scale = baseScale, OffsetX = centerX, OffsetY = centerY + 40 * baseScale }
            };
            int tIndex = _preferredWidth == screenshot.Width && _preferredHeight == screenshot.Height
                && _preferredTransform >= 0 && _preferredTransform < candidates.Length ? _preferredTransform : 1;
            ScreenTransform transform = candidates[tIndex];

            int bestRow = -1;
            double bestScore = 0;
            for (int row = 0; row < 8; row++)
            {
                Rectangle scan = ScanRects[row];
                using (Bitmap leftInd = CropScaled(screenshot, new Rectangle(scan.X - 55, scan.Y, 45, scan.Height), transform.Scale, transform.OffsetX, transform.OffsetY))
                using (Bitmap rightInd = CropScaled(screenshot, new Rectangle(scan.X + scan.Width + 10, scan.Y, 45, scan.Height), transform.Scale, transform.OffsetX, transform.OffsetY))
                {
                    double indicatorWhite = WhitePixelRatio(leftInd) + WhitePixelRatio(rightInd);
                    if (indicatorWhite > bestScore)
                    {
                        bestScore = indicatorWhite;
                        bestRow = row + 1;
                    }
                }
            }
            return bestScore >= 0.04 ? bestRow : -1;
        }

        public static Rectangle[] GetFeedbackRectangles(Bitmap screenshot)
        {
            if (screenshot == null || screenshot.Width < 960 || screenshot.Height < 540) return new Rectangle[0];
            double aspect = screenshot.Width / (double)screenshot.Height;
            double scale = aspect < 1.70 ? screenshot.Width / 1920.0 : Math.Min(screenshot.Width / 1920.0, screenshot.Height / 1080.0);
            double offsetX = aspect < 1.70 ? 0 : (screenshot.Width - 1920.0 * scale) / 2.0;
            double offsetY = aspect < 1.70 ? 0 : (screenshot.Height - 1080.0 * scale) / 2.0;
            Rectangle[] result = new Rectangle[8];
            for (int i = 0; i < ScanRects.Length; i++)
            {
                Rectangle item = ScanRects[i];
                result[i] = new Rectangle(
                    (int)Math.Round(offsetX + item.X * scale), (int)Math.Round(offsetY + item.Y * scale),
                    Math.Max(8, (int)Math.Round(item.Width * scale)), Math.Max(8, (int)Math.Round(item.Height * scale)));
            }
            return result;
        }

        private static bool TryScanTransform(Bitmap screenshot, double scale, double offsetX, double offsetY, out CayoFingerprintResult result, out string diagnostic)
        {
            result = null;
            diagnostic = "";
            List<byte[,]> targets = new List<byte[,]>();
            List<Bitmap> scans = new List<Bitmap>();
            try
            {
                for (int i = 0; i < 8; i++)
                {
                    using (Bitmap crop = CropScaled(screenshot, TargetRects[i], scale, offsetX, offsetY))
                    {
                        if (crop == null) { diagnostic = "target crop"; return false; }
                        targets.Add(ToGray(crop, 150, 24));
                    }
                    Bitmap scan = CropScaled(screenshot, ScanRects[i], scale, offsetX, offsetY);
                    if (scan == null) { diagnostic = "scan crop"; return false; }
                    scans.Add(scan);
                }

                double[,] scores = new double[8, 8];
                int cursorRow = -1;
                double cursorScore = 0;
                for (int row = 0; row < 8; row++)
                {
                    byte[,] scanGray = ToGray(scans[row], 96, 14);
                    TemplateData scanTemplate = Prepare(scanGray);
                    if (scanTemplate == null) { diagnostic = "row " + (row + 1) + " blank"; return false; }
                    for (int part = 0; part < 8; part++)
                        scores[row, part] = Correlate(targets[part], scanTemplate);

                    Rectangle scan = ScanRects[row];
                    using (Bitmap leftInd = CropScaled(screenshot, new Rectangle(scan.X - 55, scan.Y, 45, scan.Height), scale, offsetX, offsetY))
                    using (Bitmap rightInd = CropScaled(screenshot, new Rectangle(scan.X + scan.Width + 10, scan.Y, 45, scan.Height), scale, offsetX, offsetY))
                    {
                        double indicatorWhite = WhitePixelRatio(leftInd) + WhitePixelRatio(rightInd);
                        if (indicatorWhite > cursorScore)
                        {
                            cursorScore = indicatorWhite;
                            cursorRow = row + 1;
                        }
                    }
                }

                int[] assignment;
                double assignmentScore, runnerScore;
                FindBestCayoAssignment(scores, out assignment, out assignmentScore, out runnerScore);
                if (assignment == null) { diagnostic = "assignment missing"; return false; }
                double average = assignmentScore / 8.0;
                double margin = (assignmentScore - runnerScore) / 8.0;
                double minimumAssigned = Double.MaxValue;
                for (int row = 0; row < 8; row++) minimumAssigned = Math.Min(minimumAssigned, scores[row, assignment[row]]);
                if (average < MinimumAssignmentAverage || minimumAssigned < MinimumAssignedRowScore
                    || margin < MinimumAssignmentMargin)
                {
                    diagnostic = "assignment avg=" + average.ToString("F3") + " min=" + minimumAssigned.ToString("F3")
                        + " margin=" + margin.ToString("F3");
                    return false;
                }

                int[] clicks = new int[8];
                for (int row = 0; row < 8; row++)
                {
                    int offset = (row - assignment[row] + 8) % 8;
                    clicks[row] = offset == 0 ? 0 : offset <= 4 ? offset : -(8 - offset);
                }
                if (cursorScore < 0.04) { diagnostic = "cursor " + cursorScore.ToString("F3"); return false; }
                result = new CayoFingerprintResult
                {
                    Clicks = clicks,
                    CursorRow = cursorRow,
                    Confidence = average,
                    TargetSignature = MakeSignature(targets)
                };
                return true;
            }
            finally
            {
                foreach (Bitmap scan in scans) scan.Dispose();
            }
        }

        private static void FindBestCayoAssignment(double[,] scores, out int[] assignment,
            out double bestScore, out double runnerScore)
        {
            bestScore = SolveCayoAssignment(scores, -1, -1, out assignment);
            runnerScore = Double.NegativeInfinity;
            if (assignment == null) return;
            for (int row = 0; row < 8; row++)
            {
                int[] ignored;
                double alternative = SolveCayoAssignment(scores, row, assignment[row], out ignored);
                if (alternative > runnerScore) runnerScore = alternative;
            }
        }

        private static double SolveCayoAssignment(double[,] scores, int forbiddenRow,
            int forbiddenPart, out int[] assignment)
        {
            assignment = null;
            double[] current = new double[256];
            for (int mask = 0; mask < current.Length; mask++) current[mask] = Double.NegativeInfinity;
            current[0] = 0;
            int[,] parentMask = new int[9, 256];
            int[,] parentPart = new int[9, 256];
            for (int row = 0; row < 8; row++)
            {
                double[] next = new double[256];
                for (int mask = 0; mask < next.Length; mask++) next[mask] = Double.NegativeInfinity;
                for (int mask = 0; mask < 256; mask++)
                {
                    if (Double.IsNegativeInfinity(current[mask])) continue;
                    for (int part = 0; part < 8; part++)
                    {
                        int bit = 1 << part;
                        if ((mask & bit) != 0 || (row == forbiddenRow && part == forbiddenPart)) continue;
                        int nextMask = mask | bit;
                        double value = current[mask] + scores[row, part];
                        if (value <= next[nextMask]) continue;
                        next[nextMask] = value;
                        parentMask[row + 1, nextMask] = mask;
                        parentPart[row + 1, nextMask] = part;
                    }
                }
                current = next;
            }

            if (Double.IsNegativeInfinity(current[255])) return current[255];
            assignment = new int[8];
            int cursor = 255;
            for (int row = 8; row >= 1; row--)
            {
                assignment[row - 1] = parentPart[row, cursor];
                cursor = parentMask[row, cursor];
            }
            return current[255];
        }

        public static bool SameTargetSignature(string left, string right)
        {
            if (String.IsNullOrEmpty(left) || String.IsNullOrEmpty(right)) return false;
            if (left.StartsWith("SIM-", StringComparison.Ordinal) || right.StartsWith("SIM-", StringComparison.Ordinal))
                return String.Equals(left, right, StringComparison.Ordinal);
            ulong a, b;
            if (!UInt64.TryParse(left, System.Globalization.NumberStyles.HexNumber, null, out a)
                || !UInt64.TryParse(right, System.Globalization.NumberStyles.HexNumber, null, out b)) return false;
            ulong value = a ^ b;
            int distance = 0;
            while (value != 0) { value &= value - 1; distance++; }
            return distance <= 6;
        }

        private static Bitmap CropScaled(Bitmap source, Rectangle canonical, double scale, double ox, double oy)
        {
            int x = (int)Math.Round(ox + canonical.X * scale);
            int y = (int)Math.Round(oy + canonical.Y * scale);
            int w = Math.Max(8, (int)Math.Round(canonical.Width * scale));
            int h = Math.Max(8, (int)Math.Round(canonical.Height * scale));
            if (x < 0 || y < 0 || x + w > source.Width || y + h > source.Height) return null;
            Bitmap result = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(result))
                g.DrawImage(source, new Rectangle(0, 0, w, h), new Rectangle(x, y, w, h), GraphicsUnit.Pixel);
            return result;
        }

        private static byte[,] ToGray(Bitmap bitmap, int width, int height)
        {
            byte[,] pixels = new byte[height, width];
            using (Bitmap scaled = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.DrawImage(bitmap, 0, 0, width, height);
                }
                BitmapData data = scaled.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                unsafe
                {
                    byte* p = (byte*)data.Scan0;
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            int n = y * data.Stride + x * 3;
                            pixels[y, x] = (byte)((p[n + 2] * 30 + p[n + 1] * 59 + p[n] * 11) / 100);
                        }
                }
                scaled.UnlockBits(data);
            }
            return pixels;
        }

        private static TemplateData Prepare(byte[,] pixels)
        {
            int h = pixels.GetLength(0), w = pixels.GetLength(1);
            double mean = 0;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) mean += pixels[y, x];
            mean /= w * h;
            double variance = 0;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { double d = pixels[y, x] - mean; variance += d * d; }
            if (variance < 1000) return null;
            return new TemplateData { Pixels = pixels, Mean = mean, Deviation = Math.Sqrt(variance) };
        }

        private static double Correlate(byte[,] source, TemplateData template)
        {
            int sh = source.GetLength(0), sw = source.GetLength(1);
            int th = template.Pixels.GetLength(0), tw = template.Pixels.GetLength(1);
            if (tw > sw || th > sh) return -1;
            double best = -1;
            for (int oy = 0; oy <= sh - th; oy++)
                for (int ox = 0; ox <= sw - tw; ox++)
                {
                    double mean = 0;
                    for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++) mean += source[oy + y, ox + x];
                    mean /= tw * th;
                    double numerator = 0, variance = 0;
                    for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++)
                    {
                        double a = template.Pixels[y, x] - template.Mean;
                        double b = source[oy + y, ox + x] - mean;
                        numerator += a * b;
                        variance += b * b;
                    }
                    if (variance > 1e-5) best = Math.Max(best, numerator / (template.Deviation * Math.Sqrt(variance)));
                }
            return best;
        }

        private static double WhitePixelRatio(Bitmap bitmap)
        {
            if (bitmap == null) return 0;
            using (Bitmap sample = new Bitmap(40, 20, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(sample)) g.DrawImage(bitmap, 0, 0, 40, 20);
                int white = 0;
                for (int y = 0; y < sample.Height; y++) for (int x = 0; x < sample.Width; x++)
                {
                    Color c = sample.GetPixel(x, y);
                    int max = Math.Max(c.R, Math.Max(c.G, c.B));
                    int min = Math.Min(c.R, Math.Min(c.G, c.B));
                    if (max > 150 && max - min < 70) white++;
                }
                return white / (double)(sample.Width * sample.Height);
            }
        }

        private static string MakeSignature(List<byte[,]> targets)
        {
            ulong signature = 0;
            int bit = 0;
            foreach (byte[,] part in targets)
            {
                int height = part.GetLength(0), width = part.GetLength(1);
                double mean = 0;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++) mean += part[y, x];
                mean /= width * height;

                for (int gy = 0; gy < 2; gy++)
                    for (int gx = 0; gx < 4; gx++)
                    {
                        int x0 = gx * width / 4, x1 = (gx + 1) * width / 4;
                        int y0 = gy * height / 2, y1 = (gy + 1) * height / 2;
                        double cell = 0;
                        int count = 0;
                        for (int y = y0; y < y1; y++)
                            for (int x = x0; x < x1; x++) { cell += part[y, x]; count++; }
                        if (count > 0 && cell / count >= mean) signature |= 1UL << bit;
                        bit++;
                    }
            }
            return signature.ToString("X16");
        }
    }

    public static class CayoSimulatorBridge
    {
        private static readonly string StatePath = Path.Combine(Path.GetTempPath(), "lsf-cayo-simulator.state");

        public static bool TryRead(out CayoFingerprintResult result)
        {
            result = null;
            try
            {
                if (!File.Exists(StatePath)) return false;
                bool simRunning = false;
                try { simRunning = Process.GetProcessesByName("CayoFingerprintSimulator").Length > 0; } catch { }
                if (!simRunning)
                {
                    DateTime lastWrite = File.GetLastWriteTime(StatePath);
                    if (Math.Abs((DateTime.Now - lastWrite).TotalSeconds) > 10) return false;
                }

                string[] fields = File.ReadAllText(StatePath).Trim().Split('|');
                if (fields.Length != 3) return false;
                int cursor;
                if (!Int32.TryParse(fields[1], out cursor) || cursor < 1 || cursor > 8) return false;
                string[] values = fields[2].Split(',');
                if (values.Length != 8) return false;
                int[] clicks = new int[8];
                for (int row = 0; row < 8; row++)
                {
                    int current;
                    if (!Int32.TryParse(values[row], out current) || current < 0 || current > 7) return false;
                    int offset = (row - current + 8) % 8;
                    clicks[row] = offset == 0 ? 0 : offset <= 4 ? offset : -(8 - offset);
                }
                result = new CayoFingerprintResult
                {
                    Clicks = clicks,
                    CursorRow = cursor,
                    Confidence = 1.0,
                    TargetSignature = "SIM-" + fields[0]
                };
                return true;
            }
            catch { return false; }
        }
    }

    public class MainForm : Form
    {
        private const string AppProductName = "名钻指纹助手";
        public static readonly string AppProductVersion = typeof(MainForm).Assembly.GetName().Version.ToString(4);
        private const string WebsiteUrl = "https://gtacn.org/downloads/fingerprint-assistant";
        private static readonly Size CompactWindowSize = new Size(390, 324);
        private static readonly Size ExpandedWindowSize = new Size(710, 400);
        private const int RequiredStableFrames = 2;
        private const int RequiredKeypadStableFrames = 4;
        private const int RequiredGuardedFrames = 3;
        private const double FastExecutionConfidence = 0.55;
        private const int AllowedMissedFrames = 3;

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT
        {
            [FieldOffset(0)] public uint Type;
            [FieldOffset(8)] public MOUSEINPUT Mouse;
            [FieldOffset(8)] public KEYBDINPUT Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_TOGGLE_AUTO_INPUT = 20;
        private const int HOTKEY_TOGGLE_SPEED_MODE = 22;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        // DirectInput 扫描码 (GTA 5 原生)
        private const byte VK_TAB = 0x09;
        private const byte VK_RETURN = 0x0D;
        private const ushort SCAN_W = 0x11;
        private const ushort SCAN_A = 0x1E;
        private const ushort SCAN_S = 0x1F;
        private const ushort SCAN_D = 0x20;
        private const ushort SCAN_ENTER = 0x1C;
        private const ushort SCAN_TAB = 0x0F;

        private const int SafeKeyHoldMs = 45;
        private const int SafeMoveGapMs = 60;
        private const int SafeConfirmGapMs = 80;
        private const int SafeSubmitPauseMs = 105;
        private const int TurboKeyHoldMs = 28;
        private const int TurboMoveGapMs = 38;
        private const int TurboConfirmGapMs = 55;
        private const int TurboSubmitPauseMs = 85;

        private FingerprintDatabase _db = new FingerprintDatabase();
        private FingerprintTarget _current;
        private List<int> _activeSlots = new List<int> { 1, 2, 4, 6 };
        private Thread _scanThread;
        private Thread _inputThread;
        private volatile bool _isScanning = true;
        private volatile int _lastScanDurationMs = 0;
        private volatile int _lastFrameWidth = 0;
        private volatile int _lastFrameHeight = 0;
        private volatile int _lastConfidencePercent = 0;

        private volatile bool _autoInputEnabled = false;
        private volatile bool _turboModeEnabled = false;
        private volatile bool _inputInProgress = false;
        private const int ModeCasinoFingerprint = 0;
        private const int ModeCayoFingerprint = 1;
        private const int ModeCayoVoltage = 2;
        private const int ModeCasinoKeypad = 3;
        private volatile int _recognitionMode = ModeCasinoFingerprint;
        private readonly bool _simulatorMode;
        private volatile bool _automationUnlocked;
        private volatile bool _accountOnline;
        private LicenseClient.AccountProfile _accountProfile;
        private string _lastExecutedDetection = "";
        private string _pendingDetection = "";
        private int _pendingDetectionFrames = 0;
        private int _missedDetectionFrames = 0;
        private int _feedbackFailureFrames = 0;
        private volatile bool _hasStableDetection = false;
        private string _solverMode = "none";
        private CayoFingerprintResult _cayoResult;
        private CayoVoltageResult _voltageResult;
        private CasinoKeypadResult _keypadResult;
        private DateTime _keypadCapturedAtUtc = DateTime.MinValue;
        private int _keypadInputStageFrames = 0;
        // After submitting one keypad round, do not reuse its captured pattern.
        // The next pattern screen is the explicit re-arm signal for another round.
        private volatile bool _keypadWaitingForNextPattern = false;
        private volatile bool _keypadSawPatternGap = false;
        private volatile int _casinoCursorSlot = 1;
        private bool _diagnosticsExpanded = false;
        private string _lastUiStateKey = "";

        // UI 控件
        private Panel _pnlTitle;
        private Label _lblTitle;
        private Label _lblScanDot;
        private Button _btnAuto;
        private Button _btnSpeed;
        private Button _btnWebsite;
        private ContextMenuStrip _accountMenu;
        private ContextMenuStrip _toolsMenu;
        private ContextMenuStrip _recognitionMenu;
#if !MICROSOFT_STORE
        private Button _btnUpdate;
        private UpdateClient.UpdateInfo _availableUpdate;
#endif
        private Button _btnDiagnostics;
        private Button _btnRecognitionMode;
        private Button _btnMore;
        private Button _btnMinimize;
        private Button _btnClose;
        private Button[] _btnTabs = new Button[4];
        private Label _lblTargetTitle;
        private Label _lblMnemonic;
        private Label _lblDetection;
        private Panel _pnlDiagnostics;
        private PictureBox[] _slicePics = new PictureBox[4];
        private Label[] _sliceLabels = new Label[4];
        private Panel[] _gridTiles = new Panel[8];
        private Label[] _gridBadges = new Label[8];
        private Label _lblWasd;
        private Label _lblStatus;
        private Label _lblDiagnosticStats;
        private Button _btnOpenLogs;
        private Button _btnFeedback;
        private System.Windows.Forms.Timer _diagnosticsTimer;
        private System.Windows.Forms.Timer _presenceTimer;
        private int _presenceBusy;
        private ToolTip _modeTips;

        // 防止小窗抢占 GTA 5 游戏前台焦点
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00040000; // WS_EX_APPWINDOW: keep a taskbar entry for the HUD
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        public MainForm(bool simulatorMode = false, bool accountAccess = false)
        {
            _simulatorMode = simulatorMode;
            _automationUnlocked = simulatorMode || accountAccess;
            _accountProfile = accountAccess ? LicenseClient.CurrentProfile : null;
            _accountOnline = false;
            _recognitionMode = simulatorMode ? ModeCayoFingerprint : LoadRecognitionMode();
            this.Text = AppProductName + " " + AppProductVersion;
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = CompactWindowSize;
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(9, 14, 23);
            this.TopMost = true;
            this.DoubleBuffered = true;
            this.ShowInTaskbar = true;
            this.Resize += delegate
            {
                if (this.WindowState == FormWindowState.Normal) this.TopMost = true;
            };
            this.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(Color.FromArgb(0, 240, 255), 2))
                {
                    e.Graphics.DrawRectangle(pen, 1, 1, this.Width - 2, this.Height - 2);
                }
            };

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            this.Location = new Point(wa.Right - this.Width - 20, wa.Top + 30);

            string tplDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates");
            _db.Load(tplDir);
            _current = _db.Targets.Count > 0 ? _db.Targets[0] : null;
            _activeSlots = _current != null ? _current.DefaultSlots : new List<int> { 1, 2, 4, 6 };

            InitializeUI();
            _diagnosticsTimer = new System.Windows.Forms.Timer();
            _diagnosticsTimer.Interval = 500;
            _diagnosticsTimer.Tick += delegate { RefreshDiagnosticStats(); };
            _diagnosticsTimer.Start();
            _presenceTimer = new System.Windows.Forms.Timer();
            _presenceTimer.Interval = 8000;
            _presenceTimer.Tick += delegate { QueuePresenceHeartbeat(); };
            _presenceTimer.Start();
            if (_simulatorMode)
            {
                // Launching the local test lab is an explicit automation action.
                // Keep normal GTA sessions opt-in, but make the simulator useful immediately.
                _autoInputEnabled = true;
                UpdateAutoButton();
            }
            RegisterGlobalHotkeys();

            SetWaitingState(GetModeWaitingText(_recognitionMode));
            if (_simulatorMode) SetAutomationStatus("测试器模式 · 自动执行已开启，仅向本地测试场发送按键");
            else if (!_automationUnlocked) SetAutomationStatus("游客模式 · 只显示答案，不会发送按键");
            QueuePresenceHeartbeat();

            _scanThread = new Thread(ScanLoop);
            _scanThread.IsBackground = true;
            _scanThread.Start();
            if (!_simulatorMode && _automationUnlocked)
                this.Shown += delegate { BeginAccountRefresh(); };
            if (!_simulatorMode)
                this.Shown += delegate { RecognitionFeedback.PromptInitialConsent(this); };
#if !MICROSOFT_STORE
            if (!_simulatorMode)
                this.Shown += delegate { UpdateClient.BeginCheck(this, AppProductVersion, ShowAvailableUpdate); };
#endif
        }

        private void ScanLoop()
        {
            while (_isScanning)
            {
                Stopwatch scanTimer = Stopwatch.StartNew();
                try
                {
                    if (_inputInProgress)
                    {
                        scanTimer.Stop();
                        _lastScanDurationMs = (int)scanTimer.ElapsedMilliseconds;
                        Thread.Sleep(80);
                        continue;
                    }

                    if (_simulatorMode)
                    {
                        Rectangle simulatorBounds = Screen.PrimaryScreen.Bounds;
                        _lastFrameWidth = simulatorBounds.Width;
                        _lastFrameHeight = simulatorBounds.Height;
                        using (Bitmap simulatorFrame = new Bitmap(simulatorBounds.Width, simulatorBounds.Height, PixelFormat.Format24bppRgb))
                        {
                            using (Graphics graphics = Graphics.FromImage(simulatorFrame))
                                graphics.CopyFromScreen(simulatorBounds.Left, simulatorBounds.Top, 0, 0, simulatorBounds.Size);
                            CayoFingerprintResult visuallyDetected;
                            if (CayoFingerprintScanner.ScanBitmap(simulatorFrame, out visuallyDetected))
                            {
                                _lastConfidencePercent = (int)Math.Round(visuallyDetected.Confidence * 100);
                                ProcessCayoDetection(visuallyDetected);
                            }
                            else
                            {
                                _pendingDetection = "";
                                _pendingDetectionFrames = 0;
                                _hasStableDetection = false;
                                _solverMode = "none";
                                _cayoResult = null;
                                SetWaitingState("测试场画面尚未通过视觉识别 · " + CayoFingerprintScanner.LastDiagnostic);
                            }
                        }
                        scanTimer.Stop();
                        _lastScanDurationMs = (int)scanTimer.ElapsedMilliseconds;
                        Thread.Sleep(50);
                        continue;
                    }

                    Rectangle bounds;
                    if (!TryGetGtaClientBounds(out bounds))
                    {
                        _lastFrameWidth = 0;
                        _lastFrameHeight = 0;
                        _lastConfidencePercent = 0;
                        _pendingDetection = "";
                        _pendingDetectionFrames = 0;
                        _missedDetectionFrames = 0;
                        _feedbackFailureFrames = 0;
                        _hasStableDetection = false;
                        _solverMode = "none";
                        _cayoResult = null;
                        _voltageResult = null;
                        _keypadResult = null;
                        _keypadInputStageFrames = 0;
                        _keypadWaitingForNextPattern = false;
                        _keypadSawPatternGap = false;
                        _lastExecutedDetection = "";
                        SetWaitingState("等待 GTA 5 前台的" + GetModeDisplayName(_recognitionMode) + "画面");
                        scanTimer.Stop();
                        _lastScanDurationMs = (int)scanTimer.ElapsedMilliseconds;
                        Thread.Sleep(180);
                        continue;
                    }
                    _lastFrameWidth = bounds.Width;
                    _lastFrameHeight = bounds.Height;
                    using (Bitmap screenBmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb))
                    {
                        using (Graphics g = Graphics.FromImage(screenBmp))
                        {
                            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
                        }

                        if (_recognitionMode == ModeCayoVoltage)
                        {
                            CayoVoltageResult voltage;
                            if (CayoVoltageScanner.ScanBitmap(screenBmp, out voltage))
                            {
                                _lastConfidencePercent = (int)Math.Round(voltage.Confidence * 100);
                                ProcessVoltageDetection(voltage);
                            }
                            else
                            {
                                ResetMissedDetection("等待佩里科岛信号箱画面");
                            }
                            scanTimer.Stop();
                            _lastScanDurationMs = (int)scanTimer.ElapsedMilliseconds;
                            Thread.Sleep(50);
                            continue;
                        }

                        if (_recognitionMode == ModeCasinoKeypad)
                        {
                            ProcessKeypadFrame(screenBmp);
                            scanTimer.Stop();
                            _lastScanDurationMs = (int)scanTimer.ElapsedMilliseconds;
                            Thread.Sleep(35);
                            continue;
                        }

                        if (_recognitionMode == ModeCayoFingerprint)
                        {
                            CayoFingerprintResult cayo;
                            if (CayoFingerprintScanner.ScanBitmap(screenBmp, out cayo))
                            {
                                _feedbackFailureFrames = 0;
                                _lastConfidencePercent = (int)Math.Round(cayo.Confidence * 100);
                                ProcessCayoDetection(cayo);
                                scanTimer.Stop();
                                _lastScanDurationMs = (int)scanTimer.ElapsedMilliseconds;
                                Thread.Sleep(50);
                                continue;
                            }

                            _pendingDetection = "";
                            _pendingDetectionFrames = 0;
                            _missedDetectionFrames++;
                            if (_missedDetectionFrames >= AllowedMissedFrames)
                            {
                                _lastConfidencePercent = 0;
                                _hasStableDetection = false;
                                _solverMode = "none";
                                _cayoResult = null;
                                _lastExecutedDetection = "";
                                SetWaitingState("等待佩里科岛指纹画面");
                            }
                            _feedbackFailureFrames++;
                            if (_feedbackFailureFrames >= 12 && RecognitionFeedback.AutoUploadEnabled)
                            {
                                _feedbackFailureFrames = 0;
                                if (CayoFingerprintScanner.DetectCursorRow(screenBmp) > 0)
                                    RecognitionFeedback.TryQueue("cayo", screenBmp, CayoFingerprintScanner.GetFeedbackRectangles(screenBmp),
                                        CayoFingerprintScanner.LastDiagnostic, AppProductVersion);
                            }
                            scanTimer.Stop();
                            _lastScanDurationMs = (int)scanTimer.ElapsedMilliseconds;
                            Thread.Sleep(50);
                            continue;
                        }

                        FingerprintTarget matched;
                        List<int> slots;
                        double conf;

                        if (ReliableAutoScanner.ScanBitmap(_db, screenBmp, out matched, out slots, out conf))
                        {
                            _feedbackFailureFrames = 0;
                            _lastConfidencePercent = (int)Math.Round(conf * 100);
                            if (matched != null && slots != null && slots.Count == 4)
                            {
                                int detectedCursor = ReliableAutoScanner.DetectCursorSlot(screenBmp);
                                if (detectedCursor > 0) _casinoCursorSlot = detectedCursor;
                                string key = matched.Id + ":" + string.Join(",", slots.ConvertAll(v => v.ToString()).ToArray());

                                _missedDetectionFrames = 0;
                                if (key == _pendingDetection) _pendingDetectionFrames++;
                                else
                                {
                                    _pendingDetection = key;
                                    _pendingDetectionFrames = 1;
                                }

                                if (_pendingDetectionFrames >= RequiredStableFrames)
                                {
                                    bool changed = !_hasStableDetection || (_current == null) || (_current.Id != matched.Id) || !ListEqual(_activeSlots, slots);
                                    _hasStableDetection = true;
                                    _solverMode = "casino";
                                    _cayoResult = null;
                                    if (changed) SetTarget(matched, slots, true);
                                    SetStableDetectionState(matched, slots, conf);

                                    int framesNeededForInput = conf >= FastExecutionConfidence ? RequiredStableFrames : RequiredGuardedFrames;
                                    if (_autoInputEnabled && !_inputInProgress && key != _lastExecutedDetection && _pendingDetectionFrames >= framesNeededForInput)
                                    {
                                        _lastExecutedDetection = key;
                                        _inputInProgress = true;
                                        List<int> slotCopy = new List<int>(slots);
                                        StartInputWorker(delegate { ExecuteAutomaticInput(matched, slotCopy); });
                                    }
                                }
                                else SetPendingDetectionState(conf);
                            }
                        }
                        else
                        {
                            _pendingDetection = "";
                            _pendingDetectionFrames = 0;
                            _missedDetectionFrames++;
                            if (_missedDetectionFrames >= AllowedMissedFrames)
                            {
                                _lastConfidencePercent = 0;
                                _hasStableDetection = false;
                                _solverMode = "none";
                                _cayoResult = null;
                                _lastExecutedDetection = "";
                                SetWaitingState("等待名钻赌场指纹画面");
                            }
                            _feedbackFailureFrames++;
                            if (_feedbackFailureFrames >= 12 && RecognitionFeedback.AutoUploadEnabled)
                            {
                                _feedbackFailureFrames = 0;
                                if (ReliableAutoScanner.DetectCursorSlot(screenBmp) > 0)
                                    RecognitionFeedback.TryQueue("casino", screenBmp, ReliableAutoScanner.GetSlotRectangles(screenBmp),
                                        "cursor-visible-no-match", AppProductVersion);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.WriteThrottled("ScanLoop", ex);
                }
                finally
                {
                    if (scanTimer.IsRunning)
                        _lastScanDurationMs = (int)Math.Min(Int32.MaxValue, scanTimer.ElapsedMilliseconds);
                }

                Thread.Sleep(50);
            }
        }

        private void ProcessCayoDetection(CayoFingerprintResult detected)
        {
            string key = detected.DetectionKey;
            _missedDetectionFrames = 0;
            _feedbackFailureFrames = 0;
            if (key == _pendingDetection) _pendingDetectionFrames++;
            else { _pendingDetection = key; _pendingDetectionFrames = 1; }

            if (_pendingDetectionFrames < RequiredStableFrames)
            {
                SetPendingDetectionState(detected.Confidence);
                return;
            }

            bool changed = !_hasStableDetection || _solverMode != "cayo" || _cayoResult == null || _cayoResult.DetectionKey != key;
            _hasStableDetection = true;
            _solverMode = "cayo";
            _cayoResult = CloneCayoResult(detected);
            if (changed) SetCayoTarget(detected);
            SetStableCayoState(detected);

            bool allSolved = true;
            for (int i = 0; i < 8; i++) { if (detected.Clicks[i] != 0) { allSolved = false; break; } }
            if (allSolved)
            {
                SetAutomationStatus("✓ 佩里科 8 行已全部对齐匹配！");
                return;
            }

            if (_autoInputEnabled && !_inputInProgress && key != _lastExecutedDetection)
            {
                _lastExecutedDetection = key;
                _inputInProgress = true;
                CayoFingerprintResult copy = CloneCayoResult(detected);
                StartInputWorker(delegate { ExecuteCayoInput(copy); });
            }
        }

        private void ResetMissedDetection(string waitingText)
        {
            _pendingDetection = "";
            _pendingDetectionFrames = 0;
            _missedDetectionFrames++;
            if (_missedDetectionFrames < AllowedMissedFrames) return;
            _lastConfidencePercent = 0;
            _hasStableDetection = false;
            _solverMode = "none";
            _voltageResult = null;
            _lastExecutedDetection = "";
            SetWaitingState(waitingText);
        }

        private void ProcessVoltageDetection(CayoVoltageResult detected)
        {
            string key = detected.DetectionKey;
            _missedDetectionFrames = 0;
            if (key == _pendingDetection) _pendingDetectionFrames++;
            else { _pendingDetection = key; _pendingDetectionFrames = 1; }
            if (_pendingDetectionFrames < RequiredStableFrames)
            {
                SetPendingDetectionState(detected.Confidence);
                return;
            }

            bool changed = !_hasStableDetection || _solverMode != "voltage" || _voltageResult == null || _voltageResult.DetectionKey != key;
            _hasStableDetection = true;
            _solverMode = "voltage";
            _voltageResult = detected;
            _cayoResult = null;
            if (changed) SetVoltageTarget(detected);
            SetStableVoltageState(detected);
            if (_autoInputEnabled && !_inputInProgress && key != _lastExecutedDetection)
            {
                _lastExecutedDetection = key;
                _inputInProgress = true;
                StartInputWorker(delegate { ExecuteVoltageInput(detected); });
            }
        }

        private void ProcessKeypadFrame(Bitmap screenBmp)
        {
            CasinoKeypadResult detected;
            bool patternVisible = CasinoKeypadScanner.ScanPattern(screenBmp, out detected);
            if (_keypadWaitingForNextPattern)
            {
                if (!patternVisible)
                {
                    _keypadSawPatternGap = true;
                    SetWaitingState("本轮点阵已结束 · 自动保持开启，等待下一关图案");
                    return;
                }

                // Do not treat a lingering copy of the old pattern as a new
                // round. A real round boundary must contain at least one frame
                // where the six-column pattern is absent.
                if (!_keypadSawPatternGap) return;
                _keypadWaitingForNextPattern = false;
                _keypadSawPatternGap = false;
                _keypadResult = null;
                _lastExecutedDetection = "";
                _pendingDetection = "";
                _pendingDetectionFrames = 0;
                _hasStableDetection = false;
            }

            if (patternVisible)
            {
                string key = detected.DetectionKey;
                _missedDetectionFrames = 0;
                _keypadInputStageFrames = 0;
                if (key == _pendingDetection) _pendingDetectionFrames++;
                else { _pendingDetection = key; _pendingDetectionFrames = 1; }
                _lastConfidencePercent = (int)Math.Round(detected.Confidence * 100);
                if (_pendingDetectionFrames < RequiredKeypadStableFrames)
                {
                    SetPendingDetectionState(detected.Confidence);
                    return;
                }
                _keypadResult = detected;
                _keypadCapturedAtUtc = DateTime.UtcNow;
                _hasStableDetection = true;
                _solverMode = "keypad";
                SetKeypadTarget(detected, false);
                SetStableKeypadState(detected, false);
                return;
            }

            if (_keypadResult != null && DateTime.UtcNow - _keypadCapturedAtUtc > TimeSpan.FromSeconds(15))
            {
                _keypadResult = null;
                _keypadInputStageFrames = 0;
                _lastExecutedDetection = "";
                SetWaitingState("点阵图案已过期 · 请重新开始并等待闪烁画面");
                return;
            }

            if (_keypadResult != null && CasinoKeypadScanner.IsInputStage(screenBmp))
            {
                _keypadInputStageFrames++;
                SetStableKeypadState(_keypadResult, true);
                if (_autoInputEnabled && !_inputInProgress && _keypadInputStageFrames >= RequiredStableFrames
                    && _lastExecutedDetection != _keypadResult.DetectionKey)
                {
                    _lastExecutedDetection = _keypadResult.DetectionKey;
                    _inputInProgress = true;
                    CasinoKeypadResult copy = new CasinoKeypadResult
                    {
                        Rows = (int[])_keypadResult.Rows.Clone(),
                        Confidence = _keypadResult.Confidence,
                        MinimumColumnConfidence = _keypadResult.MinimumColumnConfidence
                    };
                    StartInputWorker(delegate { ExecuteKeypadInput(copy); });
                }
                return;
            }

            _keypadInputStageFrames = 0;
            _missedDetectionFrames++;
            if (_missedDetectionFrames >= AllowedMissedFrames && _keypadResult == null)
            {
                _pendingDetection = "";
                _pendingDetectionFrames = 0;
                _lastConfidencePercent = 0;
                _hasStableDetection = false;
                _solverMode = "none";
                SetWaitingState("等待赌场点阵闪烁画面 · 必须在图案显示时捕获");
            }
        }

        private void StartInputWorker(ThreadStart action)
        {
            if (!_isScanning) return;
            Thread worker = new Thread(action);
            worker.IsBackground = true;
            _inputThread = worker;
            worker.Start();
        }

        private static CayoFingerprintResult CloneCayoResult(CayoFingerprintResult source)
        {
            return new CayoFingerprintResult
            {
                Clicks = (int[])source.Clicks.Clone(),
                CursorRow = source.CursorRow,
                Confidence = source.Confidence,
                TargetSignature = source.TargetSignature
            };
        }

        private bool IsGtaForeground()
        {
            Rectangle bounds;
            return _simulatorMode ? GetSimulatorWindow() != IntPtr.Zero : TryGetGtaClientBounds(out bounds);
        }

        private bool TryGetGtaClientBounds(out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            try
            {
                IntPtr window = GetForegroundWindow();
                uint pid;
                GetWindowThreadProcessId(window, out pid);
                if (pid == 0) return false;
                string process = Process.GetProcessById((int)pid).ProcessName;
                bool isGta = process.Equals("GTA5", StringComparison.OrdinalIgnoreCase)
                    || process.Equals("GTA5_Enhanced", StringComparison.OrdinalIgnoreCase)
                    || process.StartsWith("PlayGTAV", StringComparison.OrdinalIgnoreCase);
                if (!isGta) return false;

                RECT client;
                POINT origin = new POINT { X = 0, Y = 0 };
                if (!GetClientRect(window, out client) || !ClientToScreen(window, ref origin)) return false;
                int width = client.Right - client.Left;
                int height = client.Bottom - client.Top;
                if (width < 800 || height < 500) return false;
                bounds = new Rectangle(origin.X, origin.Y, width, height);
                return true;
            }
            catch { return false; }
        }

        private Bitmap CaptureCurrentSurface()
        {
            Rectangle bounds;
            if (_simulatorMode) bounds = Screen.PrimaryScreen.Bounds;
            else if (!TryGetGtaClientBounds(out bounds)) return null;
            Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bitmap)) g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            return bitmap;
        }

        private bool VerifyCayoFrame(CayoFingerprintResult expected, int expectedCursorRow)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                Thread.Sleep(attempt == 0 ? 40 : 25);
                if (!IsGtaForeground()) return false;

                CayoFingerprintResult simulated;
                if (_simulatorMode && CayoSimulatorBridge.TryRead(out simulated)
                    && CayoFingerprintScanner.SameTargetSignature(simulated.TargetSignature, expected.TargetSignature)
                    && simulated.CursorRow == expectedCursorRow) return true;

                using (Bitmap frame = CaptureCurrentSurface())
                {
                    if (frame == null) return false;
                    CayoFingerprintResult observed;
                    if (CayoFingerprintScanner.ScanBitmap(frame, out observed)
                        && CayoFingerprintScanner.SameTargetSignature(observed.TargetSignature, expected.TargetSignature)
                        && observed.CursorRow == expectedCursorRow) return true;
                }
            }
            return false;
        }

        private bool VerifyCayoProgress(CayoFingerprintResult expected, int completedRow, int expectedCursorRow)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Thread.Sleep(attempt == 0 ? 55 : 35);
                if (!IsGtaForeground()) return false;

                CayoFingerprintResult observed;
                if (_simulatorMode)
                {
                    if (!CayoSimulatorBridge.TryRead(out observed)) continue;
                    if (observed.CursorRow != expectedCursorRow) continue;
                    bool simulatedAligned = true;
                    for (int row = 0; row <= completedRow; row++)
                    {
                        if (observed.Clicks[row] != 0) { simulatedAligned = false; break; }
                    }
                    if (simulatedAligned) return true;
                    continue;
                }
                else
                {
                    using (Bitmap frame = CaptureCurrentSurface())
                    {
                        if (frame == null || !CayoFingerprintScanner.ScanBitmap(frame, out observed)) continue;
                    }
                }

                if (!CayoFingerprintScanner.SameTargetSignature(observed.TargetSignature, expected.TargetSignature)
                    || observed.CursorRow != expectedCursorRow) continue;
                bool aligned = true;
                for (int row = 0; row <= completedRow; row++)
                {
                    if (observed.Clicks[row] != 0) { aligned = false; break; }
                }
                if (aligned) return true;
            }
            return false;
        }

        private void ExecuteCayoInput(CayoFingerprintResult result)
        {
            try
            {
                if (!IsGtaForeground()) throw new InvalidOperationException("GTA 5 当前不在前台");
                SetAutomationStatus("⚡ 佩里科指纹：正在闭环校准 8 行...");

                // 1. Fast home: use the observed cursor once, then verify row 1.
                int curCursor = GetCurrentCayoCursorRow();
                if (curCursor < 1 || curCursor > 8) curCursor = result.CursorRow;
                for (int move = curCursor; move > 1; move--)
                {
                    TapSingleKey(0x57, SCAN_W);
                    Thread.Sleep(GetCayoGapMs());
                }
                if (!VerifyCayoFrame(result, 1)) throw new InvalidOperationException("无法确认光标已回到第 1 行");

                // 2. Execute deterministically and verify at the middle and end.
                for (int row = 0; row < 8; row++)
                {
                    if (!IsGtaForeground()) throw new InvalidOperationException("GTA 5 已失去焦点");

                    int offset = result.Clicks[row];
                    if (offset != 0)
                    {
                        byte vk = offset > 0 ? (byte)0x44 : (byte)0x41;
                        ushort scan = offset > 0 ? SCAN_D : SCAN_A;
                        for (int n = 0; n < Math.Abs(offset); n++)
                        {
                            TapSingleKey(vk, scan);
                            Thread.Sleep(GetCayoGapMs());
                        }
                    }

                    if ((row == 3 || row == 7) && !VerifyCayoProgress(result, row, row + 1))
                        throw new InvalidOperationException("第 " + (row + 1) + " 行闭环复核失败");

                    if (row < 7)
                    {
                        TapSingleKey(0x53, SCAN_S);
                        Thread.Sleep(GetCayoGapMs());
                    }
                }

                // Cayo Perico validates automatically. Never send Enter or Tab.
                SetAutomationStatus("✓ 佩里科 8 行已确认对齐 · 等待游戏自动通过");
                Thread.Sleep(450);
            }
            catch (Exception ex)
            {
                if (_isScanning)
                {
                    AppLog.Write("CayoInput", ex);
                    SetAutomationStatus("⚠ 佩里科自动执行已停止：" + ex.Message);
                }
            }
            finally { _inputInProgress = false; }
        }

        private void ExecuteVoltageInput(CayoVoltageResult result)
        {
            try
            {
                if (_recognitionMode != ModeCayoVoltage || !IsGtaForeground())
                    throw new InvalidOperationException("GTA 5 当前不在前台或模式已切换");
                using (Bitmap frame = CaptureCurrentSurface())
                {
                    CayoVoltageResult verified;
                    if (frame == null || !CayoVoltageScanner.ScanBitmap(frame, out verified) || verified.DetectionKey != result.DetectionKey)
                        throw new InvalidOperationException("执行前信号箱题目发生变化");
                }

                SetAutomationStatus("⚡ 信号箱：正在连接 " + FormatVoltageAssignment(result));
                int[][] moves = new int[][]
                {
                    new int[] { 0, 0, 0 }, new int[] { 0, 1, 0 }, new int[] { 1, -1, 0 },
                    new int[] { 1, 0, 0 }, new int[] { -1, -1, 0 }, new int[] { -1, 0, 0 }
                };
                int permutationIndex = VoltagePermutationIndex(result.Assignment);
                if (permutationIndex < 0) throw new InvalidOperationException("信号箱线路组合无效");
                for (int leftIndex = 0; leftIndex < 3; leftIndex++)
                {
                    if (!IsGtaForeground() || _recognitionMode != ModeCayoVoltage)
                        throw new InvalidOperationException("执行中 GTA 5 失去前台或模式发生变化");
                    TapDirectKey(VK_RETURN, SCAN_ENTER);
                    int vertical = moves[permutationIndex][leftIndex];
                    if (vertical != 0)
                    {
                        TapDirectKey(vertical > 0 ? (byte)0x53 : (byte)0x57, vertical > 0 ? SCAN_S : SCAN_W);
                        Thread.Sleep(GetMoveGapMs());
                    }
                    TapDirectKey(VK_RETURN, SCAN_ENTER);
                    Thread.Sleep(_turboModeEnabled ? 900 : 1350);
                }
                SetAutomationStatus("✓ 信号箱线路已完成 · 等待游戏确认");
            }
            catch (Exception ex)
            {
                AppLog.Write("VoltageInput", ex);
                DisableAutoAfterSafetyStop();
                SetAutomationStatus("⚠ 信号箱本轮已停止，F8 仍保持开启：" + ex.Message);
            }
            finally { _inputInProgress = false; }
        }

        private void ExecuteKeypadInput(CasinoKeypadResult result)
        {
            try
            {
                if (_recognitionMode != ModeCasinoKeypad || !IsGtaForeground())
                    throw new InvalidOperationException("GTA 5 当前不在前台或模式已切换");
                SetAutomationStatus("⚡ 赌场点阵：正在输入 [" + string.Join(",", result.Rows) + "]");
                for (int column = 0; column < result.Rows.Length; column++)
                {
                    if (!IsGtaForeground() || _recognitionMode != ModeCasinoKeypad)
                        throw new InvalidOperationException("执行中 GTA 5 失去前台或模式发生变化");

                    int currentRow;
                    SetAutomationStatus("⚡ 赌场点阵：等待第 " + (column + 1) + " 列校验就绪");
                    if (!WaitForKeypadRing(column + 1, 9000, out currentRow))
                        throw new InvalidOperationException("第 " + (column + 1) + " 列校验超时，未检测到选择光圈（"
                            + CasinoKeypadScanner.LastRingDiagnostic + "）");

                    int targetRow = result.Rows[column];
                    MoveKeypadRing(currentRow, targetRow);
                    TapDirectKey(VK_RETURN, SCAN_ENTER);
                    // Do not use a fixed delay here. The next loop waits until
                    // GTA has finished validating and exposes the next column's
                    // ring, so turbo mode cannot outrun the game state.
                    Thread.Sleep(80);
                }
                SetAutomationStatus("✓ 赌场点阵序列已输入 · 等待游戏确认");
                _keypadWaitingForNextPattern = true;
                _keypadSawPatternGap = false;
            }
            catch (Exception ex)
            {
                AppLog.Write("KeypadInput", ex);
                _keypadResult = null;
                _lastExecutedDetection = "";
                _pendingDetection = "";
                _pendingDetectionFrames = 0;
                _keypadInputStageFrames = 0;
                _keypadWaitingForNextPattern = true;
                _keypadSawPatternGap = false;
                SetAutomationStatus("⚠ 本轮点阵已放弃，自动仍保持开启：" + ex.Message);
            }
            finally { _inputInProgress = false; }
        }

        private bool WaitForKeypadRing(int column, int timeoutMs, out int row)
        {
            row = 0;
            Stopwatch timer = Stopwatch.StartNew();
            int stableRow = 0, stableFrames = 0;
            while (_isScanning && timer.ElapsedMilliseconds < timeoutMs)
            {
                if (!IsGtaForeground() || _recognitionMode != ModeCasinoKeypad) return false;
                using (Bitmap frame = CaptureCurrentSurface())
                {
                    int detectedRow;
                    if (frame != null && CasinoKeypadScanner.TryDetectRingRow(frame, column, out detectedRow))
                    {
                        if (detectedRow == stableRow) stableFrames++;
                        else { stableRow = detectedRow; stableFrames = 1; }
                        if (stableFrames >= 2) { row = stableRow; return true; }
                    }
                    else
                    {
                        stableRow = 0;
                        stableFrames = 0;
                    }
                }
                Thread.Sleep(35);
            }
            return false;
        }

        private void MoveKeypadRing(int fromRow, int toRow)
        {
            const int rowCount = 5;
            int upSteps = (fromRow - toRow + rowCount) % rowCount;
            int downSteps = (toRow - fromRow + rowCount) % rowCount;
            byte key = upSteps <= downSteps ? (byte)0x57 : (byte)0x53;
            ushort scanCode = upSteps <= downSteps ? SCAN_W : SCAN_S;
            int steps = Math.Min(upSteps, downSteps);
            for (int i = 0; i < steps; i++)
            {
                TapDirectKey(key, scanCode);
                Thread.Sleep(GetMoveGapMs());
            }
        }

        private static int VoltagePermutationIndex(int[] assignment)
        {
            int[][] permutations = new int[][]
            {
                new int[] { 0, 1, 2 }, new int[] { 0, 2, 1 }, new int[] { 1, 0, 2 },
                new int[] { 1, 2, 0 }, new int[] { 2, 0, 1 }, new int[] { 2, 1, 0 }
            };
            if (assignment == null || assignment.Length != 3) return -1;
            for (int p = 0; p < permutations.Length; p++)
                if (assignment[0] == permutations[p][0] && assignment[1] == permutations[p][1] && assignment[2] == permutations[p][2]) return p;
            return -1;
        }

        private static string FormatVoltageAssignment(CayoVoltageResult result)
        {
            string[] parts = new string[3];
            for (int i = 0; i < 3; i++) parts[i] = result.LeftNumbers[i] + "×" + result.RightMultipliers[result.Assignment[i]];
            return string.Join(" + ", parts) + " = " + result.Target;
        }

        private int GetCurrentCayoCursorRow()
        {
            CayoFingerprintResult sim;
            if (_simulatorMode && CayoSimulatorBridge.TryRead(out sim)) return sim.CursorRow;

            using (Bitmap frame = CaptureCurrentSurface())
            {
                if (frame == null) return 0;
                return CayoFingerprintScanner.DetectCursorRow(frame);
            }
        }

        private bool IsSimulatorForeground()
        {
            try
            {
                IntPtr window = GetForegroundWindow();
                uint pid;
                GetWindowThreadProcessId(window, out pid);
                if (pid == 0) return false;
                string process = Process.GetProcessById((int)pid).ProcessName;
                return process.Equals("CayoFingerprintSimulator", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private int GetCayoHoldMs() { return _turboModeEnabled ? 25 : 40; }
        private int GetCayoGapMs() { return _turboModeEnabled ? 25 : 45; }

        private static IntPtr GetSimulatorWindow()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("CayoFingerprintSimulator");
                for (int index = 0; index < processes.Length; index++)
                {
                    IntPtr handle = processes[index].MainWindowHandle;
                    if (handle != IntPtr.Zero) return handle;
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        private void TapSingleKey(byte vk, ushort scanCode)
        {
            if (!_isScanning) throw new OperationCanceledException("程序正在退出");
            if (_simulatorMode)
            {
                IntPtr simulator = GetSimulatorWindow();
                if (simulator == IntPtr.Zero) throw new InvalidOperationException("佩里科本地测试场未运行");
                int keyDownParam = 1 | (scanCode << 16);
                int keyUpParam = keyDownParam | unchecked((int)0xC0000000);
                SendMessage(simulator, 0x0100, vk, keyDownParam);
                Thread.Sleep(GetCayoHoldMs());
                SendMessage(simulator, 0x0101, vk, keyUpParam);
                return;
            }

            INPUT[] down = new INPUT[1];
            down[0].Type = INPUT_KEYBOARD;
            down[0].Keyboard.VirtualKey = vk;
            down[0].Keyboard.ScanCode = scanCode;
            down[0].Keyboard.Flags = KEYEVENTF_SCANCODE;
            if (SendInput(1, down, Marshal.SizeOf(typeof(INPUT))) == 0)
            {
                keybd_event(vk, (byte)scanCode, KEYEVENTF_SCANCODE, UIntPtr.Zero);
            }

            Thread.Sleep(GetCayoHoldMs());

            INPUT[] up = new INPUT[1];
            up[0].Type = INPUT_KEYBOARD;
            up[0].Keyboard.VirtualKey = vk;
            up[0].Keyboard.ScanCode = scanCode;
            up[0].Keyboard.Flags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP;
            if (SendInput(1, up, Marshal.SizeOf(typeof(INPUT))) == 0)
            {
                keybd_event(vk, (byte)scanCode, KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
        }

        private void ExecuteAutomaticInput(FingerprintTarget target, List<int> slots)
        {
            try
            {
                SetAutomationStatus("执行前复核画面与光标...");
                int cursorSlot;
                if (!VerifyCasinoPreflight(target, slots, out cursorSlot))
                    throw new InvalidOperationException("画面在执行前发生变化，已阻止按键");

                List<int> route = BuildShortestSlotOrder(slots, cursorSlot);
                int currentSlot = cursorSlot;
                SetAutomationStatus(string.Format("⚡ 赌场指纹执行中：光标 {0} → [{1}]", cursorSlot, string.Join(", ", route.ConvertAll(v => v.ToString()).ToArray())));

                foreach (int s in route)
                {
                    if (!IsGtaForeground())
                        throw new InvalidOperationException("GTA 5 已失去前台焦点");

                    int curR = (currentSlot - 1) / 2;
                    int curC = (currentSlot - 1) % 2;
                    int tr = (s - 1) / 2;
                    int tc = (s - 1) % 2;

                    // 竖向移动 (W / S)
                    if (tr > curR)
                    {
                        for (int k = 0; k < tr - curR; k++)
                        {
                            TapDirectKey(0x53, SCAN_S);
                            Thread.Sleep(GetMoveGapMs());
                        }
                    }
                    else if (tr < curR)
                    {
                        for (int k = 0; k < curR - tr; k++)
                        {
                            TapDirectKey(0x57, SCAN_W);
                            Thread.Sleep(GetMoveGapMs());
                        }
                    }

                    // 横向移动 (A / D)
                    if (tc > curC)
                    {
                        for (int k = 0; k < tc - curC; k++)
                        {
                            TapDirectKey(0x44, SCAN_D);
                            Thread.Sleep(GetMoveGapMs());
                        }
                    }
                    else if (tc < curC)
                    {
                        for (int k = 0; k < curC - tc; k++)
                        {
                            TapDirectKey(0x41, SCAN_A);
                            Thread.Sleep(GetMoveGapMs());
                        }
                    }

                    currentSlot = s;

                    // 选中切片 (回车键)
                    TapDirectKey(VK_RETURN, SCAN_ENTER);
                    Thread.Sleep(GetConfirmGapMs());
                }

                // 光标检测是可选安全校验：能看清时必须一致，
                // 受特效影响时不因“未知”阻塞已经复核过的执行。
                Thread.Sleep(GetSubmitPauseMs());
                int verifiedCursor = DetectCurrentCasinoCursor();
                if (verifiedCursor > 0 && verifiedCursor != currentSlot)
                    throw new InvalidOperationException(string.Format("光标偏移：预期 {0}，实际 {1}", currentSlot, verifiedCursor));
                if (!IsGtaForeground())
                    throw new InvalidOperationException("提交前 GTA 5 已失去前台焦点");

                // 只有名钻赌场指纹需要 Tab 检验。
                TapDirectKey(VK_TAB, SCAN_TAB);

                int submission = VerifyCasinoSubmission();
                if (submission < 0)
                {
                    DisableAutoAfterSafetyStop();
                    SetAutomationStatus("⚠ 游戏返回 SIGNAL ERROR，本轮已停止，F8 仍保持开启");
                }
                else if (submission > 0)
                {
                    SetAutomationStatus("✓ 赌场指纹已通过");
                }
                else
                {
                    SetAutomationStatus("已发送 Tab · 未能确认游戏结果");
                }
            }
            catch (Exception ex)
            {
                if (_isScanning)
                {
                    AppLog.Write("CasinoInput", ex);
                    DisableAutoAfterSafetyStop();
                    SetAutomationStatus("⚠ 本轮按键已停止，F8 仍保持开启：" + ex.Message);
                }
            }
            finally
            {
                _inputInProgress = false;
            }
        }

        private void TapDirectKey(byte vk, ushort scanCode)
        {
            if (!_isScanning) throw new OperationCanceledException("程序正在退出");
            // SendInput 成功时不能再同步发送 keybd_event，
            // 否则游戏会收到两次按键，导致偶发跳格。
            INPUT[] down = new INPUT[1];
            down[0].Type = INPUT_KEYBOARD;
            down[0].Keyboard.VirtualKey = vk;
            down[0].Keyboard.ScanCode = scanCode;
            down[0].Keyboard.Flags = KEYEVENTF_SCANCODE;
            if (SendInput(1, down, Marshal.SizeOf(typeof(INPUT))) == 0)
                keybd_event(vk, (byte)scanCode, KEYEVENTF_SCANCODE, UIntPtr.Zero);

            Thread.Sleep(GetKeyHoldMs());

            INPUT[] up = new INPUT[1];
            up[0].Type = INPUT_KEYBOARD;
            up[0].Keyboard.VirtualKey = vk;
            up[0].Keyboard.ScanCode = scanCode;
            up[0].Keyboard.Flags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP;
            if (SendInput(1, up, Marshal.SizeOf(typeof(INPUT))) == 0)
                keybd_event(vk, (byte)scanCode, KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private bool VerifyCasinoPreflight(FingerprintTarget expectedTarget, List<int> expectedSlots, out int cursorSlot)
        {
            cursorSlot = _casinoCursorSlot > 0 ? _casinoCursorSlot : 1;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!IsGtaForeground()) return false;
                using (Bitmap frame = CaptureCurrentSurface())
                {
                    if (frame != null)
                    {
                        FingerprintTarget foundTarget;
                        List<int> foundSlots;
                        double confidence;
                        if (ReliableAutoScanner.ScanBitmap(_db, frame, out foundTarget, out foundSlots, out confidence)
                            && foundTarget != null && foundTarget.Id == expectedTarget.Id
                            && SameSlotSet(foundSlots, expectedSlots))
                        {
                            int detected = ReliableAutoScanner.DetectCursorSlot(frame);
                            if (detected > 0) cursorSlot = detected;
                            _casinoCursorSlot = cursorSlot;
                            return true;
                        }
                    }
                }
                Thread.Sleep(45);
            }
            return false;
        }

        private int DetectCurrentCasinoCursor()
        {
            using (Bitmap frame = CaptureCurrentSurface())
            {
                return frame == null ? 0 : ReliableAutoScanner.DetectCursorSlot(frame);
            }
        }

        // 1 = 已通过，0 = 无法确认，-1 = SIGNAL ERROR。
        private int VerifyCasinoSubmission()
        {
            int missingFrames = 0;
            for (int attempt = 0; attempt < 7; attempt++)
            {
                Thread.Sleep(70);
                using (Bitmap frame = CaptureCurrentSurface())
                {
                    if (frame == null) return 0;
                    if (ReliableAutoScanner.DetectSignalError(frame)) return -1;

                    FingerprintTarget target;
                    List<int> slots;
                    double confidence;
                    bool stillVisible = ReliableAutoScanner.ScanBitmap(_db, frame, out target, out slots, out confidence);
                    if (!stillVisible)
                    {
                        missingFrames++;
                        if (missingFrames >= 2) return 1;
                    }
                    else missingFrames = 0;
                }
            }
            return 0;
        }

        private void DisableAutoAfterSafetyStop()
        {
            // A transient focus or validation fault should stop only this round.
            // The detection key prevents an immediate retry of the same puzzle.
            _inputInProgress = false;
            UpdateAutoButton();
        }

        private static bool SameSlotSet(List<int> first, List<int> second)
        {
            if (first == null || second == null || first.Count != second.Count) return false;
            List<int> a = new List<int>(first);
            List<int> b = new List<int>(second);
            a.Sort();
            b.Sort();
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static List<int> BuildShortestSlotOrder(List<int> slots, int startSlot)
        {
            List<int> source = new List<int>(slots);
            List<int> best = null;
            int bestCost = Int32.MaxValue;
            BuildSlotPermutations(source, new List<int>(), startSlot, 0, ref bestCost, ref best);
            return best ?? source;
        }

        private static void BuildSlotPermutations(List<int> remaining, List<int> route, int currentSlot, int cost, ref int bestCost, ref List<int> best)
        {
            if (remaining.Count == 0)
            {
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = new List<int>(route);
                }
                return;
            }

            for (int i = 0; i < remaining.Count; i++)
            {
                int next = remaining[i];
                int nextCost = cost + SlotDistance(currentSlot, next);
                if (nextCost >= bestCost) continue;
                List<int> rest = new List<int>(remaining);
                rest.RemoveAt(i);
                route.Add(next);
                BuildSlotPermutations(rest, route, next, nextCost, ref bestCost, ref best);
                route.RemoveAt(route.Count - 1);
            }
        }

        private static int SlotDistance(int first, int second)
        {
            int firstRow = (first - 1) / 2;
            int firstCol = (first - 1) % 2;
            int secondRow = (second - 1) / 2;
            int secondCol = (second - 1) % 2;
            return Math.Abs(firstRow - secondRow) + Math.Abs(firstCol - secondCol);
        }

        private int GetKeyHoldMs() { return _turboModeEnabled ? TurboKeyHoldMs : SafeKeyHoldMs; }
        private int GetMoveGapMs() { return _turboModeEnabled ? TurboMoveGapMs : SafeMoveGapMs; }
        private int GetConfirmGapMs() { return _turboModeEnabled ? TurboConfirmGapMs : SafeConfirmGapMs; }
        private int GetSubmitPauseMs() { return _turboModeEnabled ? TurboSubmitPauseMs : SafeSubmitPauseMs; }

        private void SetAutomationStatus(string message)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string>(SetAutomationStatus), message);
                return;
            }
            _lblStatus.Text = message;
            _lblStatus.ForeColor = message.StartsWith("✓")
                ? Color.FromArgb(0, 255, 136)
                : message.StartsWith("⚠") ? Color.FromArgb(251, 146, 60) : Color.FromArgb(0, 240, 255);
        }

        private void ToggleAutoInput()
        {
            if (!_automationUnlocked)
            {
                SetAutomationStatus("游客模式只能查看提示 · 点击标题栏“登录”解锁自动");
                return;
            }
            _autoInputEnabled = !_autoInputEnabled;
            _lastExecutedDetection = "";
            UpdateAutoButton();
            SetAutomationStatus(_autoInputEnabled
                ? "自动已开启 · 稳定识别后执行"
                : "自动已关闭 · F8 开启");
        }

        private void ToggleSpeedMode()
        {
            if (!_automationUnlocked)
            {
                SetAutomationStatus("游客模式不能使用极速 · 请先登录网站账号");
                return;
            }
            if (_inputInProgress)
            {
                SetAutomationStatus("⚠ 自动执行中，暂时不能切换速度");
                return;
            }
            _turboModeEnabled = !_turboModeEnabled;
            _lastUiStateKey = "";
            UpdateAutoButton();
            SetAutomationStatus(_turboModeEnabled
                ? "极速模式已启用 · F10 可切回稳定"
                : "稳定模式已启用 · F10 可切换极速");
        }

        private bool ListEqual(List<int> a, List<int> b)
        {
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private void RegisterGlobalHotkeys()
        {
            try
            {
                RegisterHotKey(this.Handle, HOTKEY_TOGGLE_AUTO_INPUT, 0, 0x77); // F8
                RegisterHotKey(this.Handle, HOTKEY_TOGGLE_SPEED_MODE, 0, 0x79); // F10
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_TOGGLE_AUTO_INPUT)
                {
                    ToggleAutoInput();
                    base.WndProc(ref m);
                    return;
                }
                if (id == HOTKEY_TOGGLE_SPEED_MODE)
                {
                    ToggleSpeedMode();
                    base.WndProc(ref m);
                    return;
                }
            }
            base.WndProc(ref m);
        }

        private void InitializeUI()
        {
            _pnlTitle = new Panel
            {
                Dock = DockStyle.Top,
                Height = 34,
                BackColor = Color.FromArgb(15, 23, 42),
                Cursor = Cursors.SizeAll
            };
            _pnlTitle.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, 0xA1, 0x2, 0);
                }
            };
            this.Controls.Add(_pnlTitle);

            _lblScanDot = new Label
            {
                Text = "●",
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(8, 8),
                BackColor = Color.Transparent
            };
            _pnlTitle.Controls.Add(_lblScanDot);

            _lblTitle = new Label
            {
                Text = AppProductName,
                ForeColor = Color.FromArgb(241, 245, 249),
                Font = new Font("Microsoft YaHei", 9f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 7),
                BackColor = Color.Transparent
            };
            _pnlTitle.Controls.Add(_lblTitle);

#if !MICROSOFT_STORE
            _btnUpdate = MakeTitleButton("更新", 44);
            _btnUpdate.Dock = DockStyle.Right;
            _btnUpdate.Visible = false;
            _btnUpdate.ForeColor = Color.FromArgb(250, 204, 21);
            _btnUpdate.Click += (s, e) => StartAvailableUpdate();
            _pnlTitle.Controls.Add(_btnUpdate);
#endif

            _btnWebsite = MakeTitleButton(_automationUnlocked ? "已登录" : "登录", _automationUnlocked ? 74 : 48);
            _btnWebsite.Dock = DockStyle.Right;
            _btnWebsite.TextImageRelation = TextImageRelation.ImageBeforeText;
            _btnWebsite.ImageAlign = ContentAlignment.MiddleLeft;
            _btnWebsite.TextAlign = ContentAlignment.MiddleRight;
            _btnWebsite.Click += (s, e) => { if (_automationUnlocked) ShowAccountMenu(); else PromptAccountLogin(); };
            _pnlTitle.Controls.Add(_btnWebsite);

            _btnDiagnostics = MakeTitleButton("诊断", 46);
            _btnDiagnostics.Click += (s, e) => ToggleDiagnostics();

            _btnMore = MakeTitleButton("⋯", 34);
            _btnMore.Dock = DockStyle.Right;
            _btnMore.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            _btnMore.Click += (s, e) => ShowToolsMenu();
            _pnlTitle.Controls.Add(_btnMore);

            _btnMinimize = MakeTitleButton("—", 30);
            _btnMinimize.Dock = DockStyle.Right;
            _btnMinimize.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _btnMinimize.Click += (s, e) => { this.WindowState = FormWindowState.Minimized; };
            _pnlTitle.Controls.Add(_btnMinimize);

            _btnClose = new Button
            {
                Text = "×",
                ForeColor = Color.FromArgb(148, 163, 184),
                BackColor = Color.FromArgb(15, 23, 42),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Consolas", 10f, FontStyle.Bold),
                Size = new Size(30, 34),
                Dock = DockStyle.Right,
                Cursor = Cursors.Hand
            };
            _btnClose.FlatAppearance.BorderSize = 0;
            _btnClose.Click += (s, e) => { this.Close(); };
            _pnlTitle.Controls.Add(_btnClose);

            _btnAuto = MakeModeButton("F8  自动：关闭", new Point(10, 42));
            _btnAuto.Click += (s, e) => { if (_automationUnlocked) ToggleAutoInput(); else PromptAccountLogin(); };
            this.Controls.Add(_btnAuto);

            _btnSpeed = MakeModeButton("F10  模式：稳定", new Point(174, 42));
            _btnSpeed.Click += (s, e) => ToggleSpeedMode();
            this.Controls.Add(_btnSpeed);

            _btnRecognitionMode = new Button
            {
                Text = "",
                ForeColor = Color.FromArgb(226, 232, 240),
                BackColor = Color.FromArgb(15, 23, 42),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold),
                Location = new Point(10, 84),
                Size = new Size(320, 44),
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            _btnRecognitionMode.FlatAppearance.BorderSize = 0;
            _btnRecognitionMode.FlatAppearance.MouseOverBackColor = Color.FromArgb(20, 31, 50);
            _btnRecognitionMode.Paint += PaintRecognitionModeButton;
            _btnRecognitionMode.Click += (s, e) => ShowRecognitionModeMenu(_btnRecognitionMode);
            this.Controls.Add(_btnRecognitionMode);

            _modeTips = new ToolTip();
            _modeTips.SetToolTip(_btnAuto, "F8：开启或关闭自动选择与提交");
            _modeTips.SetToolTip(_btnSpeed, "F10：稳定模式成功率优先；极速模式速度优先");
            _modeTips.SetToolTip(_btnMore, "诊断、官网和日志");
            _modeTips.SetToolTip(_btnRecognitionMode, "当前只识别" + GetModeDisplayName(_recognitionMode) + "，点击手动选择类型");

            _lblDetection = new Label
            {
                Text = "等待识别 · 尚未发送任何按键",
                ForeColor = Color.FromArgb(148, 163, 184),
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 8f),
                Location = new Point(12, 132),
                Size = new Size(316, 20),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(2, 0, 0, 0)
            };
            this.Controls.Add(_lblDetection);

            Panel grpGrid = new Panel
            {
                Location = new Point(10, 156),
                Size = new Size(320, 124),
                BackColor = Color.FromArgb(11, 18, 30)
            };
            grpGrid.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen border = new Pen(Color.FromArgb(42, 57, 78)))
                    e.Graphics.DrawRectangle(border, 0, 0, grpGrid.Width - 1, grpGrid.Height - 1);
            };
            this.Controls.Add(grpGrid);

            Label gridTitle = new Label
            {
                Text = "DYNAMIC SOLVER   ·   1—8",
                ForeColor = Color.FromArgb(94, 234, 212),
                Font = new Font("Consolas", 7.5f, FontStyle.Bold),
                Location = new Point(9, 5),
                Size = new Size(220, 16),
                BackColor = Color.Transparent
            };
            grpGrid.Controls.Add(gridTitle);

            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 2; col++)
                {
                    int idx = row * 2 + col;
                    int slotNum = idx + 1;
                    Panel tile = new Panel
                    {
                        Size = new Size(145, 23),
                        Location = new Point(8 + col * 153, 23 + row * 24),
                        BackColor = Color.FromArgb(25, 35, 52),
                        BorderStyle = BorderStyle.None
                    };
                    Label badge = new Label
                    {
                        Text = string.Format("[{0}]", slotNum),
                        ForeColor = Color.FromArgb(71, 85, 105),
                        Font = new Font("Consolas", 9f, FontStyle.Bold),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleCenter
                    };
                    tile.Controls.Add(badge);
                    grpGrid.Controls.Add(tile);
                    _gridTiles[idx] = tile;
                    _gridBadges[idx] = badge;
                }
            }

            _pnlDiagnostics = new Panel
            {
                Location = new Point(340, 38),
                Size = new Size(310, 290),
                BackColor = Color.FromArgb(9, 14, 23),
                Visible = false
            };
            this.Controls.Add(_pnlDiagnostics);

            Panel pnlTabs = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(300, 30),
                BackColor = Color.Transparent
            };
            _pnlDiagnostics.Controls.Add(pnlTabs);

            string[] referenceLabels = new string[] { "指纹 1", "指纹 2", "指纹 3", "指纹 4" };
            for (int i = 0; i < 4; i++)
            {
                int targetIdx = i;
                Button btn = new Button
                {
                    Text = referenceLabels[i],
                    ForeColor = Color.FromArgb(56, 189, 248),
                    BackColor = Color.FromArgb(30, 41, 59),
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Microsoft YaHei", 8f, FontStyle.Bold),
                    Size = new Size(69, 25),
                    Location = new Point(i * 75, 2),
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.Click += (s, e) =>
                {
                    if (targetIdx < _db.Targets.Count)
                    {
                        var t = _db.Targets[targetIdx];
                        _hasStableDetection = true;
                        _solverMode = "casino";
                        _cayoResult = null;
                        SetTarget(t, t.DefaultSlots, false);
                        SetManualDetectionState(t);
                    }
                };
                pnlTabs.Controls.Add(btn);
                _btnTabs[i] = btn;
            }

            Panel pnlHeader = new Panel
            {
                Location = new Point(0, 32),
                Size = new Size(300, 70),
                BackColor = Color.FromArgb(15, 23, 42)
            };
            _pnlDiagnostics.Controls.Add(pnlHeader);

            _lblTargetTitle = new Label
            {
                Text = "尚未稳定识别",
                ForeColor = Color.FromArgb(250, 204, 21),
                Font = new Font("Microsoft YaHei", 9.5f, FontStyle.Bold),
                Location = new Point(6, 2),
                Size = new Size(180, 19),
                AutoEllipsis = true
            };
            pnlHeader.Controls.Add(_lblTargetTitle);

            _btnOpenLogs = new Button
            {
                Text = "日志",
                ForeColor = Color.FromArgb(125, 211, 252),
                BackColor = Color.FromArgb(30, 41, 59),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei", 7.5f, FontStyle.Bold),
                Location = new Point(247, 3),
                Size = new Size(47, 24),
                Cursor = Cursors.Hand
            };
            _btnOpenLogs.FlatAppearance.BorderSize = 0;
            _btnOpenLogs.Click += delegate { OpenLogDirectory(); };
            pnlHeader.Controls.Add(_btnOpenLogs);

            _btnFeedback = new Button
            {
                Text = "改进",
                ForeColor = Color.FromArgb(125, 211, 252),
                BackColor = Color.FromArgb(30, 41, 59),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei", 7.5f, FontStyle.Bold),
                Location = new Point(194, 3),
                Size = new Size(47, 24),
                Cursor = Cursors.Hand
            };
            _btnFeedback.FlatAppearance.BorderSize = 0;
            _btnFeedback.Click += delegate { RecognitionFeedback.ShowSettings(this, false); };
            pnlHeader.Controls.Add(_btnFeedback);

            _lblMnemonic = new Label
            {
                Text = "识别成功后显示指纹特征",
                ForeColor = Color.FromArgb(52, 211, 153),
                Font = new Font("Microsoft YaHei", 7.5f),
                Location = new Point(6, 20),
                Size = new Size(290, 16)
            };
            pnlHeader.Controls.Add(_lblMnemonic);

            _lblDiagnosticStats = new Label
            {
                Text = "赌场 · 分辨率 -- · 周期 -- · 置信度 --\n最近错误：无",
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Microsoft YaHei", 7f),
                Location = new Point(6, 38),
                Size = new Size(288, 30),
                AutoEllipsis = true
            };
            pnlHeader.Controls.Add(_lblDiagnosticStats);

            GroupBox grpSlices = new GroupBox
            {
                Text = " 基准切片 · 诊断对照 ",
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Microsoft YaHei", 8f),
                Location = new Point(0, 105),
                Size = new Size(300, 120),
                BackColor = Color.Transparent
            };
            this.Controls.Add(grpSlices);

            for (int i = 0; i < 4; i++)
            {
                PictureBox pb = new PictureBox
                {
                    Size = new Size(65, 58),
                    Location = new Point(7 + i * 72, 20),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.Black,
                    BorderStyle = BorderStyle.FixedSingle
                };
                grpSlices.Controls.Add(pb);
                _slicePics[i] = pb;

                Label slbl = new Label
                {
                    Text = string.Format("切片 {0}", i + 1),
                    ForeColor = Color.FromArgb(0, 255, 136),
                    Font = new Font("Microsoft YaHei", 7f, FontStyle.Bold),
                    Size = new Size(68, 30),
                    Location = new Point(6 + i * 72, 81),
                    TextAlign = ContentAlignment.TopCenter
                };
                grpSlices.Controls.Add(slbl);
                _sliceLabels[i] = slbl;
            }

            GroupBox grpWasd = new GroupBox
            {
                Text = " 自动按键路线 · 从实时光标起步 ",
                ForeColor = Color.FromArgb(250, 204, 21),
                Font = new Font("Microsoft YaHei", 8f, FontStyle.Bold),
                Location = new Point(0, 228),
                Size = new Size(300, 58),
                BackColor = Color.Transparent
            };
            this.Controls.Add(grpWasd);

            _lblWasd = new Label
            {
                Text = "等待稳定识别...",
                ForeColor = Color.FromArgb(248, 250, 252),
                Font = new Font("Microsoft YaHei", 7.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            grpWasd.Controls.Add(_lblWasd);
            _pnlDiagnostics.Controls.Add(grpSlices);
            _pnlDiagnostics.Controls.Add(grpWasd);

            _lblStatus = new Label
            {
                Text = "等待游戏中的指纹破解器画面",
                ForeColor = Color.FromArgb(148, 163, 184),
                BackColor = Color.FromArgb(5, 8, 15),
                Font = new Font("Microsoft YaHei", 8f),
                Dock = DockStyle.Bottom,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0)
            };
            this.Controls.Add(_lblStatus);
            UpdateAutoButton();
        }

        private Button MakeTitleButton(string text, int width)
        {
            Button button = new Button
            {
                Text = text,
                ForeColor = Color.FromArgb(148, 163, 184),
                BackColor = Color.FromArgb(15, 23, 42),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei", 7.5f, FontStyle.Bold),
                Size = new Size(width, 34),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private Button MakeModeButton(string text, Point location)
        {
            Button button = new Button
            {
                Text = text,
                ForeColor = Color.FromArgb(226, 232, 240),
                BackColor = Color.FromArgb(20, 30, 47),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei", 8.5f, FontStyle.Bold),
                Size = new Size(156, 36),
                Location = location,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(71, 85, 105);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 41, 59);
            return button;
        }

        private void PaintRecognitionModeButton(object sender, PaintEventArgs e)
        {
            Button button = sender as Button;
            if (button == null) return;
            Color accent = GetModeAccentColor(_recognitionMode);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen border = new Pen(Color.FromArgb(40, 55, 76)))
                e.Graphics.DrawRectangle(border, 0, 0, button.Width - 1, button.Height - 1);
            using (Brush accentBrush = new SolidBrush(accent))
            {
                e.Graphics.FillRectangle(accentBrush, 0, 0, 3, button.Height);
                e.Graphics.FillEllipse(accentBrush, 14, 25, 6, 6);
            }
            using (Font captionFont = new Font("Consolas", 6.8f, FontStyle.Bold))
            using (Font valueFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold))
            using (Font actionFont = new Font("Microsoft YaHei UI", 7.5f))
            {
                TextRenderer.DrawText(e.Graphics, "ACTIVE MODULE", captionFont, new Point(13, 5), Color.FromArgb(100, 116, 139));
                TextRenderer.DrawText(e.Graphics, GetModeDisplayName(_recognitionMode), valueFont,
                    new Rectangle(25, 20, 205, 20), Color.FromArgb(241, 245, 249),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(e.Graphics, "切换  ▾", actionFont,
                    new Rectangle(button.Width - 76, 0, 64, button.Height), accent,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
        }

        private static Color GetModeAccentColor(int mode)
        {
            if (mode == ModeCasinoKeypad) return Color.FromArgb(167, 139, 250);
            if (mode == ModeCayoFingerprint) return Color.FromArgb(52, 211, 153);
            if (mode == ModeCayoVoltage) return Color.FromArgb(251, 191, 36);
            return Color.FromArgb(56, 189, 248);
        }

        private void OpenWebsite()
        {
            try
            {
                Process.Start(new ProcessStartInfo(WebsiteUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetAutomationStatus("⚠ 无法打开网站：" + ex.Message);
            }
        }

#if !MICROSOFT_STORE
        private void ShowAvailableUpdate(UpdateClient.UpdateInfo update)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<UpdateClient.UpdateInfo>(ShowAvailableUpdate), update);
                return;
            }
            _availableUpdate = update;
            _btnUpdate.Text = "新版本";
            _btnUpdate.Enabled = true;
            _btnUpdate.Visible = true;
            _modeTips.SetToolTip(_btnUpdate, "发现 " + update.Version + "，点击查看并安装");
        }

        private void StartAvailableUpdate()
        {
            if (_availableUpdate == null || !_btnUpdate.Enabled) return;
            DialogResult answer = MessageBox.Show(this,
                "发现名钻指纹助手 " + _availableUpdate.Version + "。\n\n是否下载并覆盖当前安装位置？安装完成后会自动重新启动。",
                "下载新版本", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer != DialogResult.Yes) return;

            _btnUpdate.Enabled = false;
            _btnUpdate.Text = "下载中";
            SetAutomationStatus("正在下载新版安装包...");
            UpdateClient.StartDownload(this, _availableUpdate, SetAutomationStatus, UpdateDownloadProgress, FinishUpdateAttempt);
        }

        private void UpdateDownloadProgress(int percent, double megabytesPerSecond, long received, long total)
        {
            if (this.IsDisposed) return;
            _btnUpdate.Text = percent >= 0 ? percent + "%" : "下载中";
            string amount = total > 0
                ? string.Format("{0:0.0}/{1:0.0} MB", received / 1048576d, total / 1048576d)
                : string.Format("{0:0.0} MB", received / 1048576d);
            SetAutomationStatus(string.Format("正在下载新版 · {0} · {1:0.0} MB/s", amount, megabytesPerSecond));
        }

        private void FinishUpdateAttempt(bool success, string message)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<bool, string>(FinishUpdateAttempt), success, message);
                return;
            }
            if (success) return;
            _btnUpdate.Text = "重试更新";
            _btnUpdate.Enabled = true;
            SetAutomationStatus("⚠ " + message);
            MessageBox.Show(this, message + "\n\n请检查网络后点击右上角“重试更新”，或打开官网下载。",
                "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
#endif

        private void PromptAccountLogin()
        {
            using (AccountLoginForm form = new AccountLoginForm(LicenseClient.GetDeviceId(), AppProductVersion))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    SetAutomationStatus("游客模式 · 只显示答案，不会发送按键");
                    return;
                }
            }
            _automationUnlocked = true;
            _accountOnline = true;
            _accountProfile = LicenseClient.CurrentProfile;
            _lastUiStateKey = "";
            UpdateAutoButton();
            UpdateAccountButton();
            BeginLoadAccountAvatar(true);
            SetAutomationStatus("账号已登录 · F8 自动，F10 极速");
        }

        private void BeginAccountRefresh()
        {
            BeginLoadAccountAvatar(false);
            LicenseClient.BeginRevalidate(this, AppProductVersion, delegate(bool access, bool online, LicenseClient.AccountProfile profile, string message)
            {
                if (!access)
                {
                    _automationUnlocked = false;
                    _autoInputEnabled = false;
                    _turboModeEnabled = false;
                    _accountOnline = true;
                    _accountProfile = null;
                    UpdateAutoButton();
                    UpdateAccountButton();
                    SetAutomationStatus("账号授权已失效 · 已切换游客模式：" + message);
                    return;
                }
                _automationUnlocked = true;
                _accountOnline = online;
                _accountProfile = profile;
                UpdateAutoButton();
                UpdateAccountButton();
                if (online) BeginLoadAccountAvatar(true);
            });
        }

        private void UpdateAccountButton()
        {
            if (_btnWebsite == null || _btnWebsite.IsDisposed) return;
            if (this.InvokeRequired) { this.BeginInvoke(new Action(UpdateAccountButton)); return; }
            if (!_automationUnlocked)
            {
                Image old = _btnWebsite.Image;
                _btnWebsite.Image = null;
                if (old != null) old.Dispose();
                _btnWebsite.Width = 48;
                _btnWebsite.Text = "登录";
                _modeTips.SetToolTip(_btnWebsite, "登录网站账号以解锁自动与极速模式");
                return;
            }
            string nickname = _accountProfile != null && !String.IsNullOrWhiteSpace(_accountProfile.Nickname)
                ? _accountProfile.Nickname.Trim() : "已登录";
            if (nickname.Length > 5) nickname = nickname.Substring(0, 5) + "…";
            _btnWebsite.Width = 74;
            _btnWebsite.Text = nickname;
            _modeTips.SetToolTip(_btnWebsite, (_accountOnline ? "在线" : "离线授权") + " · 点击管理账号");
        }

        private void ShowAccountMenu()
        {
            if (!_automationUnlocked) { PromptAccountLogin(); return; }
            if (_accountMenu != null) { _accountMenu.Dispose(); _accountMenu = null; }
            _accountMenu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(226, 232, 240),
                ShowImageMargin = false,
                Font = new Font("Microsoft YaHei UI", 8.5f)
            };
            string nickname = _accountProfile != null ? _accountProfile.Nickname : "已登录用户";
            ToolStripMenuItem identity = new ToolStripMenuItem(nickname) { Enabled = false, ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold) };
            string lease = _accountProfile != null && _accountProfile.ExpiresAtUtc > DateTime.UtcNow
                ? "授权至 " + _accountProfile.ExpiresAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "授权状态有效";
            ToolStripMenuItem state = new ToolStripMenuItem((_accountOnline ? "● 在线 · " : "● 离线可用 · ") + lease) { Enabled = false };
            ToolStripMenuItem website = new ToolStripMenuItem("打开网站账号中心");
            website.Click += delegate { OpenAccountWebsite(); };
            ToolStripMenuItem switchAccount = new ToolStripMenuItem("切换账号");
            switchAccount.Click += delegate { LogoutAccount(false); PromptAccountLogin(); };
            ToolStripMenuItem logout = new ToolStripMenuItem("退出登录");
            logout.Click += delegate { LogoutAccount(true); };
            _accountMenu.Items.Add(identity);
            _accountMenu.Items.Add(state);
            _accountMenu.Items.Add(new ToolStripSeparator());
            _accountMenu.Items.Add(website);
            _accountMenu.Items.Add(switchAccount);
            _accountMenu.Items.Add(logout);
            _accountMenu.Show(_btnWebsite, new Point(0, _btnWebsite.Height));
        }

        private void ShowToolsMenu()
        {
            if (_toolsMenu != null) { _toolsMenu.Dispose(); _toolsMenu = null; }
            _toolsMenu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(226, 232, 240),
                ShowImageMargin = false,
                Font = new Font("Microsoft YaHei UI", 8.5f)
            };
            ToolStripMenuItem mode = BuildRecognitionModeMenu("识别模式：" + GetModeDisplayName(_recognitionMode));
            ToolStripMenuItem diagnostics = new ToolStripMenuItem(_diagnosticsExpanded ? "收起诊断面板" : "打开诊断面板");
            diagnostics.Click += delegate { ToggleDiagnostics(); };
            ToolStripMenuItem website = new ToolStripMenuItem("打开 GTACN 官网");
            website.Click += delegate { OpenWebsite(); };
            ToolStripMenuItem logs = new ToolStripMenuItem("打开错误日志");
            logs.Click += delegate { OpenLogDirectory(); };
            _toolsMenu.Items.Add(mode);
            _toolsMenu.Items.Add(diagnostics);
            _toolsMenu.Items.Add(new ToolStripSeparator());
            _toolsMenu.Items.Add(website);
            _toolsMenu.Items.Add(logs);
            _toolsMenu.Show(_btnMore, new Point(0, _btnMore.Height));
        }

        private void LogoutAccount(bool showStatus)
        {
            LicenseClient.LogoutLocal();
            _automationUnlocked = false;
            _autoInputEnabled = false;
            _turboModeEnabled = false;
            _accountOnline = false;
            _accountProfile = null;
            UpdateAutoButton();
            UpdateAccountButton();
            if (showStatus) SetAutomationStatus("已退出账号 · 游客模式仍可正常识别并显示答案");
        }

        private void OpenAccountWebsite()
        {
            try { Process.Start(new ProcessStartInfo("https://gtacn.org/") { UseShellExecute = true }); }
            catch (Exception ex) { SetAutomationStatus("⚠ 无法打开网站：" + ex.Message); }
        }

        private void BeginLoadAccountAvatar(bool forceRefresh)
        {
            LicenseClient.AccountProfile profile = _accountProfile;
            if (!_automationUnlocked || profile == null) return;
            string avatar = profile.Avatar == null ? "" : profile.Avatar.Trim();
            Thread worker = new Thread(delegate()
            {
                try
                {
                    Bitmap image = LoadAccountAvatar(avatar, profile.Nickname, forceRefresh);
                    if (this.IsDisposed) { image.Dispose(); return; }
                    this.BeginInvoke(new Action(delegate()
                    {
                        if (this.IsDisposed || !_automationUnlocked || !Object.ReferenceEquals(_accountProfile, profile))
                        {
                            image.Dispose();
                            return;
                        }
                        Image old = _btnWebsite.Image;
                        _btnWebsite.Image = image;
                        _btnWebsite.ImageAlign = ContentAlignment.MiddleLeft;
                        _btnWebsite.TextImageRelation = TextImageRelation.ImageBeforeText;
                        if (old != null) old.Dispose();
                    }));
                }
                catch (Exception ex) { AppLog.WriteThrottled("AccountAvatar", ex); }
            });
            worker.IsBackground = true;
            worker.Priority = ThreadPriority.BelowNormal;
            worker.Name = "AccountAvatarLoader";
            worker.Start();
        }

        private static Bitmap LoadAccountAvatar(string avatar, string nickname, bool forceRefresh)
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LSF", "FingerprintAssistant", "avatars");
            Directory.CreateDirectory(directory);
            string key;
            using (SHA256 sha = SHA256.Create()) key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(avatar))).Replace("-", "").Substring(0, 20);
            string cachePath = Path.Combine(directory, key + ".png");
            if (!forceRefresh && File.Exists(cachePath))
            {
                using (Image cached = Image.FromFile(cachePath)) return MakeRoundAvatar(cached, nickname);
            }

            string url = avatar;
            if (url.StartsWith("/", StringComparison.Ordinal)) url = "https://gtacn.org" + url;
            Uri parsed;
            if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) || parsed.Scheme != Uri.UriSchemeHttps)
                return MakeRoundAvatar(null, nickname);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(parsed);
            request.Method = "GET";
            request.Timeout = 5000;
            request.ReadWriteTimeout = 5000;
            request.UserAgent = "Mingzuan-Fingerprint-Assistant/" + AppProductVersion;
            byte[] bytes;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                if (response.ContentLength > 2 * 1024 * 1024) throw new InvalidOperationException("头像文件超过 2 MB");
                using (Stream input = response.GetResponseStream())
                using (MemoryStream output = new MemoryStream())
                {
                    byte[] buffer = new byte[16384];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        if (output.Length > 2 * 1024 * 1024) throw new InvalidOperationException("头像文件超过 2 MB");
                    }
                    bytes = output.ToArray();
                }
            }
            using (MemoryStream stream = new MemoryStream(bytes))
            using (Image source = Image.FromStream(stream, true, true))
            {
                Bitmap rounded = MakeRoundAvatar(source, nickname);
                try { rounded.Save(cachePath, ImageFormat.Png); } catch { }
                return rounded;
            }
        }

        private static Bitmap MakeRoundAvatar(Image source, string nickname)
        {
            Bitmap result = new Bitmap(22, 22, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(result))
            using (GraphicsPath path = new GraphicsPath())
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                path.AddEllipse(0, 0, 21, 21);
                graphics.SetClip(path);
                if (source != null) graphics.DrawImage(source, new Rectangle(0, 0, 22, 22));
                else
                {
                    graphics.Clear(Color.FromArgb(8, 145, 178));
                    string initial = !String.IsNullOrWhiteSpace(nickname) ? nickname.Trim().Substring(0, 1) : "LS";
                    using (Font font = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold))
                    using (Brush brush = new SolidBrush(Color.White))
                    {
                        SizeF size = graphics.MeasureString(initial, font);
                        graphics.DrawString(initial, font, brush, (22 - size.Width) / 2f, (22 - size.Height) / 2f);
                    }
                }
                graphics.ResetClip();
                using (Pen pen = new Pen(Color.FromArgb(56, 189, 248), 1)) graphics.DrawEllipse(pen, 0, 0, 21, 21);
            }
            return result;
        }

        private void ToggleDiagnostics()
        {
            int right = this.Left + this.Width;
            _diagnosticsExpanded = !_diagnosticsExpanded;
            _pnlDiagnostics.Visible = _diagnosticsExpanded;
            this.Size = _diagnosticsExpanded ? ExpandedWindowSize : CompactWindowSize;
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            this.Left = Math.Max(wa.Left, right - this.Width);
            this.Top = Math.Max(wa.Top, Math.Min(this.Top, wa.Bottom - this.Height));
            _btnDiagnostics.Text = _diagnosticsExpanded ? "收起" : "诊断";
            if (_diagnosticsExpanded) RefreshDiagnosticStats();
        }

        private void RefreshDiagnosticStats()
        {
            if (!_diagnosticsExpanded || _lblDiagnosticStats == null || _lblDiagnosticStats.IsDisposed) return;
            string mode = GetModeDisplayName(_recognitionMode);
            string resolution = _lastFrameWidth > 0 && _lastFrameHeight > 0
                ? _lastFrameWidth + "×" + _lastFrameHeight : "--";
            string confidence = _lastConfidencePercent > 0 ? _lastConfidencePercent + "%" : "--";
            string error = AppLog.LastErrorSummary;
            if (error.Length > 33) error = error.Substring(0, 33) + "…";
            _lblDiagnosticStats.Text = string.Format(
                "{0} · {1} · 周期 {2} ms · 置信度 {3}\n最近错误：{4}",
                mode, resolution, _lastScanDurationMs, confidence, error);
        }

        private void QueuePresenceHeartbeat()
        {
            if (_simulatorMode || !_automationUnlocked || Interlocked.Exchange(ref _presenceBusy, 1) != 0) return;
            string mode = GetModeApiName(_recognitionMode);
            bool autoEnabled = _autoInputEnabled;
            string speed = _turboModeEnabled ? "turbo" : "stable";
            string state = _inputInProgress ? "executing"
                : _hasStableDetection ? "ready"
                : _pendingDetectionFrames > 0 ? "detecting" : "waiting";
            string summary = state == "executing" ? "正在执行自动操作"
                : state == "ready" ? "已识别当前破解画面"
                : state == "detecting" ? "正在确认识别结果" : "等待破解画面";
            Thread worker = new Thread(delegate()
            {
                try { LicenseClient.SendPresence(AppProductVersion, mode, autoEnabled, speed, state, summary); }
                catch (LicenseClient.LicenseAccessRejectedException ex) { AppLog.WriteThrottled("PresenceRejected", ex); }
                catch (Exception ex) { AppLog.WriteThrottled("PresenceSync", ex); }
                finally { Interlocked.Exchange(ref _presenceBusy, 0); }
            });
            worker.IsBackground = true;
            worker.Name = "ToolPresenceHeartbeat";
            worker.Start();
        }

        private void OpenLogDirectory()
        {
            try
            {
                Directory.CreateDirectory(AppLog.LogDirectory);
                Process.Start(new ProcessStartInfo(AppLog.LogDirectory) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetAutomationStatus("⚠ 无法打开日志目录：" + ex.Message);
            }
        }

        private static string RecognitionModePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LSF", "FingerprintAssistant", "recognition.mode"); }
        }

        private static int LoadRecognitionMode()
        {
            try
            {
                int mode;
                if (Int32.TryParse(File.ReadAllText(RecognitionModePath).Trim(), out mode) && mode >= ModeCasinoFingerprint && mode <= ModeCasinoKeypad) return mode;
            }
            catch { }
            return ModeCasinoFingerprint;
        }

        private static void SaveRecognitionMode(int mode)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RecognitionModePath));
                File.WriteAllText(RecognitionModePath, mode.ToString());
            }
            catch { }
        }

        private static string GetModeDisplayName(int mode)
        {
            if (mode == ModeCayoFingerprint) return "佩里科指纹";
            if (mode == ModeCayoVoltage) return "佩里科信号箱";
            if (mode == ModeCasinoKeypad) return "赌场点阵键盘";
            return "赌场指纹";
        }

        private static string GetModeApiName(int mode)
        {
            if (mode == ModeCayoFingerprint) return "cayo_fingerprint";
            if (mode == ModeCayoVoltage) return "cayo_voltage";
            if (mode == ModeCasinoKeypad) return "casino_keypad";
            return "casino_fingerprint";
        }

        private static string GetModeWaitingText(int mode)
        {
            if (mode == ModeCayoFingerprint) return "等待佩里科岛指纹画面";
            if (mode == ModeCayoVoltage) return "等待佩里科岛信号箱画面";
            if (mode == ModeCasinoKeypad) return "等待赌场点阵闪烁画面 · 必须在图案显示时捕获";
            return "等待名钻赌场指纹画面";
        }

        private ToolStripMenuItem BuildRecognitionModeMenu(string title)
        {
            ToolStripMenuItem root = new ToolStripMenuItem(title);
            AddRecognitionModeItem(root, "名钻赌场指纹", ModeCasinoFingerprint);
            AddRecognitionModeItem(root, "名钻赌场点阵键盘", ModeCasinoKeypad);
            root.DropDownItems.Add(new ToolStripSeparator());
            AddRecognitionModeItem(root, "佩里科岛指纹", ModeCayoFingerprint);
            AddRecognitionModeItem(root, "佩里科岛信号箱", ModeCayoVoltage);
            return root;
        }

        private void AddRecognitionModeItem(ToolStripMenuItem root, string text, int mode)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text) { Checked = _recognitionMode == mode };
            item.Click += delegate { SetRecognitionMode(mode); };
            root.DropDownItems.Add(item);
        }

        private void ShowRecognitionModeMenu(Control anchor)
        {
            if (_simulatorMode) return;
            if (_recognitionMenu != null && _recognitionMenu.Visible)
            {
                _recognitionMenu.Close(ToolStripDropDownCloseReason.CloseCalled);
                return;
            }
            if (_recognitionMenu == null || _recognitionMenu.IsDisposed)
            {
                _recognitionMenu = new ContextMenuStrip
                {
                    BackColor = Color.FromArgb(15, 23, 42), ForeColor = Color.FromArgb(226, 232, 240),
                    ShowImageMargin = false, Font = new Font("Microsoft YaHei UI", 8.5f)
                };
                int[] modes = new int[] { ModeCasinoFingerprint, ModeCasinoKeypad, -1, ModeCayoFingerprint, ModeCayoVoltage };
                foreach (int mode in modes)
                {
                    if (mode < 0)
                    {
                        _recognitionMenu.Items.Add(new ToolStripSeparator());
                        continue;
                    }
                    ToolStripMenuItem item = new ToolStripMenuItem(GetModeDisplayName(mode));
                    item.Tag = mode;
                    int selectedMode = mode;
                    item.Click += delegate { SetRecognitionMode(selectedMode); };
                    _recognitionMenu.Items.Add(item);
                }
            }
            foreach (ToolStripItem rawItem in _recognitionMenu.Items)
            {
                ToolStripMenuItem item = rawItem as ToolStripMenuItem;
                if (item != null && item.Tag is int) item.Checked = (int)item.Tag == _recognitionMode;
            }
            _recognitionMenu.Show(anchor, new Point(0, anchor.Height));
        }

        private void SetRecognitionMode(int mode)
        {
            if (_simulatorMode || mode == _recognitionMode) return;
            if (_inputInProgress)
            {
                SetAutomationStatus("⚠ 正在执行按键，暂时不能切换识别模式");
                return;
            }
            _recognitionMode = mode;
            SaveRecognitionMode(mode);
            _lastUiStateKey = "";
            _pendingDetection = "";
            _pendingDetectionFrames = 0;
            _missedDetectionFrames = 0;
            _hasStableDetection = false;
            _solverMode = "none";
            _cayoResult = null;
            _voltageResult = null;
            _keypadResult = null;
            _keypadCapturedAtUtc = DateTime.MinValue;
            _keypadInputStageFrames = 0;
            _keypadWaitingForNextPattern = false;
            _keypadSawPatternGap = false;
            _lastExecutedDetection = "";
            _current = null;
            _lastUiStateKey = "";
            _btnRecognitionMode.Invalidate();
            _modeTips.SetToolTip(_btnRecognitionMode, "当前只识别" + GetModeDisplayName(mode) + "，点击手动选择类型");
            UpdateAutoButton();
            SetWaitingState("已切换：只识别" + GetModeDisplayName(mode));
        }

        private void UpdateAutoButton()
        {
            if (_btnAuto == null) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(UpdateAutoButton));
                return;
            }
            _btnAuto.Text = !_automationUnlocked ? "登录后解锁自动" : (_autoInputEnabled ? "F8  自动：已开启" : "F8  自动：已关闭");
            UpdateAccountButton();
            _btnAuto.ForeColor = !_automationUnlocked
                ? Color.FromArgb(251, 191, 36)
                : _autoInputEnabled ? Color.FromArgb(110, 231, 183) : Color.FromArgb(203, 213, 225);
            _btnAuto.BackColor = !_automationUnlocked
                ? Color.FromArgb(35, 29, 18)
                : _autoInputEnabled ? Color.FromArgb(6, 48, 39) : Color.FromArgb(30, 23, 32);
            _btnAuto.FlatAppearance.BorderColor = !_automationUnlocked
                ? Color.FromArgb(120, 83, 28)
                : _autoInputEnabled ? Color.FromArgb(16, 116, 89) : Color.FromArgb(112, 45, 59);

            if (_btnSpeed != null)
            {
                _btnSpeed.Text = !_automationUnlocked
                    ? "F10  模式：锁定"
                    : (_turboModeEnabled ? "F10  模式：极速" : "F10  模式：稳定");
                _btnSpeed.ForeColor = !_automationUnlocked
                    ? Color.FromArgb(100, 116, 139)
                    : _turboModeEnabled ? Color.FromArgb(253, 230, 138) : Color.FromArgb(125, 211, 252);
                _btnSpeed.BackColor = !_automationUnlocked
                    ? Color.FromArgb(22, 30, 43)
                    : _turboModeEnabled ? Color.FromArgb(54, 36, 12) : Color.FromArgb(10, 34, 52);
                _btnSpeed.FlatAppearance.BorderColor = !_automationUnlocked
                    ? Color.FromArgb(51, 65, 85)
                    : _turboModeEnabled ? Color.FromArgb(146, 96, 20) : Color.FromArgb(14, 88, 126);
            }
        }

        private void SetPendingDetectionState(double confidence)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<double>(SetPendingDetectionState), confidence);
                return;
            }
            string key = "pending:" + ((int)Math.Round(confidence * 100)).ToString();
            if (_lastUiStateKey == key) return;
            _lastUiStateKey = key;
            ClearGrid();
            _lblScanDot.ForeColor = Color.FromArgb(250, 204, 21);
            _lblDetection.Text = string.Format("正在确认识别  ·  {0:0}%", confidence * 100);
            _lblDetection.ForeColor = Color.FromArgb(250, 204, 21);
            _lblStatus.Text = "识别结果尚未稳定，不会发送按键";
            _lblStatus.ForeColor = Color.FromArgb(250, 204, 21);
        }

        private void SetStableDetectionState(FingerprintTarget target, List<int> slots, double confidence)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<FingerprintTarget, List<int>, double>(SetStableDetectionState), target, slots, confidence);
                return;
            }
            string slotText = string.Join(",", slots.ConvertAll(v => v.ToString()).ToArray());
            string key = string.Format("stable:{0}:{1}:{2}:{3}:{4}:{5}", target.Id, slotText, (int)Math.Round(confidence * 100), _autoInputEnabled, _turboModeEnabled, _automationUnlocked);
            if (_lastUiStateKey == key) return;
            _lastUiStateKey = key;
            _lblScanDot.ForeColor = Color.FromArgb(0, 255, 136);
            _lblDetection.Text = string.Format("指纹 {0}  ·  {1:0}%  ·  {2}", target.Id, confidence * 100, !_automationUnlocked ? "游客提示" : (_autoInputEnabled ? "自动开启" : (_turboModeEnabled ? "极速待机" : "稳定待机")));
            _lblDetection.ForeColor = Color.FromArgb(0, 255, 136);
            _lblStatus.Text = !_automationUnlocked
                ? "识别稳定 · 游客模式不会发送按键"
                : _autoInputEnabled
                ? "识别稳定 · 准备自动执行"
                : "识别稳定 · 按 F8 开启自动";
            _lblStatus.ForeColor = Color.FromArgb(125, 211, 252);
        }

        private void SetStableCayoState(CayoFingerprintResult result)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<CayoFingerprintResult>(SetStableCayoState), CloneCayoResult(result));
                return;
            }
            string key = "stable:" + result.DetectionKey + ":" + _autoInputEnabled + ":" + _turboModeEnabled + ":" + _automationUnlocked;
            if (_lastUiStateKey == key) return;
            _lastUiStateKey = key;
            _lblScanDot.ForeColor = Color.FromArgb(0, 255, 136);
            _lblDetection.Text = string.Format("佩里科岛 · {0:0}% · 光标第 {1} 行 · {2}", result.Confidence * 100, result.CursorRow, !_automationUnlocked ? "游客提示" : (_autoInputEnabled ? "自动开启" : "自动关闭"));
            _lblDetection.ForeColor = Color.FromArgb(0, 255, 136);
            _lblStatus.Text = !_automationUnlocked ? "识别稳定 · 游客模式不会发送按键" : (_autoInputEnabled ? "识别稳定 · 准备自动校准 8 行" : "识别稳定 · 按 F8 开启自动");
            _lblStatus.ForeColor = Color.FromArgb(125, 211, 252);
        }

        private void SetStableVoltageState(CayoVoltageResult result)
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action<CayoVoltageResult>(SetStableVoltageState), result); return; }
            string key = "stable:" + result.DetectionKey + ":" + _autoInputEnabled + ":" + _automationUnlocked;
            if (_lastUiStateKey == key) return;
            _lastUiStateKey = key;
            _lblScanDot.ForeColor = Color.FromArgb(0, 255, 136);
            _lblDetection.Text = string.Format("信号箱 · 目标 {0} · {1:0}% · {2}", result.Target, result.Confidence * 100,
                !_automationUnlocked ? "游客提示" : (_autoInputEnabled ? "自动开启" : "自动关闭"));
            _lblDetection.ForeColor = Color.FromArgb(0, 255, 136);
            _lblStatus.Text = !_automationUnlocked ? "识别稳定 · 游客模式不会发送按键" : (_autoInputEnabled ? "识别稳定 · 准备连接线路" : "识别稳定 · 按 F8 开启自动");
            _lblStatus.ForeColor = Color.FromArgb(125, 211, 252);
        }

        private void SetStableKeypadState(CasinoKeypadResult result, bool inputStage)
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action<CasinoKeypadResult, bool>(SetStableKeypadState), result, inputStage); return; }
            string key = "stable:" + result.DetectionKey + ":" + inputStage + ":" + _autoInputEnabled + ":" + _automationUnlocked;
            if (_lastUiStateKey == key) return;
            _lastUiStateKey = key;
            _lblScanDot.ForeColor = Color.FromArgb(0, 255, 136);
            _lblDetection.Text = "赌场点阵 · 已记录 [" + string.Join(",", result.Rows) + "] · " + (inputStage ? "等待输入" : "图案捕获");
            _lblDetection.ForeColor = Color.FromArgb(0, 255, 136);
            _lblStatus.Text = inputStage
                ? (!_automationUnlocked ? "答案已保留 · 游客模式不会发送按键" : (_autoInputEnabled ? "输入阶段已确认 · 准备执行" : "答案已保留 · 按 F8 开启自动"))
                : "请等待闪烁结束，不要切换破解模式";
            _lblStatus.ForeColor = Color.FromArgb(125, 211, 252);
        }

        private void SetVoltageTarget(CayoVoltageResult result)
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action<CayoVoltageResult>(SetVoltageTarget), result); return; }
            _lblTargetTitle.Text = "佩里科岛 · VOLTlab 信号箱";
            _lblMnemonic.Text = FormatVoltageAssignment(result);
            for (int i = 0; i < 8; i++)
            {
                bool used = i < 3;
                _gridTiles[i].BackColor = used ? Color.FromArgb(14, 116, 144) : Color.FromArgb(30, 41, 59);
                _gridBadges[i].ForeColor = used ? Color.White : Color.FromArgb(71, 85, 105);
                _gridBadges[i].Text = used ? result.LeftNumbers[i] + " ×" + result.RightMultipliers[result.Assignment[i]] : "";
            }
            _lblWasd.Text = "目标 " + result.Target + " · " + FormatVoltageAssignment(result);
        }

        private void SetKeypadTarget(CasinoKeypadResult result, bool inputStage)
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action<CasinoKeypadResult, bool>(SetKeypadTarget), result, inputStage); return; }
            _lblTargetTitle.Text = "名钻赌场 · 点阵键盘";
            _lblMnemonic.Text = "六列图案已从真实游戏闪烁画面捕获";
            for (int i = 0; i < 8; i++)
            {
                bool used = i < result.Rows.Length;
                _gridTiles[i].BackColor = used ? Color.FromArgb(8, 145, 178) : Color.FromArgb(30, 41, 59);
                _gridBadges[i].ForeColor = used ? Color.White : Color.FromArgb(71, 85, 105);
                _gridBadges[i].Text = used ? (i + 1) + "列 · 第" + result.Rows[i] + "行" : "";
            }
            _lblWasd.Text = "输入顺序：[" + string.Join(" → ", result.Rows) + "]";
        }

        private void SetCayoTarget(CayoFingerprintResult result)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<CayoFingerprintResult>(SetCayoTarget), CloneCayoResult(result));
                return;
            }
            _lblTargetTitle.Text = "佩里科岛 · 指纹克隆器";
            _lblMnemonic.Text = "同帧匹配右侧完整指纹 · 当前光标第 " + result.CursorRow + " 行";
            List<string> route = new List<string>();
            for (int i = 0; i < 8; i++)
            {
                int click = result.Clicks[i];
                bool aligned = click == 0;
                _gridTiles[i].BackColor = aligned ? Color.FromArgb(0, 255, 136) : Color.FromArgb(14, 116, 144);
                _gridBadges[i].ForeColor = aligned ? Color.FromArgb(4, 6, 9) : Color.White;
                string action = aligned ? "✓ 已对齐" : click > 0 ? "→ " + click : "← " + Math.Abs(click);
                _gridBadges[i].Text = string.Format("{0}. {1}", i + 1, action);
                _gridBadges[i].Font = new Font("Microsoft YaHei", 8f, FontStyle.Bold);
                route.Add((i + 1) + ":" + action);
            }
            for (int i = 0; i < 4; i++)
            {
                _slicePics[i].Image = null;
                _sliceLabels[i].Text = "行 " + (i * 2 + 1) + " / " + (i * 2 + 2);
                _btnTabs[i].BackColor = Color.FromArgb(30, 41, 59);
                _btnTabs[i].ForeColor = Color.FromArgb(56, 189, 248);
            }
            _lblWasd.Text = string.Join("  ", route.ToArray());
            _lblStatus.Text = "✓ 佩里科路线已生成；自动模式启动前会再次复检";
            _lblStatus.ForeColor = Color.FromArgb(0, 240, 255);
        }

        private void SetManualDetectionState(FingerprintTarget target)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<FingerprintTarget>(SetManualDetectionState), target);
                return;
            }
            _lastUiStateKey = "manual:" + target.Id.ToString();
            _lblScanDot.ForeColor = Color.FromArgb(56, 189, 248);
            _lblDetection.Text = string.Format("指纹 {0}  ·  手动  ·  自动{1}  ·  {2}", target.Id, _autoInputEnabled ? "开启" : "关闭", _turboModeEnabled ? "极速" : "稳定");
            _lblDetection.ForeColor = Color.FromArgb(56, 189, 248);
            _lblStatus.Text = _automationUnlocked ? "手动参考已载入 · 不会触发按键" : "手动参考已载入 · 游客模式仅供查看";
            _lblStatus.ForeColor = Color.FromArgb(125, 211, 252);
        }

        private void SetWaitingState(string message)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string>(SetWaitingState), message);
                return;
            }
            string key = "waiting:" + _recognitionMode + ":" + _autoInputEnabled.ToString() + ":" + _turboModeEnabled.ToString() + ":" + _automationUnlocked.ToString() + ":" + message;
            if (_lastUiStateKey == key) return;
            _lastUiStateKey = key;
            ClearGrid();
            _lblScanDot.ForeColor = Color.FromArgb(71, 85, 105);
            _lblDetection.Text = !_automationUnlocked
                ? "等待识别  ·  游客模式  ·  仅显示提示"
                : "等待识别  ·  自动" + (_autoInputEnabled ? "开启" : "关闭") + "  ·  " + (_turboModeEnabled ? "极速模式" : "稳定模式");
            _lblDetection.ForeColor = Color.FromArgb(148, 163, 184);
            _lblTargetTitle.Text = "尚未稳定识别";
            _lblMnemonic.Text = "识别成功后显示指纹特征";
            _lblWasd.Text = "等待稳定识别...";
            _lblStatus.Text = message;
            _lblStatus.ForeColor = Color.FromArgb(148, 163, 184);
            _solverMode = "none";
            _cayoResult = null;
            _voltageResult = null;
            if (_recognitionMode != ModeCasinoKeypad) _keypadResult = null;
        }

        private void ClearGrid()
        {
            for (int i = 0; i < 8; i++)
            {
                if (_gridTiles[i] == null || _gridBadges[i] == null) continue;
                _gridTiles[i].BackColor = Color.FromArgb(30, 41, 59);
                _gridBadges[i].ForeColor = Color.FromArgb(71, 85, 105);
                _gridBadges[i].Text = string.Format("[{0}]", i + 1);
                _gridBadges[i].Font = new Font("Consolas", 9f, FontStyle.Bold);
            }
        }

        public void SetTarget(FingerprintTarget t, List<int> slots, bool isAuto)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<FingerprintTarget, List<int>, bool>(SetTarget), t, slots, isAuto);
                return;
            }

            _current = t;
            _activeSlots = slots;

            _lblTargetTitle.Text = t.Title;
            _lblMnemonic.Text = "💡 " + t.Mnemonic;

            for (int i = 0; i < 4; i++)
            {
                if (i < t.Slices.Count && t.Slices[i].Image != null)
                {
                    _slicePics[i].Image = t.Slices[i].Image;
                    _sliceLabels[i].Text = t.Slices[i].Name;
                }
            }

            for (int i = 0; i < 8; i++)
            {
                int sNum = i + 1;
                if (slots.Contains(sNum))
                {
                    _gridTiles[i].BackColor = Color.FromArgb(0, 255, 136);
                    _gridBadges[i].ForeColor = Color.FromArgb(4, 6, 9);
                    _gridBadges[i].Text = string.Format("✓ [{0}]", sNum);
                    _gridBadges[i].Font = new Font("Consolas", 8.5f, FontStyle.Bold);
                }
                else
                {
                    _gridTiles[i].BackColor = Color.FromArgb(30, 41, 59);
                    _gridBadges[i].ForeColor = Color.FromArgb(71, 85, 105);
                    _gridBadges[i].Text = string.Format("[{0}]", sNum);
                    _gridBadges[i].Font = new Font("Consolas", 8f, FontStyle.Regular);
                }
            }

            _lblWasd.Text = ComputeWasd(slots, _casinoCursorSlot);

            for (int i = 0; i < 4; i++)
            {
                if (i == t.Id - 1)
                {
                    _btnTabs[i].BackColor = Color.FromArgb(0, 240, 255);
                    _btnTabs[i].ForeColor = Color.FromArgb(4, 6, 9);
                }
                else
                {
                    _btnTabs[i].BackColor = Color.FromArgb(30, 41, 59);
                    _btnTabs[i].ForeColor = Color.FromArgb(56, 189, 248);
                }
            }

            if (isAuto)
            {
                string sStr = string.Join(", ", slots.ConvertAll(s => s.ToString()).ToArray());
                _lblStatus.Text = string.Format("✓ 实时锁定: {0} | 动态命中正确格子: [ {1} ]", t.Title, sStr);
                _lblStatus.ForeColor = Color.FromArgb(0, 240, 255);
            }
        }

        private string ComputeWasd(List<int> slots, int startSlot)
        {
            if (slots == null || slots.Count == 0) return "等待选择...";

            List<int> route = BuildShortestSlotOrder(slots, startSlot > 0 ? startSlot : 1);
            int currentSlot = startSlot > 0 ? startSlot : 1;
            List<string> steps = new List<string>();

            foreach (int s in route)
            {
                int curR = (currentSlot - 1) / 2;
                int curC = (currentSlot - 1) % 2;
                int tr = (s - 1) / 2;
                int tc = (s - 1) % 2;
                List<string> moves = new List<string>();

                if (tr > curR) { for (int k = 0; k < tr - curR; k++) moves.Add("S"); }
                else if (tr < curR) { for (int k = 0; k < curR - tr; k++) moves.Add("W"); }

                if (tc > curC) { for (int k = 0; k < tc - curC; k++) moves.Add("D"); }
                else if (tc < curC) { for (int k = 0; k < curC - tc; k++) moves.Add("A"); }

                if (moves.Count > 0)
                {
                    steps.Add(string.Join("+", moves.ToArray()) + "+回车");
                }
                else
                {
                    steps.Add("回车");
                }

                currentSlot = s;
            }

            steps.Add("Tab检验");
            return string.Join(" ➔ ", steps.ToArray());
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _isScanning = false;
            _autoInputEnabled = false;
            try { if (_diagnosticsTimer != null) _diagnosticsTimer.Stop(); } catch { }
            try { if (_presenceTimer != null) _presenceTimer.Stop(); } catch { }
            try { if (_recognitionMenu != null) _recognitionMenu.Dispose(); } catch { }
            try { if (_accountMenu != null) _accountMenu.Dispose(); } catch { }
            try { if (_toolsMenu != null) _toolsMenu.Dispose(); } catch { }
            try
            {
                UnregisterHotKey(this.Handle, HOTKEY_TOGGLE_AUTO_INPUT);
                UnregisterHotKey(this.Handle, HOTKEY_TOGGLE_SPEED_MODE);
            }
            catch { }
            try
            {
                if (_scanThread != null && _scanThread.IsAlive) _scanThread.Join(300);
                if (_inputThread != null && _inputThread.IsAlive) _inputThread.Join(300);
            }
            catch (Exception ex) { AppLog.WriteThrottled("Shutdown", ex); }
            base.OnFormClosing(e);
        }
    }

    static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        private static void RestoreExistingWindow()
        {
            try
            {
                Process current = Process.GetCurrentProcess();
                foreach (Process process in Process.GetProcessesByName(current.ProcessName))
                {
                    try
                    {
                        if (process.Id != current.Id && process.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindowAsync(process.MainWindowHandle, SW_RESTORE);
                            return;
                        }
                    }
                    finally { process.Dispose(); }
                }
            }
            catch (Exception ex) { AppLog.WriteThrottled("RestoreExistingWindow", ex); }
        }

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                bool simulatorMode = args != null && Array.Exists(args, value => String.Equals(value, "--simulator", StringComparison.OrdinalIgnoreCase));
                bool ownsInstance;
                string instanceName = simulatorMode
                    ? @"Local\LSF.MingzuanFingerprintAssistant.Simulator"
                    : @"Local\LSF.MingzuanFingerprintAssistant";
                using (Mutex instanceMutex = new Mutex(true, instanceName, out ownsInstance))
                {
                    if (!ownsInstance)
                    {
                        RestoreExistingWindow();
                        return;
                    }

                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    LicenseClient.AccountProfile localProfile;
                    bool accountAccess = simulatorMode || LicenseClient.TryLoadLocalAccess(out localProfile);
                    Application.Run(new MainForm(simulatorMode, accountAccess));
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("Unhandled", ex);
            }
        }
    }
}
