using System.Text.RegularExpressions;
using NUnit.Framework;
using CompatBot.Utils;

namespace Tests;

[TestFixture]
public class RegexTest
{
    [Test]
    public void Utf8AsAsciiRegexTest()
    {
        const string input = @"·W 0:09:45.540824 {PPU[0x1000016] Thread (addContSyncThread) [HLE:0x01245834, LR:0x0019b834]} sceNp: npDrmIsAvailable(): Rap file not found: “/dev_hdd0/home/00000001/exdata/EP4062-NPEB02436_00-ADDCONTENT000001.rap”
·W 0:09:45.540866 {PPU[0x1000016] Thread (addContSyncThread) [HLE:0x01245834, LR:0x0019b834]} sceNp: sceNpDrmIsAvailable2(k_licensee=*0xd521b0, drm_path=*0xd00ddac0)";
        const RegexOptions DefaultOptions = RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.ExplicitCapture;

        var latin = input.ToLatin8BitEncoding();
        var match = Regex.Match(latin, @"Rap file not found: (\xE2\x80\x9C)?(?<rap_file>.*?)(\xE2\x80\x9D)?\r?$", DefaultOptions);
        Assert.Multiple(() =>
        {
            Assert.That(match.Success, Is.True);
            Assert.That(match.Groups["rap_file"].Value, Is.EqualTo("/dev_hdd0/home/00000001/exdata/EP4062-NPEB02436_00-ADDCONTENT000001.rap"));
        });
    }
}