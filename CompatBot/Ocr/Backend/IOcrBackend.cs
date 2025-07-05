namespace CompatBot.Ocr.Backend;

public interface IOcrBackend
{
    string Name { get; }
    Task<bool> InitializeAsync(CancellationToken cancellationToken);
    Task<string> GetTextAsync(string imgUrl, CancellationToken cancellationToken);
}