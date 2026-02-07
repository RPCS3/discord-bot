namespace CompatBot.Utils;

public static class SandboxDetector
{
    public static SandboxType Detect()
    {
        if (Environment.GetEnvironmentVariable("SNAP") is { Length: >0 })
            return SandboxType.Snap;

        if (Environment.GetEnvironmentVariable("FLATPAK_SYSTEM_DIR") is { Length: > 0 })
            return SandboxType.Flatpak;

        if (Environment.GetEnvironmentVariable("RUNNING_IN_DOCKER") is { Length: > 0 })
            return SandboxType.Docker;

        if (Environment.GetEnvironmentVariable("RUNNING_UNDER_SYSTEMD") is { Length: > 0 })
            return SandboxType.Systemd;

        return SandboxType.None;
    }
}