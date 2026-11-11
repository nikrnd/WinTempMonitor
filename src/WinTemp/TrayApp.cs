using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace WinTemp;

public sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly SensorReader _reader;
    private readonly HoverPopup _popup;
    private readonly Settings _settings;
    private readonly System.Windows.Forms.Timer _uiTimer;
    private readonly System.Windows.Forms.Timer _hoverTimer;
    private readonly System.Windows.Forms.Timer _promoteTimer;
    private readonly ToolStripMenuItem _pinItem, _trayVisibleItem, _autostartItem;
    private readonly SynchronizationContext _ui;

    private Icon? _icon;
    private string _iconKey = "";
    private Point _hoverAnchor;
    private int _promoteAttempts;

    public TrayApp()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _settings = Settings.Load();

        _popup = new HoverPopup();
        _popup.CloseRequested += () => SetPinned(false);
        _popup.DragFinished += SavePosition;

        _pinItem = new ToolStripMenuItem("Fissa la finestra sullo schermo", null, (_, _) => SetPinned(!_popup.Pinned));
        _trayVisibleItem = new ToolStripMenuItem("Icona sempre visibile sulla barra", null, (_, _) => ToggleTrayVisible());
        _autostartItem = new ToolStripMenuItem("Avvia con Windows", null, (_, _) => ToggleAutostart());
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true };
        menu.Items.Add(_pinItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_trayVisibleItem);
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Esci", null, (_, _) => ExitThread()));
        foreach (ToolStripItem i in menu.Items) { i.ForeColor = HoverPopup.Fg; i.Padding = new Padding(4, 3, 4, 3); }
        menu.Opening += (_, _) =>
        {
            if (!_popup.Pinned) HidePopup();
            _pinItem.Checked = _popup.Pinned;
            _trayVisibleItem.Checked = _settings.AlwaysVisibleInTray;
            _autostartItem.Checked = Autostart.IsEnabled();
        };

        _tray = new NotifyIcon { ContextMenuStrip = menu, Text = "WinTemp", Visible = true };
        _tray.MouseMove += (_, _) => OnTrayHover();
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) SetPinned(!_popup.Pinned); };
        RenderIcon(null);

        _hoverTimer = new System.Windows.Forms.Timer { Interval = 120 };
        _hoverTimer.Tick += (_, _) => CheckHover();

        _uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _uiTimer.Tick += (_, _) => Refresh();
        _uiTimer.Start();

        // Explorer crea la voce di registro dell'icona solo dopo che è comparsa: riprova per un po'
        _promoteTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _promoteTimer.Tick += (_, _) =>
        {
            if (TrayPromotion.TrySet(_settings.AlwaysVisibleInTray) || ++_promoteAttempts > 40)
                _promoteTimer.Stop();
        };
        _promoteTimer.Start();

        _reader = new SensorReader(TimeSpan.FromSeconds(2));
        _reader.Updated += () => _ui.Post(_ => Refresh(), null);

        if (_settings.Pinned)
            SetPinned(true);
    }

    // ---------- hover ----------

    private void OnTrayHover()
    {
        if (_popup.Pinned) return;
        _hoverAnchor = Cursor.Position;
        if (!_popup.Visible)
        {
            _popup.SetData(_reader.Current, Note());
            _popup.PlaceNear(TrayAnchor(_hoverAnchor));
            _popup.Show();
            _hoverTimer.Start();
        }
    }

    private void CheckHover()
    {
        if (_popup.Pinned || !_popup.Visible) { _hoverTimer.Stop(); return; }
        var p = Cursor.Position;
        var icon = IconRect() ?? FallbackRect();
        icon.Inflate(2, 2);
        if (!icon.Contains(p) && !_popup.Bounds.Contains(p))
            HidePopup();
    }

    private Point TrayAnchor(Point fallback) =>
        IconRect() is Rectangle r ? new Point(r.Left + r.Width / 2, r.Top) : fallback;

    private Rectangle FallbackRect()
    {
        int half = (int)(18 * _popup.DeviceDpi / 96f);
        return new Rectangle(_hoverAnchor.X - half, _hoverAnchor.Y - half, half * 2, half * 2);
    }

    private void HidePopup()
    {
        _hoverTimer.Stop();
        _popup.Hide();
    }

    // ---------- finestra fissata ----------

    private void SetPinned(bool pinned)
    {
        _settings.Pinned = pinned;
        _settings.Save();
        _hoverTimer.Stop();

        if (!pinned)
        {
            _popup.Pinned = false;
            _popup.Hide();
            return;
        }

        _popup.Pinned = true;
        _popup.SetData(_reader.Current, Note());
        if (_settings.PinnedX is int x && _settings.PinnedY is int y && IsOnAnyScreen(new Point(x, y)))
        {
            _popup.Location = new Point(x, y);
            _popup.KeepOnScreen();
        }
        else
        {
            _popup.PlaceNear(TrayAnchor(Cursor.Position));
        }
        _popup.Show();
        SavePosition();
    }

    private void SavePosition()
    {
        if (!_popup.Pinned) return;
        _settings.PinnedX = _popup.Left;
        _settings.PinnedY = _popup.Top;
        _settings.Save();
    }

    private static bool IsOnAnyScreen(Point p) =>
        Screen.AllScreens.Any(s => s.WorkingArea.Contains(p.X + 20, p.Y + 20));

    // ---------- aggiornamento ----------

    private void Refresh()
    {
        var snap = _reader.Current;
        RenderIcon(snap.HeadlineTemp);
        _tray.Text = Tooltip(snap);
        if (!_popup.Visible) return;

        var oldSize = _popup.Size;
        _popup.SetData(snap, Note());
        if (_popup.Size == oldSize) return;
        if (_popup.Pinned) _popup.KeepOnScreen();       // resta dove l'hai lasciata
        else _popup.PlaceNear(TrayAnchor(_hoverAnchor));
    }

    private static string Tooltip(Snapshot s)
    {
        // tooltip di riserva (max 127 caratteri); quello completo è la finestra popup
        var parts = new List<string>();
        foreach (var g in s.Groups)
        {
            var t = g.Rows.Where(r => r.Kind == RowKind.Temperature).Select(r => r.Value).DefaultIfEmpty(float.NaN).Max();
            if (float.IsNaN(t)) continue;
            var label = g.Type switch
            {
                HardwareType.Cpu => "CPU",
                HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "GPU",
                HardwareType.Storage => "Disco",
                HardwareType.Memory => "RAM",
                _ => "MB",
            };
            var item = $"{label} {t:0}°C";
            if (!parts.Contains(item)) parts.Add(item);
        }
        var text = parts.Count > 0 ? string.Join(" · ", parts) : "WinTemp";
        return text.Length > 127 ? text[..124] + "..." : text;
    }

    private string? Note()
    {
        var snap = _reader.Current;
        if (snap.Time == DateTime.MinValue) return null;
        bool cpuTemp = snap.Groups.Any(g => g.Type == HardwareType.Cpu && g.Rows.Any(r => r.Kind == RowKind.Temperature));
        if (cpuTemp) return null;
        return PawnIoInstalled()
            ? "Temperatura CPU non disponibile su questo sistema."
            : "Temperatura CPU non disponibile: driver PawnIO non installato.";
    }

    private static bool PawnIoInstalled()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            return k != null;
        }
        catch { return false; }
    }

    // ---------- icona ----------

    private void RenderIcon(float? temp)
    {
        string key = temp.HasValue ? Math.Round(temp.Value).ToString("0") : "--";
        if (key == _iconKey) return;
        _iconKey = key;

        int s = Math.Max(16, System.Windows.Forms.SystemInformation.SmallIconSize.Width);
        var color = temp.HasValue ? HoverPopup.TempColor(temp.Value, HardwareType.Cpu) : Color.FromArgb(120, 120, 120);

        using var bmp = new Bitmap(s, s);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using (var path = RoundRect(new RectangleF(0, 0, s - 0.5f, s - 0.5f), s * 0.22f))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);

            float fontPx = s * (key.Length >= 3 ? 0.62f : 0.82f);
            using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var text = new GraphicsPath();
            text.AddString(key, font.FontFamily, (int)font.Style, fontPx, new RectangleF(0, 0, s, s), sf);
            var b = text.GetBounds();
            using (var m = new Matrix())
            {
                float scale = Math.Min(1f, Math.Min((s - 2) / b.Width, (s - 2) / b.Height));
                m.Translate(s / 2f, s / 2f);
                m.Scale(scale, scale);
                m.Translate(-(b.X + b.Width / 2f), -(b.Y + b.Height / 2f));
                text.Transform(m);
            }
            using var textBrush = new SolidBrush(Color.FromArgb(20, 20, 20));
            g.FillPath(textBrush, text);
        }

        var hIcon = bmp.GetHicon();
        var newIcon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        _tray.Icon = newIcon;
        _icon?.Dispose();
        _icon = newIcon;
    }

    private static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ---------- posizione dell'icona nel tray ----------

    private Rectangle? IconRect()
    {
        try
        {
            var t = typeof(NotifyIcon);
            var idField = t.GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)
                          ?? t.GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
            var winField = t.GetField("_window", BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? t.GetField("window", BindingFlags.NonPublic | BindingFlags.Instance);
            if (idField == null || winField?.GetValue(_tray) is not NativeWindow win) return null;

            var nid = new NOTIFYICONIDENTIFIER
            {
                cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
                hWnd = win.Handle,
                uID = Convert.ToUInt32(idField.GetValue(_tray)),
            };
            if (Shell_NotifyIconGetRect(ref nid, out var rc) != 0) return null;
            var rect = Rectangle.FromLTRB(rc.Left, rc.Top, rc.Right, rc.Bottom);
            return rect.Width > 0 && rect.Height > 0 ? rect : null;
        }
        catch { return null; }
    }

    // ---------- opzioni ----------

    private void ToggleTrayVisible()
    {
        _settings.AlwaysVisibleInTray = !_settings.AlwaysVisibleInTray;
        _settings.Save();
        _promoteAttempts = 0;
        if (!TrayPromotion.TrySet(_settings.AlwaysVisibleInTray))
            _promoteTimer.Start();
    }

    private void ToggleAutostart()
    {
        bool ok = Autostart.IsEnabled() ? Autostart.Disable() : Autostart.Enable();
        if (!ok)
            _tray.ShowBalloonTip(3000, "WinTemp", "Impossibile modificare l'avvio automatico.", ToolTipIcon.Warning);
    }

    protected override void ExitThreadCore()
    {
        if (_popup.Pinned) SavePosition();
        _uiTimer.Stop();
        _hoverTimer.Stop();
        _promoteTimer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _popup.Dispose();
        _reader.Dispose();
        _icon?.Dispose();
        base.ExitThreadCore();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}

/// <summary>Menu contestuale scuro, coerente con la finestra delle temperature.</summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = HoverPopup.Fg;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        TextRenderer.DrawText(e.Graphics, "✓", e.Item.Font, r, Color.FromArgb(76, 194, 255),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        private static readonly Color Bg = Color.FromArgb(36, 36, 39);
        private static readonly Color Hover = Color.FromArgb(58, 58, 64);
        public override Color ToolStripDropDownBackground => Bg;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
        public override Color MenuBorder => HoverPopup.Border;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color SeparatorDark => HoverPopup.Border;
        public override Color SeparatorLight => HoverPopup.Border;
        public override Color CheckBackground => Bg;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
    }
}
