using System.IO;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace CompatBot.Utils;

internal static class ColorGetter
{
    public static SixLabors.ImageSharp.Color GetDominantColor(SixLabors.ImageSharp.Image<Rgba32> img)
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

    public static async ValueTask<DiscordColor?> GetDominantColorAsync(Stream jpg, DiscordColor? defaultColor)
    {
        try
        {
            var img = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(jpg).ConfigureAwait(false);
            var quantizerOptions = new QuantizerOptions { Dither = null, MaxColors = 4 };
            var sampling = new ExtensivePixelSamplingStrategy();
            var quantizer = new WuQuantizer().CreatePixelSpecificQuantizer<Rgba32>(new(), quantizerOptions);
            quantizer.BuildPalette(sampling, img);
            var paletteRgb = quantizer.Palette.ToArray().Select(c => (Rgb)c).ToArray();
            var paletteHsl = new Hsl[paletteRgb.Length];
            ColorSpaceConverter.Convert(paletteRgb, paletteHsl);
            var colors = paletteRgb
                .Zip(paletteHsl)
                .OrderBy(p => Math.Abs(0.75 - p.Second.L))
                .ThenByDescending(p => p.Second.S)
                .ToList();
#if DEBUG
            Config.Log.Trace("Selected palette:");
            foreach (var cl in colors)
                Config.Log.Trace($"{cl.First.ToString()}, HSL: {cl.Second.H+90:#00} {cl.Second.S:0.00} {cl.Second.L:0.00}");
#endif
            var c = colors[0].First;
            return new DiscordColor(c.R, c.G, c.B);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, "Failed to extract image palette");
            return defaultColor;
        }
    }
}