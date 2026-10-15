using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MessengerPlayer;

/// <summary>Строка списка в проводнике: иконка, подпись и путь.</summary>
public sealed record PickItem(string Glyph, string Text, string Path);

/// <summary>Нарисованный вручную список (папки / быстрые места) в стиле плейлиста.</summary>
public sealed class PickList : Control
{
    const int RowH = 38;

    List<PickItem> _items = new();
    int _scroll, _hover = -1, _selected = -1;
    readonly Font _icons = new("Segoe MDL2 Assets", 12f, FontStyle.Regular, GraphicsUnit.Point);
    readonly Font _text = new("Segoe UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
    readonly Font _small = new("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText { get; set; } = "Здесь нет папок";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PickItem? Selected => _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

    public event Action<PickItem>? ItemClicked;
    public event Action<PickItem>? ItemActivated;   // двойной клик

    public PickList()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
    }

    public void SetItems(IEnumerable<PickItem> items)
    {
        _items = items.ToList();
        _scroll = 0;
        _hover = -1;
        _selected = -1;
        Invalidate();
    }

    int MaxScroll => Math.Max(0, _items.Count * RowH - Height);
    Rectangle RowRect(int i) => new(4, i * RowH - _scroll, Width - 8, RowH - 4);

    int Hit(Point p)
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
        _scroll = Math.Clamp(_scroll - Math.Sign(e.Delta) * RowH, 0, MaxScroll);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = Hit(e.Location);
        if (i != _hover) { _hover = i; Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
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
        int i = Hit(e.Location);
        if (i < 0) return;
        _selected = i;
        Invalidate();
        ItemClicked?.Invoke(_items[i]);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left) return;
        int i = Hit(e.Location);
        if (i >= 0) ItemActivated?.Invoke(_items[i]);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, EmptyText, _small, ClientRectangle, Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        int first = Math.Max(0, _scroll / RowH);
        int last = Math.Min(_items.Count - 1, (_scroll + Height) / RowH);

        for (int i = first; i <= last; i++)
        {
            var it = _items[i];
            var r = RowRect(i);

            using (var path = Gfx.Round(r, 10))
            {
                if (i == _selected)
                {
                    using var b = new SolidBrush(Color.FromArgb(55, Theme.Accent));
                    g.FillPath(b, path);
                }
                else if (i == _hover)
                {
                    using var b = new SolidBrush(Color.FromArgb(25, 255, 255, 255));
                    g.FillPath(b, path);
                }
            }

            TextRenderer.DrawText(g, it.Glyph, _icons, new Rectangle(r.X + 6, r.Y, 30, r.Height),
                i == _selected ? Theme.Accent : Theme.Dim, Gfx.Center);
            TextRenderer.DrawText(g, it.Text, _text, new Rectangle(r.X + 42, r.Y, r.Width - 50, r.Height),
                Color.White,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _icons.Dispose(); _text.Dispose(); _small.Dispose(); }
        base.Dispose(disposing);
    }
}

/// <summary>Текстовая кнопка: залитая акцентом (основная) или с контуром (вторичная).</summary>
public sealed class PillButton : Control
{
    bool _hover, _down;
    readonly bool _primary;
    readonly Font _font = new("Segoe UI Semibold", 10.5f, FontStyle.Regular, GraphicsUnit.Point);

    public PillButton(string text, bool primary)
    {
        Text = text;
        _primary = primary;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(150, 40);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(1, 1, Width - 3, Height - 3);
        var accent = Theme.Accent;
        using var path = Gfx.Round(r, 16);

        if (_primary)
        {
            var c = _down ? ControlPaint.Dark(accent, 0.1f) : _hover ? ControlPaint.Light(accent, 0.3f) : accent;
            using var b = new SolidBrush(c);
            g.FillPath(b, path);
            TextRenderer.DrawText(g, Text, _font, ClientRectangle, Color.Black, Gfx.Center);
        }
        else
        {
            if (_hover)
            {
                using var b = new SolidBrush(Color.FromArgb(35, accent));
                g.FillPath(b, path);
            }
            using var pen = new Pen(Color.FromArgb(_hover ? 200 : 120, accent), 1.5f);
            g.DrawPath(pen, path);
            TextRenderer.DrawText(g, Text, _font, ClientRectangle, accent, Gfx.Center);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _font.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Свой «проводник» для сохранения копии трека: быстрые места слева, папки справа,
/// новая папка, имя файла. В стиле приложения (стеклянные панели, цвет акцента, фон).
/// </summary>
public sealed class SaveDialog : Form
{
    // перетаскивание окна за шапку (окно без рамки)
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);
    const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2;

    readonly string _sourcePath;
    readonly Bitmap? _bg;
    string _dir;

    GlassPanel _head = null!, _places = null!, _folders = null!, _bottom = null!;
    Label _title = null!, _path = null!, _hint = null!;
    IconButton _btnUp = null!, _btnNew = null!, _btnClose = null!;
    PickList _placeList = null!, _folderList = null!;
    TextBox _name = null!;
    PillButton _ok = null!, _cancel = null!;

    /// <summary>Полный путь сохранённой копии (после успешного «Сохранить»).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? SavedPath { get; private set; }

    public SaveDialog(string sourcePath, Image? background, string? startDir)
    {
        _sourcePath = sourcePath;
        if (background != null) _bg = new Bitmap(background);

        _dir = Directory.Exists(startDir) ? startDir!
             : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        ClientSize = new Size(820, 560);
        BackColor = Color.FromArgb(14, 18, 16);
        DoubleBuffered = true;
        KeyPreview = true;

        Build();
        Theme.Changed += OnTheme;
        Navigate(_dir);
    }

    void OnTheme() => Invalidate(true);

    // ───────────────────────── интерфейс ─────────────────────────

    Label MakeLabel(float size, Color color, ContentAlignment align, FontStyle style = FontStyle.Regular) => new()
    {
        Font = new Font("Segoe UI", size, style),
        ForeColor = color,
        BackColor = Color.Transparent,
        TextAlign = align,
        AutoEllipsis = true
    };

    void Build()
    {
        // шапка
        _head = new GlassPanel { Radius = 20, Fill = Color.FromArgb(150, 0, 0, 0) };
        _title = MakeLabel(15f, Color.White, ContentAlignment.MiddleLeft, FontStyle.Bold);
        _title.Text = "Сохранить копию";
        _hint = MakeLabel(9f, Theme.Dim, ContentAlignment.MiddleLeft);
        _hint.Text = Path.GetFileName(_sourcePath);
        _btnClose = new IconButton("\uE711", 12F) { Size = new Size(36, 36) };
        _btnClose.Click += (_, _) => DialogResult = DialogResult.Cancel;
        _head.Controls.AddRange(new Control[] { _title, _hint, _btnClose });
        foreach (Control c in new Control[] { _head, _title, _hint })
            c.MouseDown += DragWindow;

        // быстрые места
        _places = new GlassPanel { Radius = 20, Fill = Color.FromArgb(150, 0, 0, 0) };
        _placeList = new PickList { EmptyText = "" };
        _places.Controls.Add(_placeList);
        _placeList.SetItems(BuildPlaces());
        _placeList.ItemClicked += it => Navigate(it.Path);

        // папки
        _folders = new GlassPanel { Radius = 20, Fill = Color.FromArgb(150, 0, 0, 0) };
        _btnUp = new IconButton("\uE74A", 13F) { Size = new Size(40, 40) };
        _btnNew = new IconButton("\uE8F4", 13F) { Size = new Size(40, 40) };
        _path = MakeLabel(10f, Color.White, ContentAlignment.MiddleLeft);
        _folderList = new PickList { EmptyText = "В этой папке нет вложенных папок" };
        _folders.Controls.AddRange(new Control[] { _btnUp, _btnNew, _path, _folderList });
        _btnUp.Click += (_, _) => GoUp();
        _btnNew.Click += (_, _) => NewFolder();
        _folderList.ItemActivated += it => Navigate(it.Path);

        // низ: имя файла + кнопки
        _bottom = new GlassPanel { Radius = 20, Fill = Color.FromArgb(235, 26, 31, 28) };
        _name = new TextBox
        {
            Font = new Font("Segoe UI", 12F),
            BackColor = Color.FromArgb(26, 31, 28),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.None,
            Text = Path.GetFileName(_sourcePath)
        };
        _ok = new PillButton("Сохранить сюда", true);
        _cancel = new PillButton("Отмена", false) { Size = new Size(110, 40) };
        _bottom.Controls.AddRange(new Control[] { _name, _ok, _cancel });
        _ok.Click += (_, _) => Save();
        _cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[] { _head, _places, _folders, _bottom });
        Resize += (_, _) => LayoutAll();
        LayoutAll();

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel;
            else if (e.KeyCode == Keys.Enter && !_name.Focused) Save();
        };
        _name.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Save(); }
        };
    }

    void LayoutAll()
    {
        const int m = 14, gap = 12;
        int w = ClientSize.Width, h = ClientSize.Height;

        _head.SetBounds(m, m, w - 2 * m, 64);
        _title.SetBounds(20, 8, _head.Width - 80, 28);
        _hint.SetBounds(22, 36, _head.Width - 80, 20);
        _btnClose.Location = new Point(_head.Width - 48, 14);

        int bottomH = 72;
        int midTop = _head.Bottom + gap;
        int midH = h - m - bottomH - gap - midTop;

        _places.SetBounds(m, midTop, 210, midH);
        _folders.SetBounds(_places.Right + gap, midTop, w - m - (_places.Right + gap), midH);
        _placeList.SetBounds(8, 10, _places.Width - 16, _places.Height - 20);

        _btnUp.Location = new Point(10, 10);
        _btnNew.Location = new Point(_folders.Width - 50, 10);
        _path.SetBounds(58, 10, _folders.Width - 58 - 58, 40);
        _folderList.SetBounds(8, 58, _folders.Width - 16, _folders.Height - 66);

        _bottom.SetBounds(m, h - m - bottomH, w - 2 * m, bottomH);
        _cancel.Location = new Point(_bottom.Width - 16 - _cancel.Width, (bottomH - _cancel.Height) / 2);
        _ok.Location = new Point(_cancel.Left - 10 - _ok.Width, (bottomH - _ok.Height) / 2);
        int nameW = _ok.Left - 16 - 20;
        _name.SetBounds(20, (bottomH - _name.PreferredHeight) / 2, Math.Max(100, nameW), _name.PreferredHeight);
    }

    // ───────────────────────── места и папки ─────────────────────────

    static List<PickItem> BuildPlaces()
    {
        var list = new List<PickItem>();

        void Add(string glyph, string text, string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) list.Add(new PickItem(glyph, text, path));
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add("\uE80F", "Домашняя папка", home);
        Add("\uE8B7", "Рабочий стол", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Add("\uE8A5", "Документы", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add("\uE896", "Загрузки", Path.Combine(home, "Downloads"));
        Add("\uE8D6", "Музыка", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));

        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Диск" : d.VolumeLabel;
                list.Add(new PickItem("\uE88E", $"{label} ({d.Name.TrimEnd('\\')})", d.RootDirectory.FullName));
            }
            catch { }
        }
        return list;
    }

    void Navigate(string dir)
    {
        try
        {
            var items = new List<PickItem>();
            foreach (var d in new DirectoryInfo(dir).EnumerateDirectories()
                         .Where(x => (x.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                         .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
                items.Add(new PickItem("\uE8B7", d.Name, d.FullName));

            _dir = dir;
            _folderList.SetItems(items);
            _path.Text = dir;
            _btnUp.Enabled = Directory.GetParent(dir) != null;
        }
        catch (UnauthorizedAccessException)
        {
            _hint.Text = "Нет доступа к этой папке";
        }
        catch (Exception ex)
        {
            _hint.Text = "Не удалось открыть папку: " + ex.Message;
        }
    }

    void GoUp()
    {
        var parent = Directory.GetParent(_dir);
        if (parent != null) Navigate(parent.FullName);
    }

    // ───────────────────────── новая папка ─────────────────────────

    void NewFolder()
    {
        var name = AskText("Новая папка", "Введите название папки", "Новая папка");
        if (string.IsNullOrWhiteSpace(name)) return;

        name = name.Trim();
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            _hint.Text = "В названии папки есть недопустимые символы";
            return;
        }

        try
        {
            var path = Path.Combine(_dir, name);
            Directory.CreateDirectory(path);
            Navigate(path);
        }
        catch (Exception ex)
        {
            _hint.Text = "Не удалось создать папку: " + ex.Message;
        }
    }

    /// <summary>Маленькое окно ввода текста в том же стиле.</summary>
    string? AskText(string title, string caption, string initial)
    {
        using var f = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false,
            ClientSize = new Size(440, 190),
            BackColor = Color.FromArgb(14, 18, 16),
            KeyPreview = true
        };
        var panel = new GlassPanel { Radius = 20, Fill = Color.FromArgb(235, 26, 31, 28), Location = new Point(10, 10), Size = new Size(420, 170) };
        var lbl = MakeLabel(12f, Color.White, ContentAlignment.MiddleLeft, FontStyle.Bold);
        lbl.Text = caption;
        lbl.SetBounds(20, 14, 380, 30);
        var box = new TextBox
        {
            Font = new Font("Segoe UI", 12F),
            BackColor = Color.FromArgb(26, 31, 28),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.None,
            Text = initial
        };
        box.SetBounds(22, 60, 376, box.PreferredHeight);
        var ok = new PillButton("Создать", true) { Size = new Size(120, 38), Location = new Point(278, 112) };
        var cancel = new PillButton("Отмена", false) { Size = new Size(110, 38), Location = new Point(158, 112) };
        panel.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
        f.Controls.Add(panel);

        ok.Click += (_, _) => f.DialogResult = DialogResult.OK;
        cancel.Click += (_, _) => f.DialogResult = DialogResult.Cancel;
        f.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) f.DialogResult = DialogResult.Cancel;
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; f.DialogResult = DialogResult.OK; }
        };
        f.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(160, Theme.Accent), 1);
            e.Graphics.DrawRectangle(pen, 0, 0, f.Width - 1, f.Height - 1);
        };
        f.Shown += (_, _) => { box.Focus(); box.SelectAll(); };

        return f.ShowDialog(this) == DialogResult.OK ? box.Text : null;
    }

    // ───────────────────────── сохранение ─────────────────────────

    void Save()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            _hint.Text = "Укажите корректное имя файла";
            return;
        }

        // оставляем расширение исходника, если его не указали
        var srcExt = Path.GetExtension(_sourcePath);
        if (!name.EndsWith(srcExt, StringComparison.OrdinalIgnoreCase)) name += srcExt;

        var target = Path.Combine(_dir, name);
        try
        {
            if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(_sourcePath), StringComparison.OrdinalIgnoreCase))
            {
                _hint.Text = "Это тот же файл — выберите другую папку или имя";
                return;
            }

            if (File.Exists(target))
            {
                var ask = MessageBox.Show(this, "Файл с таким именем уже есть. Заменить?", "Сохранить копию",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ask != DialogResult.Yes) return;
            }

            File.Copy(_sourcePath, target, true);
            SavedPath = target;
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            _hint.Text = "Не удалось сохранить: " + ex.Message;
        }
    }

    // ───────────────────────── окно ─────────────────────────

    void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        var sz = ClientSize;
        if (_bg != null)
        {
            float scale = Math.Max(sz.Width / (float)_bg.Width, sz.Height / (float)_bg.Height);
            float dw = _bg.Width * scale, dh = _bg.Height * scale;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(_bg, (sz.Width - dw) / 2, (sz.Height - dh) / 2, dw, dh);
            using var shade = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
            g.FillRectangle(shade, 0, 0, sz.Width, sz.Height);
        }
        else
        {
            using var br = new LinearGradientBrush(new Rectangle(0, 0, sz.Width, sz.Height),
                Color.FromArgb(18, 28, 22), Color.FromArgb(6, 8, 7), 60f);
            g.FillRectangle(br, 0, 0, sz.Width, sz.Height);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(160, Theme.Accent), 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Theme.Changed -= OnTheme;
            _bg?.Dispose();
        }
        base.Dispose(disposing);
    }
}
