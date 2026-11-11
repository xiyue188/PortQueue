using Microsoft.Extensions.Configuration;
using PortQueue.Application.Mobile.Reports;
using StbImageSharp;
namespace PortQueue.Infrastructure.Mobile.Reports;
public sealed class ReportPhotoStore
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public string Root
    {
        get;
    }
    public ReportPhotoStore(IConfiguration configuration)
    {
        Root = Path.GetFullPath(configuration["Mobile:PhotoDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "mobile-private-photos"));
    }
    public string PathFor(string key)
    {
        if (key.Length != 36 || !key.EndsWith(".jpg", StringComparison.Ordinal) || !Guid.TryParseExact(key[..32], "N", out _))
            throw new InvalidOperationException("Invalid storage key.");
        return Path.Combine(Root, key);
    }
    public static async Task<byte[]> ValidateAsync(Stream input, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
                throw new MobileReportException(413, "PHOTO_TOO_LARGE", "照片不能超过5MiB。");
            await buffer.WriteAsync(chunk.AsMemory(0, read), token);
        }
        var bytes = buffer.ToArray();
        if (bytes.Length < 4 || bytes[0] != 255 || bytes[1] != 216 || bytes[^2] != 255 || bytes[^1] != 217)
            throw Invalid();
        try
        {
            using var source = new MemoryStream(bytes, false);
            var info = ImageInfo.FromStream(source);
            if (info is null || info.Value.Width <= 0 || info.Value.Height <= 0)
                throw Invalid();
            if (info.Value.Width > 1600 || info.Value.Height > 1600)
                throw new MobileReportException(400, "PHOTO_DIMENSIONS", "照片最长边不能超过1600像素。");
            token.ThrowIfCancellationRequested();
            // Header dimensions bound allocation before actual entropy decoding (at most 1600*1600*3 bytes).
            var image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlue);
            if (image.Width != info.Value.Width || image.Height != info.Value.Height || image.Data.Length != image.Width * image.Height * 3)
                throw Invalid();
        }
        catch (MobileReportException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or EndOfStreamException) { throw Invalid(); }
        return bytes;
    }
    private static MobileReportException Invalid() => new(415, "INVALID_JPEG", "照片必须是完整有效的JPEG图片。");
    public async Task SaveAsync(string key, byte[] bytes, CancellationToken token)
    {
        Directory.CreateDirectory(Root);
        var temp = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, token);
            File.Move(temp, PathFor(key), false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Delete(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path))
            File.Delete(path);
    }
}
