using System.Text.Json;
using System.Text.Json.Serialization;

namespace MessengerPlayer;

public sealed class Track
{
    public string Path { get; set; } = "";
    public string Source { get; set; } = "";
    public bool Favorite { get; set; }
    public string? YtId { get; set; }

    [JsonIgnore]
    public string Title => System.IO.Path.GetFileNameWithoutExtension(Path);
}

public sealed class AppSettings
{
    public List<Track> Tracks { get; set; } = new();
    public List<string> CustomFolders { get; set; } = new();
    public string? Background { get; set; }
    public int Accent { get; set; } = Color.FromArgb(0, 255, 64).ToArgb();
    public double Volume { get; set; } = 0.8;
    public bool Shuffle { get; set; }
    public int Repeat { get; set; }
    public bool AutoAdd { get; set; } = true;
    public bool NeonAnim { get; set; } = true;
    public string? LastTrack { get; set; }
    public string? LastSaveDir { get; set; }
    public List<string> Removed { get; set; } = new();
}

public static class Store
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MessengerPlayer");

    static readonly string FilePath = Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s != null)
                {
                    s.Tracks.RemoveAll(t => !File.Exists(t.Path));
                    return s;
                }
            }
        }
        catch { /* повреждённый файл настроек — начинаем с чистого */ }
        return new AppSettings();
    }

    public static void Save(AppSettings s)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s, Options));
        }
        catch { }
    }
}

public static class Media
{
    static readonly HashSet<string> AudioExt = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".m4a", ".aac", ".wma", ".flac", ".aif", ".aiff", ".mp4" };

    static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".gif" };

    public static bool IsAudio(string path) => AudioExt.Contains(Path.GetExtension(path));
    public static bool IsImage(string path) => ImageExt.Contains(Path.GetExtension(path));
    public static async Task<bool> WaitUnlockedAsync(string path)
    {
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(500);
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return fs.Length > 0;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            catch (IOException) { /* ещё качается */ }
            catch { return false; }
        }
        return false;
    }
}

public sealed record Source(string Name, string Folder, bool Recursive);
public sealed record Found(string Path, string Source, DateTime Created);

public static class Messengers
{
    public static List<Source> All(IEnumerable<string> custom)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var downloads = Path.Combine(home, "Downloads");

        var list = new List<Source>
        {
            new("Telegram", Path.Combine(downloads, "Telegram Desktop"), true),
            new("Viber", Path.Combine(docs, "ViberDownloads"), true),
            new("WhatsApp", Path.Combine(docs, "WhatsApp"), true),
            new("Загрузки", downloads, false),
        };

        foreach (var c in custom)
        {
            var name = Path.GetFileName(c.TrimEnd('\\', '/'));
            list.Add(new Source(string.IsNullOrEmpty(name) ? c : name, c, true));
        }

        return list.Where(s => Directory.Exists(s.Folder)).ToList();
    }

    public static List<Found> Scan(IEnumerable<Source> sources, HashSet<string> known)
    {
        var result = new List<Found>();
        var seen = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);

        foreach (var s in sources)
        {
            var opt = new EnumerationOptions
            {
                RecurseSubdirectories = s.Recursive,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 8
            };
            try
            {
                foreach (var f in Directory.EnumerateFiles(s.Folder, "*", opt))
                {
                    if (!Media.IsAudio(f) || !seen.Add(f)) continue;
                    result.Add(new Found(f, s.Name, File.GetCreationTime(f)));
                }
            }
            catch { }
        }
        return result;
    }
    public static string Detect(string path, IEnumerable<string> custom, string fallback)
    {
        if (path.Contains("\\tdata\\", StringComparison.OrdinalIgnoreCase)) return "Telegram";

        var dir = Path.GetDirectoryName(path) ?? "";
        var src = All(custom).FirstOrDefault(s =>
            path.StartsWith(s.Folder, StringComparison.OrdinalIgnoreCase) &&
            (s.Recursive || string.Equals(dir, s.Folder, StringComparison.OrdinalIgnoreCase)));

        return src?.Name ?? fallback;
    }

    static bool IsTemporary(string p) =>
        p.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) ||
        p.Contains("\\tdata\\", StringComparison.OrdinalIgnoreCase);
    public static string Import(string path)
    {
        if (!IsTemporary(path)) return path;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Messenger Player");
            Directory.CreateDirectory(dir);

            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            var dest = Path.Combine(dir, name + ext);
            for (int i = 1; File.Exists(dest); i++)
                dest = Path.Combine(dir, $"{name} ({i}){ext}");

            File.Copy(path, dest);
            return dest;
        }
        catch { return path; }
    }
}
