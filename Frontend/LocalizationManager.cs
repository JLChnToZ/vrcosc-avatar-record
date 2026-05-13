using System;
using System.Globalization;
using System.Reflection;
using System.Resources;

internal static class LocalizationManager {
    private static readonly ResourceManager resourceManager =
        new ResourceManager($"{Assembly.GetExecutingAssembly().GetName().Name}.Localizations.Strings", Assembly.GetExecutingAssembly());

    private static CultureInfo? overrideCulture;

    public static void SetCulture(CultureInfo? culture) {
        overrideCulture = culture;
    }

    public static string Get(string key) {
        CultureInfo culture = overrideCulture ?? CultureInfo.CurrentUICulture;
        string? value = resourceManager.GetString(key, culture);
        return string.IsNullOrEmpty(value) ? key : value;
    }

    public static string GetWithInt(string key, int arg0) {
        string format = Get(key);
        CultureInfo culture = overrideCulture ?? CultureInfo.CurrentUICulture;
        string argText = arg0.ToString(culture);
        return format.Replace("{0}", argText);
    }
}
