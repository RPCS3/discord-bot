using System.Text.Json;

namespace CompatApiClient;

public sealed class SnakeCaseNamingPolicy: JsonNamingPolicy
{
    public override string ConvertName(string name) => NamingStyles.Underscore(name);
}