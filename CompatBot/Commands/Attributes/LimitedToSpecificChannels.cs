using DSharpPlus.Commands.ContextChecks;

namespace CompatBot.Commands.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal class LimitedToHelpChannelAttribute: ContextCheckAttribute;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal class LimitedToOfftopicChannelAttribute: ContextCheckAttribute;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal class LimitedToSpamChannelAttribute: ContextCheckAttribute;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal class RequiresDmAttribute: ContextCheckAttribute;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal class RequiresNotMediaAttribute: ContextCheckAttribute;