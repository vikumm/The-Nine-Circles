namespace Divinity.Launcher.Security;

public static class SensitiveLogSanitizer
{
    public static string RedactKnownValues(string message, IEnumerable<string?> sensitiveValues)
    {
        var sanitized = message;
        foreach (var value in sensitiveValues.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal))
        {
            sanitized = sanitized.Replace(value!, "[redacted]", StringComparison.Ordinal);
        }

        return sanitized;
    }

    public static bool ContainsSensitiveValue(IEnumerable<string> logLines, params string?[] sensitiveValues)
    {
        var materialValues = sensitiveValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return logLines.Any(line => materialValues.Any(value => line.Contains(value!, StringComparison.Ordinal)));
    }
}
