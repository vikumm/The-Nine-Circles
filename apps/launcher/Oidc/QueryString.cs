using System.Net;

namespace Divinity.Launcher.Oidc;

public static class QueryString
{
    public static IReadOnlyDictionary<string, string> Parse(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var trimmed = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return values;
        }

        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = WebUtility.UrlDecode(parts[0]);
            var value = parts.Length == 2 ? WebUtility.UrlDecode(parts[1]) : string.Empty;
            values[key] = value;
        }

        return values;
    }
}
