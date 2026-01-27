using System.IO;
using System.Net.Http;
using CompatApiClient.Compression;
using TesseractCSharp;
using TesseractCSharp.Interop;

namespace CompatBot.Ocr.Backend;

internal class Tesseract: BackendBase
{
    private TesseractEngine engine;
    private static readonly SemaphoreSlim limiter = new(1, 1);

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

        var engModelPath = Path.Combine(ModelCachePath, "eng.traineddata");
        if (!File.Exists(engModelPath))
        {
            try
            {
                using var client = HttpClientFactory.Create(new CompressionMessageHandler());
                // existing repos: tessdata_fast, tessdata, tessdata_best
                const string uri = "https://github.com/tesseract-ocr/tessdata_best/raw/refs/heads/main/eng.traineddata";
                await using var response = await client.GetStreamAsync(uri, cancellationToken).ConfigureAwait(false);
                await using var file = File.Open(engModelPath, new FileStreamOptions
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
                Config.Log.Error(e, "Failed to download model data");
                return false;
            }
        }

        try
        {
            engine = new(ModelCachePath, "eng", EngineMode.Default);
        }
        catch (Exception e)
        {
            Config.Log.Error(e, "Failed to initialize Tesseract engine");
            return false;
        }

        return true;
    }

    public override async Task<(string result, double confidence)> GetTextAsync(string imgUrl, CancellationToken cancellationToken)
    {
        var imgData = await HttpClient.GetByteArrayAsync(imgUrl, cancellationToken).ConfigureAwait(false);
        var img = Pix.LoadFromMemory(imgData);
        var result = new (string text, float confidence)[4];
        await limiter.WaitAsync(Config.Cts.Token).ConfigureAwait(false);
        try
        {
            var pass = 0;
            do
            {
                using (var page = engine.Process(img))
                    result[pass] = (page.GetText() ?? "", page.GetMeanConfidence());
                if (pass < 3)
                {
                    var img2 = img.Rotate90((int)RotationDirection.Clockwise);
                    img.Dispose();
                    img = img2;
                }
                pass++;
            } while (pass < 4);
            var longestText = result
                .Where(i => i.confidence > 0.5)
                .OrderByDescending(i => i.text.Length)
                .FirstOrDefault();
            if (longestText is { confidence: > 0.5f, text.Length: > 0 })
                return longestText;
            else
                return result.MaxBy(i => i.confidence);
        }
        finally
        {
            limiter.Release();
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        engine.Dispose();
    }

    private enum RotationDirection
    {
        Clockwise = 1,
        CounterClockwise = -1,
    }
}