using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;
using System.Web.Script.Serialization;

// A separate, click-through window. It never loads code into Claude or opens its files.
sealed class DesktopZh : Form
{
    const string OverlayTitle = "Claude Code 中文显示";
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string className, string windowName);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    const string Family = "Claude_pzs8sxrjxfjjc";
    const string AppId = "shell:AppsFolder\\Claude_pzs8sxrjxfjjc!Claude";
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern int GetPackageFamilyName(IntPtr process, ref uint length, StringBuilder name);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
    struct RECT { public int Left, Top, Right, Bottom; }

    sealed class Caption
    {
        public Rectangle Rect;
        public string Text;
        public bool LeftAlign;
    }

    readonly ControlTranslator machine = new ControlTranslator(Root);
    readonly Dictionary<string,string> catalog;
    readonly Dictionary<string,string> common;
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly Font captionFont = new Font("Microsoft YaHei UI", 10f);
    readonly NotifyIcon tray = new NotifyIcon();
    List<Caption> captions = new List<Caption>();
    bool busy, paused, dark;
    int observed;

    static readonly Color ClearKey = Color.FromArgb(1, 2, 3);
    static string Root { get { return AppDomain.CurrentDomain.BaseDirectory; } }

    DesktopZh()
    {
        var json = new JavaScriptSerializer { MaxJsonLength = 16000000 };
        using (var file = File.OpenRead(Path.Combine(Root, "labels.json.gz")))
        using (var zip = new GZipStream(file, CompressionMode.Decompress))
        using (var reader = new StreamReader(zip, Encoding.UTF8))
            catalog = json.Deserialize<Dictionary<string,string>>(reader.ReadToEnd());
        common = json.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(Root, "labels-extra.json"), Encoding.UTF8));
        Text = OverlayTitle;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = ClearKey;
        TransparencyKey = ClearKey;
        DoubleBuffered = true;
        TopMost = true;
        var menu = new ContextMenuStrip();
        var toggle = menu.Items.Add("暂停中文显示");
        toggle.Click += delegate {
            paused = !paused;
            toggle.Text = paused ? "恢复中文显示" : "暂停中文显示";
            if (paused) Hide();
        };
        menu.Items.Add("退出中文显示", null, delegate { Close(); Application.ExitThread(); });
        tray.Icon = SystemIcons.Application;
        tray.Text = "Claude Code 中文显示";
        tray.ContextMenuStrip = menu;
        timer.Interval = 600;
        timer.Tick += async delegate { await RefreshCaptions(); };
    }

    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams {
        get {
            var p = base.CreateParams;
            p.ExStyle |= 0x08000000 | 0x00000020 | 0x00080000 | 0x00000080;
            return p;
        }
    }

    static bool IsClaude(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        uint pid;
        GetWindowThreadProcessId(window, out pid);
        try {
            using (var process = Process.GetProcessById((int)pid)) {
                if (!process.ProcessName.Equals("claude", StringComparison.OrdinalIgnoreCase)) return false;
                uint length = 256;
                var name = new StringBuilder((int)length);
                return GetPackageFamilyName(process.Handle, ref length, name) == 0 && name.ToString() == Family;
            }
        } catch (System.ComponentModel.Win32Exception) { return false; }
          catch (ArgumentException) { return false; }
          catch (InvalidOperationException) { return false; }
    }

    string Translate(string english, SHA256 hash)
    {
        if (String.IsNullOrEmpty(english) || english.Length > 600 || !english.Any(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))) return null;
        english = english.Trim();
        string zh;
        if (common.TryGetValue(english, out zh)) return zh;
        string fingerprint = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(english))).Replace("-", "").ToLowerInvariant();
        return catalog.TryGetValue(fingerprint, out zh) && zh != english ? zh : null;
    }

    static readonly ControlType[] Roles = {
        ControlType.Text, ControlType.Button, ControlType.MenuItem, ControlType.TabItem,
        ControlType.RadioButton, ControlType.CheckBox, ControlType.HeaderItem, ControlType.Hyperlink,
        ControlType.ListItem
    };

    List<Caption> ReadLabels(IntPtr window, bool useMachine = true)
    {
        var result = new List<Caption>();
        var root = AutomationElement.FromHandle(window);
        var request = new CacheRequest();
        request.Add(AutomationElement.NameProperty);
        request.Add(AutomationElement.ControlTypeProperty);
        request.Add(AutomationElement.BoundingRectangleProperty);
        request.Add(AutomationElement.IsOffscreenProperty);
        var roles = new OrCondition(Roles.Select(r => (Condition)new PropertyCondition(AutomationElement.ControlTypeProperty,r)).ToArray());
        using (var hash = SHA256.Create())
        using (request.Activate()) {
            var nodes = root.FindAll(TreeScope.Descendants, roles);
            foreach (AutomationElement node in nodes) {
                var p = node.Cached;
                if (p.IsOffscreen) continue;
                var r = p.BoundingRectangle;
                if (r.IsEmpty || r.Width < 26 || r.Height < 10 || r.Height > 76) continue;
                string text = Translate(p.Name, hash);
                if (text == null && useMachine && !String.IsNullOrEmpty(p.Name)) {
                    machine.Consider(p.Name, p.ControlType);
                    text = machine.Get(p.Name);
                }
                if (String.IsNullOrWhiteSpace(text)) continue;
                var bounds = Rectangle.FromLTRB((int)r.Left, (int)r.Top, (int)r.Right, (int)r.Bottom);
                if (p.ControlType != ControlType.Text && bounds.Width < 46) continue;
                // A large container may expose the same label as its child. Prefer the smaller text range.
                result.Add(new Caption { Rect = bounds, Text = text, LeftAlign = r.Width > 130 });
            }
        }
        if (useMachine) machine.TranslatePending();
        var distinct = new List<Caption>();
        foreach (var item in result.OrderBy(c => c.Rect.Width * c.Rect.Height))
            if (!distinct.Any(c => c.Rect.IntersectsWith(item.Rect))) distinct.Add(item);
        return distinct;
    }

    async Task RefreshCaptions()
    {
        var target = GetForegroundWindow();
        if (paused || !IsClaude(target)) { if (Visible) Hide(); return; }
        if (busy) return;
        busy = true;
        try {
            RECT rect;
            if (!GetWindowRect(target, out rect)) return;
            var next = await Task.Run(() => ReadLabels(target));
            if (paused || target != GetForegroundWindow() || IsDisposed) { Hide(); return; }
            var area = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            next.RemoveAll(c => !area.Contains(c.Rect));
            captions = next;
            observed = next.Count;
            if (observed == 0) { Hide(); return; }
            var dc = GetDC(IntPtr.Zero);
            try {
                uint pixel = GetPixel(dc, area.Left + area.Width / 2, area.Top + 15);
                dark = pixel != 0xffffffff && ((pixel & 255) + ((pixel >> 8) & 255) + ((pixel >> 16) & 255)) < 300;
            } finally { ReleaseDC(IntPtr.Zero, dc); }
            Bounds = area;
            if (!Visible) Show();
            Invalidate();
        } catch (ElementNotAvailableException) { Hide(); }
          catch (COMException) { Hide(); }
          catch (InvalidOperationException) { Hide(); }
        finally { busy = false; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var paper = dark ? Color.FromArgb(43,43,43) : Color.FromArgb(248,248,246);
        var ink = dark ? Color.WhiteSmoke : Color.FromArgb(33,33,32);
        using (var brush = new SolidBrush(paper)) {
            foreach (var caption in captions) {
                var r = caption.Rect;
                r.Offset(-Left,-Top);
                e.Graphics.FillRectangle(brush,r);
                var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | (caption.LeftAlign ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(e.Graphics,caption.Text,captionFont,r,ink,flags);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); tray.Dispose(); captionFont.Dispose(); }
        base.Dispose(disposing);
    }

    [STAThread]
    static int Main(string[] args)
    {
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) {}
        Application.EnableVisualStyles();
        using (var overlay = new DesktopZh()) {
            if (args.Contains("--probe")) {
                foreach (var p in Process.GetProcessesByName("claude")) {
                    using (p) {
                        if (!IsClaude(p.MainWindowHandle)) continue;
                        var matches = overlay.ReadLabels(p.MainWindowHandle, false);
                        var report = new { matched = matches.Count, visible = IsWindowVisible(FindWindow(null, OverlayTitle)), examples = matches.Select(m=>m.Text)
                            .Where(v => overlay.common.Values.Contains(v)).Distinct().Take(12).ToArray() };
                        using (var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)))
                            output.WriteLine(new JavaScriptSerializer().Serialize(report));
                        return matches.Count > 0 ? 0 : 2;
                    }
                }
                return 3;
            }
            if (args.Contains("--launch")) Process.Start("explorer.exe", AppId);
            using (var mutex = new System.Threading.Mutex(false, "Local\\ZhUiDesktopOverlay")) {
                if (!mutex.WaitOne(0)) return 0;
                overlay.tray.Visible = true;
                overlay.timer.Start();
                // Create a UI message loop even while the application is in the background.
                var ignored = overlay.Handle;
                Application.Run();
            }
        }
        return 0;
    }
}
