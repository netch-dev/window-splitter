using System.Diagnostics;
using Microsoft.Win32;

namespace WindowSplitter;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "WindowSplitter.SingleInstance", out bool first);
        if (!first) { MessageBox.Show("Window Splitter is already running - look for its icon in the system tray (click the ^ arrow near the clock).", "Window Splitter"); return; }
        ApplicationConfiguration.Initialize();
        var app = new TrayApp();
        app.ReportFailures();
        Application.Run(app);
    }
}

class TrayApp : ApplicationContext
{
    const uint ALT = 1, CTRL = 2, NOREPEAT = 0x4000, MODS = ALT | CTRL | NOREPEAT;
    const int WM_HOTKEY = 0x0312;
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    readonly NotifyIcon tray;
    readonly HotkeyWindow hwnd;
    readonly Dictionary<int, Action> hotkeys = new();
    Config config = Config.Load();
    string? lastWorkspace;
    int nextId = 1;

    public TrayApp()
    {
        hwnd = new HotkeyWindow(id => { if (hotkeys.TryGetValue(id, out var a)) a(); });

        // Snap hotkeys: Ctrl+Alt + key
        Snap(Keys.Left, 0, 0, .5, 1);
        Snap(Keys.Right, .5, 0, .5, 1);
        Snap(Keys.Up, 0, 0, 1, .5);
        Snap(Keys.Down, 0, .5, 1, .5);
        Snap(Keys.U, 0, 0, .5, .5);
        Snap(Keys.I, .5, 0, .5, .5);
        Snap(Keys.J, 0, .5, .5, .5);
        Snap(Keys.K, .5, .5, .5, .5);
        Snap(Keys.D1, 0, 0, 1 / 3.0, 1);
        Snap(Keys.D2, 1 / 3.0, 0, 1 / 3.0, 1);
        Snap(Keys.D3, 2 / 3.0, 0, 1 / 3.0, 1);
        Snap(Keys.Enter, 0, 0, 1, 1);
        Snap(Keys.C, .2, .1, .6, .8);

        // Workspaces
        Register(Keys.W, () => ApplyLast());
        Register(Keys.S, SavePrompt);
        for (int i = 0; i < 9; i++)
        {
            int idx = i;
            Register(Keys.F1 + i, () => { if (idx < config.Workspaces.Count) _ = Apply(config.Workspaces[idx]); });
        }

        tray = new NotifyIcon
        {
            Icon = TrayIcon.Create(),
            Text = "Window Splitter",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };
        tray.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        tray.DoubleClick += (_, _) => ApplyLast();
    }

    void Register(Keys key, Action action)
    {
        int id = nextId++;
        if (Native.RegisterHotKey(hwnd.Handle, id, MODS, (uint)key)) hotkeys[id] = action;
        else failed.Add(key.ToString());
    }
    readonly List<string> failed = new();

    void Snap(Keys key, double x, double y, double w, double h) => Register(key, () =>
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || !Native.IsAppWindow(fg)) return;
        var wa = Screen.FromHandle(fg).WorkingArea;
        Native.PlaceVisible(fg, wa.Left + (int)Math.Round(x * wa.Width), wa.Top + (int)Math.Round(y * wa.Height),
            (int)Math.Round(w * wa.Width), (int)Math.Round(h * wa.Height));
    });

    void BuildMenu()
    {
        var m = tray.ContextMenuStrip!;
        m.Items.Clear();
        config = Config.Load();
        if (config.Workspaces.Count == 0) m.Items.Add(new ToolStripMenuItem("No workspaces yet - arrange windows, then Save") { Enabled = false });
        for (int i = 0; i < config.Workspaces.Count; i++)
        {
            var ws = config.Workspaces[i];
            string hk = i < 9 ? $"  (Ctrl+Alt+F{i + 1})" : "";
            m.Items.Add($"Open: {ws.Name}{hk}", null, (_, _) => _ = Apply(ws));
        }
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Save current windows as workspace...  (Ctrl+Alt+S)", null, (_, _) => SavePrompt());
        m.Items.Add("Edit config file", null, (_, _) =>
        {
            if (!File.Exists(Config.FilePath)) config.Save();
            Process.Start(new ProcessStartInfo(Config.FilePath) { UseShellExecute = true });
        });
        var startup = new ToolStripMenuItem("Start with Windows") { Checked = IsStartup() };
        startup.Click += (_, _) => SetStartup(!startup.Checked);
        m.Items.Add(startup);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Exit", null, (_, _) => { tray.Visible = false; ExitThread(); });
    }

    static bool IsStartup()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue("WindowSplitter") != null;
    }

    static void SetStartup(bool on)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, true)!;
        if (on) k.SetValue("WindowSplitter", $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue("WindowSplitter", false);
    }

    void Notify(string text) => tray.ShowBalloonTip(2500, "Window Splitter", text, ToolTipIcon.Info);

    void ApplyLast()
    {
        config = Config.Load();
        var ws = config.Workspaces.FirstOrDefault(w => w.Name == lastWorkspace) ?? config.Workspaces.FirstOrDefault();
        if (ws == null) { Notify("No workspace saved yet. Arrange your windows and press Ctrl+Alt+S."); return; }
        _ = Apply(ws);
    }

    void SavePrompt()
    {
        int self = Environment.ProcessId;
        var cands = new List<(IntPtr h, string proc, string label)>();
        foreach (var h in Native.AppWindows().AsEnumerable().Reverse()) // bottom of z-order first
        {
            if (Native.IsIconic(h)) continue;
            using var p = Native.ProcessOf(h);
            if (p == null || p.Id == self) continue;
            cands.Add((h, p.ProcessName, $"{p.ProcessName}  -  {Native.Title(h)}"));
        }

        var picked = SaveDialog.Ask(lastWorkspace ?? "Main", cands.Select(c => c.label).ToList());
        if (picked == null) return;
        string name = picked.Value.name;

        var wa = Screen.PrimaryScreen!.WorkingArea;
        var slots = new List<Slot>();
        foreach (int i in picked.Value.indexes)
        {
            var (h, proc, _) = cands[i];
            var r = Native.VisibleRect(h);
            var slot = new Slot
            {
                Process = proc,
                X = Math.Round((double)(r.Left - wa.Left) / wa.Width, 4),
                Y = Math.Round((double)(r.Top - wa.Top) / wa.Height, 4),
                W = Math.Round((double)(r.Right - r.Left) / wa.Width, 4),
                H = Math.Round((double)(r.Bottom - r.Top) / wa.Height, 4),
            };
            if (proc.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                slot.TitleContains = Native.Title(h);
            else if (!proc.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                using (var p = Native.ProcessOf(h)) { try { slot.Launch = p?.MainModule?.FileName ?? ""; } catch { } }
            slots.Add(slot);
        }

        config = Config.Load();
        config.Workspaces.RemoveAll(w => w.Name == name);
        config.Workspaces.Add(new Workspace { Name = name, Slots = slots });
        config.Save();
        lastWorkspace = name;
        Notify($"Saved \"{name}\" with {slots.Count} windows.");
    }

    async Task Apply(Workspace ws)
    {
        lastWorkspace = ws.Name;
        var wa = Screen.PrimaryScreen!.WorkingArea;
        var used = new HashSet<IntPtr>();
        var placed = new List<IntPtr>();

        IntPtr Find(Slot s)
        {
            foreach (var h in Native.AppWindows())
            {
                if (used.Contains(h)) continue;
                using var p = Native.ProcessOf(h);
                if (p == null || !p.ProcessName.Equals(s.Process, StringComparison.OrdinalIgnoreCase)) continue;
                if (s.TitleContains.Length > 0 && !Native.Title(h).Contains(s.TitleContains, StringComparison.OrdinalIgnoreCase)) continue;
                return h;
            }
            return IntPtr.Zero;
        }

        // Launch anything that isn't open yet
        var pending = new List<Slot>();
        foreach (var s in ws.Slots)
        {
            if (Find(s) == IntPtr.Zero && s.Launch.Length > 0)
            {
                try { Process.Start(new ProcessStartInfo(s.Launch, s.Args) { UseShellExecute = true }); } catch { }
            }
        }

        foreach (var s in ws.Slots)
        {
            IntPtr h = Find(s);
            for (int i = 0; h == IntPtr.Zero && s.Launch.Length > 0 && i < 40; i++) // wait up to ~10s
            {
                await Task.Delay(250);
                h = Find(s);
            }
            if (h == IntPtr.Zero) continue;
            used.Add(h);
            placed.Add(h);
            Native.PlaceVisible(h,
                wa.Left + (int)Math.Round(s.X * wa.Width), wa.Top + (int)Math.Round(s.Y * wa.Height),
                (int)Math.Round(s.W * wa.Width), (int)Math.Round(s.H * wa.Height));
        }

        // Bring everything to the front, preserving the saved stacking order
        foreach (var h in placed) Native.Activate(h);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var id in hotkeys.Keys) Native.UnregisterHotKey(hwnd.Handle, id);
            tray.Dispose();
            hwnd.DestroyHandle();
        }
        base.Dispose(disposing);
    }

    public void ReportFailures()
    {
        Notify(failed.Count > 0 ? "Hotkeys already used by another app: Ctrl+Alt+" + string.Join(", ", failed) : "Running in the tray. Ctrl+Alt+S saves a layout, Ctrl+Alt+W opens it.");
    }

    class HotkeyWindow : NativeWindow
    {
        readonly Action<int> onHotkey;
        public HotkeyWindow(Action<int> onHotkey)
        {
            this.onHotkey = onHotkey;
            CreateHandle(new CreateParams());
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY) onHotkey((int)m.WParam);
            base.WndProc(ref m);
        }
    }
}

static class SaveDialog
{
    public static (string name, List<int> indexes)? Ask(string def, List<string> items)
    {
        using var f = new Form
        {
            Text = "Save workspace", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false, MinimizeBox = false, ClientSize = new Size(520, 380), TopMost = true
        };
        var l = new Label { Text = "Workspace name (same name overwrites):", Left = 12, Top = 10, Width = 496 };
        var tb = new TextBox { Text = def, Left = 12, Top = 32, Width = 496 };
        var l2 = new Label { Text = "Windows to include:", Left = 12, Top = 62, Width = 496 };
        var list = new CheckedListBox { Left = 12, Top = 84, Width = 496, Height = 250, CheckOnClick = true };
        foreach (var it in items) list.Items.Add(it, true);
        var ok = new Button { Text = "Save", Left = 352, Top = 346, Width = 75, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 433, Top = 346, Width = 75, DialogResult = DialogResult.Cancel };
        var none = new Button { Text = "Select none", Left = 12, Top = 346, Width = 90 };
        none.Click += (_, _) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, false); };
        f.Controls.AddRange(new Control[] { l, tb, l2, list, ok, cancel, none });
        f.AcceptButton = ok; f.CancelButton = cancel;
        f.Shown += (_, _) => { f.Activate(); tb.SelectAll(); tb.Focus(); };
        if (f.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(tb.Text)) return null;
        return (tb.Text.Trim(), list.CheckedIndices.Cast<int>().ToList());
    }
}

static class TrayIcon
{
    // A window split into panes: blue frame, one tall pane on the left, two stacked on the right.
    public static Icon Create()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var frame = new SolidBrush(Color.FromArgb(0x1F, 0x6F, 0xEB));
            using var pane = new SolidBrush(Color.White);
            using var accent = new SolidBrush(Color.FromArgb(0xFF, 0xB9, 0x00));
            g.FillRectangle(frame, 1, 3, 30, 26);
            g.FillRectangle(accent, 3, 5, 12, 22);
            g.FillRectangle(pane, 17, 5, 12, 10);
            g.FillRectangle(pane, 17, 17, 12, 10);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
