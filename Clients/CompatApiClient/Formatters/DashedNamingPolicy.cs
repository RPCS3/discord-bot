using System.Text.Json;

namespace CompatApiClient;

public sealed class DashedNamingPolicy: JsonNamingPolicy
{
    public override string ConvertName(string name) => NamingStyles.Dashed(name);
}