using System.Globalization;
using System.Text;

namespace Divinity.GameRules.Characters;

public static class CharacterName
{
    public const int MinLength = 3;
    public const int MaxLength = 20;

    public static CharacterNameResult Normalize(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return CharacterNameResult.Invalid("Name is required.");
        }

        var displayName = CollapseWhitespace(rawName.Trim());
        if (displayName.Length is < MinLength or > MaxLength)
        {
            return CharacterNameResult.Invalid($"Name must be between {MinLength} and {MaxLength} characters.");
        }

        if (displayName.Any(character => !IsAllowed(character)))
        {
            return CharacterNameResult.Invalid("Name may contain only letters, numbers, spaces, apostrophes and hyphens.");
        }

        var normalized = RemoveDiacritics(displayName).ToUpperInvariant();
        return CharacterNameResult.Valid(displayName, normalized);
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            builder.Append(character);
            previousWasSpace = false;
        }

        return builder.ToString();
    }

    private static bool IsAllowed(char character) =>
        char.IsLetterOrDigit(character) || character is ' ' or '\'' or '-';

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
