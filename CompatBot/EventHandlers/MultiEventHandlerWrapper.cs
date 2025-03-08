namespace CompatBot.EventHandlers;

public class MultiEventHandlerWrapper<T> where T: DiscordEventArgs
{
    private readonly ICollection<Func<DiscordClient,T,Task<bool>>> orderedHandlers;
    private readonly ICollection<Func<DiscordClient,T,Task>> unorderedHandlers;

    public MultiEventHandlerWrapper(ICollection<Func<DiscordClient, T, Task<bool>>> orderedHandlers, ICollection<Func<DiscordClient, T, Task>> unorderedHandlers)
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
    
    public static Func<DiscordClient, T, Task> CreateOrdered(ICollection<Func<DiscordClient, T, Task<bool>>> orderedHandlers)
        => new MultiEventHandlerWrapper<T>(orderedHandlers, []).OnEvent;

    public static Func<DiscordClient, T, Task> CreateUnordered(ICollection<Func<DiscordClient, T, Task>> unorderedHandlers)
        => new MultiEventHandlerWrapper<T>([], unorderedHandlers).OnEvent;
}