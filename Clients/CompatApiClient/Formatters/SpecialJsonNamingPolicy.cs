namespace CompatApiClient
{
    public static class SpecialJsonNamingPolicy
    {
        public static SnakeCaseNamingPolicy SnakeCase { get; } = new SnakeCaseNamingPolicy();
        public static DashedNamingPolicy Dashed { get; } = new DashedNamingPolicy(); 
    }
}