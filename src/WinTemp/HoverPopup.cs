using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace WinTemp;

/// <summary>
/// Finestra senza bordi con tutti i sensori. In modalità "hover" segue l'icona del tray;
/// in modalità "fissata" si può trascinare e resta dove la si lascia.
/// </summary>
public sealed class HoverPopup : Form
{
    public static readonly Color Bg = Color.FromArgb(28, 28, 30);
    public static readonly Color Card = Color.FromArgb(40, 40, 43);
    public static readonly Color Border = Color.FromArgb(58, 58, 62);
    public static readonly Color Fg = Color.FromArgb(240, 240, 242);
    public static readonly Color Sub = Color.FromArgb(190, 190, 196);
    public static readonly Color Dim = Color.FromArgb(128, 128, 136);
    public static readonly Color Track = Color.FromArgb(60, 60, 66);
    public static readonly Color Good = Color.FromArgb(102, 211, 120);
    public static readonly Color Warm = Color.FromArgb(255, 186, 60);
    public static readonly Color Hot = Color.FromArgb(255, 92, 80);

    private Snapshot _snapshot = Snapshot.Empty;
    private string? _note;
    private bool _pinned;
    private bool _closeHover;
    private Rectangle _closeRect;
    private Font _title = null!, _headline = null!, _group = null!, _pill = null!, _row = null!, _value = null!, _small = null!;
    private float _fontDpi;

    /// <summary>Clic sulla ✕ (solo modalità fissata).</summary>
    public event Action? CloseRequested;
    /// <summary>Fine di un trascinamento (solo modalità fissata).</summary>
    public event Action? DragFinished;

    public HoverPopup()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Bg;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        EnsureFonts();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;
            const int CS_DROPSHADOW = 0x20000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int round = 2; // DWMWCP_ROUND (angoli arrotondati Windows 11)
            DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
            int dark = 1;
            DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            int border = ColorTranslator.ToWin32(Border);
            DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));
        }
        catch { }
    }

    public bool Pinned
    {
        get => _pinned;
        set
        {
            if (_pinned == value) return;
            _pinned = value;
            Cursor = value ? Cursors.SizeAll : Cursors.Default;
            UpdateSize();
            Invalidate();
        }
    }

    public void SetData(Snapshot snapshot, string? note)
    {
        _snapshot = snapshot;
        _note = note;
        UpdateSize();
        Invalidate();
    }

    private void UpdateSize()
    {
        EnsureFonts();
        var size = Measure(out _);
        if (Size != size) Size = size;
    }

    /// <summary>Posiziona la finestra sopra (o sotto) il punto indicato, dentro l'area di lavoro.</summary>
    public void PlaceNear(Point anchor)
    {
        var wa = Screen.FromPoint(anchor).WorkingArea;
        int gap = S(12);
        int x = anchor.X - Width / 2;
        int y = anchor.Y < wa.Top + wa.Height / 2 ? anchor.Y + gap : anchor.Y - Height - gap;
        Location = ClampTo(wa, new Point(x, y), gap);
    }

    /// <summary>Mantiene la finestra interamente visibile sullo schermo in cui si trova.</summary>
    public void KeepOnScreen()
    {
        var wa = Screen.FromRectangle(Bounds).WorkingArea;
        var p = ClampTo(wa, Location, 0);
        if (p != Location) Location = p;
    }

    private Point ClampTo(Rectangle wa, Point p, int gap) => new(
        Math.Clamp(p.X, wa.Left + gap, Math.Max(wa.Left + gap, wa.Right - Width - gap)),
        Math.Clamp(p.Y, wa.Top + gap, Math.Max(wa.Top + gap, wa.Bottom - Height - gap)));

    // ---------- metriche ----------

    private float Dpi => DeviceDpi / 96f;
    private int S(float v) => (int)Math.Round(v * Dpi);

    private int Pad => S(16);
    private int HeaderH => S(58);
    private int ColW => S(380);
    private int ColGap => S(12);
    private int CardGap => S(8);
    private int CardPad => S(12);
    private int TitleH => S(24);
    private int RowH => S(24);
    private int BarW => S(64);
    private int ValW => S(66);
    private int MaxW => S(54);
    private int FooterH => S(24) + (_note != null ? S(22) : 0);

    private int CardHeight(SensorGroup g) => CardPad + TitleH + S(4) + g.Rows.Count * RowH + CardPad - S(4);

    private void EnsureFonts()
    {
        if (_fontDpi == DeviceDpi) return;
        _fontDpi = DeviceDpi;
        foreach (var f in new[] { _title, _headline, _group, _pill, _row, _value, _small }) f?.Dispose();
        Font F(string family, float px) => new(family, px * Dpi, GraphicsUnit.Pixel);
        _title = F("Segoe UI Semibold", 17);
        _headline = F("Segoe UI Semibold", 28);
        _group = F("Segoe UI Semibold", 13);
        _pill = F("Segoe UI Semibold", 10.5f);
        _row = F("Segoe UI", 13);
        _value = F("Segoe UI Semibold", 13);
        _small = F("Segoe UI", 11.5f);
    }

    private sealed record Column(List<SensorGroup> Groups, int Height);

    private Size Measure(out List<Column> columns)
    {
        var wa = Screen.FromPoint(_pinned ? Location : Cursor.Position).WorkingArea;
        int maxBody = Math.Max(S(160), (int)(wa.Height * 0.85) - HeaderH - FooterH - Pad * 2);

        columns = new List<Column>();
        var cur = new List<SensorGroup>();
        int h = 0;
        foreach (var g in _snapshot.Groups)
        {
            int gh = CardHeight(g) + CardGap;
            if (cur.Count > 0 && h + gh > maxBody)
            {
                columns.Add(new Column(cur, h));
                cur = new List<SensorGroup>();
                h = 0;
            }
            cur.Add(g);
            h += gh;
        }
        if (cur.Count > 0 || columns.Count == 0) columns.Add(new Column(cur, Math.Max(h, S(48))));

        int width = Pad * 2 + columns.Count * ColW + (columns.Count - 1) * ColGap;
        int height = Pad + HeaderH + columns.Max(c => c.Height) + FooterH + Pad - CardGap;
        return new Size(width, height);
    }

    // ---------- disegno ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        EnsureFonts();
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Bg);

        Measure(out var columns);
        int right = Width - Pad;

        // --- intestazione ---
        int top = Pad;
        if (_pinned)
        {
            int cs = S(28);
            _closeRect = new Rectangle(right - cs + S(6), top - S(6), cs, cs);
            if (_closeHover)
                using (var b = new SolidBrush(Color.FromArgb(70, 70, 76))) using (var p = RoundRect(_closeRect, S(6)))
                    g.FillPath(b, p);
            Draw(g, "✕", _group, _closeHover ? Fg : Sub, _closeRect, TextFormatFlags.HorizontalCenter);
            right = _closeRect.Left - S(8);
        }
        else _closeRect = Rectangle.Empty;

        Draw(g, "Temperature PC", _title, Fg, new Rectangle(Pad, top - S(2), S(260), S(26)), TextFormatFlags.Left);
        var sub = _snapshot.Time == DateTime.MinValue ? "lettura in corso…" : $"aggiornato alle {_snapshot.Time:HH:mm:ss}";
        Draw(g, sub, _small, Dim, new Rectangle(Pad, top + S(24), S(260), S(18)), TextFormatFlags.Left);

        if (_snapshot.HeadlineTemp is float ht)
        {
            var hc = TempColor(ht, HardwareType.Cpu);
            var txt = $"{ht:0}°";
            var sz = TextRenderer.MeasureText(g, txt, _headline, Size.Empty, TextFormatFlags.NoPadding);
            var hr = new Rectangle(right - sz.Width, top - S(6), sz.Width, S(40));
            Draw(g, txt, _headline, hc, hr, TextFormatFlags.Right | TextFormatFlags.NoPadding);
            Draw(g, _snapshot.HeadlineLabel, _small, Dim, new Rectangle(right - S(120), top + S(32), S(120), S(16)), TextFormatFlags.Right);
        }

        // --- schede ---
        int bodyTop = Pad + HeaderH;
        if (_snapshot.Groups.Count == 0)
            Draw(g, _snapshot.Time == DateTime.MinValue ? "Lettura dei sensori in corso…" : "Nessun sensore disponibile",
                 _row, Dim, new Rectangle(Pad, bodyTop, ColW, RowH), TextFormatFlags.Left);

        for (int c = 0; c < columns.Count; c++)
        {
            int cx = Pad + c * (ColW + ColGap);
            int cy = bodyTop;
            foreach (var grp in columns[c].Groups)
            {
                int ch = CardHeight(grp);
                DrawCard(g, grp, new Rectangle(cx, cy, ColW, ch));
                cy += ch + CardGap;
            }
        }

        // --- piè di pagina ---
        int fy = Height - Pad - FooterH + S(4);
        if (_note != null)
        {
            Draw(g, "⚠  " + _note, _small, Warm, new Rectangle(Pad, fy, Width - Pad * 2, S(20)), TextFormatFlags.Left);
            fy += S(22);
        }
        var hint = _pinned
            ? "Trascina per spostare  ·  ✕ per chiudere"
            : "Clic sull'icona per fissare  ·  tasto destro per le opzioni";
        Draw(g, hint, _small, Dim, new Rectangle(Pad, fy, Width - Pad * 2, S(20)), TextFormatFlags.Left);
    }

    private void DrawCard(Graphics g, SensorGroup grp, Rectangle r)
    {
        using (var b = new SolidBrush(Card)) using (var p = RoundRect(r, S(8)))
            g.FillPath(b, p);

        int x = r.X + CardPad, w = r.Width - CardPad * 2, y = r.Y + CardPad;

        // pillola con il tipo di componente + nome
        var (label, color) = TypeInfo(grp.Type);
        var ps = TextRenderer.MeasureText(g, label, _pill, Size.Empty, TextFormatFlags.NoPadding);
        var pill = new Rectangle(x, y + (TitleH - S(18)) / 2, ps.Width + S(12), S(18));
        using (var b = new SolidBrush(Color.FromArgb(45, color))) using (var p = RoundRect(pill, S(9)))
            g.FillPath(b, p);
        Draw(g, label, _pill, color, pill, TextFormatFlags.HorizontalCenter);

        bool hasMax = grp.Rows.Any(row => row.Max.HasValue);
        int titleX = pill.Right + S(8);
        Draw(g, grp.Title, _group, Fg, new Rectangle(titleX, y, x + w - titleX - (hasMax ? MaxW : 0), TitleH), TextFormatFlags.Left);
        if (hasMax)
            Draw(g, "max", _small, Dim, new Rectangle(x + w - MaxW, y, MaxW, TitleH), TextFormatFlags.Right);
        y += TitleH + S(4);

        int nameW = w - BarW - S(10) - ValW - MaxW;
        foreach (var row in grp.Rows)
        {
            Draw(g, row.Name, _row, Sub, new Rectangle(x, y, nameW, RowH), TextFormatFlags.Left);

            // barra di livello
            float? frac = row.Kind switch
            {
                RowKind.Temperature => row.Value / (grp.Type == HardwareType.Storage ? 70f : 100f),
                RowKind.Load => row.Value / 100f,
                _ => null,
            };
            var vc = row.Kind == RowKind.Temperature ? TempColor(row.Value, grp.Type) : Fg;
            if (frac is float f)
            {
                int bh = S(5);
                var track = new Rectangle(x + nameW + S(4), y + (RowH - bh) / 2, BarW, bh);
                using (var b = new SolidBrush(Track)) using (var p = RoundRect(track, bh / 2f))
                    g.FillPath(b, p);
                int fw = (int)(BarW * Math.Clamp(f, 0.04f, 1f));
                var fill = new Rectangle(track.X, track.Y, fw, bh);
                var barColor = row.Kind == RowKind.Load ? Color.FromArgb(120, 170, 255) : vc;
                using (var b = new SolidBrush(barColor)) using (var p = RoundRect(fill, bh / 2f))
                    g.FillPath(b, p);
            }

            Draw(g, Format(row.Kind, row.Value), _value, vc, new Rectangle(x + w - MaxW - ValW, y, ValW, RowH), TextFormatFlags.Right);
            if (row.Max is float m)
                Draw(g, Format(row.Kind, m, compact: true), _small, Dim, new Rectangle(x + w - MaxW, y, MaxW, RowH), TextFormatFlags.Right);
            y += RowH;
        }
    }

    private static (string, Color) TypeInfo(HardwareType t) => t switch
    {
        HardwareType.Cpu => ("CPU", Color.FromArgb(76, 194, 255)),
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => ("GPU", Color.FromArgb(183, 148, 246)),
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController => ("SCHEDA MADRE", Color.FromArgb(94, 214, 190)),
        HardwareType.Memory => ("RAM", Color.FromArgb(240, 150, 200)),
        HardwareType.Storage => ("DISCO", Color.FromArgb(150, 170, 200)),
        HardwareType.Battery => ("BATTERIA", Color.FromArgb(150, 170, 200)),
        _ => ("ALTRO", Color.FromArgb(150, 170, 200)),
    };

    private static void Draw(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags align) =>
        TextRenderer.DrawText(g, s, f, r, c,
            align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

    private static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static string Format(RowKind kind, float v, bool compact = false) => kind switch
    {
        RowKind.Temperature => compact ? $"{v:0}°" : $"{v:0.0} °C",
        RowKind.Fan => compact ? $"{v:0}" : $"{v:0} RPM",
        _ => $"{v:0} %",
    };

    public static Color TempColor(float v, HardwareType type)
    {
        var (warm, hot) = type == HardwareType.Storage ? (50f, 60f) : (65f, 85f);
        return v >= hot ? Hot : v >= warm ? Warm : Good;
    }

    // ---------- interazione ----------

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool over = _closeRect.Contains(e.Location);
        if (over != _closeHover)
        {
            _closeHover = over;
            Cursor = over ? Cursors.Hand : _pinned ? Cursors.SizeAll : Cursors.Default;
            Invalidate(_closeRect);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_closeHover) { _closeHover = false; Invalidate(_closeRect); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!_pinned || e.Button != MouseButtons.Left) return;
        if (_closeRect.Contains(e.Location))
        {
            CloseRequested?.Invoke();
            return;
        }
        // trascinamento: lascia che Windows sposti la finestra come se fosse la barra del titolo
        ReleaseCapture();
        SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
        KeepOnScreen();
        DragFinished?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var f in new[] { _title, _headline, _group, _pill, _row, _value, _small }) f?.Dispose();
        base.Dispose(disposing);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
