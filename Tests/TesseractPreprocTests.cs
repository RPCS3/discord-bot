using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TesseractCSharp;

namespace Tests;

[TestFixture]
public class TesseractPreprocTests
{
    private class CustomTesseract: CompatBot.Ocr.Backend.Tesseract
    {
        public override async Task<(string result, double confidence)> GetTextAsync(string filename, int rotation, CancellationToken cancellationToken)
        {
            var imgData = await File.ReadAllBytesAsync(filename, cancellationToken);
            using var img = Pix.LoadFromMemory(imgData);
            using var img2 = img.Scale(2, 2);
            using var img3 = img2.ConvertRGBToGray();
            using var page = Engine.Process(img2);
            return (page.GetText() ?? "", page.GetMeanConfidence());
        }
    }
    
    private readonly CustomTesseract backend = new();

    [OneTimeSetUp]
    public async Task SetupUp()
    {
        await backend.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        backend.Dispose();
    }
    
    [Explicit("Requires sample files")]
    [TestCase(@"C:\Documents\Downloads\image.jpg")]
    public async Task FiltersTest(string path)
    {
        var (text, confidence) = await backend.GetTextAsync(path, 0, CancellationToken.None).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(confidence, Is.GreaterThan(0.8));
            Assert.That(text, Contains.Substring("Beast Games Strong vs Smart Is Out Now"));
            Assert.That(text, Contains.Substring("I am pleased to announce the launch of my own cryptocurrency casino"));
        }
    }
}