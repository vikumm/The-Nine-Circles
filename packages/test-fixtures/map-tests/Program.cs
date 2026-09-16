using System.Text.Json;
using System.Text.Json.Nodes;
using Divinity.ContentBuilder;
using Divinity.ContentSchema;
using Divinity.Contracts.V1;
using Divinity.WorldRuntime.Map;

var checks = new List<MapCheck>();
var repoRoot = FindRepoRoot();
var contentRoot = Path.Combine(repoRoot, "content");

checks.Add(Check("valid Training Field map validates", ValidTrainingFieldMapValidates(contentRoot)));
checks.Add(await CheckAsync("invalid spawn outside bounds is rejected", () => InvalidMapFailsAsync(repoRoot, MoveSpawnOutsideMap, "outside map bounds")));
checks.Add(await CheckAsync("missing safe spawn is rejected", () => InvalidMapFailsAsync(repoRoot, RemoveSafeSpawn, "safe spawn")));
checks.Add(await CheckAsync("inconsistent blocked collision wall is rejected", () => InvalidMapFailsAsync(repoRoot, RemoveCollisionWallBlockedCell, "collision wall")));
checks.Add(await CheckAsync("client and server artifacts share one content hash", () => ClientAndServerArtifactsShareHashAsync(contentRoot)));
checks.Add(await CheckAsync("JoinWorld with incompatible content hash is rejected", () => JoinWithIncompatibleHashRejectedAsync(contentRoot)));
checks.Add(await CheckAsync("authoritative safe spawn is inside safe region and unblocked", () => AuthoritativeSafeSpawnIsUsableAsync(contentRoot)));
checks.Add(await CheckAsync("Unity visual artifact loads Training Field map", () => UnityVisualArtifactLoadsAsync(repoRoot, contentRoot)));

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length > 0)
{
    Console.Error.WriteLine($"VS-009 map tests failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-009 map tests passed.");
return 0;

static bool ValidTrainingFieldMapValidates(string contentRoot)
{
    var loadResult = ContentSourceLoader.Load(contentRoot);
    if (!loadResult.Success || loadResult.Catalog is null)
    {
        foreach (var error in loadResult.Errors)
        {
            Console.Error.WriteLine(error);
        }

        return false;
    }

    var map = loadResult.Catalog.Map;
    return map.Bounds is { Width: 96, Height: 96 }
        && string.Equals(map.StableId, "map_training_field_01", StringComparison.Ordinal)
        && map.Regions.Any(region => region.Kind == RegionKind.SafeSpawn)
        && map.Regions.Any(region => region.Kind == RegionKind.MovementCorridor)
        && map.Regions.Any(region => region.Kind == RegionKind.CombatZone)
        && map.Regions.Any(region => region.Kind == RegionKind.LeashArea)
        && map.Regions.Any(region => region.Kind == RegionKind.CollisionWall)
        && map.Regions.Any(region => region.Kind == RegionKind.EquipmentPoint)
        && map.Triggers.Any(trigger => trigger.Kind == TriggerKind.EquipmentPoint);
}

static async Task<bool> ClientAndServerArtifactsShareHashAsync(string contentRoot)
{
    var outputRoot = CreateTempDirectory("vs009-hash");
    try
    {
        var result = await ContentBuilder.RunAsync(new ContentBuilderOptions(contentRoot, outputRoot, WriteArtifacts: true));
        if (!result.Success || result.ClientArtifactPath is null || result.ServerArtifactPath is null)
        {
            return false;
        }

        var client = await ReadJsonAsync<ClientContentArtifact>(result.ClientArtifactPath);
        var server = await ReadJsonAsync<ServerContentArtifact>(result.ServerArtifactPath);

        return string.Equals(client.ContentHash, server.ContentHash, StringComparison.Ordinal)
            && string.Equals(client.ContentHash, result.ContentHash, StringComparison.Ordinal)
            && client.Map.Chunks is { ChunkSize: 16, Columns: 6, Rows: 6 }
            && server.Chunks is { ChunkSize: 16, Columns: 6, Rows: 6 }
            && client.Map.Chunks.Chunks.Count == 36
            && server.Chunks.Chunks.Count == 36;
    }
    finally
    {
        DeleteDirectory(outputRoot);
    }
}

static async Task<bool> JoinWithIncompatibleHashRejectedAsync(string contentRoot)
{
    var outputRoot = CreateTempDirectory("vs009-join-hash");
    try
    {
        var result = await ContentBuilder.RunAsync(new ContentBuilderOptions(contentRoot, outputRoot, WriteArtifacts: true));
        if (!result.Success || result.ServerArtifactPath is null)
        {
            return false;
        }

        var server = await ReadJsonAsync<ServerContentArtifact>(result.ServerArtifactPath);
        var catalog = WorldMapCatalog.FromArtifact(server);
        var validator = new WorldJoinMapValidator(catalog);

        var rejected = validator.Validate(new WorldJoinMapRequest(server.Map.MapId, "client-hash-does-not-match"));
        var accepted = validator.Validate(new WorldJoinMapRequest(server.Map.MapId, server.ContentHash));

        return rejected.Status == WorldJoinMapStatus.ContentHashMismatch
            && rejected.ProtocolErrorCode == ErrorCode.MalformedPayload
            && accepted.Success
            && accepted.ContentHash == server.ContentHash;
    }
    finally
    {
        DeleteDirectory(outputRoot);
    }
}

static async Task<bool> AuthoritativeSafeSpawnIsUsableAsync(string contentRoot)
{
    var outputRoot = CreateTempDirectory("vs009-safe-spawn");
    try
    {
        var result = await ContentBuilder.RunAsync(new ContentBuilderOptions(contentRoot, outputRoot, WriteArtifacts: true));
        if (!result.Success || result.ServerArtifactPath is null)
        {
            return false;
        }

        var catalog = await WorldMapCatalog.LoadFromFileAsync(result.ServerArtifactPath);
        var validator = new WorldJoinMapValidator(catalog);
        var join = validator.Validate(new WorldJoinMapRequest(catalog.Map.MapId, catalog.ContentHash));

        return join.Success
            && catalog.IsInsideBounds(join.SpawnX, join.SpawnY)
            && catalog.IsInsideSafeSpawn(join.SpawnX, join.SpawnY)
            && !catalog.IsBlocked(join.SpawnX, join.SpawnY);
    }
    finally
    {
        DeleteDirectory(outputRoot);
    }
}

static async Task<bool> UnityVisualArtifactLoadsAsync(string repoRoot, string contentRoot)
{
    var unityArtifactPath = Path.Combine(
        repoRoot,
        "apps",
        "game-client-unity",
        "Assets",
        "StreamingAssets",
        "DivinityContent",
        "training-field-01.visual.json");

    if (!File.Exists(unityArtifactPath))
    {
        Console.Error.WriteLine($"Unity visual artifact is missing: {unityArtifactPath}");
        return false;
    }

    var sourceLoad = ContentSourceLoader.Load(contentRoot);
    var unityArtifact = await ReadJsonAsync<ClientContentArtifact>(unityArtifactPath);

    return sourceLoad.Success
        && sourceLoad.Catalog is not null
        && string.Equals(unityArtifact.ContentHash, sourceLoad.Catalog.ContentHash, StringComparison.Ordinal)
        && unityArtifact.Map.Bounds is { Width: 96, Height: 96 }
        && unityArtifact.Map.Chunks is { ChunkSize: 16, Columns: 6, Rows: 6 }
        && unityArtifact.Map.Regions.Any(region => region.Kind == RegionKind.CollisionWall);
}

static async Task<bool> InvalidMapFailsAsync(string repoRoot, Action<JsonObject> mutateMap, string expectedErrorFragment)
{
    var tempRoot = CreateTempDirectory("vs009-invalid-content");
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

static void MoveSpawnOutsideMap(JsonObject map)
{
    var spawns = map["spawns"]?.AsArray() ?? throw new InvalidOperationException("Map spawns are missing.");
    var spawn = spawns[0]?.AsObject() ?? throw new InvalidOperationException("Expected at least one spawn.");
    spawn["x"] = 999;
}

static void RemoveCollisionWallBlockedCell(JsonObject map)
{
    var blockedCells = map["blockedCells"]?.AsArray() ?? throw new InvalidOperationException("Map blockedCells are missing.");
    for (var index = 0; index < blockedCells.Count; index++)
    {
        var cell = blockedCells[index]?.AsObject();
        if (cell?["x"]?.GetValue<int>() == 32 && cell["y"]?.GetValue<int>() == 40)
        {
            blockedCells.RemoveAt(index);
            return;
        }
    }

    throw new InvalidOperationException("Expected collision wall blocked cell was not found.");
}

static async Task<T> ReadJsonAsync<T>(string path)
{
    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    return await JsonSerializer.DeserializeAsync<T>(stream, ContentJson.Options)
        ?? throw new InvalidOperationException($"Unable to deserialize {path}.");
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

static MapCheck Check(string name, bool passed) => new(name, passed);

static async Task<MapCheck> CheckAsync(string name, Func<Task<bool>> check)
{
    try
    {
        return new MapCheck(name, await check());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
        return new MapCheck(name, false);
    }
}

internal readonly record struct MapCheck(string Name, bool Passed);
