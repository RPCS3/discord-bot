namespace CompatApiClient;

public enum CompatApiStatus: short
{
    IllegalQuery = -3,
    Maintenance = -2,
    InternalError = -1,
    Success = 0,
    NoResults = 1,
    NoExactMatch = 2,
}