using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.PixelFormats;

namespace CompatBot.Utils.Extensions
{
	internal static class Converters
	{
		private static readonly ColorSpaceConverter colorSpaceConverter = new ColorSpaceConverter();

		public static Color ToStandardColor(this ColorThiefDotNet.Color c)
			=> new Rgba32(c.R, c.G, c.B, c.A);

		public static Color GetComplementary(this Color src, bool preserveOpacity = false)
		{
			var c = src.ToPixel<Rgba32>();
			var a = c.A;
			//if RGB values are close to each other by a diff less than 10%, then if RGB values are lighter side,
			//decrease the blue by 50% (eventually it will increase in conversion below),
			//if RBB values are on darker side, decrease yellow by about 50% (it will increase in conversion)
			var avgColorValue = (c.R + c.G + c.B) / 3.0;
			var dR = Math.Abs(c.R - avgColorValue);
			var dG = Math.Abs(c.G - avgColorValue);
			var dB = Math.Abs(c.B - avgColorValue);
			if (dR < 20 && dG < 20 && dB < 20) //The color is a shade of gray
			{
				if (avgColorValue < 123) //color is dark
					c = new Rgba32(220, 230, 50, a);
				else
					c = new Rgba32(255, 255, 50, a);
			}
			if (!preserveOpacity)
				a = Math.Max(a, (byte)127); //We don't want contrast color to be more than 50% transparent ever.
			var hsb = colorSpaceConverter.ToHsv(new Rgb24(c.R, c.G, c.B));
			var h = hsb.H;
			h = h < 180 ? h + 180 : h - 180;
			var r = colorSpaceConverter.ToRgb(new Hsv(h, hsb.S, hsb.V));
			return new Rgba32(r.R, r.G, r.B, a);
		}
	}
}