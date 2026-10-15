using NAudio.MediaFoundation;
using NAudio.Wave;

namespace MessengerPlayer;
public static class Mp3Converter
{
    public static bool NeedsConversion(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase);
    }
    public static Task<string> ToMp3Async(string sourcePath, CancellationToken ct) =>
        Task.Run(() => ToMp3(sourcePath, ct), ct);

    static string ToMp3(string sourcePath, CancellationToken ct)
    {
        MediaFoundationApi.Startup();

        var target = Path.ChangeExtension(sourcePath, ".mp3");
        var tmp = Path.Combine(Path.GetTempPath(), "mp_" + Guid.NewGuid().ToString("N") + ".mp3");

        try
        {
            using (var reader = new MediaFoundationReader(sourcePath))
            {
                ct.ThrowIfCancellationRequested();
                MediaFoundationEncoder.EncodeToMp3(reader, tmp, 192000);
            }

            ct.ThrowIfCancellationRequested();
            File.Move(tmp, target, true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw;
        }

        if (!File.Exists(target)) return sourcePath;

        try { File.Delete(sourcePath); } catch { }
        return target;
    }
}
