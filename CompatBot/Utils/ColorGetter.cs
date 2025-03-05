using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using ColorThiefDotNet;
using SixLabors.ImageSharp.PixelFormats;

namespace CompatBot.Utils;

internal static class ColorGetter
{
    public static SixLabors.ImageSharp.Color GetDominentColor(SixLabors.ImageSharp.Image<Rgba32> img)
    {
        //img.Mutate(x => x.Resize(new ResizeOptions { Sampler = KnownResamplers.NearestNeighbor, Size = new Size(100, 0) }));
        int r = 0;
        int g = 0;
        int b = 0;
        int totalPixels = 0;

        for (int x = 0; x < img.Width; x++)
        {
            for (int y = 0; y < img.Height; y++)
            {
                var pixel = img[x, y];

                r += Convert.ToInt32(pixel.R);
                g += Convert.ToInt32(pixel.G);
                b += Convert.ToInt32(pixel.B);

                totalPixels++;
            }
        }

        r /= totalPixels;
        g /= totalPixels;
        b /= totalPixels;

        Rgba32 dominantColor = new Rgba32((byte)r, (byte)g, (byte)b, 255);
        return dominantColor;
    }

    public static DiscordColor? Analyze(byte[] jpg, DiscordColor? defaultColor)
    {
        try
        {
            // when running dotnet from the snap, it will segfault on attempt to create a Bitmap
            if (SandboxDetector.Detect() == SandboxType.Snap)
                return defaultColor;

            // TODO .net6 breaks this for linux
            if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
                return defaultColor;

            var analyzer = new ColorThief();
            using var stream = new MemoryStream(jpg);
            var bmp = new Bitmap(stream, false);
            var palette = analyzer.GetPalette(bmp, 4, ignoreWhite: false);
            var colors = palette
                .Select(p => new {c = p.Color, hsl = p.Color.ToHsl()})
                .OrderBy(p => Math.Abs(0.75 - p.hsl.L))
                .ThenByDescending(p => p.hsl.S)
                .ToList();
#if DEBUG
            Config.Log.Trace("Selected palette:");
            foreach (var cl in colors)
                Config.Log.Trace($"{cl.c.ToHexString()}, HSL: {cl.hsl.H+90:#00} {cl.hsl.S:0.00} {cl.hsl.L:0.00}");
#endif
            var c = colors[0].c;
            return new DiscordColor(c.R, c.G, c.B);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, "Failed to extract image palette");
            return defaultColor;
        }
    }
}