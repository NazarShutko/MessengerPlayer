using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.ComponentModel;

namespace MessengerPlayer;

public static class Theme
{
    public static Color Accent = Color.FromArgb(0, 255, 64);
    public static readonly Color Dim = Color.FromArgb(165, 175, 170);

    public static event Action? Changed;
    public static void SetAccent(Color c) { Accent = c; Changed?.Invoke(); }
}

public static class Gfx
{
    public const TextFormatFlags Center =
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    public static GraphicsPath Round(Rectangle r, int radius)
    {
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
    public static void GlowStroke(Graphics g, GraphicsPath p, Color c, float k = 1f, float core = 1.5f, float spread = 1f)
    {
        float[] w = { 10f, 7f, 4.5f, 2.8f };
        int[] a = { 9, 17, 30, 50 };
        for (int i = 0; i < w.Length; i++)
        {
            using var pen = new Pen(Color.FromArgb(Math.Clamp((int)(a[i] * k), 0, 255), c), w[i] * spread + core * 0.5f)
            { LineJoin = LineJoin.Round };
            g.DrawPath(pen, p);
        }
        using var cp = new Pen(Color.FromArgb(Math.Clamp((int)(190 * k + 40), 0, 255), c), core) { LineJoin = LineJoin.Round };
        g.DrawPath(cp, p);
    }

    public static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(255, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    static readonly StringFormat GlyphFmt = new(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces
    };

    public static void GlowGlyph(Graphics g, string glyph, Font font, RectangleF r, Color c, bool glow = true)
    {
        if (string.IsNullOrEmpty(glyph)) return;
        var old = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        if (glow)
        {
            foreach (var (rad, alpha) in new[] { (1.4f, 30), (2.8f, 13) })
                for (int i = 0; i < 8; i++)
                {
                    double ang = i * Math.PI / 4;
                    using var b = new SolidBrush(Color.FromArgb(alpha, c));
                    g.DrawString(glyph, font, b,
                        new RectangleF(r.X + (float)Math.Cos(ang) * rad, r.Y + (float)Math.Sin(ang) * rad, r.Width, r.Height),
                        GlyphFmt);
                }
        }
        using var main = new SolidBrush(c);
        g.DrawString(glyph, font, main, r, GlyphFmt);
        g.TextRenderingHint = old;
    }
}

public static class Orbiter
{
    static readonly System.Windows.Forms.Timer T = new() { Interval = 50 };
    static int _subs;
    static float _phase;
    static bool _enabled = true;

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (value) { if (_subs > 0) T.Start(); }
            else T.Stop();
            Tick?.Invoke();
        }
    }

    public static float Phase => _phase;
    static event Action? Tick;

    static Orbiter()
    {
        T.Tick += (_, _) =>
        {
            _phase = (_phase + 0.0043f) % 1f;
            Tick?.Invoke();
        };
    }

    public static void Subscribe(Action a) { Tick += a; if (++_subs == 1 && _enabled) T.Start(); }
    public static void Unsubscribe(Action a) { Tick -= a; if (--_subs <= 0) { _subs = 0; T.Stop(); } }
}

public sealed class GlassPanel : Control
{
    const int Inset = 6;

    private int _radius = 28;
    private Color _fill = Color.FromArgb(150, 0, 0, 0);
    private float _glow = 1f;

    bool _orbit;
    float _offset;
    static int _orbitCount;

    Image? _backdrop;
    Bitmap? _bdCache;
    Size _bdSize;

    PointF[]? _samples;
    Size _sampSize;
    int _sampRadius;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public int Radius
    {
        get => _radius;
        set { _radius = value; _samples = null; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public Color Fill
    {
        get => _fill;
        set { _fill = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public float Glow
    {
        get => _glow;
        set { _glow = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? Backdrop
    {
        get => _backdrop;
        set { _backdrop = value; _bdCache?.Dispose(); _bdCache = null; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float BackdropFocusY { get; set; } = 0.3f;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int BackdropDim { get; set; } = 85;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Orbit
    {
        get => _orbit;
        set
        {
            if (_orbit == value) return;
            _orbit = value;
            if (value) { _offset = (_orbitCount++ * 0.27f) % 1f; Orbiter.Subscribe(OnOrbit); }
            else Orbiter.Unsubscribe(OnOrbit);
            Invalidate();
        }
    }

    public GlassPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    void OnOrbit()
    {
        if (!IsHandleCreated || IsDisposed || !Visible || Width < 40 || Height < 40) return;
        var f = FindForm();
        if (f == null || f.WindowState == FormWindowState.Minimized) return;

        int b = Inset + 10, c = Radius + Inset + 10;
        Invalidate(new Rectangle(0, 0, Width, b));
        Invalidate(new Rectangle(0, Height - b, Width, b));
        Invalidate(new Rectangle(0, b, b, Height - 2 * b));
        Invalidate(new Rectangle(Width - b, b, b, Height - 2 * b));
        Invalidate(new Rectangle(0, 0, c, c));
        Invalidate(new Rectangle(Width - c, 0, c, c));
        Invalidate(new Rectangle(0, Height - c, c, c));
        Invalidate(new Rectangle(Width - c, Height - c, c, c));
    }

    void EnsureBackdrop()
    {
        if (_backdrop == null) return;
        if (_bdCache != null && _bdSize == Size) return;

        _bdCache?.Dispose();
        _bdCache = null;
        try
        {
            var bmp = new Bitmap(Width, Height);
            using (var g = Graphics.FromImage(bmp))
            {
                float iw = _backdrop.Width, ih = _backdrop.Height;
                float scale = Math.Max(Width / iw, Height / ih);
                float sw = Width / scale, sh = Height / scale;
                float sx = (iw - sw) / 2f;
                float sy = Math.Clamp(BackdropFocusY * ih - sh / 2f, 0f, Math.Max(0f, ih - sh));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(_backdrop, new Rectangle(0, 0, Width, Height), sx, sy, sw, sh, GraphicsUnit.Pixel);
                using var dim = new SolidBrush(Color.FromArgb(BackdropDim, 0, 0, 0));
                g.FillRectangle(dim, 0, 0, Width, Height);
            }
            _bdCache = bmp;
            _bdSize = Size;
        }
        catch { _bdCache = null; }
    }

    void EnsureSamples(Rectangle rect)
    {
        if (_samples != null && _sampSize == Size && _sampRadius == Radius) return;

        using var p = Gfx.Round(rect, Radius);
        p.Flatten(null, 0.25f);
        var pts = p.PathPoints.ToList();
        pts.Add(pts[0]);

        var cum = new float[pts.Count];
        for (int i = 1; i < pts.Count; i++)
        {
            float dx = pts[i].X - pts[i - 1].X, dy = pts[i].Y - pts[i - 1].Y;
            cum[i] = cum[i - 1] + (float)Math.Sqrt(dx * dx + dy * dy);
        }
        float total = Math.Max(1f, cum[^1]);

        int n = Math.Max(16, (int)(total / 8f));
        var res = new PointF[n + 1];
        int seg = 1;
        for (int i = 0; i <= n; i++)
        {
            float target = total * i / n;
            while (seg < pts.Count - 1 && cum[seg] < target) seg++;
            float len = Math.Max(0.0001f, cum[seg] - cum[seg - 1]);
            float t = Math.Clamp((target - cum[seg - 1]) / len, 0f, 1f);
            res[i] = new PointF(pts[seg - 1].X + (pts[seg].X - pts[seg - 1].X) * t,
                                pts[seg - 1].Y + (pts[seg].Y - pts[seg - 1].Y) * t);
        }
        _samples = res;
        _sampSize = Size;
        _sampRadius = Radius;
    }

    void DrawComet(Graphics g, Rectangle rect)
    {
        EnsureSamples(rect);
        var s = _samples!;
        int n = s.Length - 1;
        float head = (Orbiter.Phase + _offset) % 1f;
        const float tail = 0.22f;
        var accent = Theme.Accent;

        using var p1 = new Pen(accent, 10f) { LineJoin = LineJoin.Round };
        using var p2 = new Pen(accent, 6f) { LineJoin = LineJoin.Round };
        using var p3 = new Pen(accent, 3.5f) { LineJoin = LineJoin.Round };
        using var pc = new Pen(accent, 2.6f) { LineJoin = LineJoin.Round };

        for (int i = 0; i < n; i++)
        {
            float d = (i + 0.5f) / n;
            float t = head - d;
            if (t < 0) t += 1f;
            if (t >= tail) continue;
            float k = 1f - t / tail;
            k *= k;
            if (k < 0.03f) continue;

            var hot = Gfx.Mix(accent, Color.White, 0.55f * k);
            p1.Color = Color.FromArgb((int)(26 * k), accent);
            p2.Color = Color.FromArgb((int)(52 * k), accent);
            p3.Color = Color.FromArgb((int)(95 * k), hot);
            pc.Color = Color.FromArgb((int)(255 * k), hot);
            g.DrawLine(p1, s[i], s[i + 1]);
            g.DrawLine(p2, s[i], s[i + 1]);
            g.DrawLine(p3, s[i], s[i + 1]);
            g.DrawLine(pc, s[i], s[i + 1]);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(Inset, Inset, Width - 2 * Inset - 1, Height - 2 * Inset - 1);
        using var path = Gfx.Round(rect, Radius);

        bool drawn = false;
        if (_backdrop != null && Width > 8 && Height > 8)
        {
            EnsureBackdrop();
            if (_bdCache != null)
            {
                using var tb = new TextureBrush(_bdCache);
                g.FillPath(tb, path);
                drawn = true;
            }
        }
        if (!drawn)
        {
            using var brush = new SolidBrush(Fill);
            g.FillPath(brush, path);
        }

        if (Glow > 0.01f)
        {
            bool anim = _orbit && Orbiter.Enabled && Glow > 0.3f;
            Gfx.GlowStroke(g, path, Theme.Accent, anim ? Glow * 0.2f : Glow * 0.9f, anim ? 1.1f : 1.6f, 1.15f);
            if (anim) DrawComet(g, rect);
        }
        else
        {
            using var pen = new Pen(Color.FromArgb(45, 255, 255, 255));
            g.DrawPath(pen, path);
        }
        base.OnPaint(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_orbit) { _orbit = false; Orbiter.Unsubscribe(OnOrbit); }
            _bdCache?.Dispose();
        }
        base.Dispose(disposing);
    }
}

public sealed class IconButton : Control
{
    readonly Font _font;
    bool _hover, _down;
    private string _glyph = string.Empty;
    private bool _filled;
    private bool _active;
    private bool _ring;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Glyph
    {
        get => _glyph;
        set { _glyph = value ?? string.Empty; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Filled
    {
        get => _filled;
        set { _filled = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Ring
    {
        get => _ring;
        set { _ring = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Active
    {
        get => _active;
        set { _active = value; Invalidate(); }
    }

    public IconButton(string glyph, float size)
    {
        Glyph = glyph;
        _font = new Font("Segoe MDL2 Assets", size, FontStyle.Regular, GraphicsUnit.Point);
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(48, 48);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    static void DrawPlayPause(Graphics g, Rectangle r, Color c, bool pause)
    {
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, s = r.Width * 0.27f;
        using var path = new GraphicsPath();
        if (pause)
        {
            float bw = s * 0.62f, gap = s * 0.42f;
            using var a = Gfx.Round(new Rectangle((int)(cx - gap - bw), (int)(cy - s), (int)bw, (int)(2 * s)), 2);
            using var b = Gfx.Round(new Rectangle((int)(cx + gap), (int)(cy - s), (int)bw, (int)(2 * s)), 2);
            path.AddPath(a, false);
            path.AddPath(b, false);
        }
        else
        {
            float x0 = cx - s * 0.6f, x1 = cx + s * 0.95f;
            path.AddPolygon(new[] { new PointF(x0, cy - s), new PointF(x1, cy), new PointF(x0, cy + s) });
        }
        Gfx.GlowStroke(g, path, c, 0.9f, 2.4f, 0.5f);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var accent = Theme.Accent;
        var hot = _hover ? ControlPaint.Light(accent, 0.35f) : accent;

        if (Ring)
        {
            var rr = new Rectangle(3, 3, Width - 7, Height - 7);
            using var p = new GraphicsPath();
            p.AddEllipse(rr);
            using (var b = new SolidBrush(Color.FromArgb(_down ? 70 : _hover ? 48 : 26, accent))) g.FillPath(b, p);
            Gfx.GlowStroke(g, p, hot, 1f, 2.4f, 1.1f);
            DrawPlayPause(g, rr, hot, Glyph == "\uE769");
            return;
        }

        if (Filled)
        {
            var rf = new Rectangle(1, 1, Width - 3, Height - 3);
            var c = _down ? ControlPaint.Dark(accent, 0.1f) : _hover ? ControlPaint.Light(accent, 0.3f) : accent;
            using var b = new SolidBrush(c);
            g.FillEllipse(b, rf);
            TextRenderer.DrawText(g, Glyph, _font, ClientRectangle, Color.Black, Gfx.Center);
            return;
        }

        if (Active || _hover)
        {
            var r = new Rectangle(3, 3, Width - 7, Height - 7);
            using var p = Gfx.Round(r, 16);
            using (var b = new SolidBrush(Color.FromArgb(Active ? 62 : 28, accent))) g.FillPath(b, p);
            if (Active) Gfx.GlowStroke(g, p, accent, 0.55f, 1.2f, 0.6f);
        }

        var fore = _down ? ControlPaint.Dark(accent, 0.15f) : hot;
        Gfx.GlowGlyph(g, Glyph, _font, new RectangleF(0, 0, Width, Height), fore, glow: !_down);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _font.Dispose();
        base.Dispose(disposing);
    }
}

public sealed class SeekBar : Control
{
    const int Pad = 10;
    double _v;
    bool _drag;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Dragging => _drag;

    public event Action<double>? ValueChanged;
    public event Action<double>? Committed;

    public SeekBar()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Height = 24;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Value
    {
        get => _v;
        set
        {
            if (_drag) return;
            value = Math.Clamp(value, 0, 1);
            if (Math.Abs(value - _v) < 0.0005) return;
            _v = value;
            Invalidate();
        }
    }

    double FromX(int x) => Math.Clamp((x - Pad) / (double)Math.Max(1, Width - 2 * Pad), 0, 1);

    void Move(int x)
    {
        _v = FromX(x);
        Invalidate();
        ValueChanged?.Invoke(_v);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { _drag = true; Move(e.X); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag) Move(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_drag)
        {
            Move(e.X);
            _drag = false;
            Committed?.Invoke(_v);
        }
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int cy = Height / 2, x0 = Pad, x1 = Width - Pad;
        int px = x0 + (int)((x1 - x0) * _v);
        var accent = Theme.Accent;

        using (var tb = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
        using (var p = Gfx.Round(new Rectangle(x0, cy - 1, x1 - x0, 3), 1))
            g.FillPath(tb, p);

        if (px > x0)
        {
            using (var gb = new SolidBrush(Color.FromArgb(28, accent)))
            using (var gp = Gfx.Round(new Rectangle(x0, cy - 5, px - x0, 10), 5))
                g.FillPath(gb, gp);
            using (var gb = new SolidBrush(Color.FromArgb(55, accent)))
            using (var gp = Gfx.Round(new Rectangle(x0, cy - 3, px - x0, 6), 3))
                g.FillPath(gb, gp);
            using var fb = new SolidBrush(accent);
            using var p = Gfx.Round(new Rectangle(x0, cy - 2, px - x0, 4), 2);
            g.FillPath(fb, p);
        }

        using (var h1 = new SolidBrush(Color.FromArgb(32, accent))) g.FillEllipse(h1, px - 11, cy - 11, 22, 22);
        using (var h2 = new SolidBrush(Color.FromArgb(70, accent))) g.FillEllipse(h2, px - 8, cy - 8, 16, 16);
        using (var cb = new SolidBrush(ControlPaint.Light(accent, 0.45f))) g.FillEllipse(cb, px - 6, cy - 6, 12, 12);
        g.FillEllipse(Brushes.White, px - 2.5f, cy - 2.5f, 5, 5);
    }
}

public sealed class Swatch : Control
{
    bool _hover;

    public Color Value { get; }

    public Swatch(Color c)
    {
        Value = c;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
        Size = new Size(30, 30);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnClick(EventArgs e)
    {
        Theme.SetAccent(Value);
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(3, 3, Width - 7, Height - 7);
        using var path = Gfx.Round(r, 6);
        using (var b = new SolidBrush(_hover ? ControlPaint.Light(Value, 0.2f) : Value)) g.FillPath(b, path);

        if (Theme.Accent.ToArgb() == Value.ToArgb())
        {
            using var outer = Gfx.Round(new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), 7);
            Gfx.GlowStroke(g, outer, Color.White, 0.5f, 2f, 0.5f);
        }
    }
}

public sealed class TrackList : Control
{
    const int RowH = 46;

    List<Track> _items = new();
    int _scroll, _hover = -1;
    readonly Font _icons = new("Segoe MDL2 Assets", 12f, FontStyle.Regular, GraphicsUnit.Point);
    readonly Font _title = new("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
    readonly Font _small = new("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

    private Track? _playing;
    private Track? _selected;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Track? Playing
    {
        get => _playing;
        set { _playing = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Track? Selected
    {
        get => _selected;
        private set { _selected = value; Invalidate(); }
    }

    public event Action<Track>? PlayRequested;
    public event Action<Track>? DeleteRequested;
    public event Action<Track>? FavoriteToggled;
    public event Action<Track>? SaveRequested;

    public TrackList()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
    }
    public void SetItems(IEnumerable<Track> items)
    {
        _items = items.ToList();
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
        if (_selected != null && !_items.Contains(_selected)) _selected = null;
        _hover = -1;
        Invalidate();
    }

    int MaxScroll => Math.Max(0, _items.Count * RowH - Height);

    Rectangle RowRect(int i) => new(4, i * RowH - _scroll, Width - 8, RowH - 4);
    static Rectangle SaveRect(Rectangle r) => new(r.Right - 106, r.Y, 34, r.Height);
    static Rectangle FavRect(Rectangle r) => new(r.Right - 72, r.Y, 34, r.Height);
    static Rectangle DelRect(Rectangle r) => new(r.Right - 38, r.Y, 34, r.Height);

    int HitIndex(Point p)
    {
        if (p.Y < 0) return -1;
        int i = (p.Y + _scroll) / RowH;
        return i >= 0 && i < _items.Count && RowRect(i).Contains(p) ? i : -1;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (MaxScroll <= 0) return;
        _scroll = Math.Clamp(_scroll - Math.Sign(e.Delta) * 46, 0, MaxScroll);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = HitIndex(e.Location);
        if (i != _hover) { _hover = i; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != -1) { _hover = -1; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        int i = HitIndex(e.Location);
        if (i < 0) return;

        var item = _items[i];
        var r = RowRect(i);
        Selected = item;
        if (SaveRect(r).Contains(e.Location)) SaveRequested?.Invoke(item);
        else if (FavRect(r).Contains(e.Location)) FavoriteToggled?.Invoke(item);
        else if (DelRect(r).Contains(e.Location)) DeleteRequested?.Invoke(item);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left) return;
        int i = HitIndex(e.Location);
        if (i < 0) return;
        var r = RowRect(i);
        if (SaveRect(r).Contains(e.Location) || FavRect(r).Contains(e.Location) || DelRect(r).Contains(e.Location)) return;
        PlayRequested?.Invoke(_items[i]);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, "Здесь пока пусто.\nНайдите музыку на YouTube и дважды кликните по треку.",
                _small, ClientRectangle, Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        int first = Math.Max(0, _scroll / RowH);
        int last = Math.Min(_items.Count - 1, (_scroll + Height) / RowH);

        for (int i = first; i <= last; i++)
        {
            var t = _items[i];
            var r = RowRect(i);
            bool playing = ReferenceEquals(t, _playing);

            using (var path = Gfx.Round(r, 10))
            {
                if (playing || ReferenceEquals(t, _selected))
                {
                    using var b = new SolidBrush(Color.FromArgb(playing ? 48 : 30, Theme.Accent));
                    g.FillPath(b, path);
                    if (playing) Gfx.GlowStroke(g, path, Theme.Accent, 0.8f, 1.6f, 0.7f);
                    else
                    {
                        using var sp = new Pen(Color.FromArgb(95, Theme.Accent), 1f);
                        g.DrawPath(sp, path);
                    }
                }
                else if (i == _hover)
                {
                    using var b = new SolidBrush(Color.FromArgb(25, 255, 255, 255));
                    g.FillPath(b, path);
                }
            }

            var iconRect = new Rectangle(r.X + 6, r.Y, 30, r.Height);
            TextRenderer.DrawText(g, playing ? "\uE768" : "\uE8D6", _icons, iconRect,
                playing ? Theme.Accent : Theme.Dim, Gfx.Center);

            var titleRect = new Rectangle(r.X + 42, r.Y + 5, r.Width - 42 - 114, 20);
            TextRenderer.DrawText(g, t.Title, _title, titleRect, Color.White,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);

            var srcRect = new Rectangle(r.X + 42, r.Y + 25, r.Width - 42 - 114, 14);
            TextRenderer.DrawText(g, t.Source, _small, srcRect, playing ? Theme.Accent : Theme.Dim,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);

            TextRenderer.DrawText(g, "\uE74E", _icons, SaveRect(r), Theme.Dim, Gfx.Center);
            TextRenderer.DrawText(g, t.Favorite ? "\uE00B" : "\uE006", _icons, FavRect(r),
                t.Favorite ? Theme.Accent : Theme.Dim, Gfx.Center);
            TextRenderer.DrawText(g, "\uE74D", _icons, DelRect(r), Theme.Dim, Gfx.Center);
        }

        if (MaxScroll > 0)
        {
            int sbH = Math.Max(20, (int)((double)Height * Height / (_items.Count * RowH)));
            int sbY = (int)((double)_scroll / MaxScroll * (Height - sbH));
            using var b = new SolidBrush(Color.FromArgb(80, Color.White));
            using var p = Gfx.Round(new Rectangle(Width - 6, sbY, 4, sbH), 2);
            g.FillPath(b, p);
        }
    }
    public void Redraw() => Invalidate();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icons.Dispose();
            _title.Dispose();
            _small.Dispose();
        }
        base.Dispose(disposing);
    }

    public sealed class YtTrackList : Control
    {
        private readonly List<RenderedTrack> _tracks = new();
        private int _scrollY;
        private int _maxScrollY;
        private int _itemHeight = 64;
        private int _padding = 6;
        private RenderedTrack? _hoveredTrack;
        private RenderedTrack? _selectedTrack;
        public event Action<YtResult>? TrackDoubleClicked;

        public YtTrackList()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Theme.Changed += Invalidate;
        }
        public void SetTracks(IEnumerable<YtResult> newTracks)
        {
            _tracks.Clear();
            _scrollY = 0;
            _selectedTrack = null;
            _hoveredTrack = null;

            foreach (var t in newTracks)
            {
                _tracks.Add(new RenderedTrack(t));
            }

            UpdateLayout();
            Invalidate();
        }
        public void UpdateThumb(string id, Bitmap bmp)
        {
            var track = _tracks.Find(t => t.Data.Id == id);
            if (track != null)
            {
                track.Data.Thumb = bmp;
                Invalidate();
            }
        }

        private void UpdateLayout()
        {
            int totalHeight = _tracks.Count * (_itemHeight + _padding);
            _maxScrollY = Math.Max(0, totalHeight - Height + _padding);
            _scrollY = Math.Clamp(_scrollY, 0, _maxScrollY);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateLayout();
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_maxScrollY <= 0) return;
            _scrollY -= Math.Sign(e.Delta) * 40;
            _scrollY = Math.Clamp(_scrollY, 0, _maxScrollY);
            Invalidate();
        }
        private RenderedTrack? HitTest(Point mouseLoc)
        {
            int startY = _padding - _scrollY;
            for (int i = 0; i < _tracks.Count; i++)
            {
                var r = new Rectangle(_padding, startY + i * (_itemHeight + _padding), Width - _padding * 2 - 4, _itemHeight);
                if (r.Contains(mouseLoc)) return _tracks[i];
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var current = HitTest(e.Location);
            if (_hoveredTrack != current)
            {
                _hoveredTrack = current;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoveredTrack != null)
            {
                _hoveredTrack = null;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                var clicked = HitTest(e.Location);
                if (clicked != null && _selectedTrack != clicked)
                {
                    _selectedTrack = clicked;
                    Invalidate();
                }
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left)
            {
                var clicked = HitTest(e.Location);
                if (clicked != null)
                {
                    TrackDoubleClicked?.Invoke(clicked.Data);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int startY = _padding - _scrollY;

            g.SetClip(ClientRectangle);

            for (int i = 0; i < _tracks.Count; i++)
            {
                var track = _tracks[i];
                var itemRect = new Rectangle(_padding, startY + i * (_itemHeight + _padding), Width - _padding * 2 - 4, _itemHeight);

                if (itemRect.Bottom < 0 || itemRect.Top > Height) continue;

                using (var path = Gfx.Round(itemRect, 10))
                {
                    if (track == _selectedTrack)
                    {
                        using var b = new SolidBrush(Color.FromArgb(50, Theme.Accent));
                        using var p = new Pen(Theme.Accent, 1f);
                        g.FillPath(b, path);
                        g.DrawPath(p, path);
                    }
                    else if (track == _hoveredTrack)
                    {
                        using var b = new SolidBrush(Color.FromArgb(25, 255, 255, 255));
                        g.FillPath(b, path);
                    }
                }

                var thumbRect = new Rectangle(itemRect.X + 6, itemRect.Y + 6, 68, 52);
                using (var pPath = Gfx.Round(thumbRect, 6))
                {
                    g.SetClip(pPath);
                    if (track.Data.Thumb != null)
                        g.DrawImage(track.Data.Thumb, thumbRect);
                    else
                        g.FillRectangle(Brushes.DimGray, thumbRect);
                    g.ResetClip();
                    g.SetClip(ClientRectangle);
                }
                var titleRect = new Rectangle(thumbRect.Right + 10, itemRect.Y + 10, itemRect.Width - thumbRect.Width - 100, 22);
                using (var font = new Font("Segoe UI", 10, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, track.Data.Title, font, titleRect, Color.White,
                        TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }

                var channelRect = new Rectangle(thumbRect.Right + 10, titleRect.Bottom + 2, titleRect.Width, 18);
                using (var font = new Font("Segoe UI", 9, FontStyle.Regular))
                {
                    TextRenderer.DrawText(g, track.Data.Channel, font, channelRect, Theme.Dim,
                        TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                var durationRect = new Rectangle(itemRect.Right - 70, itemRect.Y, 60, _itemHeight);
                using (var font = new Font("Segoe UI", 9, FontStyle.Regular))
                {
                    TextRenderer.DrawText(g, track.Data.Duration, font, durationRect, Theme.Dim,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                }
            }

            g.ResetClip();
            if (_maxScrollY > 0)
            {
                int sbWidth = 4;
                int sbHeight = Math.Max(20, (int)((float)Height / (Height + _maxScrollY) * Height));
                int sbY = (int)((float)_scrollY / _maxScrollY * (Height - sbHeight));
                var sbRect = new Rectangle(Width - sbWidth - 2, sbY, sbWidth, sbHeight);

                using var sbBrush = new SolidBrush(Color.FromArgb(80, Color.White));
                using var path = Gfx.Round(sbRect, 2);
                g.FillPath(sbBrush, path);
            }
        }

        private class RenderedTrack
        {
            public YtResult Data { get; }
            public RenderedTrack(YtResult data) => Data = data;
        }
    }

}