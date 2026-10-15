using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MessengerPlayer;

public sealed class NeonMenu : Form
{
    public enum Kind { Item, Separator, Header }

    public sealed class Entry
    {
        public Kind Kind { get; init; } = Kind.Item;
        public string Text { get; init; } = "";
        public string Glyph { get; init; } = "";
        public bool Checked { get; init; }
        public Action? Click { get; init; }

        public static Entry Item(string text, Action click, string glyph = "", bool isChecked = false) =>
            new() { Text = text, Glyph = glyph, Click = click, Checked = isChecked };
        public static Entry Separator() => new() { Kind = Kind.Separator };
        public static Entry Header(string text) => new() { Kind = Kind.Header, Text = text };
    }

    const int Pad = 12;
    const int Radius = 18;
    const int RowH = 40, SepH = 13, HeadH = 30, Inner = 8;
    const int IconW = 42, CheckW = 36;

    static DateTime _closedAt;
    static Control? _closedFor;

    readonly Control _anchor;
    readonly List<Entry> _items;
    readonly int[] _y;
    readonly int _cardW, _cardH;
    readonly Font _text = new("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
    readonly Font _icon = new("Segoe MDL2 Assets", 12f, FontStyle.Regular, GraphicsUnit.Point);
    readonly Font _head = new("Segoe UI Semibold", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
    int _hover = -1;
    bool _closing;

    public static void Show(Control anchor, IEnumerable<Entry> items)
    {
        if (ReferenceEquals(_closedFor, anchor) && (DateTime.UtcNow - _closedAt).TotalMilliseconds < 250) return;
        var menu = new NeonMenu(anchor, items.ToList());
        var owner = anchor.FindForm();
        if (owner != null) menu.Show(owner); else menu.Show();
    }

    NeonMenu(Control anchor, List<Entry> items)
    {
        _anchor = anchor;
        _items = items;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Cursor = Cursors.Default;

        _y = new int[_items.Count];
        int y = 0, maxText = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            _y[i] = y;
            var it = _items[i];
            y += it.Kind switch { Kind.Separator => SepH, Kind.Header => HeadH, _ => RowH };
            if (it.Kind == Kind.Item)
                maxText = Math.Max(maxText, TextRenderer.MeasureText(it.Text, _text, new Size(1000, 100),
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width);
        }
        _cardH = y + Inner * 2;
        _cardW = Math.Clamp(maxText + IconW + CheckW + Inner * 2 + 8, 250, 460);
        Size = new Size(_cardW + Pad * 2, _cardH + Pad * 2);

        var wa = Screen.FromControl(anchor).WorkingArea;
        var top = anchor.PointToScreen(new Point(anchor.Width, 0));
        int px = top.X + 2 - Pad;
        int py = top.Y - Pad;
        if (py + Height > wa.Bottom + Pad)
            py = anchor.PointToScreen(new Point(0, anchor.Height)).Y + Pad - Height;
        py = Math.Max(wa.Top - Pad, py);
        Location = new Point(px, py);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00080000 /*WS_EX_LAYERED*/ | 0x00000080 /*WS_EX_TOOLWINDOW*/;
            return cp;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize,
        IntPtr hdcSrc, ref Point pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

    void Render()
    {
        if (!IsHandleCreated || IsDisposed) return;

        using var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) PaintMenu(g);

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(memDc, hBmp);
        try
        {
            var size = new Size(Width, Height);
            var src = Point.Empty;
            var dst = Location;
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2 /*ULW_ALPHA*/);
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hBmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    Rectangle ItemRect(int i)
    {
        int y0 = Pad + Inner + _y[i];
        return new Rectangle(Pad + Inner, y0, _cardW - Inner * 2 - 1, RowH - 4);
    }

    void PaintMenu(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        var accent = Theme.Accent;

        var card = new Rectangle(Pad, Pad, _cardW - 1, _cardH - 1);
        using var path = Gfx.Round(card, Radius);
        using (var fill = new SolidBrush(Color.FromArgb(244, 11, 15, 13))) g.FillPath(fill, path);
        using (var sheen = new LinearGradientBrush(card, Color.FromArgb(26, accent), Color.FromArgb(0, accent), 90f))
            g.FillPath(sheen, path);
        Gfx.GlowStroke(g, path, accent, 1f, 1.6f, 1.3f);

        using var fmt = new StringFormat(StringFormat.GenericTypographic)
        {
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            int y0 = Pad + Inner + _y[i];

            if (it.Kind == Kind.Separator)
            {
                using var pen = new Pen(Color.FromArgb(48, accent), 1f);
                g.DrawLine(pen, card.X + 20, y0 + SepH / 2, card.Right - 20, y0 + SepH / 2);
                continue;
            }
            if (it.Kind == Kind.Header)
            {
                using var hb = new SolidBrush(Theme.Dim);
                g.DrawString(it.Text.ToUpperInvariant(), _head, hb,
                    new RectangleF(card.X + 22, y0, card.Width - 44, HeadH - 4), fmt);
                continue;
            }

            var r = ItemRect(i);
            bool hot = i == _hover;
            if (hot)
            {
                using var rp = Gfx.Round(r, 12);
                using (var hb = new SolidBrush(Color.FromArgb(46, accent))) g.FillPath(hb, rp);
                Gfx.GlowStroke(g, rp, accent, 0.45f, 1.1f, 0.5f);
            }

            bool hasIcon = it.Glyph.Length > 0;
            if (hasIcon)
                Gfx.GlowGlyph(g, it.Glyph, _icon, new RectangleF(r.X + 6, r.Y, 30, r.Height), accent, glow: hot);

            float tx = r.X + (hasIcon ? IconW : 16);
            using (var tb = new SolidBrush(hot ? Color.White : Color.FromArgb(232, 238, 235)))
                g.DrawString(it.Text, _text, tb, new RectangleF(tx, r.Y, r.Right - tx - CheckW, r.Height), fmt);

            if (it.Checked)
                Gfx.GlowGlyph(g, "\uE73E", _icon, new RectangleF(r.Right - CheckW, r.Y, 28, r.Height), accent);
        }
    }

    int HitIndex(Point p)
    {
        for (int i = 0; i < _items.Count; i++)
            if (_items[i].Kind == Kind.Item && ItemRect(i).Contains(p)) return i;
        return -1;
    }

    void SetHover(int i)
    {
        if (i == _hover) return;
        _hover = i;
        Render();
    }

    void Activate(int i)
    {
        if (i < 0 || i >= _items.Count) return;
        var act = _items[i].Click;
        var owner = Owner;
        Close();
        if (act == null) return;
        if (owner != null && !owner.IsDisposed) owner.BeginInvoke(act);
        else act();
    }

    void MoveHover(int dir)
    {
        int n = _items.Count;
        if (n == 0) return;
        int i = _hover;
        for (int step = 0; step < n; step++)
        {
            i = ((i < 0 ? (dir > 0 ? -1 : 0) : i) + dir + n) % n;
            if (_items[i].Kind == Kind.Item) { SetHover(i); return; }
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Render();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = HitIndex(e.Location);
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        SetHover(i);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetHover(-1);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left) Activate(HitIndex(e.Location));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape: Close(); return true;
            case Keys.Down: MoveHover(1); return true;
            case Keys.Up: MoveHover(-1); return true;
            case Keys.Enter:
            case Keys.Space:
                if (_hover >= 0) Activate(_hover);
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!_closing) Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closedAt = DateTime.UtcNow;
        _closedFor = _anchor;
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _text.Dispose();
            _icon.Dispose();
            _head.Dispose();
        }
        base.Dispose(disposing);
    }
}
