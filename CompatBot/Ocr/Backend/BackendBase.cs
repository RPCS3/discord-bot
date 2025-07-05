using System.IO;
using System.Net.Http;
using CompatApiClient.Compression;

namespace CompatBot.Ocr.Backend;

public abstract class BackendBase: IOcrBackend, IDisposable
{
    protected static readonly HttpClient HttpClient = HttpClientFactory.Create(new CompressionMessageHandler());

    public abstract string Name { get; }

    public virtual Task<bool> InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(ModelCachePath))
                Directory.CreateDirectory(ModelCachePath);
        }
        catch (Exception e)
        {
            Config.Log.Error(e, $"Failed to create model cache folder '{ModelCachePath}'");
            return Task.FromResult(false);
        }
        return Task.FromResult(true);
    }

    public abstract Task<string> GetTextAsync(string imgUrl, CancellationToken cancellationToken);

    public virtual void Dispose() => HttpClient.Dispose();

    protected string ModelCachePath => Path.Combine(Config.BotAppDataFolder, "ocr-models", Name.ToLowerInvariant());
}