namespace CompatBot.Utils;

public static class DiscordEmbedBuilderExtensions
{
    public static DiscordEmbedBuilder AddFieldEx(this DiscordEmbedBuilder builder, string header, string content, bool underline = false, bool inline = false)
    {
        content = string.IsNullOrEmpty(content) ? "-" : content;
        return builder.AddField(underline ? $"__{header}__" : header, content, inline);
    }
}