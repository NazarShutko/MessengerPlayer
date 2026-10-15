using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MessengerPlayer;

public sealed class YtResult
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Channel { get; init; } = "";
    public string Duration { get; init; } = "";
    public Bitmap? Thumb { get; set; }
    public string Url => $"https://www.youtube.com/watch?v={Id}";
}
public static class YtService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    static string ToolsDir => Path.Combine(Store.Dir, "tools");

    public static string OutDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Messenger Player");

    static string? FindExe(string name)
    {
        foreach (var dir in new[] { AppContext.BaseDirectory, ToolsDir })
        {
            var p = Path.Combine(dir, name);
            if (File.Exists(p)) return p;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(dir.Trim('"'), name);
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }

    public static async Task<string> EnsureYtDlpAsync(Action<string>? report = null)
    {
        var exe = FindExe("yt-dlp.exe");
        if (exe != null) return exe;

        report?.Invoke("Скачиваю yt-dlp (нужно один раз)…");
        Directory.CreateDirectory(ToolsDir);
        var dest = Path.Combine(ToolsDir, "yt-dlp.exe");
        var tmp = dest + ".part";

        using (var resp = await Http.GetAsync(
                   "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe",
                   HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(tmp);
            await resp.Content.CopyToAsync(fs);
        }
        File.Move(tmp, dest, true);
        return dest;
    }

    static bool _ffmpegFailed;

    /// <summary>
    /// ffmpeg нужен, чтобы получить настоящий mp3 (YouTube отдаёт m4a/webm, Windows их не всегда читает).
    /// Скачивается один раз в папку данных приложения. Если не вышло — вернёт null.
    /// </summary>
    public static async Task<string?> EnsureFfmpegAsync(Action<string>? report, CancellationToken ct)
    {
        var exe = FindExe("ffmpeg.exe");
        if (exe != null) return exe;
        if (_ffmpegFailed) return null;

        var zip = Path.Combine(ToolsDir, "ffmpeg.zip.part");
        try
        {
            Directory.CreateDirectory(ToolsDir);
            report?.Invoke("Скачиваю ffmpeg (нужно один раз)…");

            using (var resp = await Http.GetAsync(
                       "https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip",
                       HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? 0;
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(zip);
                var buf = new byte[81920];
                long done = 0; int lastPct = -1, n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (total > 0)
                    {
                        int pct = (int)(done * 100 / total);
                        if (pct != lastPct) { lastPct = pct; report?.Invoke($"Скачиваю ffmpeg (один раз)… {pct}%"); }
                    }
                }
            }

            var dest = Path.Combine(ToolsDir, "ffmpeg.exe");
            using (var za = System.IO.Compression.ZipFile.OpenRead(zip))
            {
                var entry = za.Entries.FirstOrDefault(e =>
                                e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                            ?? throw new FileNotFoundException("ffmpeg.exe не найден в архиве");
                System.IO.Compression.ZipFileExtensions.ExtractToFile(entry, dest, true);
            }
            return dest;
        }
        catch (OperationCanceledException) { throw; }
        catch { _ffmpegFailed = true; return null; }
        finally { try { if (File.Exists(zip)) File.Delete(zip); } catch { } }
    }

    /// <summary>
    /// Конвертирует любой аудиофайл (m4a/mp4/webm…) в mp3 через ffmpeg.
    /// Результат создаётся рядом с исходным, исходник после успеха удаляется.
    /// </summary>
    public static async Task<string> ConvertToMp3Async(string src, Action<string>? report, CancellationToken ct)
    {
        var ffmpeg = await EnsureFfmpegAsync(report, ct)
                     ?? throw new InvalidOperationException("не удалось получить ffmpeg (положите ffmpeg.exe рядом с программой)");

        report?.Invoke("Конвертация в mp3…");

        var dir = Path.GetDirectoryName(src)!;
        var name = Path.GetFileNameWithoutExtension(src);
        var target = Path.Combine(dir, name + ".mp3");
        for (int i = 1; File.Exists(target); i++)
            target = Path.Combine(dir, $"{name} ({i}).mp3");

        // пишем во временную папку, чтобы наблюдатели папок не увидели недописанный файл
        var tmp = Path.Combine(Path.GetTempPath(), "mp_" + Guid.NewGuid().ToString("N") + ".mp3");
        try
        {
            using var p = StartProcess(ffmpeg, new[]
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-i", src, "-vn", "-c:a", "libmp3lame", "-q:a", "0", tmp
            });
            var errTask = p.StandardError.ReadToEndAsync(ct);
            var outTask = p.StandardOutput.ReadToEndAsync(ct);
            try { await p.WaitForExitAsync(ct); }
            catch { try { if (!p.HasExited) p.Kill(true); } catch { } throw; }
            await outTask;
            var err = await errTask;

            if (p.ExitCode != 0 || !File.Exists(tmp) || new FileInfo(tmp).Length == 0)
                throw new InvalidOperationException(LastLine(err));

            File.Move(tmp, target);
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }

        try { File.Delete(src); } catch { }
        return target;
    }

    static Process StartProcess(string exe, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        return Process.Start(psi)!;
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static string LastLine(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length > 0 ? lines[^1] : "неизвестная ошибка";
    }

    // youtu.be/ID, youtube.com/watch?v=ID, /shorts/ID, /live/ID, /embed/ID, music.youtube.com/watch?v=ID
    static readonly Regex VideoLink = new(
        @"^(?:https?://)?(?:www\.|m\.|music\.)?(?:youtu\.be/|youtube\.com/(?:watch\?(?:[^#\s]*&)?v=|shorts/|live/|embed/))([\w-]{11})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<List<YtResult>> SearchAsync(string query, int count, Action<string>? report, CancellationToken ct)
    {
        var exe = await EnsureYtDlpAsync(report);
        report?.Invoke("Ищу…");

        // если вставлена ссылка на конкретное видео — ищем именно его, а не по тексту
        var link = VideoLink.Match(query.Trim());
        string target = link.Success
            ? $"https://www.youtube.com/watch?v={link.Groups[1].Value}"
            : $"ytsearch{count}:{query}";

        var searchArgs = new List<string> { "--flat-playlist", "--dump-json", "--no-warnings" };
        if (link.Success) searchArgs.Add("--no-playlist");   // ссылка вида watch?v=…&list=… — берём только это видео
        searchArgs.Add(target);

        using var p = StartProcess(exe, searchArgs);
        var errTask = p.StandardError.ReadToEndAsync(ct);
        var list = new List<YtResult>();

        try
        {
            string? line;
            while ((line = await p.StandardOutput.ReadLineAsync(ct)) != null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var r = doc.RootElement;
                    var id = Str(r, "id");
                    if (id.Length == 0) continue;

                    var channel = Str(r, "channel");
                    if (channel.Length == 0) channel = Str(r, "uploader");

                    string duration = "";
                    if (r.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number)
                    {
                        var t = TimeSpan.FromSeconds(d.GetDouble());
                        duration = t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                                                     : $"{t.Minutes}:{t.Seconds:00}";
                    }

                    list.Add(new YtResult { Id = id, Title = Str(r, "title"), Channel = channel, Duration = duration });
                }
                catch (JsonException) { }
            }
            await p.WaitForExitAsync(ct);
        }
        finally
        {
            try { if (!p.HasExited) p.Kill(true); } catch { }
        }

        if (list.Count == 0 && p.ExitCode != 0)
            throw new InvalidOperationException(LastLine(await errTask));
        return list;
    }

    public static async Task<Bitmap?> LoadThumbAsync(string id)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync($"https://i.ytimg.com/vi/{id}/mqdefault.jpg");
            using var ms = new MemoryStream(bytes);
            using var img = Image.FromStream(ms);
            return new Bitmap(img);
        }
        catch { return null; }
    }

    static bool _updated;

    static async Task UpdateYtDlpAsync(string exe, CancellationToken ct)
    {
        _updated = true;
        try
        {
            using var p = StartProcess(exe, new[] { "-U" });
            _ = p.StandardError.ReadToEndAsync(ct);
            await p.StandardOutput.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch { /* нет прав на запись/сети — просто пробуем с тем, что есть */ }
    }

    public static async Task<string> DownloadAsync(YtResult r, Action<string> progress, CancellationToken ct)
    {
        try { return await DownloadOnceAsync(r, progress, ct); }
        catch (InvalidOperationException) when (!_updated)
        {
            progress("Обновляю yt-dlp и повторяю…");
            await UpdateYtDlpAsync(await EnsureYtDlpAsync(progress), ct);
            return await DownloadOnceAsync(r, progress, ct);
        }
    }

    static string LogPath => Path.Combine(Store.Dir, "ytdlp.log");

    static async Task<string> DownloadOnceAsync(YtResult r, Action<string> progress, CancellationToken ct)
    {
        var exe = await EnsureYtDlpAsync(progress);
        Directory.CreateDirectory(OutDir);
        var ffmpeg = await EnsureFfmpegAsync(progress, ct);
        var started = DateTime.Now.AddSeconds(-2);

        var args = new List<string>
        {
            "--encoding", "utf-8",
            "--no-playlist", "--no-warnings", "--no-quiet", "--windows-filenames", "--newline", "--progress",
            "--print", "after_move:filepath",
            "-o", Path.Combine(OutDir, "%(title).120s.%(ext)s")
        };
        if (ffmpeg != null)
        {
            args.AddRange(new[] {
        "-x",
        "--audio-format", "mp3",
        "--audio-quality", "0",           // максимальное качество (V0)
        "--concurrent-fragments", "4",
        "--ffmpeg-location", ffmpeg
        });
        }
        else
        {
            // без ffmpeg берём m4a/mp4 — их умеет читать Windows, дальше конвертируем в mp3 сами
            args.AddRange(new[] { "-f", "bestaudio[ext=m4a]/bestaudio[ext=mp4]/bestaudio/18/best" });
        }

        args.Add(r.Url);   // ссылка на видео — без неё yt-dlp сразу выдаёт справку по параметрам

        using var p = StartProcess(exe, args);
        var log = new StringBuilder();
        log.AppendLine("> " + exe + " " + string.Join(' ', args.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a)));
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (log) log.AppendLine(e.Data); };
        p.BeginErrorReadLine();

        string? result = null;
        try
        {
            progress("Загрузка…");
            string? line;
            while ((line = await p.StandardOutput.ReadLineAsync(ct)) != null)
            {
                line = line.Trim();
                lock (log) log.AppendLine(line);
                if (line.StartsWith("[download]"))
                {
                    var m = Regex.Match(line, @"(\d+(?:\.\d+)?)%");
                    if (m.Success) progress($"Загрузка… {m.Groups[1].Value}%");
                }
                else if (line.StartsWith("[ExtractAudio]") || line.StartsWith("[Merger]"))
                    progress("Конвертация…");
                else if (line.Length > 0 && line[0] != '[' && File.Exists(line))
                    result = line;
            }
            await p.WaitForExitAsync(ct);
        }
        finally
        {
            try { if (!p.HasExited) p.Kill(true); } catch { }
        }

        string text; lock (log) text = log.ToString();
        try { Directory.CreateDirectory(Store.Dir); File.WriteAllText(LogPath, text); } catch { }

        if (result == null)
        {
            result = new DirectoryInfo(OutDir).EnumerateFiles()
                .Where(f => f.LastWriteTime >= started && Media.IsAudio(f.FullName) && !f.Extension.EndsWith(".part"))
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => f.FullName)
                .FirstOrDefault();
        }

        if (result == null)
        {
            result = new DirectoryInfo(OutDir).EnumerateFiles()
                .Where(f => f.LastWriteTime >= started
                         && Media.IsAudio(f.FullName)
                         && !f.Extension.Equals(".part", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => f.FullName)
                .FirstOrDefault();
        }

        if (result == null)
        {
            // файл не получился — показываем настоящую причину из yt-dlp
            // (InvalidOperationException ещё и запустит обновление yt-dlp с повтором)
            var err = text.Split('\n').Select(l => l.Trim())
                          .LastOrDefault(l => l.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
                                     || l.Contains("error:", StringComparison.OrdinalIgnoreCase))
                      ?? LastLine(text);
            throw new InvalidOperationException(err + "  (подробности: " + LogPath + ")");
        }

        if (result != null && Mp3Converter.NeedsConversion(result))
        {
            progress("Конвертация в mp3…");
            try { result = await Mp3Converter.ToMp3Async(result, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // не вышло — оставляем исходный файл, он всё равно проигрывается
                progress("Не удалось конвертировать в mp3: " + ex.Message);
            }
        }

        return result;
    }
}
