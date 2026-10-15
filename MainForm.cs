using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MessengerPlayer;

public partial class MainForm : Form
{
    enum View { All, Favorites, YouTube }

    // ── данные ──
    readonly AppSettings _s = Store.Load();
    readonly AudioEngine _audio = new();
    readonly Random _rng = new();
    readonly ToolTip _tip = new();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    readonly List<FileSystemWatcher> _watchers = new();
    readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);

    List<Track> _shown = new();
    Track? _current;
    View _view = View.All;
    bool _favOnly;
    string? _filterSource;
    double _preMute = 0.8;

    // фон
    Bitmap? _bgOriginal, _bgCache;
    Size _bgCacheSize;

    // YouTube
    CancellationTokenSource? _searchCts, _downloadCts;
    bool _downloading;

    // ── элементы интерфейса ──
    GlassPanel _nav = null!, _player = null!, _searchPanel = null!, _listPanel = null!;
    IconButton _navHome = null!, _navFav = null!, _navYt = null!, _navSrc = null!, _navSet = null!;
    Label _lblTitle = null!, _lblSub = null!, _lblCur = null!, _lblTotal = null!, _lblCount = null!, _lblStatus = null!;
    SeekBar _seek = null!, _vol = null!;
    IconButton _btnShuffle = null!, _btnPrev = null!, _btnPlay = null!, _btnNext = null!, _btnRepeat = null!, _btnVol = null!;
    IconButton _btnAdd = null!, _btnPaste = null!, _btnBg = null!, _btnScan = null!, _btnCancel = null!;
    TextBox _searchBox = null!;
    TrackList _list = null!;
    TrackList.YtTrackList _ytList = null!;
    readonly List<Swatch> _swatches = new();
    Bitmap? _playerBg;
    CoverArt _cover = null!;
    Equalizer _eq = null!;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainForm()
    {
        Text = "Messenger Player";
        ClientSize = new Size(1000, 700);
        MinimumSize = new Size(880, 620);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(14, 18, 16);
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        AllowDrop = true;

        Theme.Accent = Color.FromArgb(_s.Accent);
        Orbiter.Enabled = _s.NeonAnim;
        _audio.Volume = (float)_s.Volume;
        LoadBackground();
        _playerBg = LoadEmbedded("player_bg.png");

        BuildUi();
        HookDrop(this);

        _audio.Ended += () =>
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => Advance(auto: true)));
        };
        _audio.Failed += msg =>
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(new Action(() =>
                {
                    var cur = _current;
                    if (cur != null && Mp3Converter.NeedsConversion(cur.Path)) { RepairTrack(cur); return; }
                    SetStatus("Не удалось воспроизвести: " + msg);
                    UpdatePlayIcon();
                }));
        };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        Theme.Changed += OnThemeChanged;
        Shown += async (_, _) =>
        {
            StartWatchers();
            if (_s.AutoAdd) await RescanAsync(silent: true);
        };

        RefreshView();
        var last = _s.Tracks.FirstOrDefault(t => t.Path == _s.LastTrack);
        if (last != null) PlayTrack(last, autoPlay: false);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int dark = 1;
            DwmSetWindowAttribute(Handle, 20, ref dark, 4);          // тёмная рамка окна
            int caption = 0x000C0E0A, text = 0x00F0F0F0;               // COLORREF = 0x00BBGGRR
            DwmSetWindowAttribute(Handle, 35, ref caption, 4);       // цвет заголовка (Windows 11)
            DwmSetWindowAttribute(Handle, 36, ref text, 4);          // цвет текста заголовка
        }
        catch { /* старая Windows — остаётся стандартный заголовок */ }
    }

    // ───────────────────────── Построение интерфейса ─────────────────────────

    static Bitmap? LoadEmbedded(string name)
    {
        try
        {
            using var s = typeof(MainForm).Assembly.GetManifestResourceStream(name);
            if (s == null) return null;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            ms.Position = 0;
            using var img = Image.FromStream(ms);
            return new Bitmap(img);
        }
        catch { return null; }
    }

    Label MakeLabel(Font font, Color color, ContentAlignment align = ContentAlignment.MiddleLeft) => new()
    {
        Font = font,
        ForeColor = color,
        BackColor = Color.Transparent,
        TextAlign = align,
        AutoEllipsis = true,
        UseMnemonic = false
    };

    IconButton Nav(string glyph, string tip, GlassPanel parent)
    {
        var b = new IconButton(glyph, 22F) { Size = new Size(64, 64) };
        _tip.SetToolTip(b, tip);
        parent.Controls.Add(b);
        return b;
    }

    void BuildUi()
    {
        var accentFont = new Font("Segoe UI Semibold", 22F, FontStyle.Regular, GraphicsUnit.Point);
        var subFont = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
        var smallFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        // Левая навигация
        _nav = new GlassPanel { Radius = 32, Fill = Color.FromArgb(135, 0, 0, 0), Orbit = true };
        _navHome = Nav("", "Все треки", _nav);
        _navFav = Nav("", "Избранное", _nav);
        _navYt = Nav("", "Импорт с YouTube", _nav);
        _navSrc = Nav("", "Мессенджеры (фильтр по источнику)", _nav);
        _navSet = Nav("", "Настройки", _nav);

        _navHome.Click += (_, _) => { _favOnly = false; _filterSource = null; SetView(View.All); };
        _navFav.Click += (_, _) => { _favOnly = true; _filterSource = null; SetView(View.Favorites); };
        _navYt.Click += (_, _) => SetView(View.YouTube);
        _navSrc.Click += (_, _) => ShowSourceMenu();
        _navSet.Click += (_, _) => ShowSettingsMenu();

        // Карточка плеера
        _player = new GlassPanel { Radius = 32, Fill = Color.FromArgb(135, 0, 0, 0), Orbit = true };
        _lblTitle = MakeLabel(accentFont, Color.White);
        _lblTitle.Text = "Выберите трек";
        _lblSub = MakeLabel(subFont, Theme.Accent);
        _lblSub.Text = "Двойной клик по треку — играть";
        _lblCur = MakeLabel(smallFont, Theme.Dim, ContentAlignment.MiddleLeft);
        _lblCur.Text = "0:00";
        _lblTotal = MakeLabel(smallFont, Theme.Dim, ContentAlignment.MiddleRight);
        _lblTotal.Text = "0:00";

        _seek = new SeekBar();
        _seek.ValueChanged += v =>
        {
            if (_audio.HasTrack) _lblCur.Text = Fmt(TimeSpan.FromSeconds(_audio.Duration.TotalSeconds * v));
        };
        _seek.Committed += v =>
        {
            if (_audio.HasTrack) _audio.Position = TimeSpan.FromSeconds(_audio.Duration.TotalSeconds * v);
        };

        _btnShuffle = new IconButton("", 16F) { Size = new Size(48, 48), Active = _s.Shuffle };
        _btnPrev = new IconButton("", 18F) { Size = new Size(48, 48) };
        _btnPlay = new IconButton("", 28F) { Size = new Size(76, 76), Ring = true };
        _btnNext = new IconButton("", 18F) { Size = new Size(48, 48) };
        _btnRepeat = new IconButton("", 16F) { Size = new Size(48, 48) };
        _btnVol = new IconButton("", 14F) { Size = new Size(40, 40) };
        _vol = new SeekBar { Value = _s.Volume };

        _tip.SetToolTip(_btnShuffle, "Перемешать");
        _tip.SetToolTip(_btnPrev, "Предыдущий (Ctrl+←)");
        _tip.SetToolTip(_btnPlay, "Играть / пауза (Пробел)");
        _tip.SetToolTip(_btnNext, "Следующий (Ctrl+→)");
        _tip.SetToolTip(_btnRepeat, "Повтор: выкл / все / один");
        _tip.SetToolTip(_btnVol, "Без звука");

        _btnShuffle.Click += (_, _) => { _s.Shuffle = !_s.Shuffle; SyncToggles(); Store.Save(_s); };
        _btnPrev.Click += (_, _) => Advance(auto: false, back: true);
        _btnPlay.Click += (_, _) => TogglePlay();
        _btnNext.Click += (_, _) => Advance(auto: false);
        _btnRepeat.Click += (_, _) => { _s.Repeat = (_s.Repeat + 1) % 3; SyncToggles(); Store.Save(_s); };
        _btnVol.Click += (_, _) =>
        {
            if (_s.Volume > 0) { _preMute = _s.Volume; SetVolume(0); }
            else SetVolume(_preMute > 0 ? _preMute : 0.8);
            _vol.Value = _s.Volume;
        };
        _vol.ValueChanged += SetVolume;
        _vol.Committed += _ => Store.Save(_s);

        _player.Controls.AddRange(new Control[]
        {
            _lblTitle, _lblSub, _lblCur, _seek, _lblTotal,
            _btnShuffle, _btnPrev, _btnPlay, _btnNext, _btnRepeat, _btnVol, _vol
        });
        _cover = new CoverArt { Image = _playerBg };   // статичное фото вместо меняющейся обложки
        _eq = new Equalizer();
        _player.Controls.Add(_cover);
        _player.Controls.Add(_eq);

        // Строка поиска и кнопки
        _searchPanel = new GlassPanel { Radius = 22, Glow = 0.7f, Orbit = true, Fill = Color.FromArgb(235, 26, 31, 28) };
        _searchBox = new TextBox
        {
            Font = new Font("Segoe UI", 12F),
            BackColor = Color.FromArgb(26, 31, 28),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.None
        };
        _searchBox.PlaceholderText = "Поиск по трекам…  (Ctrl+F)";
        _searchPanel.Controls.Add(_searchBox);
        _searchPanel.Paint += (_, e) =>
        {
            using var f = new Font("Segoe MDL2 Assets", 13f, FontStyle.Regular, GraphicsUnit.Point);
            Gfx.GlowGlyph(e.Graphics, "\uE721", f, new RectangleF(16, 0, 28, _searchPanel.Height), Color.FromArgb(225, 232, 228), glow: false);
        };
        _searchBox.TextChanged += (_, _) => { if (_view != View.YouTube) RefreshView(); };
        _searchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _view == View.YouTube)
            {
                e.SuppressKeyPress = true;
                StartMusicSearch();
            }
        };

        _btnAdd = new IconButton("", 16F) { Size = new Size(44, 44) };
        _btnPaste = new IconButton("", 16F) { Size = new Size(44, 44) };
        _btnBg = new IconButton("", 16F) { Size = new Size(44, 44) };
        _btnScan = new IconButton("", 16F) { Size = new Size(44, 44) };
        _btnCancel = new IconButton("", 14F) { Size = new Size(36, 36), Visible = false };
        _tip.SetToolTip(_btnAdd, "Добавить файлы… (Ctrl+O)");
        _tip.SetToolTip(_btnPaste, "Вставить из буфера (Ctrl+V): трек или картинку-фон");
        _tip.SetToolTip(_btnBg, "Выбрать фон…");
        _tip.SetToolTip(_btnScan, "Найти в мессенджерах");
        _tip.SetToolTip(_btnCancel, "Отменить загрузку");
        _btnAdd.Click += (_, _) => AddFilesDialog();
        _btnPaste.Click += (_, _) => PasteFromClipboard();
        _btnBg.Click += (_, _) => ChooseBackground();
        _btnScan.Click += async (_, _) => await RescanAsync(silent: false);
        _btnCancel.Click += (_, _) => _downloadCts?.Cancel();

        var colors = new[]
        {
            Color.FromArgb(0, 255, 64), Color.FromArgb(0, 200, 255), Color.FromArgb(170, 90, 255),
            Color.FromArgb(255, 60, 140), Color.FromArgb(255, 150, 0), Color.FromArgb(255, 70, 70)
        };
        foreach (var c in colors)
        {
            var sw = new Swatch(c);
            _tip.SetToolTip(sw, "Цвет интерфейса");
            _swatches.Add(sw);
        }

        // Список
        _listPanel = new GlassPanel { Radius = 28, Fill = Color.FromArgb(135, 0, 0, 0), Orbit = true };
        _list = new TrackList();
        _ytList = new TrackList.YtTrackList { Visible = false };
        _lblCount = MakeLabel(smallFont, Theme.Dim);
        _lblStatus = MakeLabel(smallFont, Theme.Dim, ContentAlignment.MiddleRight);

        _list.PlayRequested += t => PlayTrack(t, autoPlay: true);
        _list.DeleteRequested += RemoveTrack;
        _list.SaveRequested += SaveCopy;
        _list.FavoriteToggled += t =>
        {
            t.Favorite = !t.Favorite;
            Store.Save(_s);
            if (_favOnly) RefreshView(); else _list.Redraw();
        };
        _ytList.TrackDoubleClicked += OnYtTrackActivated;

        _listPanel.Controls.AddRange(new Control[] { _list, _ytList, _lblCount, _lblStatus, _btnCancel });

        Controls.Add(_nav);
        Controls.Add(_player);
        Controls.Add(_searchPanel);
        Controls.Add(_btnAdd);
        Controls.Add(_btnPaste);
        Controls.Add(_btnBg);
        Controls.Add(_btnScan);
        foreach (var sw in _swatches) Controls.Add(sw);
        Controls.Add(_listPanel);

        SyncToggles();
        UpdateVolIcon();
        LayoutAll();
        Resize += (_, _) => LayoutAll();
    }

    // ───────────────────────── Раскладка ─────────────────────────

    void LayoutAll()
    {
        int W = ClientSize.Width, H = ClientSize.Height;
        if (W <= 0 || H <= 0) return;

        const int m = 24, navW = 88, gap = 16, playerH = 270, rowH = 44;
        int x = m + navW + gap, w = W - x - m;

        _nav.SetBounds(m, m, navW, H - 2 * m);
        var navBtns = new[] { _navHome, _navFav, _navYt, _navSrc, _navSet };
        for (int i = 0; i < navBtns.Length; i++)
            navBtns[i].SetBounds((navW - 64) / 2, 18 + i * 80, 64, 64);

        _player.SetBounds(x, m, w, playerH);
        LayoutPlayer();

        // строка поиска + кнопки + цвета
        int y = m + playerH + gap;
        int sx = x + w;
        for (int i = _swatches.Count - 1; i >= 0; i--)
        {
            sx -= 30;
            _swatches[i].SetBounds(sx, y + (rowH - 30) / 2, 30, 30);
            sx -= 6;
        }
        sx -= 6;
        foreach (var b in new[] { _btnScan, _btnBg, _btnPaste, _btnAdd })
        {
            sx -= 44;
            b.SetBounds(sx, y, 44, 44);
            sx -= 2;
        }
        sx -= 10;
        _searchPanel.SetBounds(x, y, Math.Max(100, sx - x), rowH);
        _searchBox.SetBounds(54, (rowH - _searchBox.PreferredHeight) / 2, Math.Max(40, _searchPanel.Width - 54 - 20), _searchBox.PreferredHeight);

        int ly = y + rowH + gap;
        _listPanel.SetBounds(x, ly, w, Math.Max(80, H - ly - m));
        LayoutList();
    }

    void LayoutPlayer()
    {
        int pw = _player.Width, ph = _player.Height;

        // обложка слева (видимый квадрат ~190 px, +6 px на свечение)
        int cs = Math.Min(ph - 2 * 24 - 24, 196);
        _cover.SetBounds(24, 24, cs + 6, cs + 6);

        int left = 24 + cs + 6 + 22;
        int right = pw - 36;
        int cw = Math.Max(100, right - left);

        _lblTitle.SetBounds(left, 22, cw, 44);
        _lblSub.SetBounds(left, 66, cw, 26);

        _lblCur.SetBounds(left, 112, 50, 24);
        _lblTotal.SetBounds(right - 50, 112, 50, 24);
        _seek.SetBounds(left + 54, 112, Math.Max(50, cw - 108), 24);

        int cy = 184;
        int vw = Math.Clamp(cw / 5, 70, 130);
        bool showVol = cw > 420;
        _btnVol.Visible = _vol.Visible = showVol;
        _vol.SetBounds(right - vw, cy - 12, vw, 24);
        _btnVol.SetBounds(_vol.Left - 44, cy - 20, 40, 40);

        int regionR = showVol ? _btnVol.Left : right;
        int mid = (left + regionR) / 2;
        float k = Math.Clamp((regionR - left) / 2f / 190f, 0.74f, 1f);
        int d1 = (int)(87 * k), d2 = (int)(160 * k);
        _btnPlay.SetBounds(mid - 38, cy - 38, 76, 76);
        _btnPrev.SetBounds(mid - d1 - 24, cy - 24, 48, 48);
        _btnNext.SetBounds(mid + d1 - 24, cy - 24, 48, 48);
        _btnShuffle.SetBounds(mid - d2 - 24, cy - 24, 48, 48);
        _btnRepeat.SetBounds(mid + d2 - 24, cy - 24, 48, 48);

        _eq.SetBounds(right - 196, ph - 24 - 44, 196, 44);
        _eq.Visible = cw > 520;
    }

    void LayoutList()
    {
        int w = _listPanel.Width, h = _listPanel.Height;
        var r = new Rectangle(14, 14, w - 28, h - 14 - 40);
        _list.Bounds = r;
        _ytList.Bounds = r;
        _lblCount.SetBounds(24, h - 34, 150, 24);
        _btnCancel.SetBounds(w - 24 - 36, h - 40, 36, 36);
        int right = _btnCancel.Visible ? 44 : 0;
        _lblStatus.SetBounds(180, h - 34, Math.Max(50, w - 180 - 24 - right), 24);
    }

    // ───────────────────────── Фон ─────────────────────────

    void LoadBackground()
    {
        LoadBackgroundCore();
    }

    void LoadBackgroundCore()
    {
        _bgOriginal?.Dispose(); _bgOriginal = null;
        _bgCache?.Dispose(); _bgCache = null;

        var p = _s.Background;
        if (string.IsNullOrEmpty(p) || !File.Exists(p)) return;
        try
        {
            using var ms = new MemoryStream(File.ReadAllBytes(p));
            using var img = Image.FromStream(ms);
            _bgOriginal = new Bitmap(img);
        }
        catch { _bgOriginal = null; }
    }

    void EnsureBgCache()
    {
        var sz = ClientSize;
        if (sz.Width <= 0 || sz.Height <= 0) return;
        if (_bgCache != null && _bgCacheSize == sz) return;

        _bgCache?.Dispose();
        var bmp = new Bitmap(sz.Width, sz.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            if (_bgOriginal != null)
            {
                float scale = Math.Max(sz.Width / (float)_bgOriginal.Width, sz.Height / (float)_bgOriginal.Height);
                float dw = _bgOriginal.Width * scale, dh = _bgOriginal.Height * scale;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(_bgOriginal, (sz.Width - dw) / 2, (sz.Height - dh) / 2, dw, dh);
                using var shade = new SolidBrush(Color.FromArgb(105, 0, 0, 0));
                g.FillRectangle(shade, 0, 0, sz.Width, sz.Height);
            }
            else
            {
                using var br = new LinearGradientBrush(new Rectangle(0, 0, sz.Width, sz.Height),
                    Color.FromArgb(18, 28, 22), Color.FromArgb(6, 8, 7), 60f);
                g.FillRectangle(br, 0, 0, sz.Width, sz.Height);
            }
        }
        _bgCache = bmp;
        _bgCacheSize = sz;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        EnsureBgCache();
        if (_bgCache != null) e.Graphics.DrawImageUnscaled(_bgCache, 0, 0);
        else base.OnPaintBackground(e);
    }

    void ClearOldBackgrounds()
    {
        try
        {
            if (!Directory.Exists(Store.Dir)) return;
            foreach (var f in Directory.GetFiles(Store.Dir, "background.*"))
                try { File.Delete(f); } catch { }
        }
        catch { }
    }

    void SetBackgroundFromFile(string path)
    {
        try
        {
            Directory.CreateDirectory(Store.Dir);
            var ext = Path.GetExtension(path);
            var dest = Path.Combine(Store.Dir, "background" + ext);

            // проверяем, что это картинка, до того как трогать старый фон
            byte[] bytes = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(bytes)) using (Image.FromStream(ms)) { }

            ClearOldBackgrounds();
            File.WriteAllBytes(dest, bytes);
            _s.Background = dest;
            Store.Save(_s);
            LoadBackground();
            Invalidate(true);
        }
        catch (Exception ex)
        {
            SetStatus("Не удалось поставить фон: " + ex.Message);
        }
    }

    void SetBackgroundFromImage(Image img)
    {
        try
        {
            Directory.CreateDirectory(Store.Dir);
            ClearOldBackgrounds();
            var dest = Path.Combine(Store.Dir, "background.png");
            img.Save(dest, ImageFormat.Png);
            _s.Background = dest;
            Store.Save(_s);
            LoadBackground();
            Invalidate(true);
        }
        catch (Exception ex)
        {
            SetStatus("Не удалось поставить фон: " + ex.Message);
        }
    }

    void ChooseBackground()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Выберите фон",
            Filter = "Картинки|*.jpg;*.jpeg;*.jfif;*.png;*.bmp;*.gif"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            SetBackgroundFromFile(dlg.FileName);
            SetStatus("Фон обновлён");
        }
    }

    void ResetBackground()
    {
        ClearOldBackgrounds();
        _s.Background = null;
        Store.Save(_s);
        LoadBackground();
        Invalidate(true);
    }

    // ───────────────────────── Вид / список ─────────────────────────

    void SetView(View v)
    {
        _view = v;
        bool yt = v == View.YouTube;

        _list.Visible = !yt;
        _ytList.Visible = yt;
        _btnCancel.Visible = yt && _downloading;
        _searchBox.PlaceholderText = yt
            ? "Название или ссылка на YouTube · Enter — искать, двойной клик — скачать"
            : "Поиск по трекам…  (Ctrl+F)";
        _searchBox.Text = "";

        SyncToggles();
        LayoutList();
        if (yt)
        {
            SetStatus(_ytList.Visible ? "Введите запрос и нажмите Enter" : "");
            _lblCount.Text = "YouTube";
        }
        else RefreshView();
        _searchBox.Focus();
    }

    void SyncToggles()
    {
        _navHome.Active = _view == View.All && _filterSource == null;
        _navFav.Active = _view == View.Favorites;
        _navYt.Active = _view == View.YouTube;
        _navSrc.Active = _view == View.All && _filterSource != null;
        _btnShuffle.Active = _s.Shuffle;
        _btnRepeat.Active = _s.Repeat > 0;
        _btnRepeat.Glyph = _s.Repeat == 2 ? "" : "";
        _btnRepeat.Invalidate();
    }

    void RefreshView()
    {
        if (_view == View.YouTube) return;

        IEnumerable<Track> q = _s.Tracks;
        if (_favOnly) q = q.Where(t => t.Favorite);
        if (_filterSource != null) q = q.Where(t => t.Source == _filterSource);
        var text = _searchBox.Text.Trim();
        if (text.Length > 0) q = q.Where(t => t.Title.Contains(text, StringComparison.CurrentCultureIgnoreCase));

        _shown = q.ToList();
        _list.SetItems(_shown);
        _list.Playing = _current;
        _lblCount.Text = $"{_shown.Count} {Plural(_shown.Count)}";
    }

    static string Plural(int n)
    {
        int a = n % 100, b = n % 10;
        if (a is >= 11 and <= 14) return "треков";
        return b == 1 ? "трек" : b is >= 2 and <= 4 ? "трека" : "треков";
    }

    void SetStatus(string text)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(text))); return; }
        _lblStatus.Text = text;
    }

    void ToggleNeon()
    {
        _s.NeonAnim = !_s.NeonAnim;
        Orbiter.Enabled = _s.NeonAnim;
        UpdatePlayIcon();
        Store.Save(_s);
        SetStatus(_s.NeonAnim
            ? "Неоновая анимация включена (Ctrl+N)"
            : "Неоновая анимация выключена — меньше нагрузка на процессор (Ctrl+N)");
    }

    // ───────────────────────── Меню ─────────────────────────

    void ShowSourceMenu()
    {
        var items = new List<NeonMenu.Entry>
        {
            NeonMenu.Entry.Header("Источники"),
            NeonMenu.Entry.Item($"Все источники ({_s.Tracks.Count})",
                () => { _favOnly = false; _filterSource = null; SetView(View.All); },
                "\uE80F", _filterSource == null && _view == View.All && !_favOnly),
            NeonMenu.Entry.Separator()
        };

        foreach (var g in _s.Tracks.GroupBy(t => t.Source).OrderBy(g => g.Key))
        {
            var name = g.Key;
            items.Add(NeonMenu.Entry.Item($"{name} ({g.Count()})",
                () => { _favOnly = false; _filterSource = name; SetView(View.All); },
                "\uE8D6", _filterSource == name));
        }
        NeonMenu.Show(_navSrc, items);
    }

    void ShowSettingsMenu()
    {
        var items = new List<NeonMenu.Entry>
        {
            NeonMenu.Entry.Header("Настройки"),
            NeonMenu.Entry.Item("Добавить папку мессенджера…", async () => await AddFolder(), "\uE8F4"),
            NeonMenu.Entry.Item("Автодобавление новых файлов", () =>
            {
                _s.AutoAdd = !_s.AutoAdd;
                Store.Save(_s);
                StartWatchers();
            }, "\uE895", _s.AutoAdd),
            NeonMenu.Entry.Item("Неоновая анимация (Ctrl+N)", ToggleNeon, "\uE706", _s.NeonAnim),
            NeonMenu.Entry.Separator(),
            NeonMenu.Entry.Item("Сбросить фон", ResetBackground, "\uE91B"),
            NeonMenu.Entry.Item("Очистить плейлист", () =>
            {
                if (MessageBox.Show(this, "Очистить плейлист? Файлы на диске останутся.", "Messenger Player",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                StopPlayback();
                _s.Tracks.Clear();
                Store.Save(_s);
                RefreshView();
            }, "\uE74D"),
            NeonMenu.Entry.Item("Открыть папку данных", () =>
            {
                try { Directory.CreateDirectory(Store.Dir); Process.Start("explorer.exe", Store.Dir); } catch { }
            }, "\uE838")
        };
        NeonMenu.Show(_navSet, items);
    }

    async Task AddFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Папка, куда мессенджер сохраняет файлы" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (!_s.CustomFolders.Contains(dlg.SelectedPath, StringComparer.OrdinalIgnoreCase))
            _s.CustomFolders.Add(dlg.SelectedPath);
        Store.Save(_s);
        StartWatchers();
        await RescanAsync(silent: false);
    }

    // ───────────────────────── Добавление треков ─────────────────────────

    bool AddTrack(string path, string source, string? ytId = null)
    {
        if (_s.Tracks.Any(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase))) return false;
        _s.Removed.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));   // добавили явно — снова разрешаем
        _s.Tracks.Insert(0, new Track { Path = path, Source = source, YtId = ytId });
        return true;
    }

    void AddFilesDialog()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Добавить музыку",
            Multiselect = true,
            Filter = "Аудио|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac;*.aiff;*.mp4|Все файлы|*.*"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) HandleFiles(dlg.FileNames, "Файлы");
    }

    void HandleFiles(IEnumerable<string> paths, string source)
    {
        int added = 0;
        bool bg = false;
        foreach (var raw in paths)
        {
            if (!File.Exists(raw)) continue;
            if (Media.IsAudio(raw))
            {
                var p = Messengers.Import(raw);
                if (AddTrack(p, source)) added++;
            }
            else if (Media.IsImage(raw))
            {
                SetBackgroundFromFile(raw);
                bg = true;
            }
        }

        if (added > 0) { Store.Save(_s); RefreshView(); }

        var parts = new List<string>();
        if (added > 0) parts.Add("Добавлено: " + added);
        if (bg) parts.Add("фон обновлён");
        SetStatus(parts.Count > 0
            ? string.Join(" · ", parts)
            : "Нечего добавлять (поддерживаются mp3, m4a, flac, wav, wma, aac)");
    }

    void PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList().Cast<string>().ToList();
                HandleFiles(files, "Буфер обмена");
            }
            else if (Clipboard.ContainsImage())
            {
                using var img = Clipboard.GetImage();
                if (img != null) { SetBackgroundFromImage(img); SetStatus("Фон обновлён из буфера обмена"); }
            }
            else SetStatus("В буфере нет файлов или картинки");
        }
        catch (Exception ex) { SetStatus("Не удалось вставить: " + ex.Message); }
    }

    void HookDrop(Control c)
    {
        if (c is TextBox) return;
        c.AllowDrop = true;
        c.DragEnter += OnDragEnter;
        c.DragDrop += OnDragDrop;
        foreach (Control child in c.Controls) HookDrop(child);
    }

    void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) HandleFiles(files, "Перетащено");
    }

    // ───────────────────────── Авто-поиск в мессенджерах ─────────────────────────

    void StopWatchers()
    {
        foreach (var w in _watchers) { try { w.EnableRaisingEvents = false; w.Dispose(); } catch { } }
        _watchers.Clear();
    }

    void StartWatchers()
    {
        StopWatchers();
        if (!_s.AutoAdd) return;

        foreach (var src in Messengers.All(_s.CustomFolders))
        {
            try
            {
                var w = new FileSystemWatcher(src.Folder)
                {
                    IncludeSubdirectories = src.Recursive,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    InternalBufferSize = 32768
                };
                w.Created += (_, e) => PostNewFile(e.FullPath);
                w.Renamed += (_, e) => PostNewFile(e.FullPath);
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch { }
        }
    }

    void PostNewFile(string path)
    {
        if (!Media.IsAudio(path)) return;
        if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => OnNewFile(path)));
    }

    async void OnNewFile(string path)
    {
        if (_s.Removed.Any(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase))) return;
        lock (_pending) { if (!_pending.Add(path)) return; }
        try
        {
            if (!await Media.WaitUnlockedAsync(path)) return;
            var src = Messengers.Detect(path, _s.CustomFolders, "Папка");
            if (AddTrack(path, src))
            {
                Store.Save(_s);
                RefreshView();
                SetStatus($"Новый трек из {src}: {Path.GetFileNameWithoutExtension(path)}");
            }
        }
        finally { lock (_pending) _pending.Remove(path); }
    }

    async Task RescanAsync(bool silent)
    {
        if (!silent) SetStatus("Ищу музыку в папках мессенджеров…");

        var known = new HashSet<string>(_s.Tracks.Select(t => t.Path).Concat(_s.Removed), StringComparer.OrdinalIgnoreCase);
        var custom = _s.CustomFolders.ToList();
        var found = await Task.Run(() => Messengers.Scan(Messengers.All(custom), known));

        int n = 0;
        foreach (var f in found.OrderBy(f => f.Created))
            if (AddTrack(f.Path, f.Source)) n++;

        if (n > 0) { Store.Save(_s); RefreshView(); }
        if (!silent || n > 0)
            SetStatus(n > 0 ? "Найдено в мессенджерах: " + n : "Новых треков не найдено");
    }

    // Сохранить копию трека в любую папку через свой проводник
    void SaveCopy(Track t)
    {
        if (!File.Exists(t.Path)) { SetStatus("Файл не найден — копию сделать нельзя"); return; }

        using var dlg = new SaveDialog(t.Path, _bgOriginal, _s.LastSaveDir);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SavedPath != null)
        {
            _s.LastSaveDir = Path.GetDirectoryName(dlg.SavedPath);
            Store.Save(_s);
            SetStatus("Копия сохранена: " + dlg.SavedPath);
        }
    }

    private void InitializeComponent()
    {
        System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
        SuspendLayout();
        // 
        // MainForm
        // 
        ClientSize = new Size(284, 261);
        Icon = (Icon)resources.GetObject("$this.Icon");
        Name = "MainForm";
        ResumeLayout(false);

    }

    void RemoveTrack(Track t)
    {
        if (ReferenceEquals(t, _current)) StopPlayback();
        _s.Tracks.Remove(t);
        if (!_s.Removed.Any(x => string.Equals(x, t.Path, StringComparison.OrdinalIgnoreCase))) _s.Removed.Add(t.Path);
        Store.Save(_s);
        RefreshView();
        SetStatus("Убрано из плейлиста (файл на диске не тронут)");
    }

    // ───────────────────────── Воспроизведение ─────────────────────────

    // Старые m4a/mp4, которые Windows не читает: превращаем в mp3 и играем
    bool _repairing;

    async void RepairTrack(Track t)
    {
        if (_repairing) return;
        _repairing = true;
        try
        {
            SetStatus("Файл не читается — конвертирую в mp3…");
            var mp3 = await YtService.ConvertToMp3Async(t.Path, SetStatus, CancellationToken.None);
            t.Path = mp3;
            Store.Save(_s);
            RefreshView();
            SetStatus("Конвертировано в mp3: " + t.Title);
            PlayTrack(t, autoPlay: true);
        }
        catch (Exception ex)
        {
            SetStatus("Не удалось конвертировать в mp3: " + ex.Message);
        }
        finally { _repairing = false; }
    }

    void PlayTrack(Track t, bool autoPlay)
    {
        if (!File.Exists(t.Path))
        {
            SetStatus("Не удалось открыть: файл не найден");
            _s.Tracks.Remove(t);
            Store.Save(_s);
            RefreshView();
            return;
        }

        try
        {
            _audio.Load(t.Path);
            if (autoPlay) _audio.Play();
        }
        catch (Exception ex)
        {
            if (Mp3Converter.NeedsConversion(t.Path)) { RepairTrack(t); return; }
            SetStatus("Не удалось открыть: " + ex.Message);
            return;
        }

        _current = t;
        _list.Playing = t;
        _lblTitle.Text = t.Title;
        _lblSub.Text = t.Source;
        _lblTotal.Text = Fmt(_audio.Duration);
        _s.LastTrack = t.Path;
        Store.Save(_s);
        Tick();
    }

    void StopPlayback()
    {
        _audio.Stop();
        _current = null;
        _list.Playing = null;
        _lblTitle.Text = "Выберите трек";
        _lblSub.Text = "Двойной клик по треку — играть";
        _lblCur.Text = "0:00";
        _lblTotal.Text = "0:00";
        _seek.Value = 0;
        UpdatePlayIcon();
    }

    void TogglePlay()
    {
        if (!_audio.HasTrack)
        {
            var start = _list.Selected ?? _shown.FirstOrDefault() ?? _s.Tracks.FirstOrDefault();
            if (start != null) PlayTrack(start, autoPlay: true);
            return;
        }
        if (_audio.IsPlaying) _audio.Pause(); else _audio.Play();
        UpdatePlayIcon();
    }

    void Advance(bool auto, bool back = false)
    {
        var list = _shown.Count > 0 ? _shown : _s.Tracks;
        if (list.Count == 0) return;

        if (back && _audio.HasTrack && _audio.Position.TotalSeconds > 3)
        {
            _audio.Position = TimeSpan.Zero;
            return;
        }
        if (auto && _s.Repeat == 2 && _current != null)
        {
            PlayTrack(_current, autoPlay: true);
            return;
        }

        int idx = _current == null ? -1 : list.IndexOf(_current);
        int next;
        if (back) next = idx <= 0 ? list.Count - 1 : idx - 1;
        else if (_s.Shuffle && list.Count > 1)
        {
            do next = _rng.Next(list.Count); while (next == idx);
        }
        else
        {
            next = idx + 1;
            if (next >= list.Count)
            {
                if (auto && _s.Repeat != 1) { _audio.Stop(); UpdatePlayIcon(); return; }
                next = 0;
            }
        }
        PlayTrack(list[next], autoPlay: true);
    }

    void SetVolume(double v)
    {
        v = Math.Clamp(v, 0, 1);
        _s.Volume = v;
        _audio.Volume = (float)v;
        UpdateVolIcon();
    }

    void UpdateVolIcon() => _btnVol.Glyph = _s.Volume <= 0.001 ? "" : "";

    void UpdatePlayIcon()
    {
        var g = _audio.IsPlaying ? "" : "";
        if (_btnPlay.Glyph != g) _btnPlay.Glyph = g;
        _eq.Active = _audio.IsPlaying && Orbiter.Enabled;
    }

    void Tick()
    {
        UpdatePlayIcon();
        if (!_audio.HasTrack || _seek.Dragging) return;
        var dur = _audio.Duration;
        var pos = _audio.Position;
        _seek.Value = dur.TotalSeconds > 0 ? pos.TotalSeconds / dur.TotalSeconds : 0;
        _lblCur.Text = Fmt(pos);
    }

    static string Fmt(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    // ───────────────────────── YouTube ─────────────────────────

    async void StartMusicSearch()
    {
        string query = _searchBox.Text.Trim();
        if (query.Length == 0) return;

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        SetStatus("Связь с YouTube…");
        try
        {
            var results = await YtService.SearchAsync(query, 20, SetStatus, token);
            if (token.IsCancellationRequested) return;

            _ytList.SetTracks(results);
            _lblCount.Text = $"{results.Count} {Plural(results.Count)}";
            SetStatus(results.Count == 0
                ? "Ничего не найдено."
                : "Двойной клик по треку — скачать и добавить в плейлист");

            foreach (var r in results) _ = LoadThumbAsync(r, token);
        }
        catch (OperationCanceledException) { SetStatus("Поиск отменён."); }
        catch (Exception ex)
        {
            SetStatus("Ошибка поиска.");
            MessageBox.Show(this, "Не удалось выполнить поиск: " + ex.Message, "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    async Task LoadThumbAsync(YtResult r, CancellationToken token)
    {
        var bmp = await YtService.LoadThumbAsync(r.Id);
        if (bmp == null) return;
        if (token.IsCancellationRequested || IsDisposed) { bmp.Dispose(); return; }
        _ytList.UpdateThumb(r.Id, bmp);   // продолжение выполняется в UI-потоке
    }

    async void OnYtTrackActivated(YtResult r)
    {
        // уже скачан раньше — просто играем
        var existing = _s.Tracks.FirstOrDefault(t => t.YtId == r.Id && File.Exists(t.Path) && !Mp3Converter.NeedsConversion(t.Path));
        if (existing != null)
        {
            SetStatus("Этот трек уже в плейлисте.");
            PlayTrack(existing, autoPlay: true);
            return;
        }
        if (_downloading)
        {
            SetStatus("Дождитесь окончания текущей загрузки (или отмените её).");
            return;
        }

        _downloading = true;
        _btnCancel.Visible = true;
        LayoutList();
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;

        try
        {
            SetStatus("Подготовка к загрузке…");
            string path = await YtService.DownloadAsync(r, SetStatus, ct);
            if (!File.Exists(path))
                throw new FileNotFoundException("Файл не найден после загрузки: " + path);

            // убираем «битые» записи (например, исходный .mp4, который заменён на .mp3)
            _s.Tracks.RemoveAll(t => !File.Exists(t.Path));
            // старые нечитаемые версии этого же ролика (m4a/mp4) заменяем новым mp3
            _s.Tracks.RemoveAll(t => t.YtId == r.Id && !string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));

            var track = _s.Tracks.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
            if (track == null)
            {
                AddTrack(path, "YouTube", r.Id);
                track = _s.Tracks[0];
            }
            else track.YtId = r.Id;

            Store.Save(_s);
            RefreshView();
            SetStatus("Добавлено в плейлист: " + track.Title);
            PlayTrack(track, autoPlay: true);
        }
        catch (OperationCanceledException) { SetStatus("Загрузка отменена."); }
        catch (Exception ex)
        {
            SetStatus("Ошибка загрузки.");
            MessageBox.Show(this, "Не удалось скачать трек: " + ex.Message, "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _downloading = false;
            _btnCancel.Visible = false;
            LayoutList();
        }
    }

    // ───────────────────────── Прочее ─────────────────────────

    void OnThemeChanged()
    {
        _s.Accent = Theme.Accent.ToArgb();
        Store.Save(_s);
        _lblSub.ForeColor = Theme.Accent;
        Invalidate(true);
        foreach (var sw in _swatches) sw.Invalidate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.F:
                if (_view == View.YouTube) { _favOnly = false; _filterSource = null; SetView(View.All); }
                _searchBox.Focus();
                _searchBox.SelectAll();
                return true;
            case Keys.Control | Keys.O:
                AddFilesDialog();
                return true;
            case Keys.Control | Keys.N:
                ToggleNeon();
                return true;
            case Keys.Control | Keys.V:
                if (_searchBox.Focused && Clipboard.ContainsText()) return base.ProcessCmdKey(ref msg, keyData);
                PasteFromClipboard();
                return true;
            case Keys.Control | Keys.Left:
                Advance(auto: false, back: true);
                return true;
            case Keys.Control | Keys.Right:
                Advance(auto: false);
                return true;
            case Keys.Space:
                if (_searchBox.Focused) break;
                TogglePlay();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Stop();
        _searchCts?.Cancel();
        _downloadCts?.Cancel();
        StopWatchers();
        Theme.Changed -= OnThemeChanged;
        Store.Save(_s);
        _audio.Dispose();
        _bgOriginal?.Dispose();
        _bgCache?.Dispose();
        _playerBg?.Dispose();
        base.OnFormClosing(e);
    }
}
