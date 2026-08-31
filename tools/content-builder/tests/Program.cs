using System.Text.Json;
using System.Text.Json.Nodes;
using Divinity.ContentBuilder;
using Divinity.ContentSchema;

var checks = new List<ContentCheck>();
var repoRoot = FindRepoRoot();
var contentRoot = Path.Combine(repoRoot, "content");

var validLoad = ContentSourceLoader.Load(contentRoot);
checks.Add(Check("valid Training Field content validates", validLoad.Success));
checks.Add(Check("lookup skill, item and loot table", RequiredLookupsExist(validLoad.Catalog)));
checks.Add(await CheckAsync("builder writes client and server artifacts", () => BuilderWritesArtifactsAsync(contentRoot)));
checks.Add(await CheckAsync("client and server artifacts share content hash", () => ClientAndServerArtifactsShareHashAsync(contentRoot)));
checks.Add(await CheckAsync("invalid map without safe spawn fails with actionable error", () => InvalidMapFailsAsync(repoRoot, RemoveSafeSpawn, "safe spawn")));
checks.Add(await CheckAsync("invalid map bounds fail with actionable error", () => InvalidMapFailsAsync(repoRoot, InvalidateBounds, "bounds")));
checks.Add(await CheckAsync("spawn outside map fails with actionable error", () => InvalidMapFailsAsync(repoRoot, MoveSpawnOutsideMap, "outside map bounds")));
checks.Add(await CheckAsync("inconsistent blocked cell fails with actionable error", () => InvalidMapFailsAsync(repoRoot, DuplicateBlockedCell, "duplicated")));

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length > 0)
{
    Console.Error.WriteLine($"VS-004 content tests failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-004 content tests passed.");
return 0;

static bool RequiredLookupsExist(ContentCatalog? catalog) =>
    catalog?.FindSkill("knight_basic_slash") is not null
    && catalog.FindSkill("knight_shield_bash_r1") is not null
    && catalog.FindItem("knight_wooden_shield_t0") is not null
    && catalog.FindLootTable("mob_moss_slime_l1") is not null;

static async Task<bool> BuilderWritesArtifactsAsync(string contentRoot)
{
    var outputRoot = CreateTempDirectory("vs004-builder-artifacts");
    try
    {
        var result = await ContentBuilder.RunAsync(new ContentBuilderOptions(contentRoot, outputRoot, WriteArtifacts: true));
        return result.Success
            && result.ClientArtifactPath is not null
            && result.ServerArtifactPath is not null
            && File.Exists(result.ClientArtifactPath)
            && File.Exists(result.ServerArtifactPath);
    }
    finally
    {
        DeleteDirectory(outputRoot);
    }
}

static async Task<bool> ClientAndServerArtifactsShareHashAsync(string contentRoot)
{
    var outputRoot = CreateTempDirectory("vs004-builder-hash");
    try
    {
        var result = await ContentBuilder.RunAsync(new ContentBuilderOptions(contentRoot, outputRoot, WriteArtifacts: true));
        if (!result.Success || result.ClientArtifactPath is null || result.ServerArtifactPath is null)
        {
            return false;
        }

        var client = JsonSerializer.Deserialize<ClientContentArtifact>(
            await File.ReadAllTextAsync(result.ClientArtifactPath),
            ContentJson.Options);
        var server = JsonSerializer.Deserialize<ServerContentArtifact>(
            await File.ReadAllTextAsync(result.ServerArtifactPath),
            ContentJson.Options);

        return client is not null
            && server is not null
            && !string.IsNullOrWhiteSpace(client.ContentHash)
            && string.Equals(client.ContentHash, server.ContentHash, StringComparison.Ordinal)
            && string.Equals(client.ContentHash, result.ContentHash, StringComparison.Ordinal);
    }
    finally
    {
        DeleteDirectory(outputRoot);
    }
}

static async Task<bool> InvalidMapFailsAsync(string repoRoot, Action<JsonObject> mutateMap, string expectedErrorFragment)
{
    var tempRoot = CreateTempDirectory("vs004-invalid-content");
    try
    {
        var tempContentRoot = Path.Combine(tempRoot, "content");
        CopyDirectory(Path.Combine(repoRoot, "content"), tempContentRoot);

        var mapPath = Path.Combine(tempContentRoot, "maps", "training-field-01", "map.json");
        var map = JsonNode.Parse(await File.ReadAllTextAsync(mapPath))?.AsObject()
            ?? throw new InvalidOperationException("Unable to parse copied map fixture.");
        mutateMap(map);
        await File.WriteAllTextAsync(mapPath, map.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

        var result = await ContentBuilder.RunAsync(new ContentBuilderOptions(tempContentRoot, Path.Combine(tempRoot, "artifacts"), WriteArtifacts: false));
        var containsExpectedError = result.Errors.Any(error => error.Contains(expectedErrorFragment, StringComparison.OrdinalIgnoreCase));
        if (result.Success || !containsExpectedError)
        {
            Console.Error.WriteLine($"Invalid fixture did not fail as expected. Expected fragment: '{expectedErrorFragment}'.");
            foreach (var error in result.Errors)
            {
                Console.Error.WriteLine($"  {error}");
            }
        }

        return !result.Success && containsExpectedError;
    }
    finally
    {
        DeleteDirectory(tempRoot);
    }
}

static void RemoveSafeSpawn(JsonObject map)
{
    var regions = map["regions"]?.AsArray() ?? throw new InvalidOperationException("Map regions are missing.");
    for (var index = regions.Count - 1; index >= 0; index--)
    {
        if (string.Equals(regions[index]?["kind"]?.GetValue<string>(), "safeSpawn", StringComparison.Ordinal))
        {
            regions.RemoveAt(index);
        }
    }

    map["safeSpawns"] = new JsonArray();
}

static void InvalidateBounds(JsonObject map)
{
    var bounds = map["bounds"]?.AsObject() ?? throw new InvalidOperationException("Map bounds are missing.");
    bounds["width"] = 0;
}

static void MoveSpawnOutsideMap(JsonObject map)
{
    var spawns = map["spawns"]?.AsArray() ?? throw new InvalidOperationException("Map spawns are missing.");
    var spawn = spawns[0]?.AsObject() ?? throw new InvalidOperationException("Expected at least one spawn.");
    spawn["x"] = 999;
}

static void DuplicateBlockedCell(JsonObject map)
{
    var blockedCells = map["blockedCells"]?.AsArray() ?? throw new InvalidOperationException("Map blockedCells are missing.");
    blockedCells.Add(new JsonObject
    {
        ["x"] = 18,
        ["y"] = 18
    });
}

static string FindRepoRoot()
{
    var current = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "content", "maps", "training-field-01", "map.json")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new DirectoryNotFoundException("Unable to locate repository root from the current directory.");
}

static string CreateTempDirectory(string name)
{
    var path = Path.Combine(Path.GetTempPath(), "divinity", name, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static void CopyDirectory(string sourceDirectory, string targetDirectory)
{
    Directory.CreateDirectory(targetDirectory);
    foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
    {
        Directory.CreateDirectory(directory.Replace(sourceDirectory, targetDirectory, StringComparison.Ordinal));
    }

    foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
    {
        File.Copy(file, file.Replace(sourceDirectory, targetDirectory, StringComparison.Ordinal), overwrite: true);
    }
}

static void DeleteDirectory(string path)
{
    if (Directory.Exists(path))
    {
        Directory.Delete(path, recursive: true);
    }
}

static ContentCheck Check(string name, bool passed) => new(name, passed);

static async Task<ContentCheck> CheckAsync(string name, Func<Task<bool>> check)
{
    try
    {
        return new ContentCheck(name, await check());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
        return new ContentCheck(name, false);
    }
}

internal readonly record struct ContentCheck(string Name, bool Passed);
