using Florence2;

namespace CompatBot.Ocr.Backend;

public class Florence2: BackendBase
{
    private Florence2Model model;

    public override string Name => "florence2";

    public override async Task<bool> InitializeAsync(CancellationToken cancellationToken)
    {
        if (!await base.InitializeAsync(cancellationToken).ConfigureAwait(false))
            return false;

        var modelSource = new FlorenceModelDownloader(ModelCachePath);
        try
        {
            var errors = false;
            await modelSource.DownloadModelsAsync(s =>
                {
                    if (s.Error is { Length: > 0 } errorMsg)
                    {
                        Config.Log.Error($"Failed to download Florence2 model files: {errorMsg}");
                        errors = true;
                    }
                    else if (s.Message is { Length: > 0 } msg)
                    {
                        Config.Log.Info($"Florence2 model download message: {msg}");
                    }
                },
                Config.LoggerFactory.CreateLogger("florence2"),
                cancellationToken
            ).ConfigureAwait(false);
            if (errors)
                return false;
        }
        catch (Exception e)
        {
            Config.Log.Error(e, "Failed to download Florence2 model files");
            return false;
        }

        try
        {
            model = new(modelSource);
        }
        catch (Exception e)
        {
            Config.Log.Error(e, "Failed to initialize Florence2 model");
            return false;
        }
        return true;
    }

    public override async Task<(string result, double confidence)> GetTextAsync(string imgUrl, int rotation, CancellationToken cancellationToken)
    {
        if (rotation > 0)
            return ("", 0);

        await using var imgStream = await HttpClient.GetStreamAsync(imgUrl, cancellationToken).ConfigureAwait(false);
        var results = model.Run(TaskTypes.OCR_WITH_REGION, [imgStream], "", CancellationToken.None);
        var result = new StringBuilder();
        foreach (var box in results[0].OCRBBox)
            result.AppendLine(box.Text);
        return (result.ToString().TrimEnd(), 1);
    }
}