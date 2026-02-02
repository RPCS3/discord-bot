using CompatApiClient.Utils;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;

namespace CompatBot.Ocr.Backend;

public class AzureVision: IOcrBackend
{
    private ComputerVisionClient cvClient;

    public string Name => "azure";

    public Task<bool> InitializeAsync(CancellationToken cancellationToken)
    {
        if (Config.AzureComputerVisionKey is not { Length: > 0 })
            return Task.FromResult(false);

        cvClient = new(new ApiKeyServiceClientCredentials(Config.AzureComputerVisionKey))
        {
            Endpoint = Config.AzureComputerVisionEndpoint
        };
        return Task.FromResult(true);
    }

    public async Task<(string result, double confidence)> GetTextAsync(string imgUrl, int rotation, CancellationToken cancellationToken)
    {
        if (rotation > 0)
            return ("", 0);

        var headers = await cvClient.ReadAsync(imgUrl, cancellationToken: cancellationToken).ConfigureAwait(false);
        var operationId = new Guid(new Uri(headers.OperationLocation).Segments.Last());
        ReadOperationResult? result;
        bool waiting;
        do
        {
            result = await cvClient.GetReadResultAsync(operationId, Config.Cts.Token).ConfigureAwait(false);
            waiting = result.Status is OperationStatusCodes.NotStarted or OperationStatusCodes.Running;
            if (waiting)
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        } while (waiting);
        if (result.Status is OperationStatusCodes.Succeeded)
        {
            if (result.AnalyzeResult?.ReadResults?.SelectMany(r => r.Lines).Any() ?? false)
            {
                var ocrTextBuf = new StringBuilder();
                foreach (var r in result.AnalyzeResult.ReadResults)
                foreach (var l in r.Lines)
                    ocrTextBuf.AppendLine(l.Text);
                return (ocrTextBuf.ToString(), 1);
            }
        }
        Config.Log.Warn($"Failed to OCR image {imgUrl}: {result.Status}");
        return ("", 0);
    }
}