using System.Net;
using System.Text.RegularExpressions;
using DSharpPlus.Commands.EventArgs;
using DSharpPlus.Commands.Exceptions;
using DSharpPlus.Commands.Processors.TextCommands;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace CompatBot.Commands.Processors;

internal static partial class CommandErroredHandler
{
    [GeneratedRegex(
        @"^\s*(am I|(are|is|do(es)|did|can(?!\s+of\s+)|should|must|have)(n't)?|shall|shan't|may|will|won't)\b",
        RegexOptions.Singleline | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase
    )]
    private static partial Regex BinaryQuestion();

    // CommandsExtension.DefaultCommandErrorHandlerAsync()
    public static async Task OnError(CommandsExtension sender, CommandErroredEventArgs eventArgs)
    {
        StringBuilder stringBuilder = new();
        DiscordMessageBuilder messageBuilder = new();

        if (eventArgs is
            {
                Exception: CommandNotFoundException or CommandNotExecutableException,
                Context: TextCommandContext tctx
            })
        {
            if (tctx.Prefix == Config.CommandPrefix || tctx.Prefix == Config.AutoRemoveCommandPrefix)
            {
                try
                {
                    await tctx.Message.DeleteAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, "Failed to remove message with unknown text command");
                }
            }
            else
            {
                if (tctx.Message.Content is string msgTxt
                    && (msgTxt.EndsWith('?') || BinaryQuestion().IsMatch(msgTxt.AsSpan(tctx.Prefix!.Length)))
                    && tctx.Extension.Commands.TryGetValue("8ball", out var cmd))
                {
                    await using var scope = tctx.Extension.ServiceProvider.CreateAsyncScope();
                    var updatedContext = new TextCommandContext()
                    {
                        User = tctx.User,
                        Channel = tctx.Channel,
                        Message = tctx.Message,
                        PrefixLength = tctx.Prefix!.Length,

                        Command = cmd,
                        Arguments = new Dictionary<CommandParameter, object?>()
                        {
                            [new() { Name = "question", Type = typeof(string), Parent = cmd }] = msgTxt[tctx.Prefix!.Length ..].Trim()
                        },
                        Extension = tctx.Extension,
                        ServiceScope = scope,
                    };
                    try
                    {
                        await tctx.Extension.CommandExecutor.ExecuteAsync(updatedContext).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Config.Log.Warn(e, "Failed to execute custom 8ball command");
                    }
                    return;
                }
            }
            return;
        }
        
        // Error message
        stringBuilder.Append(eventArgs.Exception switch
        {
            CommandNotFoundException commandNotFoundException
                => $"Command ``{commandNotFoundException.CommandName}`` was not found.",
            
            CommandRegistrationFailedException
                => "Application commands failed to register.",

            ArgumentParseException { ConversionResult.Value: not null } argumentParseException
                => $"Failed to parse argument ``{argumentParseException.Parameter.Name}``: ``{argumentParseException.ConversionResult.Value.ToString() ?? "<null>"}`` is not a valid value. {argumentParseException.Message}",

            ArgumentParseException argumentParseException
                => $"Failed to parse argument ``{argumentParseException.Parameter.Name}``: {argumentParseException.Message}",

            ChecksFailedException { Errors.Count: 1 } checksFailedException
                => $"The following error occurred: ``{checksFailedException.Errors[0].ErrorMessage}``",

            ChecksFailedException checksFailedException
                => $"""
                    The following context checks failed: ```
                    {string.Join("\n- ", checksFailedException.Errors.Select(x => x.ErrorMessage)).Trim()}
                    ```
                    """,

            ParameterChecksFailedException { Errors.Count: 1 } checksFailedException
                => $"The following error occurred: ``{checksFailedException.Errors[0].ErrorMessage}``",

            ParameterChecksFailedException checksFailedException
                => $"""
                    The following context checks failed: ```
                    {string.Join("\n- ", checksFailedException.Errors.Select(x => x.ErrorMessage)).Trim()}
                    ```
                    """,

            DiscordException { Response.StatusCode: >= (HttpStatusCode)500 and < (HttpStatusCode)600 } discordException
                => $"Discord API error {discordException.Response.StatusCode} occurred: {discordException.JsonMessage ?? "No further information was provided."}",

            DiscordException { Response: not null } discordException
                => $"Discord API error {discordException.Response.StatusCode} occurred: {discordException.JsonMessage ?? discordException.Message}",

            _ => $"An unexpected error occurred: {eventArgs.Exception.Message}",
        });

        // Stack trace
        if (!string.IsNullOrWhiteSpace(eventArgs.Exception.StackTrace))
        {
            // If the stack trace can fit inside a codeblock
            if (8 + eventArgs.Exception.StackTrace.Length + stringBuilder.Length <= 2000)
            {
                stringBuilder.Append($"```\n{eventArgs.Exception.StackTrace}\n```");
                messageBuilder.WithContent(stringBuilder.ToString());
            }
            // If the exception message exceeds the message character limit, cram it all into an attatched file with a simple message in the content.
            else if (stringBuilder.Length >= 2000)
            {
                messageBuilder.WithContent("Exception Message exceeds character limit, see attached file.");
                string formattedFile = $"{stringBuilder}{Environment.NewLine}{Environment.NewLine}Stack Trace:{Environment.NewLine}{eventArgs.Exception.StackTrace}";
                messageBuilder.AddFile(
                    "MessageAndStackTrace.txt",
                    Config.MemoryStreamManager.GetStream(Encoding.UTF8.GetBytes(formattedFile)),
                    AddFileOptions.CloseStream
                );
            }
            // Otherwise, display the exception message in the content and the trace in an attached file
            else
            {
                messageBuilder.WithContent(stringBuilder.ToString());
                messageBuilder.AddFile(
                    "StackTrace.txt",
                    Config.MemoryStreamManager.GetStream(Encoding.UTF8.GetBytes(eventArgs.Exception.StackTrace)),
                    AddFileOptions.CloseStream
                );
            }
        }
        // If no stack trace, and the message is still too long, attatch a file with the message and use a simple message in the content.
        else if (stringBuilder.Length >= 2000)
        {
            messageBuilder.WithContent("Exception Message exceeds character limit, see attached file.");
            messageBuilder.AddFile(
                "Message.txt",
                Config.MemoryStreamManager.GetStream(Encoding.UTF8.GetBytes(stringBuilder.ToString())),
                AddFileOptions.CloseStream
            );
        }
        // Otherwise, if no stack trace and the Exception message will fit, send the message as content
        else
            messageBuilder.WithContent(stringBuilder.ToString());

        if (eventArgs.Context is SlashCommandContext { Interaction.ResponseState: not DiscordInteractionResponseState.Unacknowledged })
            await eventArgs.Context.FollowupAsync(new DiscordFollowupMessageBuilder(messageBuilder).AsEphemeral());
        else
            await eventArgs.Context.RespondAsync(new DiscordInteractionResponseBuilder(messageBuilder).AsEphemeral());
    }
}