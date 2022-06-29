using System.IO;
using IrdLibraryClient.IrdFormat;
using NUnit.Framework;

namespace Tests;

[TestFixture, Explicit("Requires files to run")]
public class IrdTests
{
    [Test]
    public void ParsingTest()
    {
        var baseDir = TestContext.CurrentContext.TestDirectory;
        var testFiles = Directory.GetFiles(baseDir, "*.ird", SearchOption.AllDirectories);
        Assert.That(testFiles.Length, Is.GreaterThan(0));

        foreach (var file in testFiles)
        {
            var bytes = File.ReadAllBytes(file);
            Assert.That(() => IrdParser.Parse(bytes), Throws.Nothing, "Failed to parse " + Path.GetFileName(file));
        }
    }

    [Test]
    public void HeaderParsingTest()
    {
        var baseDir = TestContext.CurrentContext.TestDirectory;
        var testFiles = Directory.GetFiles(baseDir, "*.ird", SearchOption.AllDirectories);
        Assert.That(testFiles.Length, Is.GreaterThan(0));

        foreach (var file in testFiles)
        {
            var bytes = File.ReadAllBytes(file);
            var ird = IrdParser.Parse(bytes);
            Assert.That(ird.FileCount, Is.GreaterThan(0));

            var fileList = ird.GetFilenames();
            Assert.That(fileList.Count, Is.EqualTo(ird.FileCount));
        }
    }
}