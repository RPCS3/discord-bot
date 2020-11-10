using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

// This file is used by Code Analysis to maintain SuppressMessage 
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given 
// a specific target and scoped to a namespace, type, member, etc.

[assembly: SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "async background check by design", Scope = "member", Target = "~M:CompatBot.Commands.Moderation.Audit.SpoofingCheck(DSharpPlus.CommandsNext.CommandContext)")]
[assembly: SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "async background check by design", Scope = "member", Target = "~M:CompatBot.ThumbScrapper.PsnScraper.CheckContentIdAsync(DSharpPlus.CommandsNext.CommandContext,System.String,System.Threading.CancellationToken)")]
[assembly: SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "async background check by design", Scope = "member", Target = "~M:CompatBot.EventHandlers.LogParsingHandler.EnqueueLogProcessing(DSharpPlus.DiscordClient,DSharpPlus.Entities.DiscordChannel,DSharpPlus.Entities.DiscordMessage,DSharpPlus.Entities.DiscordMember,System.Boolean,System.Boolean)")]
[assembly: InternalsVisibleTo("Tests")]