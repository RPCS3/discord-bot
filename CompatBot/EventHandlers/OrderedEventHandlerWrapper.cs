using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers;

public class OrderedEventHandlerWrapper<T> where T: DiscordEventArgs
{
    private readonly ICollection<Func<DiscordClient,T,Task<bool>>> orderedHandlers;
    private readonly ICollection<Func<DiscordClient,T,Task>> unorderedHandlers;

    public OrderedEventHandlerWrapper(ICollection<Func<DiscordClient, T, Task<bool>>> orderedHandlers, ICollection<Func<DiscordClient, T, Task>> unorderedHandlers)
    {
        this.orderedHandlers = orderedHandlers;
        this.unorderedHandlers = unorderedHandlers;
    }

    public async Task OnEvent(DiscordClient client, T eventArgs)
    {
        try
        {
            foreach (var h in orderedHandlers)
                if (!await h(client, eventArgs).ConfigureAwait(false))
                    return;

            var unorderedTasks = unorderedHandlers.Select(async h => await h(client, eventArgs).ConfigureAwait(false));
            await Task.WhenAll(unorderedTasks).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
        }
    }
}