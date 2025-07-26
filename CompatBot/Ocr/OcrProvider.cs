using System.Runtime.InteropServices;
using CompatBot.Ocr.Backend;

namespace CompatBot.Ocr;

public static class OcrProvider
{
    private static IOcrBackend? backend;

    public static bool IsAvailable => backend is not null;
    public static string BackendName => backend?.Name ?? "not configured";

    public static async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var backendName = Config.OcrBackend;
        if (GetBackend(backendName) is not {} result)
        {
            if (Config.AzureComputerVisionKey is { Length: > 0 })
                backendName = "azure";
            else if (GC.GetGCMemoryInfo().TotalAvailableMemoryBytes > 4L * 1024 * 1024 * 1024
                || RuntimeInformation.OSArchitecture is not (Architecture.X64 or Architecture.X86))
            {
                backendName = "florence2";
            }
            else
                backendName = "tesseract";
            result = GetBackend(backendName)!;
        }
        Config.Log.Info($"Initializing OCR backend {BackendName}…");
        if (await result.InitializeAsync(cancellationToken).ConfigureAwait(false))
        {
            backend = result;
            Config.Log.Info($"Initialized OCR backend {BackendName}");
        }
    }

    public static async Task<(string result, double confidence)> GetTextAsync(string imageUrl, CancellationToken cancellationToken)
    {
        if (backend is null)
            return ("", -1);

        try
        {
            return await backend.GetTextAsync(imageUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, $"Failed to OCR image {imageUrl}");
            return ("", 0);
        }
    }

    private static IOcrBackend? GetBackend(string name)
        => name.ToLowerInvariant() switch
        {
            "tesseract" => new Backend.Tesseract(),
            "florence2" => new Backend.Florence2(),
            "azure" => new Backend.AzureVision(),
            _ => null,
        };
}