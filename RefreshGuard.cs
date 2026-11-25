using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RefreshGuard
{
    internal enum PowerMode { Ac, Battery }

    // ---------------------------------------------------------------- paths

    internal static class Paths
    {
        // portable: config/state/logs live next to the exe, so the whole folder can be moved freely
        public static readonly string Root =
            AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        public static readonly string ConfigFile = Path.Combine(Root, "config.ini");
        public static readonly string StateFile = Path.Combine(Root, "state.ini");
        public static readonly string LogDir = Path.Combine(Root, "logs");

        public static string LogFile()
        {
            return Path.Combine(LogDir, DateTime.Now.ToString("yyyy-MM") + ".log");
        }
    }

    // ----------------------------------------------------------------- log

    internal static class Log
    {
        public static void Info(string msg) { Write("INFO", msg); }
        public static void Warn(string msg) { Write("WARN", msg); }

        private static void Write(string level, string msg)
        {
            try
            {
                Directory.CreateDirectory(Paths.LogDir);
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + msg + Environment.NewLine;
                File.AppendAllText(Paths.LogFile(), line, new UTF8Encoding(false));
            }
            catch { }
        }
    }

    // ------------------------------------------------- console bridge (winexe)

    internal static class Con
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        private static bool attached;

        public static void Init()
        {
            try
            {
                attached = AttachConsole(-1);
                if (attached)
                {
                    // 必须跟随控制台代码页：硬编码 UTF-8 会在 cp936 的真实控制台里变乱码
                    StreamWriter w = new StreamWriter(Console.OpenStandardOutput(), Console.OutputEncoding);
                    w.AutoFlush = true;
                    Console.SetOut(w);
                }
            }
            catch { attached = false; }
        }

        // always logs; prints to console only when a parent console was attached
        public static void Print(string s)
        {
            Log.Info(s);
            if (attached)
            {
                try { Console.Out.WriteLine(s); } catch { }
            }
        }
    }

    // ------------------------------------------------------------- settings

    internal class Settings
    {
        public int BatteryHz = 60;
        public int AcHz = 120;
        public int DelaySeconds = 10;
        public bool Notifications = true;
        public bool SilentOnLogon = true;
        public bool SkipWhenFullscreen = true;

        public static Settings Load()
        {
            Settings s = new Settings();
            if (!File.Exists(Paths.ConfigFile))
            {
                s.Save();
                return s;
            }
            try
            {
                foreach (string raw in File.ReadAllLines(Paths.ConfigFile))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    string k = line.Substring(0, idx).Trim().ToLowerInvariant();
                    string v = line.Substring(idx + 1).Trim();
                    if (k == "batteryhz") s.BatteryHz = ParseInt(v, s.BatteryHz);
                    else if (k == "aczh") s.AcHz = ParseInt(v, s.AcHz);
                    else if (k == "delayseconds") s.DelaySeconds = ParseInt(v, s.DelaySeconds);
                    else if (k == "notifications") s.Notifications = ParseBool(v, s.Notifications);
                    else if (k == "silentonlogon") s.SilentOnLogon = ParseBool(v, s.SilentOnLogon);
                    else if (k == "skipwhenfullscreen") s.SkipWhenFullscreen = ParseBool(v, s.SkipWhenFullscreen);
                }
            }
            catch (Exception ex) { Log.Warn("读 config.ini 失败，使用默认值: " + ex.Message); }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Paths.Root);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# RefreshGuard 配置");
                sb.AppendLine("# 修改后无需重启，下次事件触发时生效");
                sb.AppendLine("batteryHz=" + BatteryHz);
                sb.AppendLine("acHz=" + AcHz);
                sb.AppendLine("delaySeconds=" + DelaySeconds);
                sb.AppendLine("notifications=" + (Notifications ? "true" : "false"));
                sb.AppendLine("silentOnLogon=" + (SilentOnLogon ? "true" : "false"));
                sb.AppendLine("skipWhenFullscreen=" + (SkipWhenFullscreen ? "true" : "false"));
                File.WriteAllText(Paths.ConfigFile, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Warn("写 config.ini 失败: " + ex.Message); }
        }

        public static int ParseInt(string v, int fallback)
        {
            int r;
            if (int.TryParse(v, out r)) return r;
            return fallback;
        }

        public static bool ParseBool(string v, bool fallback)
        {
            string s = v.ToLowerInvariant();
            if (s == "true" || s == "1" || s == "yes") return true;
            if (s == "false" || s == "0" || s == "no") return false;
            return fallback;
        }
    }

    // ---------------------------------------------------------------- state

    internal class State
    {
        public string Mode = "";      // "ac" | "battery" | ""
        public int Hz;
        public string Updated = "";
        public string Reason = "";

        public static State Load()
        {
            State s = new State();
            if (!File.Exists(Paths.StateFile)) return s;
            try
            {
                foreach (string raw in File.ReadAllLines(Paths.StateFile))
                {
                    string line = raw.Trim();
                    int idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    string k = line.Substring(0, idx).Trim().ToLowerInvariant();
                    string v = line.Substring(idx + 1).Trim();
                    if (k == "mode") s.Mode = v;
                    else if (k == "hz") s.Hz = Settings.ParseInt(v, s.Hz);
                    else if (k == "updated") s.Updated = v;
                    else if (k == "reason") s.Reason = v;
                }
            }
            catch (Exception ex) { Log.Warn("读 state.ini 失败，按空状态处理: " + ex.Message); }
            return s;
        }

        public void Save(PowerMode mode, int hz, string reason)
        {
            string m = (mode == PowerMode.Battery) ? "battery" : "ac";
            string now = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            Mode = m; Hz = hz; Updated = now; Reason = reason;
            try
            {
                Directory.CreateDirectory(Paths.Root);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("mode=" + m);
                sb.AppendLine("hz=" + hz);
                sb.AppendLine("updated=" + now);
                sb.AppendLine("reason=" + reason);
                File.WriteAllText(Paths.StateFile, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Warn("写 state.ini 失败，去重基准可能丢失: " + ex.Message); }
        }
    }

    // --------------------------------------------------------------- native

    internal static class Native
    {
        public const int ENUM_CURRENT_SETTINGS = -1;
        public const uint DM_BITSPERPEL = 0x00040000;
        public const uint DM_PELSWIDTH = 0x00080000;
        public const uint DM_PELSHEIGHT = 0x00100000;
        public const uint DM_DISPLAYFREQUENCY = 0x00400000;
        public const int CDS_UPDATEREGISTRY = 0x00000001;
        public const int CDS_TEST = 0x00000002;
        public const int DISP_CHANGE_SUCCESSFUL = 0;
        public const uint DESKTOP_READOBJECTS = 0x0001;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public uint dmDisplayOrientation;
            public uint dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public uint dmPelsWidth;
            public uint dmPelsHeight;
            public uint dmDisplayFlags;
            public uint dmDisplayFrequency;
            public uint dmICMMethod;
            public uint dmICMIntent;
            public uint dmMediaType;
            public uint dmDitherType;
            public uint dmReserved1;
            public uint dmReserved2;
            public uint dmPanningWidth;
            public uint dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE devMode,
            IntPtr hwnd, int flags, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern int SHQueryUserNotificationState(out int state);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool CloseDesktop(IntPtr hDesktop);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern uint GetDpiForSystem();
    }

    // -------------------------------------------------------------- display

    internal static class Display
    {
        public static Native.DEVMODE Current()
        {
            Native.DEVMODE dm = new Native.DEVMODE();
            try { Native.EnumDisplaySettings(null, Native.ENUM_CURRENT_SETTINGS, ref dm); }
            catch { }
            return dm;
        }

        public static int CurrentHz() { return (int)Current().dmDisplayFrequency; }
        public static int CurrentWidth() { return (int)Current().dmPelsWidth; }
        public static int CurrentHeight() { return (int)Current().dmPelsHeight; }
        public static int CurrentBpp() { return (int)Current().dmBitsPerPel; }

        public static List<int> AvailableHz(int w, int h, int bpp)
        {
            List<int> list = new List<int>();
            try
            {
                Native.DEVMODE dm = new Native.DEVMODE();
                for (int i = 0; i < 5000; i++)
                {
                    if (!Native.EnumDisplaySettings(null, i, ref dm)) break;
                    if ((int)dm.dmPelsWidth != w || (int)dm.dmPelsHeight != h) continue;
                    if ((int)dm.dmBitsPerPel != bpp) continue;
                    int hz = (int)dm.dmDisplayFrequency;
                    if (hz > 1 && !list.Contains(hz)) list.Add(hz);
                }
            }
            catch { }
            list.Sort();
            return list;
        }

        public static int Nearest(List<int> sorted, int target)
        {
            if (sorted.Count == 0) return target;
            int best = sorted[0];
            int diff = Math.Abs(sorted[0] - target);
            for (int i = 1; i < sorted.Count; i++)
            {
                int d = Math.Abs(sorted[i] - target);
                if (d < diff) { diff = d; best = sorted[i]; }
            }
            return best;
        }

        // validate with CDS_TEST first, then apply, then read back
        public static bool Apply(int hz, out int actualHz, out string error)
        {
            actualHz = 0;
            error = null;
            try
            {
                Native.DEVMODE dm = Current();
                if (dm.dmSize == 0) { error = "无法读取当前显示模式"; return false; }
                dm.dmDisplayFrequency = (uint)hz;
                dm.dmFields = Native.DM_PELSWIDTH | Native.DM_PELSHEIGHT
                            | Native.DM_BITSPERPEL | Native.DM_DISPLAYFREQUENCY;

                int test = Native.ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, Native.CDS_TEST, IntPtr.Zero);
                if (test != Native.DISP_CHANGE_SUCCESSFUL)
                {
                    error = "CDS_TEST 未通过，返回码 " + test;
                    return false;
                }

                int r = Native.ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, Native.CDS_UPDATEREGISTRY, IntPtr.Zero);
                if (r != Native.DISP_CHANGE_SUCCESSFUL)
                {
                    error = "ChangeDisplaySettingsEx 失败，返回码 " + r;
                    return false;
                }

                actualHz = CurrentHz();
                if (actualHz != hz)
                {
                    error = "回读不一致：期望 " + hz + " Hz，实际 " + actualHz + " Hz";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    // -------------------------------------------------------------- session

    internal static class Session
    {
        public static PowerMode GetMode()
        {
            // 刚登录时 PowerStatus 可能短暂返回 Unknown，短重试几次再兜底
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    PowerStatus ps = SystemInformation.PowerStatus;
                    if (ps.PowerLineStatus == PowerLineStatus.Online) return PowerMode.Ac;
                    if (ps.PowerLineStatus == PowerLineStatus.Offline) return PowerMode.Battery;
                }
                catch { }
                if (attempt < 2) Thread.Sleep(200);
            }
            // 未知时按 AC 兜底，避免在不确定状态下误降频
            Log.Warn("供电状态未知，按已接通电源处理");
            return PowerMode.Ac;
        }

        public static string ModeText(PowerMode m)
        {
            return (m == PowerMode.Battery) ? "电池供电" : "已接通电源";
        }

        public static bool IsLocked()
        {
            try
            {
                IntPtr h = Native.OpenInputDesktop(0, false, Native.DESKTOP_READOBJECTS);
                if (h == IntPtr.Zero) return true;
                Native.CloseDesktop(h);
                return false;
            }
            catch { return false; }
        }

        public static bool IsFullscreen()
        {
            try
            {
                int st;
                if (Native.SHQueryUserNotificationState(out st) != 0) return false;
                return st == 2 || st == 3 || st == 4;   // BUSY / D3D_FULLSCREEN / PRESENTATION
            }
            catch { return false; }
        }
    }

    // ---------------------------------------------------------- banner form

    internal class BannerForm : Form
    {
        public enum Outcome { Expired, Cancelled, AppliedNow, Reverted, Dismissed }

        private const int W_BASE = 416;
        private const int H_BASE = 138;
        private const int PAD = 20;

        private readonly PowerMode _mode;
        private readonly int _fromHz;
        private readonly int _toHz;
        private readonly bool _watchPower;
        private readonly int _total;
        private int _remaining;

        private readonly float _s;
        private Rectangle _rCancel;
        private Rectangle _rApply;
        private int _hover = -1;

        private readonly System.Windows.Forms.Timer _tick;
        private readonly System.Windows.Forms.Timer _anim;
        private float _t;
        private int _x0, _x1, _y0;

        // 默认必须是「不动作」：Alt+F4 / 注销等意外关闭不能被当成用户主动取消
        public Outcome Result = Outcome.Dismissed;

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        public BannerForm(PowerMode mode, int fromHz, int toHz, int delaySeconds, bool watchPower)
        {
            _mode = mode;
            _fromHz = fromHz;
            _toHz = toHz;
            _watchPower = watchPower;
            _total = Math.Max(1, delaySeconds);
            _remaining = _total;

            _s = 1f;
            try
            {
                uint dpi = Native.GetDpiForSystem();
                if (dpi > 0) _s = dpi / 96f;
            }
            catch { _s = 1f; }
            if (_s < 1f) _s = 1f;

            int w = (int)(W_BASE * _s);
            int h = (int)(H_BASE * _s);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            _x1 = wa.Right - w - (int)(22 * _s);
            _y0 = wa.Bottom - h - (int)(18 * _s);
            _x0 = wa.Right + 8;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(w, h);
            Location = new Point(_x1, _y0);
            BackColor = Color.FromArgb(31, 33, 36);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint, true);
            DoubleBuffered = true;

            UpdateRegion();

            _rApply = new Rectangle((int)((W_BASE - PAD - 104) * _s), (int)(88 * _s), (int)(104 * _s), (int)(32 * _s));
            _rCancel = new Rectangle((int)((W_BASE - PAD - 104 - 8 - 72) * _s), (int)(88 * _s), (int)(72 * _s), (int)(32 * _s));

            _tick = new System.Windows.Forms.Timer();
            _tick.Interval = 1000;
            _tick.Tick += OnTick;

            _anim = new System.Windows.Forms.Timer();
            _anim.Interval = 16;
            _anim.Tick += OnAnim;

            if (_watchPower) SystemEvents.PowerModeChanged += OnPowerChanged;
        }

        private void UpdateRegion()
        {
            try
            {
                RectangleF r = new RectangleF(0, 0, ClientSize.Width, ClientSize.Height);
                using (GraphicsPath p = Round(r, 14f * _s))
                    Region = new Region(p);
            }
            catch { }
        }

        private static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2;
            if (d <= 0) { d = 1; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Left = _x0;
            Top = _y0;
            _anim.Start();
            _tick.Start();
        }

        private void OnAnim(object sender, EventArgs e)
        {
            _t += 0.075f;
            if (_t >= 1f) { _t = 1f; _anim.Stop(); }
            float ease = 1f - (1f - _t) * (1f - _t);
            Left = (int)(_x0 + (_x1 - _x0) * ease);
        }

        private void OnTick(object sender, EventArgs e)
        {
            _remaining--;
            if (_remaining <= 0)
            {
                Result = Outcome.Expired;
                Close();
                return;
            }
            Invalidate();
        }

        private void OnPowerChanged(object sender, PowerModeChangedEventArgs e)
        {
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed) return;
                    if (Session.GetMode() != _mode)
                    {
                        Result = Outcome.Reverted;
                        Close();
                    }
                });
            }
            catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                if (_watchPower) SystemEvents.PowerModeChanged -= OnPowerChanged;
                _tick.Stop();
                _anim.Stop();
                _tick.Dispose();
                _anim.Dispose();
            }
            catch { }
            base.OnFormClosed(e);
        }

        // ------------------------------------------------------------- paint

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            float s = _s;
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            RectangleF all = new RectangleF(0, 0, w, h);
            using (GraphicsPath bg = Round(all, 14f * s))
            using (SolidBrush br = new SolidBrush(Color.FromArgb(31, 33, 36)))
                g.FillPath(br, bg);
            using (GraphicsPath bd = Round(new RectangleF(0.5f, 0.5f, w - 1f, h - 1f), 14f * s))
            using (Pen p = new Pen(Color.FromArgb(64, 68, 74), 1f))
                g.DrawPath(p, bd);

            string title = "即将切换到 " + _toHz + " Hz";
            string sub = (_mode == PowerMode.Battery)
                ? "检测到已断电（电池供电），降低刷新率以节省电量"
                : "检测到已接通电源，恢复高刷新率";

            Color fg = Color.FromArgb(238, 240, 243);
            Color muted = Color.FromArgb(158, 164, 172);
            Color accent = Color.FromArgb(59, 130, 246);

            using (Font ft = MakeFont(13.5f, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(fg))
                g.DrawString(title, ft, b, PAD * s, 15 * s);

            using (Font fs = MakeFont(9.5f, FontStyle.Regular))
            using (SolidBrush b = new SolidBrush(muted))
                g.DrawString(sub, fs, b, PAD * s, 47 * s);

            // countdown text
            string cd = _remaining + " 秒后自动切换";
            using (Font fc = MakeFont(9.5f, FontStyle.Regular))
            using (SolidBrush b = new SolidBrush(muted))
                g.DrawString(cd, fc, b, PAD * s + 2 * s, 88 * s + 8 * s);

            // buttons
            DrawButton(g, _rCancel, "取消", false, _hover == 0);
            DrawButton(g, _rApply, "立即切换", true, _hover == 1);

            // progress line at the bottom edge
            float frac = (float)_remaining / (float)_total;
            if (frac < 0f) frac = 0f;
            if (frac > 1f) frac = 1f;
            float barY = h - 4f * s;
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(44, 48, 54)))
                g.FillRectangle(tb, 0, barY, w, 4f * s);
            using (SolidBrush ab = new SolidBrush(accent))
                g.FillRectangle(ab, 0, barY, w * frac, 4f * s);
        }

        private static Font MakeFont(float size, FontStyle style)
        {
            try { return new Font("Microsoft YaHei UI", size, style); }
            catch { return new Font("Segoe UI", size, style); }
        }

        private void DrawButton(Graphics g, Rectangle r, string text, bool primary, bool hover)
        {
            float radius = 8f * _s;
            RectangleF rf = new RectangleF(r.X, r.Y, r.Width, r.Height);
            using (GraphicsPath p = Round(rf, radius))
            {
                if (primary)
                {
                    Color c = hover ? Color.FromArgb(72, 146, 255) : Color.FromArgb(59, 130, 246);
                    using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
                }
                else
                {
                    Color c = hover ? Color.FromArgb(48, 52, 58) : Color.FromArgb(42, 45, 50);
                    using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
                    using (Pen pn = new Pen(Color.FromArgb(78, 84, 92), 1f)) g.DrawPath(pn, p);
                }
            }

            Color tc = primary ? Color.White : Color.FromArgb(206, 210, 216);
            using (Font f = MakeFont(9.5f, FontStyle.Regular))
            using (SolidBrush b = new SolidBrush(tc))
            {
                SizeF sz = g.MeasureString(text, f);
                g.DrawString(text, f, b,
                    r.X + (r.Width - sz.Width) / 2f,
                    r.Y + (r.Height - sz.Height) / 2f);
            }
        }

        // ------------------------------------------------------------- mouse

        private int HitTest(Point p)
        {
            if (_rCancel.Contains(p)) return 0;
            if (_rApply.Contains(p)) return 1;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != _hover)
            {
                _hover = h;
                Cursor = (h >= 0) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hover != -1) { _hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h == 0)
            {
                Result = Outcome.Cancelled;
                Close();
            }
            else if (h == 1)
            {
                Result = Outcome.AppliedNow;
                Close();
            }
            base.OnMouseDown(e);
        }
    }

    // -------------------------------------------------------------- program

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try { Native.SetProcessDPIAware(); } catch { }
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
            }
            catch { }
            Con.Init();

            // 零常驻工具不能弹 WinForms 默认错误对话框，也不能无日志崩掉
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                Log.Warn("UI 线程未处理异常: " + e.Exception.Message);
                Environment.Exit(1);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                Log.Warn("未处理异常: " + (ex != null ? ex.Message : "unknown"));
            };

            List<string> a = new List<string>();
            for (int i = 0; i < args.Length; i++) a.Add(args[i].Trim().ToLowerInvariant());

            if (a.Contains("--list")) return CmdList();
            if (a.Contains("--status")) return CmdStatus();

            Settings cfg = Settings.Load();
            State st = State.Load();

            bool simBattery = a.Contains("--battery");
            bool simAc = a.Contains("--ac");
            bool dry = a.Contains("--dry-run");
            bool now = a.Contains("--now");
            bool forceSilent = a.Contains("--silent");
            bool demo = a.Contains("--banner-demo");

            PowerMode mode;
            string source;
            bool watchPower = false;
            bool writeState = true;

            if (simBattery || simAc)
            {
                mode = simBattery ? PowerMode.Battery : PowerMode.Ac;
                source = "manual";
                writeState = false;
            }
            else if (forceSilent)
            {
                mode = Session.GetMode();
                source = "logon";
                if (!cfg.SilentOnLogon) forceSilent = false;   // 配置要求登录时也提示
            }
            else
            {
                mode = Session.GetMode();
                source = "event";
                watchPower = true;
            }

            Mutex mutex = new Mutex(false, "RefreshGuard.SingleInstance");
            bool owned = false;
            try { owned = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned)
            {
                Log.Warn("已有实例在运行，退出");
                return 2;
            }

            try
            {
                // 登录时桌面和显示驱动可能还没就绪：先等显示模式可读，避免把 0Hz 当成当前状态
                if (source == "logon")
                {
                    bool ready = false;
                    for (int i = 0; i < 20; i++)
                    {
                        Native.DEVMODE probe = Display.Current();
                        if (probe.dmSize != 0 && probe.dmDisplayFrequency > 1) { ready = true; break; }
                        Thread.Sleep(500);
                    }
                    if (!ready) Log.Warn("登录后等待 10 秒仍读不到显示模式，继续尝试");
                }

                // dedupe: only react when the power mode actually changed
                if (source == "event" && !demo && st.Mode.Length > 0)
                {
                    string nowMode = (mode == PowerMode.Battery) ? "battery" : "ac";
                    if (st.Mode == nowMode)
                    {
                        int curHz = Display.CurrentHz();
                        if (st.Hz == curHz)
                        {
                            Log.Info("供电模式未变化（state=" + st.Mode + "），退出");
                            return 0;
                        }
                        Log.Info("供电模式未变化但刷新率漂移（state=" + st.Hz + "Hz，实际 " + curHz + "Hz），继续校正");
                    }
                }

                Native.DEVMODE cur = Display.Current();
                if (cur.dmSize == 0)
                {
                    Con.Print("无法读取当前显示模式（EnumDisplaySettings 失败），本次退出");
                    return 1;
                }
                int fromHz = (int)cur.dmDisplayFrequency;
                int targetHz = (mode == PowerMode.Battery) ? cfg.BatteryHz : cfg.AcHz;
                int w = (int)cur.dmPelsWidth;
                int hh = (int)cur.dmPelsHeight;
                int bpp = (int)cur.dmBitsPerPel;
                List<int> avail = Display.AvailableHz(w, hh, bpp);

                Con.Print("== RefreshGuard ==");
                Con.Print("触发来源: " + source);
                Con.Print("供电状态: " + Session.ModeText(mode));
                Con.Print("当前模式: " + w + "x" + hh + "@" + fromHz + "Hz (" + bpp + "bpp)");
                Con.Print("可用刷新率: " + JoinInts(avail));
                Con.Print("目标刷新率: " + targetHz + "Hz");

                if (demo)
                {
                    int demoTo = (fromHz == cfg.BatteryHz) ? cfg.AcHz : cfg.BatteryHz;
                    PowerMode demoMode = (demoTo == cfg.BatteryHz) ? PowerMode.Battery : PowerMode.Ac;
                    Con.Print("[banner-demo] 演示 " + fromHz + "Hz -> " + demoTo + "Hz，不会真的切换");
                    return RunBanner(cfg, st, demoMode, fromHz, demoTo, false, false, false);
                }

                if (fromHz == targetHz)
                {
                    Con.Print("已经是目标刷新率，无需切换");
                    if (writeState) st.Save(mode, fromHz, "already");
                    return 0;
                }

                if (!avail.Contains(targetHz))
                {
                    int fb = Display.Nearest(avail, targetHz);
                    Con.Print("警告: 目标 " + targetHz + "Hz 在该分辨率不可用，改用 " + fb + "Hz");
                    targetHz = fb;
                }

                if (dry)
                {
                    Con.Print("[dry-run] 将切换 " + fromHz + "Hz -> " + targetHz + "Hz，未执行");
                    return 0;
                }

                if (now)
                {
                    Con.Print("--now：跳过倒计时，立即切换");
                    ApplyNow(targetHz, mode, st, writeState);
                    return 0;
                }

                if (forceSilent || Session.IsLocked())
                {
                    Con.Print(forceSilent ? "静默模式（登录触发），直接切换" : "屏幕已锁定，静默切换");
                    // 登录瞬间显示驱动可能还在初始化，多给两次机会
                    ApplyNow(targetHz, mode, st, writeState, source == "logon" ? 4 : 2);
                    return 0;
                }

                if (cfg.SkipWhenFullscreen && Session.IsFullscreen())
                {
                    Con.Print("检测到全屏独占应用，本次跳过（用户在全屏时切换会闪屏）");
                    return 0;
                }

                if (cfg.Notifications)
                {
                    return RunBanner(cfg, st, mode, fromHz, targetHz, watchPower, writeState, true);
                }

                Con.Print("通知已关闭，直接切换");
                ApplyNow(targetHz, mode, st, writeState);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Warn("执行失败: " + ex.Message);
                return 1;
            }
            finally
            {
                try { mutex.ReleaseMutex(); } catch { }
                try { mutex.Dispose(); } catch { }
            }
        }

        private static int RunBanner(Settings cfg, State st, PowerMode mode, int fromHz, int toHz,
                                     bool watchPower, bool writeState, bool applyOnExpire)
        {
            BannerForm f = new BannerForm(mode, fromHz, toHz, cfg.DelaySeconds, watchPower);
            Application.Run(f);

            switch (f.Result)
            {
                case BannerForm.Outcome.Expired:
                    if (!applyOnExpire) { Con.Print("演示模式：不执行切换"); break; }
                    if (PowerDrifted(mode, writeState)) break;
                    Con.Print("倒计时结束，执行切换");
                    ApplyNow(toHz, mode, st, writeState);
                    break;
                case BannerForm.Outcome.AppliedNow:
                    if (!applyOnExpire) { Con.Print("演示模式：点击了立即切换，但不执行"); break; }
                    if (PowerDrifted(mode, writeState)) break;
                    Con.Print("用户点击「立即切换」");
                    ApplyNow(toHz, mode, st, writeState);
                    break;
                case BannerForm.Outcome.Cancelled:
                    Con.Print("用户取消，保持 " + Display.CurrentHz() + "Hz");
                    if (writeState) st.Save(mode, Display.CurrentHz(), "cancelled");
                    break;
                case BannerForm.Outcome.Reverted:
                    Con.Print("倒计时期间供电状态又变化，本次作废");
                    break;
                case BannerForm.Outcome.Dismissed:
                    Con.Print("横幅被意外关闭（Alt+F4 / 注销），本次不作数");
                    break;
            }
            return 0;
        }

        // writeState=false 意味着手动模拟，mode 不是真实供电，不能拿它复核
        private static bool PowerDrifted(PowerMode mode, bool writeState)
        {
            if (!writeState) return false;
            if (Session.GetMode() == mode) return false;
            Con.Print("决策落地前供电状态已变化，本次作废");
            return true;
        }

        private static void ApplyNow(int hz, PowerMode mode, State st, bool writeState, int attempts = 2)
        {
            int actual = 0;
            string err = null;
            bool ok = false;
            for (int i = 0; i < attempts; i++)
            {
                ok = Display.Apply(hz, out actual, out err);
                if (ok) break;
                if (i < attempts - 1)
                {
                    Log.Warn("切换失败（第 " + (i + 1) + "/" + attempts + " 次）: " + err + "，300ms 后重试");
                    Thread.Sleep(300);
                }
            }

            if (ok)
            {
                Con.Print("已切换到 " + actual + "Hz");
                if (writeState) st.Save(mode, actual, "applied");
            }
            else
            {
                Log.Warn("切换失败（共 " + attempts + " 次）: " + err);
                Con.Print("切换失败: " + err);
                if (writeState) st.Save(mode, Display.CurrentHz(), "failed");
            }
        }

        private static string JoinInts(List<int> list)
        {
            if (list.Count == 0) return "(无)";
            string[] arr = new string[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = list[i].ToString();
            return string.Join(", ", arr);
        }

        private static int CmdList()
        {
            int w = Display.CurrentWidth();
            int h = Display.CurrentHeight();
            int bpp = Display.CurrentBpp();
            Con.Print("当前: " + w + "x" + h + "@" + Display.CurrentHz() + "Hz (" + bpp + "bpp)");
            Con.Print("该分辨率下可用刷新率: " + JoinInts(Display.AvailableHz(w, h, bpp)));
            Con.Print("供电状态: " + Session.ModeText(Session.GetMode()));
            Con.Print("屏幕锁定: " + (Session.IsLocked() ? "是" : "否"));
            Con.Print("全屏独占: " + (Session.IsFullscreen() ? "是" : "否"));
            return 0;
        }

        private static int CmdStatus()
        {
            Settings cfg = Settings.Load();
            State st = State.Load();
            Con.Print("== 状态 ==");
            Con.Print("供电: " + Session.ModeText(Session.GetMode()));
            Con.Print("当前刷新率: " + Display.CurrentHz() + "Hz");
            Con.Print("配置: battery=" + cfg.BatteryHz + " ac=" + cfg.AcHz
                    + " delay=" + cfg.DelaySeconds + "s"
                    + " notify=" + (cfg.Notifications ? "on" : "off")
                    + " skipFullscreen=" + (cfg.SkipWhenFullscreen ? "on" : "off"));
            Con.Print("state: mode=" + (st.Mode.Length == 0 ? "(空)" : st.Mode)
                    + " hz=" + st.Hz + " reason=" + st.Reason + " updated=" + st.Updated);
            Con.Print("运行目录: " + Paths.Root);
            return 0;
        }
    }
}
