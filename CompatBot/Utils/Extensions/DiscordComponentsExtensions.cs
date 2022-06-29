using System.Reflection;
using DSharpPlus.Entities;

namespace CompatBot.Utils.Extensions;

public static class DiscordComponentsExtensions
{
    public static DiscordButtonComponent SetEnabled(this DiscordButtonComponent button, bool isEnabled)
        => isEnabled ? button.Enable() : button.Disable();

    public static DiscordButtonComponent SetDisabled(this DiscordButtonComponent button, bool isDisabled)
        => isDisabled ? button.Disable() : button.Enable();

    public static DiscordButtonComponent SetEmoji(this DiscordButtonComponent button, DiscordComponentEmoji emoji)
    {
#warning Ugly hack, needs builder method or better yet making the setter public like before
        var property = button.GetType().GetProperty(nameof(button.Emoji));
        property?.SetValue(button, emoji, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty, null, null, null);
        return button;
    }
}