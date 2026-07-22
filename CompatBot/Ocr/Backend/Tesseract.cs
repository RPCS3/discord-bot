using System.IO;
using System.Net.Http;
using CompatApiClient.Compression;
using TesseractCSharp;
using TesseractCSharp.Interop;

namespace CompatBot.Ocr.Backend;

internal class Tesseract: BackendBase
{
    private TesseractEngine engine = null!;
    private static readonly SemaphoreSlim Limiter = new(1, 1);

    public override string Name => "tesseract";

    public override async Task<bool> InitializeAsync(CancellationToken cancellationToken)
    {
        if (!await base.InitializeAsync(cancellationToken).ConfigureAwait(false))
            return false;

        try
        {
            NativeConstants.InitNativeLoader();
        }
        catch (Exception e)
        {
            Config.Log.Error(e, "Failed to load Tesseract native dependencies");
            return false;
        }

        var modelsAvailable = await EnsureModelIsCached("eng", cancellationToken).ConfigureAwait(false)
                              && await EnsureModelIsCached("rus", cancellationToken).ConfigureAwait(false);
        if (!modelsAvailable)
            return false;

        try
        {
            engine = new(ModelCachePath, "eng+rus", EngineMode.Default);
        }
        catch (Exception e)
        {
            Config.Log.Error(e, "Failed to initialize Tesseract engine");
            return false;
        }

        return true;
    }

    public override async Task<(string result, double confidence)> GetTextAsync(string imgUrl, int rotation, CancellationToken cancellationToken)
    {
        var imgData = await HttpClient.GetByteArrayAsync(imgUrl, cancellationToken).ConfigureAwait(false);
        var img = Pix.LoadFromMemory(imgData);
        try
        {
            if (rotation > 0)
            {
                var img2 = rotation switch
                {
                    1 => img.Rotate90((int)RotationDirection.Clockwise),
                    2 => img.Rotate((float)Math.PI),
                    3 => img.Rotate90((int)RotationDirection.CounterClockwise),
                    _ => throw new InvalidOperationException($"Can only rotate 3 times at most, but asked for {rotation}"),
                };
                img.Dispose();
                img = img2;
            }
            await Limiter.WaitAsync(Config.Cts.Token).ConfigureAwait(false);
            try
            {
                using var page = engine.Process(img);
                return (page.GetText() ?? "", page.GetMeanConfidence());
            }
            finally
            {
                Limiter.Release();
            }
        }
        finally
        {
            img.Dispose();
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        engine.Dispose();
    }

    private async ValueTask<bool> EnsureModelIsCached(string lang, CancellationToken cancellationToken)
    {
        var modelPath = Path.Combine(ModelCachePath, $"{lang}.traineddata");
        if (File.Exists(modelPath))
            return true;
        
        try
        {
            using var client = HttpClientFactory.Create(new CompressionMessageHandler());
            // existing repos: tessdata_fast, tessdata, tessdata_best
            var uri = $"https://github.com/tesseract-ocr/tessdata_best/raw/refs/heads/main/{lang}.traineddata";
            await using var response = await client.GetStreamAsync(uri, cancellationToken).ConfigureAwait(false);
            await using var file = File.Open(modelPath, new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
            await response.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Error(e, $"Failed to download model data for language {lang}");
            return false;
        }
        return true;
    }

    private enum RotationDirection
    {
        Clockwise = 1,
        CounterClockwise = -1,
    }
}