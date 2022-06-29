using System;

namespace CompatBot.Utils;

public static class SandboxDetector
{
    public static SandboxType Detect()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNAP")))
            return SandboxType.Snap;

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLATPAK_SYSTEM_DIR")))
            return SandboxType.Flatpak;

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RUNNING_IN_DOCKER")))
            return SandboxType.Docker;

        return SandboxType.None;
    }
}