namespace PsnClient.POCOs;
// https://transact.playstation.com/assets/app.json
// returns an array of different objects
// api endpoints, oauth, oauth authorize, telemetry, localization options, billing template, locales, country names, topup settings, paypal sandbox settings, gct, apm, sofort, ...

// this is item #6 in App array
public sealed class AppLocales
{
    public string[]? EnabledLocales; // "ar-AE",…
    public AppLocaleOverride[]? Overrides;
}

public sealed class AppLocaleOverride
{
    public AppLocaleOverrideCriteria? Criteria;
    public string? GensenLocale; // "ar-AE"
}

public sealed class AppLocaleOverrideCriteria
{
    public string? Language; // "ar"
    public string? Country; // "AE|BH|KW|LB|OM|QA|SA"
}