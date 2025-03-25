namespace CompatBot.Utils;

public static class DiscordGuildExtensions
{
    public static int GetAttachmentSizeLimit(this CommandContext ctx)
        => ctx.Guild.GetAttachmentSizeLimit();
    
    public static int GetAttachmentSizeLimit(this DiscordGuild? guild)
        => guild?.PremiumTier switch
        {
            DiscordPremiumTier.Tier_3 => 100 * 1024 * 1024,
            DiscordPremiumTier.Tier_2 => 50 * 1024 * 1024,
            _ => Config.AttachmentSizeLimit,
        };
}