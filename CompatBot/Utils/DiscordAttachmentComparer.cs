namespace CompatBot.Utils;

internal class DiscordAttachmentComparer : IEqualityComparer<DiscordAttachment>
{
    bool IEqualityComparer<DiscordAttachment>.Equals(DiscordAttachment? x, DiscordAttachment? y)
    {
        if (x is null && y is null)
            return true;

        if (x is null || y is null)
            return false;

        return x.FileName == y.FileName && x.FileSize == y.FileSize;
    }

    int IEqualityComparer<DiscordAttachment>.GetHashCode(DiscordAttachment obj)
        => HashCode.Combine((obj.FileName ?? "").GetHashCode(), obj.FileSize.GetHashCode());

    public static DiscordAttachmentComparer Instance { get; } = new DiscordAttachmentComparer();
}

internal class DiscordAttachmentFuzzyComparer : IEqualityComparer<DiscordAttachment>
{
    bool IEqualityComparer<DiscordAttachment>.Equals(DiscordAttachment? x, DiscordAttachment? y)
    {
        if (x is null && y is null)
            return true;

        if (x is null || y is null)
            return false;

        return x.FileSize == y.FileSize;
    }

    int IEqualityComparer<DiscordAttachment>.GetHashCode(DiscordAttachment obj)
        => obj.FileSize.GetHashCode();

    public static DiscordAttachmentComparer Instance { get; } = new DiscordAttachmentComparer();
}
