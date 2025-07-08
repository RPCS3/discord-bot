namespace CompatBot.Ocr.Backend;

public interface IOcrBackend
{
    string Name { get; }
    Task<bool> InitializeAsync(CancellationToken cancellationToken);
    Task<(string result, double confidence)> GetTextAsync(string imgUrl, CancellationToken cancellationToken);
}