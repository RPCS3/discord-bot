namespace CompatBot.Utils;

public static class ServiceProviderExteinsions
{
    public static T? GetService<T>(this IServiceProvider provider)
        => (T?)provider.GetService(typeof(T));
}