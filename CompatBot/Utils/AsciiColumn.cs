namespace CompatBot.Utils;

public sealed class AsciiColumn
{
    public AsciiColumn(string? name = null, bool disabled = false, bool alignToRight = false, int maxWidth = 80)
    {
        Name = name;
        Disabled = disabled;
        AlignToRight = alignToRight;
        MaxWidth = maxWidth;
    }

    public string? Name;
    public bool Disabled;
    public bool AlignToRight;
    public int MaxWidth;
}