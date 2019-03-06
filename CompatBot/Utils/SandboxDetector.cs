using System;

namespace CompatBot.Utils
{
    public static class SandboxDetector
    {
        public static string Detect()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNAP")))
                return "Snap";

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLATPAK_SYSTEM_DIR")))
                return "Flatpak";

            return null;
        }
    }
}
