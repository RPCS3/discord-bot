namespace CompatApiClient.Formatters
{
    public static class SpecialJsonNamingPolicy
    {
        public static SnakeCaseNamingPolicy SnakeCase { get; } = new();
        public static DashedNamingPolicy Dashed { get; } = new(); 
    }
}