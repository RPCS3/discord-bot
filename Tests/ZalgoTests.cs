using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CompatBot.EventHandlers;
using NUnit.Framework;
using File = System.IO.File;

namespace Tests
{
    [TestFixture]
    public class ZalgoTests
    {
        [Test, Explicit("Requires external data")]
        public async Task ZalgoAuditTest()
        {
            var samplePath = @"C:/Users/13xforever/Downloads/names.txt";
            var resultPath = Path.Combine(Path.GetDirectoryName(samplePath), "zalgo.txt");

            var names = await File.ReadAllLinesAsync(samplePath, Encoding.UTF8);
            using (var r = File.Open(resultPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var w = new StreamWriter(r, new UTF8Encoding(false)))
                foreach (var line in names)
                {
                    var user = UserInfo.Parse(line);
                    var isZalgo = UsernameZalgoMonitor.NeedsRename(user.DisplayName);
                    w.Write(isZalgo ? 1 : 0);
                    w.Write('\t');
                    w.WriteLine(user.DisplayName);
                }
        }

        [TestCase("ᵇᶦᵒˢʰᵒᶜᵏ96", false)]
        [TestCase("GodPan กับยูนิตแขนที่หายไป", false)]
        [TestCase("⛧Bζ͜͡annerBomb⛧", false)]
        [TestCase("(_A_Y_A_Z_)  (͡๏̯͡๏)", false)]
        [TestCase("🎮P̷͙͋a̵̛̳k̶̫̀o̸̿͜ỏ̸̝🎮", true)]
        [TestCase("Cindellด้้้", true)]
        public void ZalgoDetectionTest(string name, bool isBad)
        {
            Assert.That(UsernameZalgoMonitor.NeedsRename(name), Is.EqualTo(isBad));
        }
    }

    internal class UserInfo
    {
        public string Username { get; private set; }
        public string Nickname { get; private set; }
        public DateTime JoinDate { get; private set; }
        public string[] Roles { get; private set; }

        public string DisplayName => string.IsNullOrEmpty(Nickname) ? Username : Nickname;

        public static UserInfo Parse(string line)
        {
            var parts = line.Split('\t');
            if (parts.Length != 4)
                throw new FormatException("Inalid user info line: " + line);

            return new UserInfo
            {
                Username = parts[0],
                Nickname = parts[1],
                JoinDate = DateTime.Parse(parts[2], CultureInfo.InvariantCulture),
                Roles = parts[3]?.Split(',') ?? new string[0],
            };
        }
    }
}
