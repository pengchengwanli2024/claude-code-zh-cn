using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;
using System.Web.Script.Serialization;

[assembly: AssemblyFileVersion("0.2.0.0")]
[assembly: AssemblyInformationalVersion("0.2.0")]
// A separate, click-through window using the accessibility tree and local language resources.
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
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool SetProp(IntPtr window, string name, IntPtr value);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr GetProp(IntPtr window, string name);
    delegate void WinEvent(IntPtr hook, uint kind, IntPtr window, int objectId, int child, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module,
        WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [StructLayout(LayoutKind.Sequential)] struct TitleBar {
        public int Size;
        public RECT Rect;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=6)] public uint[] States;
    }
    [DllImport("user32.dll")] static extern bool GetTitleBarInfo(IntPtr window, ref TitleBar info);
    struct RECT { public int Left, Top, Right, Bottom; }

    sealed class Caption
    {
        public Rectangle Rect;
        public string Text;
        public bool LeftAlign, Multiline, Compact;
        public Color Paper, Ink;
        public Point Sample;
        public Rectangle HitRect;
        public bool Disabled, Menu;
        public bool Same(Caption other) {
            return Rect == other.Rect && Text == other.Text && LeftAlign == other.LeftAlign && Multiline == other.Multiline && Compact == other.Compact && Disabled == other.Disabled && Sample == other.Sample && HitRect == other.HitRect && Menu == other.Menu;
        }
    }

    readonly ControlTranslator machine = new ControlTranslator(Root);
    Dictionary<string,string> native = new Dictionary<string,string>();
    uint nativePid;
    readonly Dictionary<string,string> catalog;
    readonly Dictionary<string,string> common;
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly System.Windows.Forms.Timer wake = new System.Windows.Forms.Timer { Interval = 32 };
    readonly List<IntPtr> appHooks = new List<IntPtr>();
    readonly WinEvent eventCallback;
    IntPtr foregroundHook;
    uint watchedPid;
    readonly Font captionFont = new Font("Microsoft YaHei UI", 10f);
    readonly Font compactFont = new Font("Microsoft YaHei UI", 8.5f);
    readonly NotifyIcon tray = new NotifyIcon();
    List<Caption> captions = new List<Caption>();
    bool busy, paused, dark;
    IntPtr lastTarget;
    int menuCandidates;
    int emptyScans, scans, paints, hovered = -1;

    static readonly Color ClearKey = Color.FromArgb(1, 2, 3);
    static string Root { get { return AppDomain.CurrentDomain.BaseDirectory; } }

    DesktopZh()
    {
        eventCallback = OnWindowEvent;
        wake.Tick += async delegate {
            wake.Stop();
            if (busy) { wake.Start(); return; }
            await RefreshCaptions();
        };
        var json = new JavaScriptSerializer { MaxJsonLength = 16000000 };
        using (var file = File.OpenRead(Path.Combine(Root, "labels.json.gz")))
        using (var zip = new GZipStream(file, CompressionMode.Decompress))
        using (var reader = new StreamReader(zip, Encoding.UTF8))
            catalog = json.Deserialize<Dictionary<string,string>>(reader.ReadToEnd());
        common = json.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(Root, "labels-extra.json"), Encoding.UTF8));
        Text = OverlayTitle;
        AutoScaleMode = AutoScaleMode.None;
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
        timer.Interval = 240;
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

    void LoadNativeLocale(IntPtr window)
    {
        uint pid;
        GetWindowThreadProcessId(window, out pid);
        if (pid == nativePid) return;
        nativePid = pid;
        native = new Dictionary<string,string>();
        try {
            using (var process = Process.GetProcessById((int)pid)) {
                string app = Path.GetDirectoryName(process.MainModule.FileName);
                native = NativeCatalog.Load(Path.Combine(app, "resources", "app.asar"));
            }
        } catch (System.ComponentModel.Win32Exception) { }
          catch (InvalidOperationException) { }
          catch (ArgumentException) { }
    }

    void WatchApp(IntPtr window)
    {
        uint pid;
        GetWindowThreadProcessId(window, out pid);
        if (pid == watchedPid) return;
        foreach (var hook in appHooks) UnhookWinEvent(hook);
        appHooks.Clear();
        watchedPid = pid;
        // Out-of-process notifications, limited to this Claude process. The timer
        // also detects controls whose provider emits no accessibility events.
        foreach (var range in new[] { new uint[] { 4, 7 }, new uint[] { 0x8002, 0x8005 } }) {
            var hook = SetWinEventHook(range[0], range[1], IntPtr.Zero, eventCallback, pid, 0, 2);
            if (hook != IntPtr.Zero) appHooks.Add(hook);
        }
    }

    void OnWindowEvent(IntPtr hook, uint kind, IntPtr window, int objectId, int child, uint thread, uint time)
    {
        if (IsDisposed || Disposing || paused) return;
        if (kind == 3 && !IsClaude(window)) { ClearFrame(); return; }
        if (!wake.Enabled) wake.Start();
    }

    string Translate(string english, SHA256 hash)
    {
        if (String.IsNullOrEmpty(english) || english.Length > 600 || !english.Any(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))) return null;
        english = english.Trim();
        string zh;
        if (common.TryGetValue(english, out zh) || native.TryGetValue(english, out zh)) return zh;
        string fingerprint = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(english))).Replace("-", "").ToLowerInvariant();
        return catalog.TryGetValue(fingerprint, out zh) && zh != english ? zh : null;
    }

    static readonly ControlType[] Roles = {
        ControlType.Text, ControlType.Button, ControlType.MenuItem, ControlType.TabItem,
        ControlType.RadioButton, ControlType.CheckBox, ControlType.HeaderItem, ControlType.Hyperlink,
        ControlType.ListItem, ControlType.ComboBox, ControlType.Group, ControlType.Edit
    };
    internal static readonly Regex Shortcut = new Regex(@"\s+(?:(?:(?:Ctrl|Alt|Shift|Win|Cmd)\+)+(?:\S+)|F(?:[1-9]|1[0-9]|2[0-4]))$", RegexOptions.IgnoreCase);
    internal static readonly Regex ShortcutList = new Regex(@"^(.+?) ((?:Ctrl|Alt|Shift|Esc|F\d|Up or Down|1…9)(?:\S| (?:or |Up|Down))*)$");
    static readonly Regex CountLabel = new Regex(@"^(\d+) (tasks?|sessions?|web searches|installed Claude Code versions?)(\.?)$");
    static readonly Regex Used = new Regex(@"^(\d+(?:\.\d+)?%) used$");

    string ResolveLabel(string name, SHA256 hash, bool menu)
    {
        if (String.IsNullOrWhiteSpace(name)) return null;
        if (menu) name = Shortcut.Replace(name.Trim(), "").TrimEnd();
        var known = Translate(name, hash);
        if (known != null) return known;
        var version = Regex.Match(name, @"^Copy version ([0-9]+(?:\.[0-9]+){2,3}(?: \([0-9a-fA-F]+\))?) to clipboard$");
        if (version.Success) return "版本 " + version.Groups[1].Value;
        var count = CountLabel.Match(name);
        if (count.Success) {
            string kind = count.Groups[2].Value;
            string unit = kind.StartsWith("task") ? " 个任务" : kind.StartsWith("session") ? " 个会话" :
                kind == "web searches" ? " 次网页搜索" : " 个已安装的 Claude Code 版本";
            return count.Groups[1].Value + unit + (count.Groups[3].Length > 0 ? "。" : "");
        }
        var used = Used.Match(name);
        return used.Success ? "已用 " + used.Groups[1].Value : null;
    }

    internal static string LabelName(string name)
    {
        return name == null ? null : name.Replace('\u00a0', ' ').Replace('\u202f', ' ');
    }

    internal static bool Overlaps(Rectangle first, Rectangle second)
    {
        // UIA uses fractional screen coordinates. Rounding two adjacent rows can
        // give them an overlapping pixel at 125% or 150% Windows display scaling.
        var overlap = Rectangle.Intersect(first, second);
        return overlap.Width > 2 && overlap.Height > 2;
    }

    static TextFormatFlags Flags(bool left, bool multiline)
    {
        return TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
            (multiline ? TextFormatFlags.WordBreak : TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine) |
            (left ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
    }

    // Reserve the native check/icon gutter, submenu arrow, and accelerator column.
    // Small centered buttons retain their original footprint. Text blocks keep their line breaks.
    Rectangle LabelBounds(Rectangle row, string original, string chinese, ControlType role, bool menu, bool checkGutter, float scale)
    {
        int line = (int)(22 * scale);
        if (role == ControlType.Text || role == ControlType.Group || role == ControlType.Hyperlink) return row;
        int left = 0, right = 0;
        if (menu) {
            left = (int)((checkGutter ? 45 : 18) * scale);
            right = (int)(30 * scale);
            var shortcut = Shortcut.Match(original);
            if (shortcut.Success) {
                int keys = TextRenderer.MeasureText(shortcut.Value.Trim(), captionFont).Width;
                right = keys + (int)(14 * scale);
            }
        } else if (role == ControlType.ComboBox) {
            left = (int)((original == "Search" ? 34 : 8) * scale); right = (int)(22 * scale);
        } else if (row.Width > 130 * scale && row.Height >= line) {
            left = (int)(38 * scale); right = (int)(6 * scale);
        }
        int available = row.Width - left - right;
        if (available < 10 * scale) return Rectangle.Empty;
        if (menu) {
            int originalWidth = TextRenderer.MeasureText(Shortcut.Replace(original, ""), captionFont).Width;
            int zhWidth = TextRenderer.MeasureText(chinese, captionFont).Width;
            available = Math.Min(available, Math.Max(originalWidth, zhWidth) + (int)(5 * scale));
        }
        return new Rectangle(row.X + left, row.Y, available, row.Height);
    }

    List<Caption> ReadLabels(IntPtr window, bool useMachine = true)
    {
        LoadNativeLocale(window);
        var result = new List<Caption>();
        var menuRows = new HashSet<System.Windows.Rect>();
        var root = AutomationElement.FromHandle(window);
        bool about = root.Current.Name == "About Claude";
        var request = new CacheRequest();
        request.Add(AutomationElement.NameProperty);
        request.Add(AutomationElement.ControlTypeProperty);
        request.Add(AutomationElement.BoundingRectangleProperty);
        request.Add(AutomationElement.IsOffscreenProperty);
        request.Add(AutomationElement.LocalizedControlTypeProperty);
        request.Add(AutomationElement.IsPasswordProperty);
        request.Add(AutomationElement.IsEnabledProperty);
        float scale = Math.Max(1f, GetDpiForWindow(window) / 96f);
        var roles = new OrCondition(Roles.Select(r => (Condition)new PropertyCondition(AutomationElement.ControlTypeProperty,r)).ToArray());
        // Popup menus can extend outside their parent's client rectangle. A menu has priority
        // over controls behind it while the surrounding page keeps its translated frame.
        var menus = root.FindAll(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu),
            new PropertyCondition(AutomationElement.IsOffscreenProperty, false)));
        var popupList = menus.Cast<AutomationElement>().ToList();
        var popupBounds = popupList.Select(m => m.Current.BoundingRectangle).Where(b => !b.IsEmpty).ToList();
        var scopes = new[] {root}.Concat(popupList);

        using (var hash = SHA256.Create())
        using (request.Activate()) {
            foreach (var scope in scopes) {
                var nodes = scope.FindAll(TreeScope.Descendants, roles).Cast<AutomationElement>().ToArray();
                bool popup = !object.ReferenceEquals(scope, root);
                bool checkGutter = popup && nodes.Any(n => n.Cached.ControlType == ControlType.CheckBox || n.Cached.ControlType == ControlType.RadioButton);
                var scopeRect = scope.Current.BoundingRectangle;
                foreach (AutomationElement node in nodes) {
                    var p = node.Cached;
                    if (p.IsOffscreen || p.IsPassword) continue;
                    var r = p.BoundingRectangle;
                    if (r.IsEmpty || r.Width < 12 || r.Height < 9 || r.Height > 240 * scale) continue;
                    if (!scopeRect.Contains(new System.Windows.Point(r.Left + r.Width / 2, r.Top + r.Height / 2))) continue;
                    if (object.ReferenceEquals(scope, root) && popupBounds.Any(b => b.IntersectsWith(r))) continue;
                    var role = p.ControlType;
                    if (role == ControlType.Group && !Regex.IsMatch(p.LocalizedControlType ?? "", @"^(heading|标题|章节标题|paragraph|段落)$", RegexOptions.IgnoreCase)) continue;
                    string name = LabelName(p.Name);
                    bool placeholder = role == ControlType.Edit || (role == ControlType.ComboBox &&
                        (p.Name == "Search settings" || p.Name == "Search sessions and projects"));
                    if (placeholder) {
                        if (name != "Search shortcuts" && name != "Search settings" && name != "Search sessions and projects") continue;
                        if (!String.Equals(node.GetCurrentPropertyValue(ValuePattern.ValueProperty, true) as string, "")) continue;
                        if (name == "Search settings") name = "Search";
                    } else if (role == ControlType.ComboBox) {
                        if (name == null || !common.ContainsKey(name)) continue;
                        // Only resolve a selected value for a catalogued settings control.
                        name = node.GetCurrentPropertyValue(ValuePattern.ValueProperty, true) as string;
                        if (String.IsNullOrWhiteSpace(name)) continue;
                    }
                    bool menu = !object.ReferenceEquals(scope, root) || role == ControlType.MenuItem;
                    var keys = role == ControlType.ListItem && name != null ? ShortcutList.Match(name) : Match.Empty;
                    if (keys.Success && common.ContainsKey(keys.Groups[1].Value)) name = keys.Groups[1].Value;
                    if (menu && (role == ControlType.MenuItem || role == ControlType.RadioButton || role == ControlType.CheckBox)) menuRows.Add(r);
                    string text = ResolveLabel(name, hash, menu);
                    DateTime date;
                    if (text == null && about && role == ControlType.Text && DateTime.TryParseExact(name,
                        new[] { "MMM d, yyyy, h:mm tt", "MMMM d, yyyy, h:mm tt" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                        text = date.ToString("yyyy年M月d日 HH:mm", CultureInfo.InvariantCulture);
                    if (text == null && useMachine && !String.IsNullOrWhiteSpace(name)) {
                        machine.Consider(name, role);
                        text = machine.Get(name);
                    }
                    if (String.IsNullOrWhiteSpace(text)) continue;
                    var row = Rectangle.FromLTRB((int)Math.Floor(r.Left), (int)Math.Floor(r.Top),
                        (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom));
                    if (role != ControlType.Text && row.Width < 41 * scale && !menu) continue;
                    if (role != ControlType.Text && role != ControlType.Hyperlink && row.Height > 65 * scale) continue;
                    var box = LabelBounds(row, name, text, role, menu, checkGutter, scale);
                    if (keys.Success && common.ContainsKey(name)) {
                        int reserved = Math.Max((int)(45 * scale), TextRenderer.MeasureText(keys.Groups[2].Value, captionFont).Width + (int)(48 * scale));
                        int labelWidth = Math.Max(TextRenderer.MeasureText(name, captionFont).Width, TextRenderer.MeasureText(text, captionFont).Width) + (int)(8 * scale);
                        int width = Math.Min(labelWidth, row.Width - reserved);
                        if (width <= 0) continue;
                        box = new Rectangle(row.X, row.Y + 1, width, row.Height - 2);
                    }
                    if (placeholder) {
                        // The UIA edit bounds can include its search icon. Leave the native focus ring and caret visible.
                        int inset = (int)((role == ControlType.ComboBox ? 34 : 24) * scale);
                        if (row.Width <= inset + 16 || row.Height < 9) continue;
                        box = new Rectangle(row.X + inset, row.Y + 3, row.Width - inset - 10, row.Height - 6);
                    }
                    if (box.IsEmpty) continue;
                    bool multi = text.Contains("\n") || (role == ControlType.Text || role == ControlType.Group) && row.Height >= 30 * scale && (name.Length > 50 || text.Length > 28);
                    bool left = menu || multi || row.Width > 130 * scale || role == ControlType.Text || role == ControlType.Group;
                    // Never obscure the original with a truncated translation (especially a permission warning).
                    var limit = new Size(multi ? box.Width : 32000, 32000);
                    var measure = Flags(left, multi) & ~TextFormatFlags.VerticalCenter;
                    var size = TextRenderer.MeasureText(text, captionFont, limit, measure);
                    bool compact = size.Width > box.Width + 1 || size.Height > box.Height + 3;
                    if (compact) size = TextRenderer.MeasureText(text, compactFont, limit, measure);
                    if (size.Width > box.Width + 1 || size.Height > box.Height + 3) continue;
                    var sample = new Point(menu ? row.Right - (int)(5 * scale) : box.X != row.X ? row.Left + (int)(5 * scale) : row.Left - 2,
                        menu ? row.Top + row.Height / 2 : row.Top + 2);
                    result.Add(new Caption { Rect = box, Text = text, LeftAlign = left, Multiline = multi,
                        Compact = compact, Disabled = !p.IsEnabled, Menu = menu, Sample = sample, HitRect = row });
                }
            }
        }
        if (about) {
            var bar = new TitleBar { Size = Marshal.SizeOf(typeof(TitleBar)), States = new uint[6] };
            if (GetTitleBarInfo(window, ref bar) && bar.Rect.Bottom > bar.Rect.Top && (bar.States[0] & 0x8000) == 0) {
                var box = new Rectangle(bar.Rect.Left + (int)(27*scale), bar.Rect.Top,
                    (int)(112*scale), bar.Rect.Bottom - bar.Rect.Top);
                result.Add(new Caption { Rect = box, HitRect = box, Text = "关于 Claude", LeftAlign = true,
                    Sample = new Point(box.Right+2, box.Top+box.Height/2) });
            }
        }
        menuCandidates = menuRows.Count;
        if (useMachine) machine.TranslatePending();
        var distinct = new List<Caption>();
        foreach (var item in result.OrderBy(c => (long)c.Rect.Width * c.Rect.Height))
            if (!distinct.Any(c => Overlaps(c.Rect, item.Rect))) distinct.Add(item);
        return distinct.OrderBy(c => c.Rect.Top).ThenBy(c => c.Rect.Left).ThenBy(c => c.Text, StringComparer.Ordinal).ToList();
    }

    // Sample an unpainted edge of the original row, so native backgrounds
    // (including hover/selection) show through. Pixels are local and are never logged or saved.
    void SetColors(Caption label, IntPtr dc)
    {
        uint pixel = GetPixel(dc, label.Sample.X, label.Sample.Y);
        if (pixel == 0xffffffff) label.Paper = dark ? Color.FromArgb(43,43,43) : Color.FromArgb(248,248,246);
        else label.Paper = Color.FromArgb((int)(pixel & 255), (int)((pixel >> 8) & 255), (int)((pixel >> 16) & 255));
        label.Ink = (label.Paper.R * 299 + label.Paper.G * 587 + label.Paper.B * 114) < 145000 ?
            Color.WhiteSmoke : Color.FromArgb(32,32,32);
        if (label.Disabled) label.Ink = Color.FromArgb((label.Ink.R + label.Paper.R)/2, (label.Ink.G + label.Paper.G)/2, (label.Ink.B + label.Paper.B)/2);
    }

    void ClearFrame()
    {
        if (Visible) Hide();
        captions.Clear();
        lastTarget = IntPtr.Zero;
        emptyScans = 0;
        hovered = -1;
    }

    async Task RefreshCaptions()
    {
        var target = GetForegroundWindow();
        if (paused || !IsClaude(target)) { ClearFrame(); return; }
        if (busy) return;
        WatchApp(target);
        busy = true;
        try {
            RECT rect;
            if (!GetWindowRect(target, out rect)) return;
            var next = await Task.Run(() => ReadLabels(target));
            if (IsDisposed) return;
            if (paused || target != GetForegroundWindow()) { ClearFrame(); return; }
            if (next.Count == 0) {
                if (++emptyScans >= 2 || lastTarget != target) ClearFrame();
                return;
            }
            emptyScans = 0;
            var area = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            var desktop = SystemInformation.VirtualScreen;
            next.RemoveAll(c => !desktop.IntersectsWith(c.Rect));
            foreach (var c in next) area = Rectangle.Union(area, Rectangle.Intersect(c.Rect, desktop));
            area = Rectangle.Intersect(area, desktop);
            if (area.IsEmpty) { ClearFrame(); return; }
            SetProp(Handle, "ZhUiScans", new IntPtr(++scans));
            bool moved = !Visible || Bounds != area || target != lastTarget;
            int hover = next.FindIndex(c => c.HitRect.Contains(Cursor.Position));
            bool equal = !moved && next.Count == captions.Count && next.Zip(captions, (a,b) => a.Same(b)).All(x => x);
            var dc = GetDC(IntPtr.Zero);
            try {
                uint pixel = GetPixel(dc, area.Left + area.Width / 2, area.Top + 10);
                bool night = pixel == 0xffffffff ? dark : ((pixel & 255) + ((pixel >> 8) & 255) + ((pixel >> 16) & 255)) < 300;
                bool themeChanged = night != dark;
                dark = night;
                if (equal && !themeChanged) {
                    foreach (var c in captions.Where(c => c.Menu)) {
                        var previous = c.Paper;
                        SetColors(c, dc);
                        if (previous != c.Paper) Invalidate(RectangleToClient(c.Rect));
                    }
                    if (hover == hovered) { timer.Interval = 560; return; }
                }
                if (equal && !themeChanged) {
                    foreach (int i in new[] { hovered, hover }) if (i >= 0) {
                        SetColors(captions[i], dc);
                        Invalidate(RectangleToClient(captions[i].Rect));
                    }
                    hovered = hover;
                    return;
                }
                foreach (var c in next) {
                    var old = !moved && !themeChanged ? captions.Find(prev => c.Same(prev)) : null;
                    if (old == null) SetColors(c, dc);
                    else { c.Paper = old.Paper; c.Ink = old.Ink; }
                }
                if (!moved && !themeChanged) {
                    foreach (var c in captions.Where(p => !next.Any(n => n.Same(p)))) Invalidate(RectangleToClient(c.Rect));
                    foreach (var c in next.Where(n => !captions.Any(p => n.Same(p)))) Invalidate(RectangleToClient(c.Rect));
                } else {
                    if (Bounds != area) Bounds = area;
                    Invalidate();
                }
                timer.Interval = 240;
                captions = next;
                hovered = hover;
                lastTarget = target;
                if (!Visible) Show();
                // Electron places freshly opened popup HWNDs above existing topmost windows.
                SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
            } finally { ReleaseDC(IntPtr.Zero, dc); }
        } catch (ElementNotAvailableException) { if (target != lastTarget || ++emptyScans >= 2) ClearFrame(); }
          catch (COMException) { if (target != lastTarget || ++emptyScans >= 2) ClearFrame(); }
          catch (InvalidOperationException) { if (!IsDisposed) ClearFrame(); }
        finally { busy = false; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        SetProp(Handle, "ZhUiPaints", new IntPtr(++paints));
        foreach (var caption in captions) {
            var r = caption.Rect;
            r.Offset(-Left,-Top);
            if (!e.ClipRectangle.IntersectsWith(r)) continue;
            using (var brush = new SolidBrush(caption.Paper)) e.Graphics.FillRectangle(brush,r);
            TextRenderer.DrawText(e.Graphics,caption.Text,caption.Compact ? compactFont : captionFont,r,caption.Ink,Flags(caption.LeftAlign, caption.Multiline));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
            foreach (var hook in appHooks) UnhookWinEvent(hook);
            wake.Dispose(); timer.Dispose(); tray.Dispose(); captionFont.Dispose(); compactFont.Dispose(); }
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
                        var fg = GetForegroundWindow();
                        var matches = overlay.ReadLabels(IsClaude(fg) ? fg : p.MainWindowHandle, false);
                        var viewer = FindWindow(null, OverlayTitle);
                        var report = new { nativeLabels = overlay.native.Count, matched = matches.Count, menuCandidates = overlay.menuCandidates, menuTranslated = matches.Count(c => c.Menu), visible = IsWindowVisible(viewer), scans = GetProp(viewer,"ZhUiScans").ToInt32(), paints = GetProp(viewer,"ZhUiPaints").ToInt32(), examples = matches.Select(m=>m.Text)
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
                overlay.foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, overlay.eventCallback, 0, 0, 2);
                overlay.timer.Start();
                // Create a UI message loop even while the application is in the background.
                var ignored = overlay.Handle;
                Application.Run();
            }
        }
        return 0;
    }
}
