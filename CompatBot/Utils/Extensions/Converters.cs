using System.Drawing;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;

namespace CompatBot.Utils.Extensions
{
    internal static class Converters
	{
		public static Color ToStandardColor(this ColorThiefDotNet.Color c)
			=> Color.FromArgb(c.A, c.R, c.G, c.B);

		public static Rectangle ToRectangle(this BoundingRect rect)
			=> Rectangle.FromLTRB(rect.X, rect.Y, rect.X + rect.W, rect.Y + rect.H);
	}
}
