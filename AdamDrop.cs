// AdamDrop - nhan anh/video tu iPhone qua Wi-Fi, co bang dieu khien va lich su chia se.
// Bien dich bang csc.exe co san cua .NET Framework 4 (C# 5) - xem install.bat
// Toan bo trang web duoc nhung trong exe (/resource) -> chi con 1 tep de chay.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AdamDrop
{
    // ------------------------------------------------------------------ Config
    static class Config
    {
        public static int Port = 8765;
        public static string Key = "";
        public static string SaveDir = "";
        public static bool OpenFolder = true;
        public static bool KeepAwake = true;
        public static string Lang = "en";      // "en" = English (mac dinh), "vi" = Tiếng Việt
        // Ten may hien tren dien thoai (rong = ten may that + .local). Dat khi ten may that kho doc
        // hoac khi mang khong phan giai duoc .local. Chi cho ky tu an toan trong dia chi web.
        public static string HostOverride = "";

        public static string BaseDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string IniPath { get { return Path.Combine(BaseDir, "adamdrop.ini"); } }
        public static string LogPath { get { return Path.Combine(BaseDir, "adamdrop.log"); } }
        public static string HistoryPath { get { return Path.Combine(BaseDir, "history.tsv"); } }
        public static string RecentPath { get { return Path.Combine(BaseDir, "recent.tsv"); } }

        // Tep phim tat dung san (chua ma khoa) chi co khi ban build kem tep do. Ban phat hanh
        // cong khai KHONG kem (ly do rieng tu) nen giao dien phai AN cac nut do thay vi de 404.
        public static bool HasResource(string name)
        {
            try { return System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceInfo(name) != null; }
            catch { return false; }
        }
        public static bool ShortcutFile { get { return HasResource("web.AdamDrop.shortcut"); } }
        public static bool AutoFile { get { return HasResource("web.AdamDrop.auto.shortcut"); } }

        public static void Load()
        {
            if (File.Exists(IniPath))
            {
                foreach (string raw in File.ReadAllLines(IniPath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    int i = line.IndexOf('=');
                    if (line.StartsWith("#") || i < 1) continue;
                    string k = line.Substring(0, i).Trim().ToLowerInvariant();
                    string v = line.Substring(i + 1).Trim();
                    int n;
                    if (k == "port" && int.TryParse(v, out n) && n > 0 && n < 65536) Port = n;
                    else if (k == "key") Key = v;
                    else if (k == "savedir") SaveDir = v;
                    else if (k == "openfolder") OpenFolder = (v == "1" || v.ToLowerInvariant() == "true");
                    else if (k == "keepawake") KeepAwake = (v == "1" || v.ToLowerInvariant() == "true");
                    else if (k == "lang") Lang = (v.ToLowerInvariant().StartsWith("vi") ? "vi" : "en");
                    else if (k == "host")
                    {
                        StringBuilder hb = new StringBuilder();
                        foreach (char ch in v.Trim())
                            if (char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' || ch == '_' || ch == ':') hb.Append(ch);
                        HostOverride = hb.ToString();
                    }
                }
            }
            bool changed = false;
            if (Key.Length < 6) { Key = NewKey(); changed = true; }
            if (SaveDir.Length == 0) { SaveDir = DefaultDownloads(); changed = true; }
            if (changed) Save();
        }

        // Ghi an toan: ghi ra tep tam roi thay the, giu ban .bak -> mat dien khong mat cau hinh
        public static void Save()
        {
            try
            {
                string[] lines = new string[] {
                    "# Cau hinh AdamDrop",
                    "port=" + Port,
                    "key=" + Key,
                    "savedir=" + SaveDir,
                    "openfolder=" + (OpenFolder ? "1" : "0"),
                    "keepawake=" + (KeepAwake ? "1" : "0"),
                    "host=" + HostOverride,
                    "lang=" + (Lang == "vi" ? "vi" : "en")
                };
                string tmp = IniPath + ".tmp";
                File.WriteAllLines(tmp, lines, new UTF8Encoding(false));
                if (File.Exists(IniPath))
                {
                    try { File.Replace(tmp, IniPath, IniPath + ".bak", true); }
                    catch { File.Copy(tmp, IniPath, true); File.Delete(tmp); }
                }
                else File.Move(tmp, IniPath);
            }
            catch (Exception ex) { Log.Write("Loi luu cau hinh: " + ex.Message); }
        }

        static string NewKey()
        {
            const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
            byte[] b = new byte[10];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < b.Length; i++) sb.Append(alphabet[b[i] % alphabet.Length]);
            return sb.ToString();
        }

        public static string MaskedKey()
        {
            if (Key.Length <= 4) return "****";
            return Key.Substring(0, 4) + "******";
        }

        // Ma phien cho bang dieu khien. Tinh tu ma khoa nen KHONG doi khi app khoi dong lai:
        // the dashboard dang mo khong bi 403 sau moi lan restart. Chi doi khi doi ma khoa.
        public static string AdminToken()
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("adamdrop-admin|" + Key + "|" + Environment.MachineName));
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // --- chay cung Windows (khoa HKCU, khong can quyen quan tri)
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValueName = "AdamDrop";

        public static bool AutoStart
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                    {
                        if (k == null) return false;
                        return k.GetValue(RunValueName) != null;
                    }
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                    {
                        if (k == null) return;
                        if (value) k.SetValue(RunValueName, "\"" + Path.Combine(BaseDir, "AdamDrop.exe") + "\"");
                        else if (k.GetValue(RunValueName) != null) k.DeleteValue(RunValueName, false);
                    }
                }
                catch (Exception ex) { Log.Write("Loi dat khoi dong cung Windows: " + ex.Message); }
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);

        static string DefaultDownloads()
        {
            try
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    IntPtr p;
                    if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
                    {
                        string s = Marshal.PtrToStringUni(p);
                        Marshal.FreeCoTaskMem(p);
                        if (!string.IsNullOrEmpty(s)) return s;
                    }
                }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    // ------------------------------------------------------------------ Log
    static class Log
    {
        static readonly object sync = new object();
        const long MaxBytes = 64 * 1024;
        const int KeepLines = 200;
        public static List<string> Recent = new List<string>();

        public static void Write(string line)
        {
            try
            {
                lock (sync)
                {
                    string s = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line;
                    File.AppendAllText(Config.LogPath, s + "\r\n", new UTF8Encoding(false));
                    lock (Recent) { Recent.Add(s); if (Recent.Count > 200) Recent.RemoveAt(0); }
                    Trim();
                }
            }
            catch { }
        }

        static void Trim()
        {
            try
            {
                FileInfo fi = new FileInfo(Config.LogPath);
                if (!fi.Exists || fi.Length < MaxBytes) return;
                string[] all = File.ReadAllLines(Config.LogPath, Encoding.UTF8);
                if (all.Length <= KeepLines + 100) return;
                string[] keep = new string[KeepLines];
                Array.Copy(all, all.Length - KeepLines, keep, 0, KeepLines);
                File.WriteAllLines(Config.LogPath, keep, new UTF8Encoding(false));
                lock (Recent)
                {
                    Recent.Clear();
                    Recent.AddRange(keep);
                }
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ History (lich su chia se)
    class Session
    {
        public DateTime End;
        public double Seconds;
        public int Files;
        public long Bytes;
        public double Avg, Max, Min;
        public string Last = "";
        public string Dir = "";
    }

    static class History
    {
        static readonly object sync = new object();
        static readonly List<Session> items = new List<Session>();
        const int MaxItems = 2000;

        public static void Load()
        {
            lock (sync)
            {
                items.Clear();
                try
                {
                    if (!File.Exists(Config.HistoryPath)) return;
                    foreach (string line in File.ReadAllLines(Config.HistoryPath, Encoding.UTF8))
                    {
                        string[] p = line.Split('\t');
                        if (p.Length < 7) continue;
                        Session s = new Session();
                        long ticks;
                        if (!long.TryParse(p[0], out ticks)) continue;
                        s.End = new DateTime(ticks, DateTimeKind.Local);
                        s.Seconds = Num(p[1]); s.Files = (int)Num(p[2]); s.Bytes = (long)Num(p[3]);
                        s.Avg = Num(p[4]); s.Max = Num(p[5]); s.Min = Num(p[6]);
                        s.Last = p.Length > 7 ? p[7] : "";
                        s.Dir = p.Length > 8 ? p[8] : "";
                        items.Add(s);
                    }
                }
                catch (Exception ex) { Log.Write("Loi doc lich su: " + ex.Message); }
            }
        }

        static double Num(string s)
        {
            double d;
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
            return d;
        }

        public static void Add(Session s)
        {
            lock (sync)
            {
                items.Add(s);
                if (items.Count > MaxItems) items.RemoveRange(0, items.Count - MaxItems);
                try
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (Session x in items)
                        sb.Append(x.End.Ticks).Append('\t')
                          .Append(x.Seconds.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(x.Files).Append('\t')
                          .Append(x.Bytes).Append('\t')
                          .Append(x.Avg.ToString("0.#", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(x.Max.ToString("0.#", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(x.Min.ToString("0.#", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(x.Last.Replace('\t', ' ').Replace("\r", " ").Replace("\n", " "))
                          .Append('\t')
                          .Append(x.Dir.Replace('\t', ' ').Replace("\r", " ").Replace("\n", " "))
                          .Append("\r\n");
                    File.WriteAllText(Config.HistoryPath, sb.ToString(), new UTF8Encoding(false));
                }
                catch (Exception ex) { Log.Write("Loi ghi lich su: " + ex.Message); }
            }
        }

        public static List<Session> Snapshot(int days)
        {
            lock (sync)
            {
                DateTime from = DateTime.Now.AddDays(-days);
                List<Session> r = new List<Session>();
                foreach (Session s in items) if (days <= 0 || s.End >= from) r.Add(s);
                return r;
            }
        }

        public static Session Last()
        {
            lock (sync) { return items.Count == 0 ? null : items[items.Count - 1]; }
        }

        // thong ke: hom nay / 7 ngay / tong
        public static void Totals(out int fToday, out long bToday, out int fWeek, out long bWeek, out int fAll, out long bAll)
        {
            fToday = 0; bToday = 0; fWeek = 0; bWeek = 0; fAll = 0; bAll = 0;
            DateTime today = DateTime.Today;
            DateTime week = today.AddDays(-6);
            lock (sync)
            {
                foreach (Session s in items)
                {
                    fAll += s.Files; bAll += s.Bytes;
                    if (s.End >= week) { fWeek += s.Files; bWeek += s.Bytes; }
                    if (s.End >= today) { fToday += s.Files; bToday += s.Bytes; }
                }
            }
        }
    }

    // ------------------------------------------------------------------ Ui hooks (do TrayApp gan vao)
    static class Ui
    {
        public static Func<string[]> PickFiles;
        public static Func<string> PickFolder;          // hien hop thoai chon thu muc, tra ve duong dan hoac null
        public static Action<string, bool> OpenPath;    // mo thu muc / tep trong Explorer
        public static Action<string, string> Balloon;   // bong bong thong bao
        public static Action ConfigChanged;             // bao tray cap nhat lai menu
        public static Action LangChanged;               // bao tray doi ngon ngu menu
    }

    // ------------------------------------------------------------------ dua hop thoai cua chinh app len truoc
    // Hop thoai chon tep la cua so rieng cua tien trinh nay; khi trinh duyet dang o tien canh,
    // Windows co the mo no o PHIA SAU va nguoi dung tuong nut khong chay. Phai ep len truoc.
    static class Win
    {
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint attach, uint attachTo, bool fAttach);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        delegate bool EnumProc(IntPtr h, IntPtr p);
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2;
        const int SW_RESTORE = 9;

        static IntPtr Find()
        {
            IntPtr found = IntPtr.Zero;
            uint me = (uint)Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr h, IntPtr p)
            {
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid != me) return true;
                StringBuilder cls = new StringBuilder(64);
                GetClassNameW(h, cls, 64);
                if (cls.ToString() != "#32770") return true;
                found = h;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        // Tra ve true neu da dua duoc hop thoai len tren cung (khong the bi che khuat nua).
        public static bool FocusPicker()
        {
            IntPtr h = Find();
            if (h == IntPtr.Zero) return false;
            uint pid;
            IntPtr fg = GetForegroundWindow();
            uint fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out pid);
            uint myThread = GetCurrentThreadId();
            // Windows chan tien trinh nen tu doi cua so tien canh, tru khi cung hang doi nhap
            // voi cua so dang o tien canh -> noi tam thoi roi tra lai.
            bool attached = fgThread != 0 && fgThread != myThread && AttachThreadInput(fgThread, myThread, true);
            try
            {
                ShowWindow(h, SW_RESTORE);
                BringWindowToTop(h);
                SetForegroundWindow(h);
                // Giu luon tren cung trong luc hop thoai mo: khong the tut ra sau trinh duyet.
                SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            }
            finally { if (attached) AttachThreadInput(fgThread, myThread, false); }
            return GetForegroundWindow() == h;
        }

        // Doi hop thoai xuat hien roi dua len truoc (chay o luong nen, khong chan gi).
        public static void FocusPickerSoon()
        {
            Thread t = new Thread(delegate()
            {
                for (int i = 0; i < 40; i++)
                {
                    if (FocusPicker()) return;
                    Thread.Sleep(120);
                }
            });
            t.IsBackground = true;
            t.Start();
        }
    }

    // ------------------------------------------------------------------ Upload dang chay
    class ActiveUpload
    {
        public string Id = "";
        public string Name = "";
        public string Ip = "";
        public long Total;
        public long Done;
        public double Speed;
        public DateTime Start = DateTime.Now;
        public volatile bool Cancel;
    }

    // ------------------------------------------------------------------ Ngon ngu
    // Toan bo chu hien ra ngoai giao dien Windows (menu khay, bong bong, hop thoai).
    // Mac dinh tieng Anh; doi bang bang dieu khien hoac nut EN/VI.
    static class L
    {
        public static bool En { get { return !(Config.Lang == "vi"); } }

        // Ngon ngu cua lan mo hop thoai chon tep gan nhat, do bang dieu khien gui kem.
        // De hop thoai Windows khop voi ngon ngu nguoi dung dang nhin (the dashboard co the
        // dang la tieng Viet trong khi cai dat cua app la tieng Anh).
        public static bool DialogVi = false;

        public static string T(string key)
        {
            Dictionary<string, string> d = En ? EN : VI;
            string s;
            if (d.TryGetValue(key, out s)) return s;
            if (EN.TryGetValue(key, out s)) return s;
            return key;
        }

        public static string T(string key, params object[] a)
        {
            string s = T(key);
            try { return string.Format(s, a); } catch { return s; }
        }

        static readonly Dictionary<string, string> EN = new Dictionary<string, string>
        {
            { "tray.dash", "Open dashboard" },
            { "tray.folder", "Open received folder" },
            { "tray.pick", "Change receive folder..." },
            { "tray.awake", "Keep PC awake while receiving" },
            { "tray.autorun", "Start with Windows" },
            { "tray.autoopen", "Open folder when a batch is done" },
            { "tray.log", "View log (when something fails)" },
            { "tray.exit", "Exit" },
            { "tray.tip", "AdamDrop - receive photos and videos from iPhone" },
            { "bal.received.text", "{0} file(s) · {1}\nSaved to: {2}" },
            { "bal.recovered.text", "Connection recovered on port {0}." },
            { "bal.nospace.title", "Disk is full" },
            { "bal.nospace.text", "Not enough free space on this PC to receive {0}." },
            { "bal.writefail.title", "Could not save the file" },
            { "bal.stopped.title", "Transfer did not finish" },
            { "bal.stopped.text", "Connection dropped after {0} of {1} bytes of {2}. Please send this file again." },
            { "msg.port.text", "Could not open port {0}.\n\nRun install.bat again and choose Yes when Windows asks for administrator rights.\n\nDetails: see adamdrop.log" }
        };

        static readonly Dictionary<string, string> VI = new Dictionary<string, string>
        {
            { "tray.dash", "Mở bảng điều khiển" },
            { "tray.folder", "Mở thư mục nhận tệp" },
            { "tray.pick", "Đổi thư mục nhận tệp..." },
            { "tray.awake", "Ngăn máy ngủ khi đang nhận" },
            { "tray.autorun", "Chạy cùng Windows" },
            { "tray.autoopen", "Tự mở thư mục khi nhận xong" },
            { "tray.log", "Xem nhật ký (khi có lỗi)" },
            { "tray.exit", "Thoát" },
            { "tray.tip", "AdamDrop - nhận ảnh/video từ iPhone" },
            { "bal.received.text", "Đã nhận {0} tệp ({1})\nLưu tại: {2}" },
            { "bal.recovered.text", "Đã tự phục hồi kết nối ở cổng {0}." },
            { "bal.nospace.title", "Ổ đĩa hết chỗ trống" },
            { "bal.nospace.text", "Không đủ dung lượng để nhận {0}." },
            { "bal.writefail.title", "Không lưu được tệp" },
            { "bal.stopped.title", "Tệp gửi chưa xong" },
            { "bal.stopped.text", "Kết nối bị ngắt giữa chừng: máy tính mới nhận được {0} trên {1} byte của {2}. Hãy gửi lại tệp này." },
            { "msg.port.text", "Không mở được cổng {0}.\n\nHãy chạy lại tệp install.bat (chọn Yes khi Windows hỏi quyền quản trị).\n\nChi tiết: xem tệp adamdrop.log" }
        };
    }

    // ------------------------------------------------------------------ Server
    class ReceivedFile
    {
        public string Name;
        public long Size;
        public DateTime Time;
    }

    class Server
    {
        // Duoi tep tam: nhan dien de don dep sau khi tat may / treo may dot ngot
        public const string PartExt = ".adamdrop.part";

        volatile HttpListener listener;
        volatile bool stopping;
        readonly object nameLock = new object();
        static readonly object recentLock = new object();
        static readonly List<ReceivedFile> recent = new List<ReceivedFile>();

        readonly object upLock = new object();
        readonly Dictionary<string, ActiveUpload> uploads = new Dictionary<string, ActiveUpload>();
        int upSeq = 0;

        public event Action<int, long, string> BatchDone;    // (so tep, tong byte, duong dan tep cuoi)
        public event Action<string, string> Problem;         // (tieu de, noi dung) -> bong bong canh bao
        string lastPath = "";
        readonly object batchLock = new object();
        int batchCount = 0;
        long batchBytes = 0;
        double batchSeconds = 0, batchMax = 0, batchMin = 0;
        int activeUploads = 0;
        DateTime batchStart = DateTime.MinValue, batchLast = DateTime.MinValue;
        System.Threading.Timer batchTimer;

        long requests = 0;
        public DateTime StartedAt = DateTime.Now;
        volatile string netCategory = "";

        // --- Chan do ma khoa: qua 20 lan sai trong 1 phut thi tam khoa IP do
        class FailRec { public int Count; public DateTime Since; }
        readonly Dictionary<string, FailRec> failLog = new Dictionary<string, FailRec>();
        readonly object failLock = new object();
        const int FailLimit = 20;
        static readonly TimeSpan FailWindow = TimeSpan.FromMinutes(1);

        // 0 = dung ma khoa, 1 = sai lan dau, 2 = sai tiep, 3 = tam khoa IP
        int FailState(string ip, bool keyOk)
        {
            lock (failLock)
            {
                if (failLog.Count > 64) failLog.Clear();
                FailRec r;
                bool has = failLog.TryGetValue(ip, out r);
                if (keyOk)
                {
                    if (has) failLog.Remove(ip);
                    return 0;
                }
                if (!has || DateTime.Now - r.Since > FailWindow)
                {
                    r = new FailRec();
                    r.Since = DateTime.Now;
                    failLog[ip] = r;
                }
                r.Count++;
                if (r.Count > FailLimit) return 3;
                return r.Count == 1 ? 1 : 2;
            }
        }

        void Notify(string title, string text)
        {
            Action<string, string> h = Problem;
            if (h != null) h(title, text);
        }

        // ---------------- dot nhan (gom cac tep lien tiep)
        void UploadStarted()
        {
            lock (batchLock)
            {
                activeUploads++;
                if (activeUploads == 1) KeepSystemAwake(true);
                if (batchStart == DateTime.MinValue) batchStart = DateTime.Now;
                if (batchTimer != null) batchTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        void UploadEnded(bool ok, long size, double seconds, double maxSp, double minSp)
        {
            lock (batchLock)
            {
                activeUploads--;
                if (activeUploads <= 0) KeepSystemAwake(false);
                if (ok)
                {
                    batchCount++;
                    batchBytes += size;
                    batchSeconds += seconds;
                    if (maxSp > batchMax) batchMax = maxSp;
                    if (minSp > 0 && (batchMin == 0 || minSp < batchMin)) batchMin = minSp;
                    batchLast = DateTime.Now;
                }
                if (activeUploads <= 0 && batchCount > 0)
                {
                    if (batchTimer == null) batchTimer = new System.Threading.Timer(delegate(object o) { FlushBatch(); }, null, 3000, Timeout.Infinite);
                    else batchTimer.Change(3000, Timeout.Infinite);
                }
            }
        }

        void FlushBatch()
        {
            int n;
            long bytes;
            string last;
            double secs, maxSp, minSp;
            DateTime start, end;
            lock (batchLock)
            {
                if (activeUploads > 0 || batchCount == 0) return;
                n = batchCount; batchCount = 0;
                bytes = batchBytes; batchBytes = 0;
                secs = batchSeconds; batchSeconds = 0;
                maxSp = batchMax; batchMax = 0;
                minSp = batchMin; batchMin = 0;
                start = batchStart; batchStart = DateTime.MinValue;
                end = batchLast; batchLast = DateTime.MinValue;
                last = lastPath;
                if (batchTimer != null) batchTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            if (secs <= 0) secs = Math.Max(0.2, (end - start).TotalSeconds);
            double avg = bytes / secs;
            // giu cho so lieu luon hop ly: thap nhat <= trung binh <= cao nhat
            if (maxSp < avg) maxSp = avg;
            if (minSp <= 0 || minSp > avg) minSp = avg;
            Session s = new Session();
            s.End = DateTime.Now;
            s.Files = n;
            s.Bytes = bytes;
            s.Seconds = secs;
            s.Avg = avg;
            s.Max = maxSp;
            s.Min = minSp;
            s.Last = Path.GetFileName(last);
            if (!string.IsNullOrEmpty(last)) { try { s.Dir = Path.GetDirectoryName(last); } catch { } }
            History.Add(s);
            Log.Write("Dot nhan: " + n + " tep, " + bytes + " byte, " + FormatSpeed(avg) + " (cao nhat " + FormatSpeed(maxSp) + ")");
            Action<int, long, string> h = BatchDone;
            if (h != null) h(n, bytes, last);
        }

        public static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0) return "0";
            if (bytesPerSecond < 1024) return bytesPerSecond.ToString("0") + " B/s";
            if (bytesPerSecond < 1048576) return (bytesPerSecond / 1024).ToString("0.#") + " KB/s";
            return (bytesPerSecond / 1048576).ToString("0.##") + " MB/s";
        }

        // ---------------- giu may khong ngu khi dang nhan
        [DllImport("kernel32.dll")]
        static extern uint SetThreadExecutionState(uint esFlags);
        const uint ES_CONTINUOUS = 0x80000000;
        const uint ES_SYSTEM_REQUIRED = 0x00000001;

        void KeepSystemAwake(bool on)
        {
            if (!Config.KeepAwake) return;
            try { SetThreadExecutionState(on ? (ES_CONTINUOUS | ES_SYSTEM_REQUIRED) : ES_CONTINUOUS); }
            catch { }
        }

        // ---------------- mo cong
        public bool Start()
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    HttpListener nl = new HttpListener();
                    nl.Prefixes.Add("http://+:" + Config.Port + "/");
                    nl.Start();
                    listener = nl;
                    Thread t = new Thread(AcceptLoop);
                    t.IsBackground = true;
                    t.Start();
                    Log.Write("Da mo cong " + Config.Port + " (lan thu " + attempt + ")");
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Write("Khong mo duoc cong " + Config.Port + " (lan " + attempt + "): " + ex.Message);
                    try { if (listener != null) listener.Close(); } catch { }
                    listener = null;
                    if (attempt < 3) Thread.Sleep(attempt == 1 ? 2000 : 5000);
                }
            }
            return false;
        }

        public void Stop()
        {
            stopping = true;
            try { listener.Close(); } catch { }
        }

        void AcceptLoop()
        {
            int wait = 5;
            while (!stopping)
            {
                try
                {
                    HttpListener l = listener;
                    HttpListenerContext ctx = l.GetContext();
                    wait = 5;
                    ThreadPool.QueueUserWorkItem(delegate(object o) { HandleSafe((HttpListenerContext)o); }, ctx);
                    continue;
                }
                catch (Exception ex)
                {
                    if (stopping) return;
                    Log.Write("Vong nhan dung: " + ex.Message);
                }
                bool ok = false;
                for (int i = 0; i < 10 && !stopping && !ok; i++)
                {
                    Thread.Sleep(wait * 1000);
                    if (wait < 60) wait *= 2;
                    ok = Reopen();
                }
                if (ok)
                {
                    Log.Write("Da tu phuc hoi, cong " + Config.Port + " hoat dong lai.");
                    Notify("AdamDrop", L.T("bal.recovered.text", Config.Port));
                }
                else if (!stopping) Log.Write("Bo thu mo lai cong sau nhieu lan that bai.");
            }
        }

        bool Reopen()
        {
            try { if (listener != null) listener.Close(); } catch { }
            try
            {
                HttpListener nl = new HttpListener();
                nl.Prefixes.Add("http://+:" + Config.Port + "/");
                nl.Start();
                listener = nl;
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("Thu mo lai cong that bai: " + ex.Message);
                listener = null;
                return false;
            }
        }

        // ---------------- danh sach tep vua nhan (luu ben qua cac lan chay)
        public static void LoadRecent()
        {
            lock (recentLock)
            {
                recent.Clear();
                try
                {
                    if (!File.Exists(Config.RecentPath)) return;
                    foreach (string line in File.ReadAllLines(Config.RecentPath, Encoding.UTF8))
                    {
                        string[] p = line.Split('\t');
                        if (p.Length < 3) continue;
                        long ticks, size;
                        if (!long.TryParse(p[0], out ticks) || !long.TryParse(p[1], out size)) continue;
                        ReceivedFile r = new ReceivedFile();
                        r.Time = new DateTime(ticks, DateTimeKind.Local);
                        r.Size = size;
                        r.Name = p[2];
                        recent.Add(r);
                        if (recent.Count >= 30) break;
                    }
                }
                catch (Exception ex) { Log.Write("Loi doc danh sach tep vua nhan: " + ex.Message); }
            }
        }

        static void SaveRecent()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                lock (recentLock)
                {
                    foreach (ReceivedFile r in recent)
                        sb.Append(r.Time.Ticks).Append('\t').Append(r.Size).Append('\t')
                          .Append(r.Name.Replace('\t', ' ').Replace("\r", " ").Replace("\n", " ")).Append("\r\n");
                }
                File.WriteAllText(Config.RecentPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Write("Loi ghi danh sach tep vua nhan: " + ex.Message); }
        }

        // ---------------- don tep tam
        public static void CleanParts()
        {
            try
            {
                if (!Directory.Exists(Config.SaveDir)) return;
                int n = 0;
                foreach (string p in Directory.GetFiles(Config.SaveDir, "*" + PartExt))
                {
                    try
                    {
                        // tep do giu lai 24 gio de con gui tiep
                        if (DateTime.UtcNow - File.GetLastWriteTimeUtc(p) > TimeSpan.FromHours(24)) { File.Delete(p); n++; }
                    }
                    catch { }
                }
                if (n > 0) Log.Write("Da don " + n + " tep tam " + PartExt);
            }
            catch { }
        }

        void HandleSafe(HttpListenerContext ctx)
        {
            try { Handle(ctx); }
            catch (Exception)
            {
                try { ctx.Response.Abort(); } catch { }
            }
        }

        static Dictionary<string, string> ParseQuery(string rawUrl, out string path)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            int q = rawUrl.IndexOf('?');
            path = q < 0 ? rawUrl : rawUrl.Substring(0, q);
            if (q < 0) return d;
            foreach (string part in rawUrl.Substring(q + 1).Split('&'))
            {
                if (part.Length == 0) continue;
                int e = part.IndexOf('=');
                string k = e < 0 ? part : part.Substring(0, e);
                string v = e < 0 ? "" : part.Substring(e + 1);
                try { d[Uri.UnescapeDataString(k)] = Uri.UnescapeDataString(v.Replace("+", "%20")); }
                catch { }
            }
            return d;
        }

        static string Get(Dictionary<string, string> q, string k)
        {
            string v;
            return q.TryGetValue(k, out v) ? v : "";
        }

        static long ParseLong(Dictionary<string, string> q, string k)
        {
            long v;
            long.TryParse(Get(q, k), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
            return v;
        }

        static bool Flag(Dictionary<string, string> q, string k, bool def)
        {
            string v = Get(q, k);
            if (v.Length == 0) return def;
            return v == "1" || v.ToLowerInvariant() == "true";
        }

        static bool SameKey(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        void Handle(HttpListenerContext ctx)
        {
            Interlocked.Increment(ref requests);
            string path;
            Dictionary<string, string> q = ParseQuery(ctx.Request.RawUrl, out path);
            string method = ctx.Request.HttpMethod;
            string key = Get(q, "k");
            bool keyOk = SameKey(key, Config.Key);
            ctx.Response.Headers["Cache-Control"] = "no-store";

            if (path == "/ping")
            {
                ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
                SendText(ctx, 200, "text/plain", "ok", false);
                return;
            }

            // --- Anh huong dan (bang quan tri xem tu may nay, trang gui xem tu dien thoai)
            if (path.StartsWith("/guide/") && path.EndsWith(".png"))
            {
                if (!ctx.Request.IsLocal && !keyOk) { SendText(ctx, 403, "text/plain", "Forbidden", true); return; }
                string fn = path.Substring(7);
                bool fnOk = fn.Length >= 5 && fn.Length <= 64 && fn.IndexOf("..") < 0;
                if (fnOk)
                {
                    for (int i = 0; i < fn.Length; i++)
                    {
                        char c = fn[i];
                        bool okc = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.';
                        if (!okc) { fnOk = false; break; }
                    }
                }
                if (!fnOk) { SendText(ctx, 404, "text/plain", "Not found", true); return; }
                SendBinary(ctx, "guide." + fn, "image/png");
                return;
            }

            // --- Trang quan tri + tai nguyen rieng cua may tinh: chi truy cap duoc tu chinh may nay
            if (path == "/dashboard" || path == "/qrcode.js" || path.StartsWith("/api/")
                || path == "/adam-chan-dung.png" || path == "/logo.ico" || path == "/avatar.png" || path == "/logo-small.png")
            {
                if (!ctx.Request.IsLocal || !TrustedAdminHost(ctx.Request.Url.Host)) { SendText(ctx, 403, "text/plain", "Forbidden", true); return; }
                if (path == "/dashboard") { SendPage(ctx, "dashboard.html", "text/html; charset=utf-8", true); return; }
                if (path == "/qrcode.js") { SendPage(ctx, "qrcode.js", "application/javascript; charset=utf-8", false); return; }
                if (path == "/adam-chan-dung.png") { SendBinary(ctx, "adam-chan-dung.png", "image/png"); return; }
                if (path == "/logo-small.png") { SendBinary(ctx, "logo-small.png", "image/png"); return; }
                if (path == "/logo.ico") { SendBinary(ctx, "logo.ico", "image/x-icon"); return; }
                if (path == "/avatar.png") { SendBinary(ctx, "logo.png", "image/png"); return; }
                Api(ctx, path, q, method);
                return;
            }

            // --- Logo cho ca dien thoai (can dung ma khoa)
            if (path == "/logo.png" || path == "/logo-180.png")
            {
                if (!keyOk) { SendText(ctx, 403, "text/plain", "Forbidden", true); return; }
                SendBinary(ctx, path.Substring(1), "image/png");
                return;
            }

            if (path == "/AdamDrop.shortcut" || path == "/AdamDrop.auto.shortcut")
            {
                if (!keyOk) { SendText(ctx, 403, "text/plain", "Forbidden", true); return; }
                if (!Config.HasResource("web." + path.Substring(1)))
                {
                    SendText(ctx, 404, "text/plain", "Shortcut file is not part of this build. Build the shortcut by hand (see the Guide tab).", true);
                    return;
                }
                string scFile = path.Substring(1);
                try { ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + scFile + "\""; }
                catch { }
                SendBinary(ctx, scFile, "application/octet-stream");
                return;
            }

            // --- Chan do ma khoa (chi tinh voi cac duong dan can ma khoa)
            string ip = "?";
            try { if (ctx.Request.RemoteEndPoint != null) ip = ctx.Request.RemoteEndPoint.Address.ToString(); }
            catch { }
            bool keyedPath = (path == "/" || path == "/upload" || path == "/done" || path == "/resume" || path == "/shares" || path == "/download" || path == "/list");
            int failState = keyedPath ? FailState(ip, keyOk) : 0;
            if (failState == 3)
            {
                Log.Write("Tam khoa " + ip + " trong 1 phut do sai ma khoa qua nhieu lan");
                SendText(ctx, 429, "text/plain; charset=utf-8", "Qua nhieu lan sai ma khoa. Thu lai sau 1 phut.", true);
                return;
            }
            if (failState == 1) Log.Write("Sai ma khoa tu " + ip);

            if (path == "/shares" || path == "/download" || path == "/list")
            {
                if (!keyOk) { SendText(ctx, 403, "text/plain", "Forbidden", false); return; }
                if (method != "GET" && method != "HEAD") { SendText(ctx, 405, "text/plain", "GET required", false); return; }
                if (path == "/shares") SendText(ctx, 200, "application/json; charset=utf-8", SharesJson(), false);
                else if (path == "/list") SendText(ctx, 200, "text/plain; charset=utf-8", ShareListText(ctx, q), false);
                else DownloadShare(ctx, q);
                return;
            }

            // --- Trang danh cho iPhone
            if (path == "/" && method == "GET")
            {
                if (!keyOk)
                {
                    SendText(ctx, 403, "text/html; charset=utf-8",
                        "<!doctype html><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'>" +
                        "<body style='font-family:-apple-system,sans-serif;padding:40px 24px;text-align:center'>" +
                        "<h2>Li&ecirc;n k&#7871;t kh&ocirc;ng h&#7907;p l&#7879;</h2><p>H&atilde;y qu&eacute;t l&#7841;i m&atilde; QR &#273;ang hi&#7879;n tr&ecirc;n m&aacute;y t&iacute;nh.</p>", false);
                    return;
                }
                SendPage(ctx, "index.html", "text/html; charset=utf-8", true);
                return;
            }

            if (path == "/resume")
            {
                if (!keyOk) { SendText(ctx, 403, "application/json", "{\"ok\":false}", false); return; }
                string sid = Get(q, "sid");
                long got = PartLength(sid);
                SendText(ctx, 200, "application/json; charset=utf-8",
                    "{\"ok\":true,\"got\":" + got + "}", false);
                return;
            }

            if (path == "/upload" && method == "POST")
            {
                if (!keyOk) { SendText(ctx, 403, "application/json", "{\"ok\":false,\"error\":\"key\"}", false); return; }
                Upload(ctx, q, ip);
                return;
            }

            if (path == "/done" && method == "POST")
            {
                if (!keyOk) { SendText(ctx, 403, "application/json", "{\"ok\":false}", false); return; }
                SendText(ctx, 200, "application/json", "{\"ok\":true}", false);
                FlushBatch();
                return;
            }

            SendText(ctx, 404, "text/plain", "Not found", true);
        }

        static bool TrustedAdminHost(string host)
        {
            IPAddress address;
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) || host.Equals(HostName(), StringComparison.OrdinalIgnoreCase)) return true;
            if (IPAddress.TryParse(host.Trim('[', ']'), out address) && IPAddress.IsLoopback(address)) return true;
            foreach (string local in LocalAddresses()) if (host == local) return true;
            return false;
        }

        class SharedFile
        {
            public string Id, Path, Name, Mime;
            public long Size;
            public volatile bool Revoked;
        }
        readonly object shareLock = new object();
        readonly Dictionary<string, SharedFile> shares = new Dictionary<string, SharedFile>();
        readonly string adminToken = Config.AdminToken();
        int activeDownloads, pickingFiles;

        static string ShareMime(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".png": return "image/png";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                case ".heic": return "image/heic";
                case ".heif": return "image/heif";
                case ".mp4": case ".m4v": return "video/mp4";
                case ".mov": return "video/quicktime";
                case ".webm": return "video/webm";
                case ".mp3": return "audio/mpeg";
                case ".pdf": return "application/pdf";
                case ".txt": return "text/plain";
                case ".zip": return "application/zip";
                default: return "application/octet-stream";
            }
        }

        // Ten tep an toan cho header Content-Disposition (phan du phong ASCII): bo dau ngoac kep,
        // dau cham phay va ky tu ngoai ASCII de khong pha vo header.
        static string AsciiName(string name)
        {
            StringBuilder b = new StringBuilder();
            foreach (char c in name)
                b.Append((c > 32 && c < 127 && c != '"' && c != '\\' && c != ';' && c != '(' && c != ')') ? c : '_');
            string s = b.ToString().Trim('_');
            return s.Length == 0 ? "download.bin" : s;
        }

        // Only the trusted native picker calls this; no HTTP path input is accepted.
        void AddSharedFiles(string[] paths)
        {
            lock (shareLock)
            {
                foreach (string path in paths)
                {
                    try
                    {
                        FileInfo f = new FileInfo(path);
                        if (!f.Exists || (f.Attributes & FileAttributes.Directory) != 0) continue;
                        bool duplicate = false;
                        foreach (SharedFile old in shares.Values)
                            if (string.Equals(old.Path, f.FullName, StringComparison.OrdinalIgnoreCase)) duplicate = true;
                        if (duplicate) continue;
                        SharedFile item = new SharedFile();
                        item.Id = Guid.NewGuid().ToString("N"); item.Path = f.FullName;
                        item.Name = f.Name; item.Size = f.Length; item.Mime = ShareMime(f.Name);
                        shares.Add(item.Id, item);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        // Danh sach link tai, moi tep MOT dong, de Phim tat iOS chi can: Lay noi dung cua URL
        // -> Tach van ban theo dong -> Lap lai -> Luu vao Album anh. Khong co dong nao khac
        // (ke ca dong trong) vi vong lap se thu tai ca dong do.
        string ShareListText(HttpListenerContext ctx, Dictionary<string, string> q)
        {
            string host = HostOnly(ctx.Request.Headers["Host"]);
            if (host.Length == 0) host = "localhost:" + Config.Port;
            string key = Uri.EscapeDataString(Get(q, "k"));
            StringBuilder b = new StringBuilder();
            lock (shareLock)
                foreach (SharedFile f in shares.Values)
                    b.Append("http://").Append(host).Append("/download?k=").Append(key)
                     .Append("&id=").Append(f.Id).Append('\n');
            return b.ToString();
        }

        // Chi giu ky tu hop le cua host[:port], chan ca CRLF/space de khong chen duoc dong khac.
        static string HostOnly(string host)
        {
            if (string.IsNullOrEmpty(host)) return "";
            StringBuilder b = new StringBuilder();
            foreach (char c in host)
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                    || c == '.' || c == '-' || c == ':' || c == '[' || c == ']') b.Append(c);
            return b.ToString();
        }

        string SharesJson()
        {
            StringBuilder b = new StringBuilder("{\"files\":[");
            lock (shareLock)
            {
                bool first = true;
                foreach (SharedFile f in shares.Values)
                {
                    if (!first) b.Append(','); first = false;
                    b.Append("{\"id\":").Append(J(f.Id)).Append(",\"name\":").Append(J(f.Name))
                        .Append(",\"size\":").Append(f.Size).Append(",\"mime\":").Append(J(f.Mime)).Append('}');
                }
            }
            return b.Append("]}").ToString();
        }

        void ShareAdmin(HttpListenerContext ctx, string path, Dictionary<string, string> q)
        {
            Uri origin;
            string value = ctx.Request.Headers["Origin"];
            bool originOk = string.IsNullOrEmpty(value) || (Uri.TryCreate(value, UriKind.Absolute, out origin)
                && origin.GetLeftPart(UriPartial.Authority) == ctx.Request.Url.GetLeftPart(UriPartial.Authority));
            if (ctx.Request.HttpMethod != "POST") { SendText(ctx, 405, "text/plain", "POST required", false); return; }
            if (!ctx.Request.IsLocal || !originOk || ctx.Request.Headers["Sec-Fetch-Site"] == "cross-site"
                || !SameKey(ctx.Request.Headers["X-AdamDrop-Admin"], adminToken))
            { SendText(ctx, 403, "text/plain", "Forbidden", false); return; }
            if (path == "/api/share/pick")
            {
                if (Interlocked.CompareExchange(ref pickingFiles, 1, 0) != 0)
                {
                    // Hop thoai da mo (co the dang nam sau cua so khac): dua no len truoc va bao ro,
                    // thay vi im lang de nguoi dung tuong nut khong chay.
                    bool shown = Win.FocusPicker();
                    SendText(ctx, 200, "application/json",
                        "{\"ok\":true,\"alreadyOpen\":true,\"focused\":" + (shown ? "true" : "false") + "}", false);
                    return;
                }
                try
                {
                    L.DialogVi = Get(q, "lang") == "vi";
                    string[] picked = Ui.PickFiles == null ? null : Ui.PickFiles();
                    if (picked != null) AddSharedFiles(picked);
                    SendText(ctx, 200, "application/json", "{\"ok\":true,\"cancelled\":" + (picked == null ? "true" : "false") + "}", false);
                }
                finally { Interlocked.Exchange(ref pickingFiles, 0); }
                return;
            }
            lock (shareLock)
            {
                if (path == "/api/share/stop")
                { foreach (SharedFile f in shares.Values) f.Revoked = true; shares.Clear(); }
                else if (path == "/api/share/remove")
                {
                    SharedFile f;
                    if (shares.TryGetValue(Get(q, "id"), out f)) { f.Revoked = true; shares.Remove(f.Id); }
                }
                else { SendText(ctx, 404, "text/plain", "Not found", false); return; }
            }
            SendText(ctx, 200, "application/json", "{\"ok\":true}", false);
        }

        void DownloadShare(HttpListenerContext ctx, Dictionary<string, string> q)
        {
            SharedFile f;
            lock (shareLock) shares.TryGetValue(Get(q, "id"), out f);
            if (f == null || f.Revoked) { SendText(ctx, 404, "text/plain", "No longer shared", false); return; }
            FileStream input;
            try { input = new FileStream(f.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan); }
            catch (IOException) { SendText(ctx, 410, "text/plain", "File unavailable", false); return; }
            catch (UnauthorizedAccessException) { SendText(ctx, 410, "text/plain", "File unavailable", false); return; }
            using (input)
            {
                long length = input.Length, start = 0, end = length - 1;
                string range = ctx.Request.Headers["Range"];
                // Without a stable entity validator, If-Range requires a full response.
                if (!string.IsNullOrEmpty(ctx.Request.Headers["If-Range"])) range = null;
                if (!string.IsNullOrEmpty(range))
                {
                    bool valid = range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase);
                    string[] parts = valid ? range.Substring(6).Split('-') : new string[0];
                    long a = 0, z = 0;
                    valid = valid && parts.Length == 2 && length > 0;
                    if (valid && parts[0].Length == 0)
                    {
                        valid = long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out z) && z > 0;
                        start = Math.Max(0, length - z);
                    }
                    else if (valid)
                    {
                        valid = long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out a) && a < length;
                        start = a;
                        if (parts[1].Length > 0)
                        { valid = valid && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out z) && z >= a; end = Math.Min(end, z); }
                    }
                    if (!valid)
                    {
                        ctx.Response.Headers["Content-Range"] = "bytes */" + length;
                        SendText(ctx, 416, "text/plain", "Range not satisfiable", false); return;
                    }
                    ctx.Response.StatusCode = 206;
                    ctx.Response.Headers["Content-Range"] = "bytes " + start + "-" + end + "/" + length;
                }
                bool preview = Get(q, "view") == "1" && (f.Mime.StartsWith("image/") || f.Mime.StartsWith("video/"));
                ctx.Response.ContentType = f.Mime;
                ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
                ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
                ctx.Response.Headers["Accept-Ranges"] = "bytes";
                if (preview)
                {
                    // KHONG gui CSP kem `sandbox` cho ban xem truoc: WebKit (Safari tren iOS) coi CSP
                    // sandbox la ly do de TAI VE thay vi hien thi, nen bam "Luu vao Anh" lai thay tep
                    // nam trong Tep. Ban xem truoc chi mo cho image/* va video/* (da loc), nen khong
                    // con duong chay script; giu nosniff.
                    ctx.Response.Headers["Content-Disposition"] = "inline";
                }
                else
                {
                    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'none'";
                    ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + AsciiName(f.Name)
                        + "\"; filename*=UTF-8''" + Uri.EscapeDataString(f.Name);
                }
                ctx.Response.ContentLength64 = length == 0 ? 0 : end - start + 1;
                if (ctx.Request.HttpMethod == "HEAD") { ctx.Response.Close(); return; }
                Interlocked.Increment(ref activeDownloads);
                try
                {
                    input.Position = start;
                    byte[] buffer = new byte[65536]; long left = ctx.Response.ContentLength64;
                    while (left > 0)
                    {
                        if (f.Revoked) { ctx.Response.Abort(); return; }
                        int n = input.Read(buffer, 0, (int)Math.Min(left, buffer.Length));
                        if (n == 0) { ctx.Response.Abort(); return; }
                        ctx.Response.OutputStream.Write(buffer, 0, n); left -= n;
                    }
                    ctx.Response.Close();
                }
                finally { Interlocked.Decrement(ref activeDownloads); }
            }
        }

        // ---------------- API cho dashboard (chi tu may tinh)
        void Api(HttpListenerContext ctx, string path, Dictionary<string, string> q, string method)
        {
            if (path.StartsWith("/api/share/")) { ShareAdmin(ctx, path, q); return; }
            switch (path)
            {
                case "/api/state":
                    SendText(ctx, 200, "application/json; charset=utf-8", StateJson(), false);
                    return;

                case "/api/history":
                    SendText(ctx, 200, "application/json; charset=utf-8", HistoryJson((int)ParseLong(q, "days")), false);
                    return;

                case "/api/log":
                    SendText(ctx, 200, "text/plain; charset=utf-8", LogText(), false);
                    return;

                case "/api/set":
                    SendText(ctx, 200, "application/json; charset=utf-8", ApplySet(q), false);
                    return;

                case "/api/pickfolder":
                {
                    string dir = null;
                    Func<string> f = Ui.PickFolder;
                    if (f != null) { try { dir = f(); } catch (Exception ex) { Log.Write("Loi chon thu muc: " + ex.Message); } }
                    if (string.IsNullOrEmpty(dir))
                    {
                        SendText(ctx, 200, "application/json; charset=utf-8", "{\"ok\":false,\"cancelled\":true}", false);
                        return;
                    }
                    string err = SetSaveDir(dir);
                    SendText(ctx, 200, "application/json; charset=utf-8",
                        err.Length == 0 ? "{\"ok\":true,\"saveDir\":" + J(dir) + "}" : "{\"ok\":false,\"message\":" + J(err) + "}", false);
                    return;
                }

                case "/api/open":
                {
                    string name = Get(q, "name");
                    string target = name.Length > 0 ? Path.Combine(Config.SaveDir, name) : Config.SaveDir;
                    Action<string, bool> op = Ui.OpenPath;
                    bool ok = false;
                    if (name == "__log__")
                    {
                        try { Process.Start("notepad.exe", "\"" + Config.LogPath + "\""); ok = true; } catch { }
                    }
                    else if (op != null && (Directory.Exists(target) || File.Exists(target)))
                    {
                        try { op(target, name.Length > 0); ok = true; } catch { }
                    }
                    SendText(ctx, 200, "application/json; charset=utf-8", "{\"ok\":" + (ok ? "true" : "false") + "}", false);
                    return;
                }

                case "/api/cancel":
                {
                    string id = Get(q, "id");
                    bool ok = false;
                    lock (upLock)
                    {
                        ActiveUpload u;
                        if (uploads.TryGetValue(id, out u)) { u.Cancel = true; ok = true; }
                    }
                    Log.Write(ok ? "Nguoi dung huy phien nhan " + id : "Khong tim thay phien " + id);
                    SendText(ctx, 200, "application/json; charset=utf-8", "{\"ok\":" + (ok ? "true" : "false") + "}", false);
                    return;
                }

                case "/api/parts":
                {
                    // xoa tep do khi nguoi dung yeu cau
                    string clear = Get(q, "clear");
                    if (clear.Length > 0)
                    {
                        try { File.Delete(Path.Combine(Config.SaveDir, "." + clear + PartExt)); } catch { }
                    }
                    SendText(ctx, 200, "application/json; charset=utf-8", PartsJson(), false);
                    return;
                }

                default:
                    SendText(ctx, 404, "text/plain; charset=utf-8", "Không rõ API", true);
                    return;
            }
        }

        // ap dung cai dat tu dashboard
        string ApplySet(Dictionary<string, string> q)
        {
            List<string> changed = new List<string>();
            if (q.ContainsKey("saveDir"))
            {
                string err = SetSaveDir(Get(q, "saveDir"));
                if (err.Length > 0) return "{\"ok\":false,\"message\":" + J(err) + "}";
                changed.Add("thư mục lưu");
            }
            if (q.ContainsKey("openfolder")) { Config.OpenFolder = Flag(q, "openfolder", true); changed.Add("tự mở thư mục"); }
            if (q.ContainsKey("keepawake")) { Config.KeepAwake = Flag(q, "keepawake", true); changed.Add("ngăn máy ngủ"); }
            if (q.ContainsKey("autostart"))
            {
                bool on = Flag(q, "autostart", true);
                Config.AutoStart = on;
                changed.Add(on ? "chạy cùng Windows: bật" : "chạy cùng Windows: tắt");
            }
            if (q.ContainsKey("lang"))
            {
                string lg = Get(q, "lang").ToLowerInvariant();
                Config.Lang = lg.StartsWith("vi") ? "vi" : "en";
                changed.Add(Config.Lang == "vi" ? "ngôn ngữ: Tiếng Việt" : "language: English");
                Action lc = Ui.LangChanged;
                if (lc != null) { try { lc(); } catch { } }
            }
            if (Flag(q, "newkey", false))
            {
                Config.Key = "";
                Config.Load();
                changed.Add("mã khoá mới");
                Log.Write("Da doi ma khoa (ma khoa khong duoc ghi vao nhat ky)");
            }
            Config.Save();
            Action c = Ui.ConfigChanged;
            if (c != null) { try { c(); } catch { } }
            if (changed.Count == 0) return "{\"ok\":true}";
            StringBuilder sb = new StringBuilder("{\"ok\":true,\"changed\":[");
            for (int i = 0; i < changed.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(J(changed[i]));
            }
            sb.Append("]}");
            return sb.ToString();
        }

        string SetSaveDir(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return "Chưa chọn thư mục.";
                if (!Directory.Exists(dir))
                {
                    try { Directory.CreateDirectory(dir); }
                    catch { return "Không tạo được thư mục này."; }
                }
                string probe = Path.Combine(dir, ".adamdrop-writetest");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                Config.SaveDir = dir;
                Config.Save();
                CleanParts();
                Log.Write("Da doi thu muc nhan tep: " + dir);
                return "";
            }
            catch (Exception ex)
            {
                Log.Write("Loi doi thu muc nhan tep: " + ex.Message);
                return "Không ghi được vào thư mục này (" + ex.Message + ").";
            }
        }

        string StateJson()
        {
            int fT, fW, fA; long bT, bW, bA;
            History.Totals(out fT, out bT, out fW, out bW, out fA, out bA);
            Session last = History.Last();
            bool pub = netCategory == "Public";
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"pcName\":").Append(J(Environment.MachineName));
            sb.Append(",\"port\":").Append(Config.Port);
            sb.Append(",\"key\":").Append(J(Config.Key));
            sb.Append(",\"adminToken\":").Append(J(adminToken));
            sb.Append(",\"sharing\":").Append(SharesJson());
            sb.Append(",\"activeDownloads\":").Append(Interlocked.CompareExchange(ref activeDownloads, 0, 0));
            sb.Append(",\"saveDir\":").Append(J(Config.SaveDir));
            sb.Append(",\"iniPath\":").Append(J(Config.IniPath));
            sb.Append(",\"logPath\":").Append(J(Config.LogPath));
            sb.Append(",\"openFolder\":").Append(Config.OpenFolder ? "true" : "false");
            sb.Append(",\"keepAwake\":").Append(Config.KeepAwake ? "true" : "false");
            sb.Append(",\"autoStart\":").Append(Config.AutoStart ? "true" : "false");
            sb.Append(",\"lang\":").Append(J(Config.Lang == "vi" ? "vi" : "en"));
            sb.Append(",\"netCategory\":").Append(J(netCategory.Length == 0 ? "?" : netCategory));
            sb.Append(",\"netWarning\":").Append(pub ? "true" : "false");
            sb.Append(",\"listening\":").Append((listener != null && listener.IsListening) ? "true" : "false");
            sb.Append(",\"startedAt\":").Append(J(StartedAt.ToString("HH:mm")));
            sb.Append(",\"requests\":").Append(Interlocked.Read(ref requests));
            sb.Append(",\"hostUrl\":").Append(J("http://" + HostName() + ":" + Config.Port + "/?k=" + Config.Key));
            sb.Append(",\"shortcutUrl\":").Append(J("http://" + HostName() + ":" + Config.Port + "/AdamDrop.shortcut?k=" + Config.Key));
            sb.Append(",\"autoUrl\":").Append(J("http://" + HostName() + ":" + Config.Port + "/AdamDrop.auto.shortcut?k=" + Config.Key));
            sb.Append(",\"shortcutFile\":").Append(Config.ShortcutFile ? "true" : "false");
            sb.Append(",\"autoFile\":").Append(Config.AutoFile ? "true" : "false");
            sb.Append(",\"urls\":[");
            bool first = true;
            foreach (string ipAddr in LocalAddresses())
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(J("http://" + ipAddr + ":" + Config.Port + "/?k=" + Config.Key));
            }
            sb.Append("],\"stats\":{\"today\":{\"files\":").Append(fT).Append(",\"bytes\":").Append(bT)
              .Append("},\"week\":{\"files\":").Append(fW).Append(",\"bytes\":").Append(bW)
              .Append("},\"all\":{\"files\":").Append(fA).Append(",\"bytes\":").Append(bA).Append("}}");
            sb.Append(",\"last\":").Append(last == null ? "null" :
                "{\"files\":" + last.Files + ",\"bytes\":" + last.Bytes +
                ",\"seconds\":" + N(last.Seconds) + ",\"avg\":" + N(last.Avg) +
                ",\"max\":" + N(last.Max) + ",\"min\":" + N(last.Min) +
                ",\"at\":" + J(last.End.ToString("dd/MM HH:mm")) + ",\"name\":" + J(last.Last) + "}");
            sb.Append(",\"live\":[");
            lock (upLock)
            {
                first = true;
                foreach (KeyValuePair<string, ActiveUpload> kv in uploads)
                {
                    ActiveUpload u = kv.Value;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"id\":").Append(J(u.Id))
                      .Append(",\"name\":").Append(J(u.Name))
                      .Append(",\"done\":").Append(u.Done)
                      .Append(",\"total\":").Append(u.Total)
                      .Append(",\"speed\":").Append(N(u.Speed))
                      .Append(",\"ip\":").Append(J(u.Ip))
                      .Append(",\"seconds\":").Append(N((DateTime.Now - u.Start).TotalSeconds)).Append('}');
                }
            }
            sb.Append(']');
            sb.Append(",\"recent\":[");
            lock (recentLock)
            {
                for (int i = 0; i < recent.Count && i < 20; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"name\":").Append(J(recent[i].Name))
                      .Append(",\"size\":").Append(recent[i].Size)
                      .Append(",\"time\":").Append(J(recent[i].Time.ToString("HH:mm:ss"))).Append('}');
                }
            }
            sb.Append("],\"sessions\":[");
            List<Session> sl = History.Snapshot(7);
            for (int i = sl.Count - 1, k = 0; i >= 0 && k < 20; i--, k++)
            {
                Session s = sl[i];
                if (k > 0) sb.Append(',');
                sb.Append("{\"end\":").Append(J(s.End.ToString("dd/MM HH:mm")))
                  .Append(",\"files\":").Append(s.Files)
                  .Append(",\"bytes\":").Append(s.Bytes)
                  .Append(",\"seconds\":").Append(N(s.Seconds))
                  .Append(",\"avg\":").Append(N(s.Avg))
                  .Append(",\"max\":").Append(N(s.Max))
                  .Append(",\"min\":").Append(N(s.Min))
                  .Append(",\"name\":").Append(J(s.Last)).Append(",\"dir\":").Append(J(s.Dir)).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        string HistoryJson(int days)
        {
            if (days <= 0) days = 30;
            List<Session> sl = History.Snapshot(days);
            int fT, fW, fA; long bT, bW, bA;
            History.Totals(out fT, out bT, out fW, out bW, out fA, out bA);
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"days\":").Append(days);
            sb.Append(",\"stats\":{\"today\":{\"files\":").Append(fT).Append(",\"bytes\":").Append(bT)
              .Append("},\"week\":{\"files\":").Append(fW).Append(",\"bytes\":").Append(bW)
              .Append("},\"all\":{\"files\":").Append(fA).Append(",\"bytes\":").Append(bA).Append("}}");
            sb.Append(",\"sessions\":[");
            for (int i = sl.Count - 1; i >= 0; i--)
            {
                if (i != sl.Count - 1) sb.Append(',');
                Session s = sl[i];
                sb.Append("{\"end\":").Append(J(s.End.ToString("dd/MM/yyyy HH:mm:ss")))
                  .Append(",\"files\":").Append(s.Files)
                  .Append(",\"bytes\":").Append(s.Bytes)
                  .Append(",\"seconds\":").Append(N(s.Seconds))
                  .Append(",\"avg\":").Append(N(s.Avg))
                  .Append(",\"max\":").Append(N(s.Max))
                  .Append(",\"min\":").Append(N(s.Min))
                  .Append(",\"name\":").Append(J(s.Last)).Append(",\"dir\":").Append(J(s.Dir)).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        string PartsJson()
        {
            StringBuilder sb = new StringBuilder("{\"parts\":[");
            bool first = true;
            try
            {
                foreach (string p in Directory.GetFiles(Config.SaveDir, "*" + PartExt))
                {
                    long len = 0;
                    DateTime t = DateTime.Now;
                    try { len = new FileInfo(p).Length; t = File.GetLastWriteTime(p); } catch { }
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"id\":").Append(J(Path.GetFileName(p).TrimStart('.').Replace(PartExt, "")))
                      .Append(",\"bytes\":").Append(len)
                      .Append(",\"time\":").Append(J(t.ToString("dd/MM HH:mm"))).Append('}');
                }
            }
            catch { }
            sb.Append("]}");
            return sb.ToString();
        }

        string LogText()
        {
            List<string> lines;
            lock (Log.Recent) lines = new List<string>(Log.Recent);
            // Doc tu tep: nhat ky phai con sau khi khoi dong lai, khong chi hien phien dang chay
            try
            {
                if (File.Exists(Config.LogPath))
                {
                    List<string> all = new List<string>(File.ReadAllLines(Config.LogPath, Encoding.UTF8));
                    if (all.Count > 0)
                    {
                        lines = all;
                        if (lines.Count > 200) lines.RemoveRange(0, lines.Count - 200);
                    }
                }
            }
            catch { }
            StringBuilder sb = new StringBuilder();
            foreach (string l in lines) sb.Append(l).Append('\n');
            return sb.ToString();
        }

        // ---------------- doc tep trong multipart/form-data (bang chia se iOS Shortcuts, curl -F)
        static string BoundaryOf(string ct)
        {
            if (ct == null) return "";
            int i = ct.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            string b = ct.Substring(i + 9).Trim();
            if (b.Length > 0 && b[0] == '"')
            {
                int e = b.IndexOf('"', 1);
                return e > 1 ? b.Substring(1, e - 1) : b.Trim('"');
            }
            int sem = b.IndexOf(';');
            return sem >= 0 ? b.Substring(0, sem).Trim() : b;
        }

        static string ReadHeadLine(Stream s)
        {
            StringBuilder sb = new StringBuilder();
            int prev = -1;
            while (true)
            {
                int c = s.ReadByte();
                if (c < 0) break;
                if (prev == '\r' && c == '\n') { if (sb.Length > 0) sb.Length = sb.Length - 1; break; }
                sb.Append((char)c);
                prev = c;
                if (sb.Length > 8192) break;
            }
            return sb.ToString();
        }

        // lay ten tep trong multipart; bo qua cac o nhap chu (phan khong co ten tep)
        static string MultipartFilename(PartStream r, string boundary, out PartStream part)
        {
            part = null;
            string first = r.Line();
            if (first.IndexOf(boundary, StringComparison.Ordinal) < 0) return "";
            byte[] delim = Encoding.ASCII.GetBytes("\r\n--" + boundary);
            for (int lap = 0; lap < 40; lap++)
            {
                string name = "";
                while (true)
                {
                    string h = r.Line();
                    if (h.Length == 0) break;
                    if (h.StartsWith("--" + boundary)) return "";
                    if (h.IndexOf("Content-Disposition", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        int i = h.IndexOf("filename=\"", StringComparison.OrdinalIgnoreCase);
                        if (i >= 0)
                        {
                            int st = i + 10;
                            int en = h.IndexOf('"', st);
                            if (en > st) name = h.Substring(st, en - st);
                        }
                    }
                }
                r.DataDelimiter(delim);
                if (name.Length > 0) { part = r; return name; }
                if (!r.SkipRest()) return "";
                string sep = r.Line();          // dong ket thuc cua duong bao
                if (sep.Length > 0) return "";
            }
            return "";
        }

        // luong doc multipart: giu MOT bo dem duy nhat nen doc dong va doc du lieu khong bi lech
        sealed class PartStream : Stream
        {
            Stream src; byte[] delim; byte[] buf; int pos; int len; bool fin; bool eof; bool saw;

            public bool SawBoundary { get { return saw; } }

            public PartStream(Stream src)
            {
                this.src = src;
                buf = new byte[1 << 16];
            }

            // doc mot dong (dung khi ket thuc dong), khong doc qua dong
            public string Line()
            {
                StringBuilder sb = new StringBuilder();
                while (true)
                {
                    if (pos >= len)
                    {
                        pos = 0; len = 0;
                        int r = src.Read(buf, 0, buf.Length);
                        if (r <= 0) return sb.ToString();
                        len = r;
                    }
                    byte b = buf[pos++];
                    if (b == (byte)'\n')
                    {
                        if (sb.Length > 0 && sb[sb.Length - 1] == '\r') sb.Length = sb.Length - 1;
                        return sb.ToString();
                    }
                    sb.Append((char)b);
                    if (sb.Length > 8192) return sb.ToString();
                }
            }

            public void DataDelimiter(byte[] d) { delim = d; fin = false; saw = false; }

            // doc bo phan du lieu con lai cua phan hien tai (o nhap chu)
            public bool SkipRest()
            {
                byte[] tmp = new byte[1 << 16];
                while (Read(tmp, 0, tmp.Length) > 0) { }
                return saw;
            }

            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { return 0; } }
            public override long Position { get { return 0; } set { } }
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin s) { return 0; }
            public override void SetLength(long v) { }
            public override void Write(byte[] b, int o, int c) { }

            int FindAt(byte[] d, int from, int to)
            {
                if (d == null) return -1;
                for (int i = from; i + d.Length <= to; i++)
                {
                    bool ok = true;
                    for (int j = 0; j < d.Length; j++) if (buf[i + j] != d[j]) { ok = false; break; }
                    if (ok) return i;
                }
                return -1;
            }

            public override int Read(byte[] dest, int off, int count)
            {
                if (fin) return 0;
                while (true)
                {
                    int at = FindAt(delim, pos, len);
                    if (at >= 0)
                    {
                        int give = at - pos;
                        if (give > count) give = count;
                        if (give > 0)
                        {
                            Array.Copy(buf, pos, dest, off, give);
                            pos += give;
                            return give;
                        }
                        saw = true; fin = true;
                        pos = at + delim.Length;   // bo qua duong bao, giu phan con lai trong bo dem
                        return 0;
                    }
                    int keep = delim.Length - 1;
                    int avail = len - pos;
                    if (avail > keep)
                    {
                        int give = avail - keep;
                        if (give > count) give = count;
                        Array.Copy(buf, pos, dest, off, give);
                        pos += give;
                        return give;
                    }
                    if (eof)
                    {
                        if (avail <= 0) { fin = true; return 0; }
                        int give = avail > count ? count : avail;
                        Array.Copy(buf, pos, dest, off, give);
                        pos += give;
                        return give;
                    }
                    if (len == buf.Length)
                    {
                        if (pos > 0) { Buffer.BlockCopy(buf, pos, buf, 0, len - pos); len -= pos; pos = 0; }
                        else Array.Resize(ref buf, buf.Length * 2);
                    }
                    int r = src.Read(buf, len, buf.Length - len);
                    if (r <= 0) { eof = true; continue; }
                    len += r;
                }
            }
        }

        // ---------------- nhan tep
        void Upload(HttpListenerContext ctx, Dictionary<string, string> q, string ip)
        {
            UploadStarted();
            bool ok = false;
            long size = 0;
            double seconds = 0, maxSp = 0, minSp = 0;
            try
            {
                ok = UploadCore(ctx, q, ip, out size, out seconds, out maxSp, out minSp);
            }
            catch (Exception ex)
            {
                long declared = -1;
                try { declared = ctx.Request.ContentLength64; } catch { }
                Log.Write((IsClientGone(ex) ? "Ket noi bi ngat khi dang nhan tep (dien thoai bao " + declared + " byte): "
                                            : "Loi khi nhan tep: ") + ex.GetType().Name + " " + ex.Message);
            }
            finally { UploadEnded(ok, size, seconds, maxSp, minSp); }
        }

        static bool IsClientGone(Exception ex)
        {
            if (ex is HttpListenerException || ex is SocketException) return true;
            Exception inx = ex.InnerException;
            while (inx != null)
            {
                if (inx is SocketException || inx is HttpListenerException) return true;
                inx = inx.InnerException;
            }
            if (ex is IOException && ex.Message.IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static string SidHash(string sid)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(sid));
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        static string PartPathForSid(string sid)
        {
            return Path.Combine(Config.SaveDir, "." + SidHash(sid) + PartExt);
        }

        static long PartLength(string sid)
        {
            try
            {
                if (string.IsNullOrEmpty(sid)) return 0;
                string p = PartPathForSid(sid);
                return File.Exists(p) ? new FileInfo(p).Length : 0;
            }
            catch { return 0; }
        }

        static bool EnoughSpace(long need)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(Config.SaveDir));
                if (string.IsNullOrEmpty(root)) return true;
                DriveInfo d = new DriveInfo(root);
                return d.AvailableFreeSpace > need + (64L << 20);
            }
            catch { return true; }
        }

        // doc bo phan than con lai (chi dung khi con it) de Windows gui duoc phan hoi
        static void Drain(Stream input, byte[] buf)
        {
            try { while (input.Read(buf, 0, buf.Length) > 0) { } }
            catch { }
        }

        // gui phan hoi roi ngat ket noi dang do (tranh treo ket noi khi chua doc het phan than)
        static void AbortSoon(HttpListenerContext ctx)
        {
            ThreadPool.QueueUserWorkItem(delegate(object o)
            {
                Thread.Sleep(600);
                try { ((HttpListenerContext)o).Response.Abort(); } catch { }
            }, ctx);
        }

        static string Why(Exception ex)
        {
            if (ex is UnauthorizedAccessException)
                return "Máy tính không cho phép ghi vào thư mục nhận tệp. Hãy kiểm tra lại thư mục nhận (chuột phải biểu tượng khay > Đổi thư mục nhận tệp).";
            if (ex is DirectoryNotFoundException)
                return "Thư mục nhận tệp không tồn tại (ổ đĩa chưa cắm hoặc đã bị đổi tên).";
            if (ex is PathTooLongException)
                return "Đường dẫn tệp quá dài.";
            if (ex is IOException)
                return "Không ghi được tệp (ổ đĩa đã hết chỗ hoặc tệp đang bị phần mềm khác giữ): " + ex.Message;
            return ex.Message;
        }

        bool UploadCore(HttpListenerContext ctx, Dictionary<string, string> q, string ip,
                        out long outSize, out double outSeconds, out double outMax, out double outMin)
        {
            outSize = 0; outSeconds = 0; outMax = 0; outMin = 0;

            long segLen = ctx.Request.ContentLength64;   // so byte cua lan gui nay
            string sid = Get(q, "sid");                  // co sid = gui theo tung doan (ho tro gui tiep)
            long off = ParseLong(q, "off");              // vi tri bat dau cua doan
            long total = ParseLong(q, "total");          // tong kich thuoc tep
            bool seg = sid.Length > 0;

            Stream input = ctx.Request.InputStream;
            byte[] buf = new byte[1 << 18];

            // --- multipart/form-data (bang chia se iOS Shortcuts, curl -F, FormData cua trinh duyet)
            PartStream part = null;
            string nameParam = Get(q, "name");
            long whole = segLen;
            string ctype = ctx.Request.ContentType ?? "";
            if (ctype.TrimStart().ToLowerInvariant().StartsWith("multipart/form-data"))
            {
                string bnd = BoundaryOf(ctype);
                if (bnd.Length == 0)
                {
                    SendText(ctx, 400, "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"boundary\"}", true);
                    return false;
                }
                string fname = MultipartFilename(new PartStream(input), bnd, out part);
                if (part == null)
                {
                    SendText(ctx, 400, "application/json; charset=utf-8",
                        "{\"ok\":false,\"error\":\"nofile\",\"message\":" + J("Không thấy tệp trong yêu cầu.") + "}", true);
                    return false;
                }
                if (nameParam.Length == 0) nameParam = fname;
                input = part;
                sid = ""; off = 0; total = 0; segLen = -1; seg = false;
            }

            int have = 0;
            while (have < 64)
            {
                int r = input.Read(buf, have, buf.Length - have);
                if (r <= 0) break;
                have += r;
            }
            if (have == 0)
            {
                SendText(ctx, 400, "application/json; charset=utf-8",
                    "{\"ok\":false,\"error\":\"empty\",\"message\":" + J("Không nhận được dữ liệu.") + "}", true);
                return false;
            }

            string name = nameParam;
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) name = AutoName(ctx.Request.ContentType, buf, have);
            else if (Path.GetExtension(name).Length == 0) name = name + GuessExt(ctx.Request.ContentType, buf, have);
            name = SafeName(name);

            // --- ifnew=1: bo qua tep da nhan truoc do (Phim tat tu dong chay nhieu lan, khong sinh (1)(2)(3))
            if (!seg && q.ContainsKey("ifnew") && name.Length > 0)
            {
                bool daCo = false;
                try { daCo = File.Exists(Path.Combine(Config.SaveDir, name)); }
                catch { }
                if (daCo)
                {
                    try { Drain(input, buf); } catch { }
                    Log.Write("Bo qua " + name + " (da co trong thu muc luu)");
                    SendText(ctx, 200, "application/json; charset=utf-8",
                        "{\"ok\":true,\"dup\":true,\"name\":" + J(name) + "}", false);
                    return false;
                }
            }

            // --- duong dan tep tam
            string partPath = seg ? PartPathForSid(sid) : Path.Combine(Config.SaveDir, name + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + PartExt);

            if (seg)
            {
                long cur = 0;
                try { if (File.Exists(partPath)) cur = new FileInfo(partPath).Length; } catch { }
                if (cur != off)
                {
                    // client gui lech: bao cho client biet da nhan duoc bao nhieu
                    SendText(ctx, 200, "application/json; charset=utf-8",
                        "{\"ok\":false,\"error\":\"offset\",\"got\":" + cur + "}", false);
                    return false;
                }
            }

            long need = total > 0 ? Math.Max(1, total - off) : (segLen > 0 ? segLen : 0);
            if (part != null && whole > 0) need = whole;   // multipart: ca yeu cau la muc tren, du de kiem tra cho trong
            if (need > 0 && !EnoughSpace(need))
            {
                string msg = "Ổ đĩa trên máy tính không còn đủ chỗ trống để nhận tệp này.";
                Log.Write("Tu choi " + name + ": het dung luong (can " + need + " byte)");
                Notify(L.T("bal.nospace.title"), L.T("bal.nospace.text", name));
                // Windows chi gui phan hoi sau khi doc het phan than yêu cầu: doc bo neu con it,
                // con nhieu thi gui phan hoi roi ngat ket noi (dien thoai se thay loi mang).
                if (need <= (4L << 20)) Drain(input, buf);
                SendText(ctx, 507, "application/json; charset=utf-8",
                    "{\"ok\":false,\"error\":\"disk\",\"message\":" + J(msg) + "}", true);
                AbortSoon(ctx);
                return false;
            }

            ActiveUpload up = new ActiveUpload();
            up.Name = name;
            up.Ip = ip;
            up.Total = total > 0 ? total : (segLen > 0 ? off + segLen : 0);
            up.Done = off;
            lock (upLock)
            {
                upSeq++;
                up.Id = (seg ? SidHash(sid) : "u" + upSeq);
                uploads[up.Id] = up;
            }

            long written = 0;
            bool writeError = false;
            bool cancelled = false;
            string why = "";
            bool complete = false;
            double seconds = 0, maxSp = 0, minSp = 0;
            try
            {
                Directory.CreateDirectory(Config.SaveDir);
                FileStream fs = new FileStream(partPath, off > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
                Stopwatch sw = Stopwatch.StartNew();
                long markBytes = 0;
                double markT = 0;
                try
                {
                    fs.Write(buf, 0, have);
                    written = have;
                    int n;
                    while ((n = input.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (up.Cancel) { cancelled = true; break; }
                        fs.Write(buf, 0, n);
                        written += n;
                        up.Done = off + written;
                        double t = sw.Elapsed.TotalSeconds;
                        if (t - markT >= 0.25)
                        {
                            double sp = (written - markBytes) / (t - markT);
                            up.Speed = sp;
                            if (t > 0.5)
                            {
                                if (sp > maxSp) maxSp = sp;
                                if (minSp == 0 || sp < minSp) minSp = sp;
                            }
                            markBytes = written;
                            markT = t;
                        }
                    }
                }
                finally { fs.Close(); }
                if (part != null) { try { Drain(ctx.Request.InputStream, buf); } catch { } }
                seconds = Math.Max(0.05, sw.Elapsed.TotalSeconds);
                // tep ngan/qua nhanh co the chua kip lay mau toc do -> lay toc do trung binh lam moc
                if (maxSp <= 0 && written > 0) { maxSp = written / seconds; if (minSp <= 0) minSp = maxSp; }

                if (!cancelled)
                {
                    if (segLen >= 0 && written < segLen) complete = false;   // doan bi thieu
                    else if (seg && total > 0) complete = (off + written) >= total;
                    else if (!seg) complete = (segLen < 0 || written >= segLen);
                    else complete = false;
                    if (part != null && !part.SawBoundary) complete = false;   // multipart bi cat ngang
                }
            }
            catch (Exception ex)
            {
                writeError = !IsClientGone(ex) && !(ex is OperationCanceledException);
                why = writeError ? Why(ex) : "";
                Log.Write((writeError ? "Loi ghi " : "Ket noi bi ngat ") + name + ": " + ex.GetType().Name + " " + ex.Message);
            }
            finally
            {
                lock (upLock) { uploads.Remove(up.Id); }
            }

            outSize = written;
            outSeconds = seconds;
            outMax = maxSp;
            outMin = minSp;

            if (cancelled)
            {
                Log.Write("Da huy phien nhan " + name + " (giu tep do de gui tiep)");
                SendText(ctx, 200, "application/json; charset=utf-8",
                    "{\"ok\":false,\"error\":\"cancelled\",\"got\":" + (off + written) + "}", false);
                return false;
            }

            if (writeError)
            {
                Log.Write("Loi ghi " + name + ": " + why);
                Notify(L.T("bal.writefail.title"), why);
                SendText(ctx, 500, "application/json; charset=utf-8",
                    "{\"ok\":false,\"error\":\"write\",\"message\":" + J(why) + "}", true);
                if (!seg) { try { File.Delete(partPath); } catch { } }
                return false;
            }

            if (!complete)
            {
                if (seg)
                {
                    // giu tep do, client se gui tiep tu "got"
                    Log.Write("Doan cua " + name + " moi nhan " + written + "/" + segLen + " byte (giu de gui tiep)");
                    SendText(ctx, 200, "application/json; charset=utf-8",
                        "{\"ok\":false,\"error\":\"partial\",\"got\":" + (off + written) + "}", false);
                    outSize = written;
                    return false;
                }
                double expBytes = segLen;
                string note = "Kết nối bị ngắt giữa chừng: máy tính mới nhận được " + written + " trên " + expBytes + " byte của " + name + ". Hãy gửi lại tệp này.";
                Log.Write("Chua xong " + name + ": " + note);
                Notify(L.T("bal.stopped.title"), L.T("bal.stopped.text", written, expBytes, name));
                try { File.Delete(partPath); } catch { }
                SendText(ctx, 400, "application/json; charset=utf-8",
                    "{\"ok\":false,\"error\":\"incomplete\",\"message\":" + J("Tệp gửi chưa xong, máy tính chỉ nhận được " + written + " trên " + expBytes + " byte.") + "}", true);
                return false;
            }

            // --- xong: doi ten tep tam thanh ten that
            string finalPath;
            try
            {
                lock (nameLock) { finalPath = UniquePath(Config.SaveDir, name); File.Move(partPath, finalPath); }
            }
            catch (Exception ex)
            {
                Log.Write("Khong doi duoc ten " + partPath + ": " + ex.Message);
                SendText(ctx, 500, "application/json; charset=utf-8",
                    "{\"ok\":false,\"error\":\"rename\",\"message\":" + J("Không đổi được tên tệp: " + ex.Message) + "}", true);
                return false;
            }

            string mt;
            double ms;
            if (q.TryGetValue("mtime", out mt) &&
                double.TryParse(mt, NumberStyles.Float, CultureInfo.InvariantCulture, out ms) && ms > 0)
            {
                try
                {
                    DateTime t = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms);
                    File.SetLastWriteTimeUtc(finalPath, t);
                }
                catch { }
            }

            long fileSize = 0;
            try { fileSize = new FileInfo(finalPath).Length; } catch { }
            lastPath = finalPath;
            Log.Write("Nhan " + Path.GetFileName(finalPath) + " (" + fileSize + " byte)");
            lock (recentLock)
            {
                ReceivedFile r = new ReceivedFile();
                r.Name = Path.GetFileName(finalPath);
                r.Size = fileSize;
                r.Time = DateTime.Now;
                recent.Insert(0, r);
                if (recent.Count > 30) recent.RemoveAt(recent.Count - 1);
            }
            SaveRecent();
            SendText(ctx, 200, "application/json; charset=utf-8",
                "{\"ok\":true,\"done\":true,\"got\":" + fileSize + ",\"name\":" + J(Path.GetFileName(finalPath)) + ",\"size\":" + fileSize + "}", false);
            outSize = fileSize;
            return true;
        }

        static string AutoName(string contentType, byte[] head, int len)
        {
            string ext = GuessExt(contentType, head, len);
            string prefix = "FILE_";
            string e = ext.ToLowerInvariant();
            if (e == ".jpg" || e == ".heic" || e == ".png" || e == ".gif" || e == ".webp" || e == ".tiff" || e == ".dng") prefix = "IMG_";
            else if (e == ".mov" || e == ".mp4" || e == ".m4v") prefix = "VID_";
            return prefix + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ext;
        }

        static bool Has(byte[] b, int len, int offset, string ascii)
        {
            if (len < offset + ascii.Length) return false;
            for (int i = 0; i < ascii.Length; i++) if (b[offset + i] != (byte)ascii[i]) return false;
            return true;
        }

        public static string GuessExt(string contentType, byte[] b, int len)
        {
            if (len >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
            if (len >= 8 && b[0] == 0x89 && Has(b, len, 1, "PNG")) return ".png";
            if (Has(b, len, 0, "GIF8")) return ".gif";
            if (Has(b, len, 0, "RIFF") && Has(b, len, 8, "WEBP")) return ".webp";
            if (Has(b, len, 0, "%PDF")) return ".pdf";
            if (Has(b, len, 0, "PK\u0003\u0004")) return ".zip";
            if (Has(b, len, 4, "ftyp") && len >= 12)
            {
                string brand = Encoding.ASCII.GetString(b, 8, 4).ToLowerInvariant();
                if (brand == "heic" || brand == "heix" || brand == "mif1" || brand == "msf1" || brand == "hevc" || brand == "heim" || brand == "heis") return ".heic";
                if (brand == "avif") return ".avif";
                if (brand == "qt  ") return ".mov";
                if (brand == "m4v ") return ".m4v";
                if (brand == "m4a ") return ".m4a";
                return ".mp4";
            }
            if ((Has(b, len, 0, "II*\u0000") || Has(b, len, 0, "MM\u0000*")) && len >= 4) return ".tiff";
            string ct = (contentType ?? "").ToLowerInvariant();
            int semi = ct.IndexOf(';');
            if (semi >= 0) ct = ct.Substring(0, semi);
            ct = ct.Trim();
            switch (ct)
            {
                case "image/jpeg": return ".jpg";
                case "image/heic": case "image/heif": return ".heic";
                case "image/png": return ".png";
                case "image/gif": return ".gif";
                case "video/quicktime": return ".mov";
                case "video/mp4": return ".mp4";
                case "application/pdf": return ".pdf";
                case "text/plain": return ".txt";
                case "audio/mpeg": return ".mp3";
                case "audio/x-m4a": case "audio/mp4": return ".m4a";
            }
            return ".bin";
        }

        static readonly string[] Reserved = new string[] {
            "CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
            "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9" };

        public static string SafeName(string name)
        {
            if (name == null) name = "";
            name = name.Replace('\\', '/');
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1);
            StringBuilder sb = new StringBuilder();
            foreach (char c in name)
            {
                if (c < 32 || c == ':' || c == '*' || c == '?' || c == '\"' || c == '<' || c == '>' || c == '|') sb.Append('_');
                else sb.Append(c);
            }
            name = sb.ToString().Trim().TrimEnd('.', ' ');
            while (name.StartsWith(".")) name = name.Substring(1);
            if (name.Length == 0) name = "tep";
            string ext = Path.GetExtension(name);
            string stem = Path.GetFileNameWithoutExtension(name);
            if (ext.Length > 12) { stem = name; ext = ""; }
            if (stem.Length > 120) stem = stem.Substring(0, 120);
            if (stem.Length == 0) stem = "tep";
            foreach (string r in Reserved)
                if (string.Equals(stem, r, StringComparison.OrdinalIgnoreCase)) { stem = "_" + stem; break; }
            return stem + ext;
        }

        static string UniquePath(string dir, string name)
        {
            string stem = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);
            string p = Path.Combine(dir, name);
            int i = 1;
            while (File.Exists(p) || Directory.Exists(p))
            {
                p = Path.Combine(dir, stem + " (" + i + ")" + ext);
                i++;
            }
            return p;
        }

        // ---------- dia chi mang
        public static List<string> LocalAddresses()
        {
            List<KeyValuePair<int, string>> found = new List<KeyValuePair<int, string>>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    IPInterfaceProperties props = ni.GetIPProperties();
                    int score = 0;
                    try
                    {
                        foreach (GatewayIPAddressInformation g in props.GatewayAddresses)
                            if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) { score += 10; break; }
                    }
                    catch { }
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) score += 5;
                    else if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score += 3;
                    string desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    foreach (string bad in new string[] { "virtual", "vmware", "hyper-v", "vethernet", "wsl", "tap-", "vpn", "loopback", "bluetooth", "docker" })
                        if (desc.Contains(bad)) { score -= 20; break; }
                    foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = ua.Address.ToString();
                        if (ip.StartsWith("169.254.") || ip.StartsWith("127.")) continue;
                        found.Add(new KeyValuePair<int, string>(score, ip));
                    }
                }
            }
            catch { }
            found.Sort(delegate(KeyValuePair<int, string> a, KeyValuePair<int, string> b) { return b.Key.CompareTo(a.Key); });
            List<string> ips = new List<string>();
            foreach (KeyValuePair<int, string> kv in found) if (!ips.Contains(kv.Value)) ips.Add(kv.Value);
            return ips;
        }

        static string HostName()
        {
            if (Config.HostOverride.Length > 0) return Config.HostOverride;
            return Environment.MachineName.ToLowerInvariant() + ".local";
        }

        // ---------- theo doi loai mang dang dung (Rieng tu / Cong cong) de canh bao
        public void StartNetWatch()
        {
            Thread t = new Thread(delegate()
            {
                while (!stopping)
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo("powershell.exe",
                            "-NoProfile -WindowStyle Hidden -Command \"(Get-NetConnectionProfile | Where-Object {$_.IPv4Connectivity -eq 'Internet'} | Select-Object -First 1 -ExpandProperty NetworkCategory)\"");
                        psi.CreateNoWindow = true;
                        psi.UseShellExecute = false;
                        psi.RedirectStandardOutput = true;
                        using (Process p = Process.Start(psi))
                        {
                            string outp = p.StandardOutput.ReadToEnd().Trim();
                            p.WaitForExit(4000);
                            if (outp.Length > 0) netCategory = outp;
                        }
                    }
                    catch { }
                    for (int i = 0; i < 60 && !stopping; i++) Thread.Sleep(2000);
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        // ---------- tien ich tra loi
        static string N(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
            return d.ToString("0.##", CultureInfo.InvariantCulture);
        }

        static string J(string s)
        {
            if (s == null) s = "";
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '\"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\r' || c == '\n' || c == '\t') sb.Append(' ');
                else if (c < 32 || c == '<' || c == '>' || c == '&') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('\"').ToString();
        }

        static void SendText(HttpListenerContext ctx, int status, string type, string body, bool close)
        {
            if (close) ctx.Response.KeepAlive = false;
            byte[] data = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = type;
            ctx.Response.ContentLength64 = data.Length;
            ctx.Response.OutputStream.Write(data, 0, data.Length);
            ctx.Response.Close();
        }

        static void SendBinary(HttpListenerContext ctx, string file, string type)
        {
            byte[] data;
            try { data = ReadPageBytes(file); }
            catch (Exception ex) { SendText(ctx, 500, "text/plain; charset=utf-8", "Lỗi: " + ex.Message, true); return; }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = type;
            ctx.Response.ContentLength64 = data.Length;
            ctx.Response.OutputStream.Write(data, 0, data.Length);
            ctx.Response.Close();
        }

        static byte[] ReadPageBytes(string file)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string res = "web." + file;
            using (Stream s = asm.GetManifestResourceStream(res))
            {
                if (s == null) throw new FileNotFoundException("Thiếu tài nguyên nhúng trong exe (" + res + "). Hãy chạy lại install.bat.");
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buf = new byte[16384];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                    return ms.ToArray();
                }
            }
        }

        static string ReadPage(string file)
        {
            return Encoding.UTF8.GetString(ReadPageBytes(file));
        }

        static void SendPage(HttpListenerContext ctx, string file, string type, bool template)
        {
            string text;
            try { text = ReadPage(file); }
            catch (Exception ex)
            {
                Log.Write("Loi doc trang " + file + ": " + ex.Message);
                SendText(ctx, 500, "text/plain; charset=utf-8", "Lỗi: " + ex.Message, true);
                return;
            }
            if (template)
            {
                text = text.Replace("{{PCNAME}}", WebUtility.HtmlEncode(Environment.MachineName))
                           .Replace("{{HOST}}", HostName())
                           .Replace("{{PORT}}", Config.Port.ToString())
                           .Replace("{{SCFILE}}", Config.ShortcutFile ? "true" : "false")
                           .Replace("{{AUTOFILE}}", Config.AutoFile ? "true" : "false");
            }
            SendText(ctx, 200, type, text, false);
        }
    }

    // ------------------------------------------------------------------ Tray
    class TrayApp : ApplicationContext
    {
        readonly NotifyIcon icon;
        readonly Control ui;
        string balloonPath = "";
        readonly Server server;

        public TrayApp(Server srv, bool showDash)
        {
            server = srv;
            ui = new Control();
            IntPtr force = ui.Handle;
            if (force == IntPtr.Zero) { }

            icon = new NotifyIcon();
            icon.Icon = LoadTrayIcon();
            icon.Visible = true;
            icon.DoubleClick += delegate { OpenDash(); };
            icon.BalloonTipClicked += delegate { if (balloonPath.Length > 0) OpenPath(balloonPath, true); };
            BuildMenu();

            // --- noi Server voi giao dien
            Ui.PickFiles = delegate {
                string[] result = null;
                ui.Invoke((MethodInvoker)delegate {
                    using (OpenFileDialog d = new OpenFileDialog()) {
                        d.Multiselect = true; d.CheckFileExists = true; d.RestoreDirectory = true;
                        d.Title = L.DialogVi ? "Chọn tệp chia sẻ với iPhone" : "Share files with iPhone";
                        // Windows hay mo hop thoai o PHIA SAU cua so dang o tien canh (trinh duyet)
                        // => phai ep no len truoc, neu khong nguoi dung thay nhu nut khong chay.
                        Win.FocusPickerSoon();
                        if (d.ShowDialog() == DialogResult.OK) result = d.FileNames;
                    }
                });
                return result;
            };
            Ui.PickFolder = delegate { return PickFolder(); };
            Ui.OpenPath = delegate(string p, bool sel) { OpenPath(p, sel); };
            Ui.Balloon = delegate(string title, string text)
            {
                ui.BeginInvoke((MethodInvoker)delegate { icon.ShowBalloonTip(8000, title, text, ToolTipIcon.Warning); });
            };
            Ui.ConfigChanged = delegate
            {
                ui.BeginInvoke((MethodInvoker)delegate
                {
                    if (miAwake != null) miAwake.Checked = Config.KeepAwake;
                    if (miAutoOpen != null) miAutoOpen.Checked = Config.OpenFolder;
                    if (miAutoRun != null) miAutoRun.Checked = Config.AutoStart;
                });
            };
            Ui.LangChanged = delegate
            {
                ui.BeginInvoke((MethodInvoker)delegate { BuildMenu(); });
            };

            server.BatchDone += delegate(int count, long bytes, string last)
            {
                ui.BeginInvoke((MethodInvoker)delegate
                {
                    balloonPath = last;
                    icon.ShowBalloonTip(5000, "AdamDrop",
                        L.T("bal.received.text", count, Size(bytes), Config.SaveDir),
                        ToolTipIcon.Info);
                    if (Config.OpenFolder) OpenPath(last, true);
                });
            };

            server.Problem += delegate(string title, string text)
            {
                Log.Write("Su co: " + title + " - " + text);
                ui.BeginInvoke((MethodInvoker)delegate
                {
                    icon.ShowBalloonTip(8000, title, text, ToolTipIcon.Warning);
                });
            };

            if (showDash) OpenDash();
        }

        ToolStripMenuItem miAwake, miAutoRun, miAutoOpen;

        // Menu khay dung lai duoc (khi doi ngon ngu)
        void BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem miDash = new ToolStripMenuItem(L.T("tray.dash"));
            miDash.Font = new Font(miDash.Font, FontStyle.Bold);
            miDash.Click += delegate { OpenDash(); };
            ToolStripMenuItem miFolder = new ToolStripMenuItem(L.T("tray.folder"));
            miFolder.Click += delegate { OpenPath(Config.SaveDir, false); };
            ToolStripMenuItem miPick = new ToolStripMenuItem(L.T("tray.pick"));
            miPick.Click += delegate
            {
                string dir = PickFolder();
                if (string.IsNullOrEmpty(dir)) return;
                string err = SetSaveDirSafe(dir);
                if (err.Length > 0) MessageBox.Show(err, "AdamDrop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            miAwake = new ToolStripMenuItem(L.T("tray.awake"));
            miAwake.CheckOnClick = true;
            miAwake.Checked = Config.KeepAwake;
            miAwake.CheckedChanged += delegate { Config.KeepAwake = miAwake.Checked; Config.Save(); };
            miAutoRun = new ToolStripMenuItem(L.T("tray.autorun"));
            miAutoRun.CheckOnClick = true;
            miAutoRun.Checked = Config.AutoStart;
            miAutoRun.CheckedChanged += delegate { Config.AutoStart = miAutoRun.Checked; };
            miAutoOpen = new ToolStripMenuItem(L.T("tray.autoopen"));
            miAutoOpen.CheckOnClick = true;
            miAutoOpen.Checked = Config.OpenFolder;
            miAutoOpen.CheckedChanged += delegate { Config.OpenFolder = miAutoOpen.Checked; Config.Save(); };
            ToolStripMenuItem miLog = new ToolStripMenuItem(L.T("tray.log"));
            miLog.Click += delegate { OpenLog(); };
            ToolStripMenuItem miExit = new ToolStripMenuItem(L.T("tray.exit"));
            miExit.Click += delegate { Log.Write("Nguoi dung thoat chuong trinh"); icon.Visible = false; server.Stop(); ExitThread(); };
            menu.Items.Add(miDash);
            menu.Items.Add(miFolder);
            menu.Items.Add(miPick);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miAwake);
            menu.Items.Add(miAutoRun);
            menu.Items.Add(miAutoOpen);
            menu.Items.Add(miLog);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miExit);
            ContextMenuStrip cu = icon.ContextMenuStrip;
            icon.ContextMenuStrip = menu;
            icon.Text = L.T("tray.tip");
            if (cu != null) cu.Dispose();
        }

        // Icon khay: dung logo that nhung trong exe; neu thieu thi ve bang ma
        static Icon LoadTrayIcon()
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("web.logo.ico"))
                {
                    if (s != null)
                    {
                        using (MemoryStream ms = new MemoryStream())
                        {
                            byte[] b = new byte[8192];
                            int n;
                            while ((n = s.Read(b, 0, b.Length)) > 0) ms.Write(b, 0, n);
                            ms.Position = 0;
                            return new Icon(ms, SystemInformation.SmallIconSize);
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Write("Khong doc duoc logo nhung: " + ex.Message); }
            return MakeIcon();
        }

        static string SetSaveDirSafe(string dir)
        {
            // dung chung logic voi dashboard: thu ghi truoc khi doi
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".adamdrop-writetest");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                Config.SaveDir = dir;
                Config.Save();
                Server.CleanParts();
                Log.Write("Da doi thu muc nhan tep: " + dir);
                return "";
            }
            catch (Exception ex)
            {
                Log.Write("Loi doi thu muc nhan tep: " + ex.Message);
                return "Không ghi được vào thư mục này.\n\n" + ex.Message;
            }
        }

        string PickFolder()
        {
            string result = null;
            try
            {
                ui.Invoke((MethodInvoker)delegate
                {
                    using (FolderBrowserDialog d = new FolderBrowserDialog())
                    {
                        d.Description = "Chọn thư mục nhận tệp từ iPhone";
                        d.ShowNewFolderButton = true;
                        try { if (Directory.Exists(Config.SaveDir)) d.SelectedPath = Config.SaveDir; } catch { }
                        if (d.ShowDialog() == DialogResult.OK) result = d.SelectedPath;
                    }
                });
            }
            catch (Exception ex) { Log.Write("Loi hop thoai chon thu muc: " + ex.Message); }
            return result;
        }

        static void OpenDash()
        {
            try { Process.Start("http://localhost:" + Config.Port + "/dashboard"); } catch { }
        }

        static string Size(long n)
        {
            if (n < 1024) return n + " B";
            if (n < 1048576) return (n / 1024.0).ToString("0") + " KB";
            if (n < 1073741824) return (n / 1048576.0).ToString("0.#") + " MB";
            return (n / 1073741824.0).ToString("0.##") + " GB";
        }

        static void OpenLog()
        {
            try
            {
                if (!File.Exists(Config.LogPath)) File.AppendAllText(Config.LogPath, "", new UTF8Encoding(false));
                Process.Start("notepad.exe", "\"" + Config.LogPath + "\"");
            }
            catch { }
        }

        static void OpenPath(string path, bool select)
        {
            try
            {
                if (select && File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else Process.Start("explorer.exe", "\"" + (Directory.Exists(path) ? path : Config.SaveDir) + "\"");
            }
            catch { }
        }

        // Logo AdamDrop: o vuong bo goc vang #D89B2B + mui ten trang
        static Icon MakeIcon()
        {
            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(0xD8, 0x9B, 0x2B)))
                {
                    using (GraphicsPath path = RoundRect(new Rectangle(1, 1, 30, 30), 8))
                        g.FillPath(b, path);
                }
                using (Pen p = new Pen(Color.FromArgb(0x11, 0x11, 0x11), 3.4f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                    g.DrawLine(p, 16, 7, 16, 19);
                    g.DrawLines(p, new Point[] { new Point(10, 14), new Point(16, 20), new Point(22, 14) });
                    g.DrawLine(p, 10, 25, 22, 25);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ------------------------------------------------------------------ Main
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            bool console = Array.IndexOf(args, "--console") >= 0;
            bool show = Array.IndexOf(args, "--show") >= 0;
            Config.Load();
            History.Load();
            Server.LoadRecent();
            Server.CleanParts();
            Log.Write("Khoi dong: port=" + Config.Port + " savedir=" + Config.SaveDir);

            if (console) return RunConsole();

            bool created;
            using (Mutex m = new Mutex(true, "AdamDrop_single_instance", out created))
            {
                if (!created)
                {
                    Log.Write("Da co ban khac dang chay, chi mo bang dieu khien");
                    try { Process.Start("http://localhost:" + Config.Port + "/dashboard"); } catch { }
                    return 0;
                }
                Application.EnableVisualStyles();
                Server server = new Server();
                if (!server.Start())
                {
                    Log.Write("Khong the mo cong " + Config.Port + " sau 3 lan thu");
                    MessageBox.Show(L.T("msg.port.text", Config.Port),
                        "AdamDrop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 1;
                }
                server.StartNetWatch();
                Application.Run(new TrayApp(server, show));
                GC.KeepAlive(m);
            }
            return 0;
        }

        static int RunConsole()
        {
            Server server = new Server();
            server.BatchDone += delegate(int c, long b, string p) { Console.WriteLine("BATCH " + c + " " + b + " " + p); };
            if (!server.Start())
            {
                Console.WriteLine("START_FAILED");
                return 1;
            }
            Console.WriteLine("Key available in local dashboard only");
            Console.WriteLine("DIR " + Config.SaveDir);
            Console.WriteLine("PORT " + Config.Port);
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }
    }
}
