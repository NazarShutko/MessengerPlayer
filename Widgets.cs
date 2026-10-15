using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MessengerPlayer;

public sealed class CoverArt : Control
{
    Image? _img;
    readonly Font _note = new("Segoe MDL2 Assets", 40f, FontStyle.Regular, GraphicsUnit.Point);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? Image
    {
        get => _img;
        set { _img = value; Invalidate(); }
    }

    public CoverArt()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(3, 3, Width - 7, Height - 7);
        using var path = Gfx.Round(rect, 20);

        bool drawn = false;
        if (_img != null)
        {
            try
            {
                g.SetClip(path);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float side = Math.Min(_img.Width, _img.Height);
                g.DrawImage(_img, rect, (_img.Width - side) / 2f, (_img.Height - side) / 2f, side, side, GraphicsUnit.Pixel);
                g.ResetClip();
                drawn = true;
            }
            catch { g.ResetClip(); }
        }
        if (!drawn)
        {
            using (var b = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.FillPath(b, path);
            Gfx.GlowGlyph(g, "\uE8D6", _note, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), Theme.Accent);
        }
        Gfx.GlowStroke(g, path, Theme.Accent, 0.9f, 1.5f);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _note.Dispose();
        base.Dispose(disposing);
    }
}
public sealed class Equalizer : Control
{
    const int Bars = 26;
    readonly float[] _h = new float[Bars];
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 45 };
    readonly Random _rng = new();
    float _phase;
    bool _active;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            if (value && !_timer.Enabled) _timer.Start();
        }
    }

    public Equalizer()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
        for (int i = 0; i < Bars; i++) _h[i] = 0.05f;
        _timer.Tick += (_, _) => Step();
    }

    void Step()
    {
        _phase += 0.33f;
        bool moving = false;
        for (int i = 0; i < Bars; i++)
        {
            float env = 0.22f + 0.78f * i / (Bars - 1);
            float wave = 0.5f + 0.5f * (float)Math.Sin(_phase * 0.9 + i * 0.75);
            float target = _active ? env * (0.3f + 0.7f * wave) * (0.65f + 0.35f * (float)_rng.NextDouble()) : 0.04f;
            _h[i] += (target - _h[i]) * 0.45f;
            if (Math.Abs(target - _h[i]) > 0.01f) moving = true;
        }
        if (Visible) Invalidate();
        if (!_active && !moving) _timer.Stop();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var accent = Theme.Accent;
        const float barW = 4f;
        float gap = (Width - Bars * barW) / (Bars - 1);
        for (int i = 0; i < Bars; i++)
        {
            float h = Math.Max(3f, _h[i] * (Height - 2));
            float x = i * (barW + gap), y = Height - h;
            using (var glow = new SolidBrush(Color.FromArgb(28, accent)))
            using (var gp = Gfx.Round(new Rectangle((int)x - 2, (int)y - 2, (int)barW + 4, (int)h + 3), 3))
                g.FillPath(glow, gp);
            using var b = new SolidBrush(Color.FromArgb(120 + (int)(120 * _h[i]), accent));
            using var p = Gfx.Round(new Rectangle((int)x, (int)y, (int)barW, (int)h), 2);
            g.FillPath(b, p);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
