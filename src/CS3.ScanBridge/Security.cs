using System.Text;

namespace CS3.ScanBridge;

public static class OriginPolicy
{
    public static bool IsAllowed(string? origin, IEnumerable<string> allowedOrigins) =>
        origin is not null && allowedOrigins.Any(value =>
            string.Equals(value.TrimEnd('/'), origin, StringComparison.Ordinal));

    public static bool IsValidConfiguredOrigin(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin) || origin.Contains('*', StringComparison.Ordinal)) return false;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        if (string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        if (uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
        var canonical = uri.GetLeftPart(UriPartial.Authority);
        return string.Equals(canonical, origin.TrimEnd('/'), StringComparison.Ordinal);
    }
}

public static class FilenameSanitizer
{
    private const string DefaultPrefix = "delivery-note";

    public static string Sanitize(string? suggested, DateTimeOffset now)
    {
        var fallback = $"{DefaultPrefix}-{now:yyyyMMdd-HHmmss}.pdf";
        if (string.IsNullOrWhiteSpace(suggested)) return fallback;

        var name = Path.GetFileName(suggested.Trim());
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (!invalid.Contains(character) && !char.IsControl(character)) builder.Append(character);
        }

        name = builder.ToString().Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(name)) return fallback;
        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name += ".pdf";
        if (name.Length > 120) name = name[..116] + ".pdf";
        return name;
    }
}
