using System.Text.Json;

namespace Divinity.ContentSchema;

public static class ContentSourceLoader
{
    private const string MapPath = "maps/training-field-01/map.json";

    public static ContentLoadResult Load(string contentRoot)
    {
        var normalizedRoot = Path.GetFullPath(contentRoot);
        var errors = new List<string>();
        var files = new List<ContentFile>();

        if (!Directory.Exists(normalizedRoot))
        {
            return new ContentLoadResult(null, [$"content root not found: {normalizedRoot}"]);
        }

        var map = ReadJson<MapDefinition>(normalizedRoot, MapPath, errors, files);
        var skills = ReadJsonDirectory<SkillDefinition>(normalizedRoot, "skills/knight", errors, files);
        var items = ReadJsonDirectory<ItemDefinition>(normalizedRoot, "items/tier-0", errors, files);
        var lootTables = ReadJsonDirectory<LootTableDefinition>(normalizedRoot, "loot-tables", errors, files);

        var draft = new ContentDraft(map, skills, items, lootTables);
        errors.AddRange(ContentValidator.Validate(draft));

        if (errors.Count > 0 || map is null)
        {
            return new ContentLoadResult(null, errors);
        }

        var hash = ContentHasher.ComputeHash(files);
        var catalog = new ContentCatalog
        {
            Map = map,
            Skills = skills,
            Items = items,
            LootTables = lootTables,
            ContentHash = hash
        };

        return new ContentLoadResult(catalog, []);
    }

    private static List<T> ReadJsonDirectory<T>(
        string contentRoot,
        string relativeDirectory,
        List<string> errors,
        List<ContentFile> files)
    {
        var fullDirectory = Path.Combine(contentRoot, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(fullDirectory))
        {
            errors.Add($"{relativeDirectory}: required content directory not found.");
            return [];
        }

        var values = new List<T>();
        foreach (var path in Directory.EnumerateFiles(fullDirectory, "*.json", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var relativePath = ToRelativePath(contentRoot, path);
            var value = ReadJson<T>(contentRoot, relativePath, errors, files);
            if (value is not null)
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static T? ReadJson<T>(string contentRoot, string relativePath, List<string> errors, List<ContentFile> files)
    {
        var fullPath = Path.Combine(contentRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            errors.Add($"{relativePath}: required content file not found.");
            return default;
        }

        try
        {
            var json = File.ReadAllText(fullPath);
            var value = JsonSerializer.Deserialize<T>(json, ContentJson.Options);
            if (value is null)
            {
                errors.Add($"{relativePath}: content document deserialized to null.");
                return default;
            }

            files.Add(new ContentFile(relativePath, json));
            return value;
        }
        catch (JsonException ex)
        {
            errors.Add($"{relativePath}: invalid JSON: {ex.Message}");
            return default;
        }
    }

    private static string ToRelativePath(string contentRoot, string fullPath) =>
        Path.GetRelativePath(contentRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');
}
