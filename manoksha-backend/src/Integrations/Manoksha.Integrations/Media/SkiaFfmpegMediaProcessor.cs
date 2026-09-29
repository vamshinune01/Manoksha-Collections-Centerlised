using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Manoksha.Application.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace Manoksha.Integrations.Media;

public sealed class MediaProcessingOptions
{
    public string FfmpegPath { get; set; } = "ffmpeg";

    public string FfprobePath { get; set; } = "ffprobe";

    /// <summary>WebP quality for image renditions and posters (0–100).</summary>
    public int WebpQuality { get; set; } = 80;

    /// <summary>Refuse images above this many pixels (decompression-bomb guard).</summary>
    public long MaxImagePixels { get; set; } = 60_000_000;
}

/// <summary>
/// Web/mobile optimization: images → WebP renditions (never upscaled, EXIF orientation applied); video → H.264/AAC MP4 with fast
/// start, bounded height and bitrate, plus a WebP poster. Originals are never served to customers.
/// </summary>
public sealed class SkiaFfmpegMediaProcessor(IOptions<MediaProcessingOptions> options) : IMediaProcessor
{
    private readonly MediaProcessingOptions _o = options.Value;

    public ImageRenditionSet ProcessImage(Stream original, IReadOnlyList<ImageSize> sizes)
    {
        using var data = SKData.Create(original);
        using var codec = data is null ? null : SKCodec.Create(data);
        if (codec is null)
        {
            throw new MediaProcessingException("The file is not a supported image (use JPEG, PNG or WebP).");
        }
        if ((long)codec.Info.Width * codec.Info.Height > _o.MaxImagePixels)
        {
            throw new MediaProcessingException("The image is too large (maximum 60 megapixels).");
        }
        using var decoded = SKBitmap.Decode(codec) ?? throw new MediaProcessingException("The image could not be decoded.");
        using var oriented = Orient(decoded, codec.EncodedOrigin);

        var renditions = new List<ImageRendition>();
        foreach (var size in sizes)
        {
            var width = Math.Min(size.MaxWidth, oriented.Width);
            var height = Math.Max(1, (int)Math.Round(oriented.Height * (width / (double)oriented.Width)));
            using var resized = width == oriented.Width ? oriented.Copy() : oriented.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
            using var image = SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, _o.WebpQuality);
            renditions.Add(new ImageRendition(size.Name, width, height, encoded.ToArray()));
        }
        return new ImageRenditionSet(oriented.Width, oriented.Height, renditions);
    }

    public async Task<VideoRendition> ProcessVideoAsync(string originalPath, string workDirectory, int maxHeight, TimeSpan maxDuration, CancellationToken cancellationToken)
    {
        var probe = await RunAsync(_o.FfprobePath,
            ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height:format=duration", "-of", "json", originalPath], cancellationToken);
        double duration;
        try
        {
            using var doc = JsonDocument.Parse(probe);
            var stream = doc.RootElement.GetProperty("streams")[0];
            _ = stream.GetProperty("width").GetInt32();
            duration = double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException or FormatException or ArgumentNullException)
        {
            throw new MediaProcessingException("The file is not a supported video (use MP4, MOV or WebM).", ex);
        }
        if (duration > maxDuration.TotalSeconds)
        {
            throw new MediaProcessingException($"The video is too long (maximum {maxDuration.TotalSeconds:0} seconds).");
        }

        var mp4 = Path.Combine(workDirectory, "video.mp4");
        await RunAsync(_o.FfmpegPath,
        [
            "-y", "-v", "error", "-i", originalPath,
            "-vf", $"scale=-2:'min({maxHeight},ih)'", "-c:v", "libx264", "-preset", "veryfast", "-crf", "26", "-maxrate", "2500k", "-bufsize", "5000k",
            "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "96k", "-ac", "2", "-movflags", "+faststart", mp4,
        ], cancellationToken);
        var poster = Path.Combine(workDirectory, "poster.png");
        await RunAsync(_o.FfmpegPath,
            ["-y", "-v", "error", "-ss", Math.Min(1.0, duration / 2).ToString("0.###", CultureInfo.InvariantCulture), "-i", mp4, "-frames:v", "1", poster], cancellationToken);

        var outProbe = await RunAsync(_o.FfprobePath, ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "json", mp4], cancellationToken);
        using var outDoc = JsonDocument.Parse(outProbe);
        var outStream = outDoc.RootElement.GetProperty("streams")[0];
        await using var posterStream = File.OpenRead(poster);
        var posterSet = ProcessImage(posterStream, [new ImageSize("poster", 1280)]);
        var p = posterSet.Renditions[0];
        return new VideoRendition(mp4, outStream.GetProperty("width").GetInt32(), outStream.GetProperty("height").GetInt32(), Math.Round(duration, 2), p.Webp, p.Width, p.Height);
    }

    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
        {
            return source.Copy();
        }
        var swap = origin is SKEncodedOrigin.RightTop or SKEncodedOrigin.LeftBottom or SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightBottom;
        var result = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height);
        using var canvas = new SKCanvas(result);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Scale(-1, 1, result.Width / 2f, 0);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.RotateDegrees(180, result.Width / 2f, result.Height / 2f);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Scale(1, -1, 0, result.Height / 2f);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(result.Width, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, result.Height);
                canvas.RotateDegrees(270);
                break;
            case SKEncodedOrigin.LeftTop: // transpose: (x, y) → (y, x)
                canvas.SetMatrix(new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1));
                break;
            case SKEncodedOrigin.RightBottom: // transverse: (x, y) → (W − y, H − x)
                canvas.SetMatrix(new SKMatrix(0, -1, result.Width, -1, 0, result.Height, 0, 0, 1));
                break;
        }
        canvas.DrawBitmap(source, 0, 0);
        return result;
    }

    private static async Task<string> RunAsync(string file, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        using var process = Process.Start(psi) ?? throw new MediaProcessingException("Video processing is not available.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        if (process.ExitCode != 0)
        {
            throw new MediaProcessingException($"The video could not be processed ({(await stderr).Split('\n').FirstOrDefault()?.Trim()}).");
        }
        return await stdout;
    }
}
