using DSharpPlus.Commands.ContextChecks;

namespace CompatBot.Commands.Attributes;

internal abstract class CheckAttributeWithReactions(
    DiscordEmoji? reactOnSuccess = null,
    DiscordEmoji? reactOnFailure = null
) : ContextCheckAttribute
{
    public DiscordEmoji? ReactOnSuccess { get; } = reactOnSuccess;
    public DiscordEmoji? ReactOnFailure { get; } = reactOnFailure;
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal class RequiresBotSudoerRoleAttribute(): CheckAttributeWithReactions(reactOnFailure: Config.Reactions.Denied);

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal class RequiresBotModRoleAttribute(): CheckAttributeWithReactions(reactOnFailure: Config.Reactions.Denied);

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal class RequiresWhitelistedRoleAttribute(): CheckAttributeWithReactions(reactOnFailure: Config.Reactions.Denied);

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal class RequiresSmartlistedRoleAttribute(): CheckAttributeWithReactions(reactOnFailure: Config.Reactions.Denied);

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal class RequiresSupporterRoleAttribute(): CheckAttributeWithReactions(reactOnFailure: Config.Reactions.Denied);
