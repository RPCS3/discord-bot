namespace CompatBot.Utils;

public static class DiscordGuildExtensions
{
    public static int GetAttachmentSizeLimit(this CommandContext ctx)
        => ctx.Guild.GetAttachmentSizeLimit();
    
    public static int GetAttachmentSizeLimit(this DiscordGuild? guild)
        => guild?.PremiumTier switch
        {
            PremiumTier.Tier_3 => 100 * 1024 * 1024,
            PremiumTier.Tier_2 => 50 * 1024 * 1024,
            _ => Config.AttachmentSizeLimit,
        };
}