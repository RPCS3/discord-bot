using System.IO;
using System.Net.Http;
using CompatApiClient.Compression;
using TesseractCSharp;
using TesseractCSharp.Interop;

namespace CompatBot.Ocr.Backend;

internal class Tesseract: BackendBase
{
    private TesseractEngine engine;

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
        using var img = Pix.LoadFromMemory(imgData);
        using var page = engine.Process(img);
        return (page.GetText() ?? "", page.GetMeanConfidence());
    }

    public override void Dispose()
    {
        base.Dispose();
        engine.Dispose();
    }
}