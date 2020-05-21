using System;
using System.Drawing;
using CompatApiClient.Utils;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;

namespace CompatBot.Utils.Extensions
{
	internal static class Converters
	{
		private const double tolerance = 0.000000000000001;

		public static Color ToStandardColor(this ColorThiefDotNet.Color c)
			=> Color.FromArgb(c.A, c.R, c.G, c.B);

		public static Rectangle ToRectangle(this BoundingRect rect)
			=> Rectangle.FromLTRB(rect.X, rect.Y, rect.X + rect.W, rect.Y + rect.H);

		public static Color GetComplementary(this Color src, bool preserveOpacity = false)
		{
			var c = src;
			//if RGB values are close to each other by a diff less than 10%, then if RGB values are lighter side, decrease the blue by 50% (eventually it will increase in conversion below), if RBB values are on darker side, decrease yellow by about 50% (it will increase in conversion)
			var avgColorValue = (src.R + src.G + src.B) / 3.0;
			var dR = Math.Abs(src.R - avgColorValue);
			var dG = Math.Abs(src.G - avgColorValue);
			var dB = Math.Abs(src.B - avgColorValue);
			if (dR < 20 && dG < 20 && dB < 20) //The color is a shade of gray
			{
				if (avgColorValue < 123) //color is dark
					c = Color.FromArgb(src.A, 220, 230, 50);
				else
					c = Color.FromArgb(src.A, 255, 255, 50);
			}
			var a = src.A;
			if (!preserveOpacity)
				a = Math.Max(src.A, (byte)127); //We don't want contrast color to be more than 50% transparent ever.
			var (h, s, b) = (c.GetHue(), c.GetSaturation(), c.GetBrightness());
			h = h < 180 ? h + 180 : h - 180;
			return FromHsb(h, s, b, a);
		}

		public static Color FromHsb(double h, double s, double b, int a = 255)
		{
			h = h.Clamp(0, 360);
			s = s.Clamp(0, 1);
			b = b.Clamp(0, 1);
			a = a.Clamp(0, 255);

			var r = 0.0;
			var g = 0.0;
			var bl = 0.0;

			if (Math.Abs(s) < tolerance)
				r = g = bl = b;
			else
			{
				// the argb wheel consists of 6 sectors. Figure out which sector
				// you're in.
				var sectorPos = h / 60;
				var sectorNumber = (int)Math.Floor(sectorPos);
				// get the fractional part of the sector
				var fractionalSector = sectorPos - sectorNumber;

				// calculate values for the three axes of the argb.
				var p = b * (1 - s);
				var q = b * (1 - s * fractionalSector);
				var t = b * (1 - s + s * fractionalSector);

				// assign the fractional colors to r, g, and b based on the sector
				// the angle is in.
				switch (sectorNumber)
				{
					case 0:
						r = b;
						g = t;
						bl = p;
						break;
					case 1:
						r = q;
						g = b;
						bl = p;
						break;
					case 2:
						r = p;
						g = b;
						bl = t;
						break;
					case 3:
						r = p;
						g = q;
						bl = b;
						break;
					case 4:
						r = t;
						g = p;
						bl = b;
						break;
					case 5:
						r = b;
						g = p;
						bl = q;
						break;
				}
			}
			return Color.FromArgb(
				a,
				((int)(r * 255)).Clamp(0, 255),
				((int)(g * 255)).Clamp(0, 255),
				((int)(bl * 255)).Clamp(0, 255)
			);
		}
	}
}