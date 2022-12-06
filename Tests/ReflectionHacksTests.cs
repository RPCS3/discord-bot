using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using DSharpPlus;
using DSharpPlus.Entities;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class ReflectionHacksTests
{
    [Test]
    public void DiscordButtonComponentEmojiSetterTest()
    {
        var button = new DiscordButtonComponent(ButtonStyle.Primary, "test", "Test");
        var property = button.GetType().GetProperty(nameof(button.Emoji));
        Assert.That(property, Is.Not.Null);
        Assert.That(property.GetMethod?.IsPublic, Is.True);
        
        var setter = property.SetMethod;
        Assert.That(setter, Is.Not.Null);
        Assert.That(setter.IsPublic, Is.False, $"{nameof(DiscordButtonComponent)}.{nameof(DiscordButtonComponent.Emoji)} setter is now public, please remove hack in {nameof(DiscordComponentsExtensions)}.{nameof(DiscordComponentsExtensions.SetEmoji)}");
    }

    [Test]
    public void DiscordMessageBuilderReplyIdSetterTest()
    {
        var messageBuilder = new DiscordMessageBuilder();
        var property = messageBuilder.GetType().GetProperty(nameof(messageBuilder.ReplyId));
        Assert.That(property, Is.Not.Null);
        Assert.That(property.GetMethod?.IsPublic, Is.True);

        var setter = property.SetMethod;
        Assert.That(setter, Is.Not.Null);
        Assert.That(setter.IsPublic, Is.False, $"{nameof(DiscordMessageBuilder)}.{nameof(DiscordMessageBuilder.ReplyId)} setter is now public, please remove hack in {nameof(DiscordMessageExtensions)}.{nameof(DiscordMessageExtensions.UpdateOrCreateMessageAsync)}");
    }

    [Test]
    public void DiscordMessageChannelSetterTest()
    {
        var property = typeof(DiscordMessage).GetProperty(nameof(DiscordMessage.Channel));
        Assert.That(property, Is.Not.Null);
        Assert.That(property.GetMethod?.IsPublic, Is.True);

        var setter = property.SetMethod;
        Assert.That(setter, Is.Not.Null);
        Assert.That(setter.IsPublic, Is.False, $"{nameof(DiscordMessage)}.{nameof(DiscordMessage.Channel)} setter is now public, please remove hack in {nameof(DiscordMessageExtensions)}.{nameof(DiscordMessageExtensions.UpdateOrCreateMessageAsync)}");
    }
}