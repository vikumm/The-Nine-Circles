using System.Text.Json;
using Divinity.ContentSchema;

namespace Divinity.WorldRuntime.Map;

public sealed class WorldMapCatalog
{
    public const string DefaultArtifactRelativePath = "tools/content-builder/artifacts/server/training-field-01.authoritative.json";

    private readonly HashSet<(int X, int Y)> _blockedCells;

    private WorldMapCatalog(ServerContentArtifact artifact)
    {
        Artifact = artifact;
        _blockedCells = artifact.Map.BlockedCells
            .Select(cell => (cell.X, cell.Y))
            .ToHashSet();
    }

    public ServerContentArtifact Artifact { get; }
    public string ContentHash => Artifact.ContentHash;
    public MapDefinition Map => Artifact.Map;
    public SafeSpawn? PrimarySafeSpawn => Map.SafeSpawns.FirstOrDefault();

    public static WorldMapCatalog FromArtifact(ServerContentArtifact artifact) => new(artifact);

    public static async Task<WorldMapCatalog> LoadFromFileAsync(string artifactPath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(artifactPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var artifact = await JsonSerializer.DeserializeAsync<ServerContentArtifact>(stream, ContentJson.Options, cancellationToken)
            ?? throw new InvalidOperationException($"Server content artifact deserialized to null: {artifactPath}");

        return new WorldMapCatalog(artifact);
    }

    public static Task<WorldMapCatalog> LoadDefaultAsync(CancellationToken cancellationToken = default) =>
        LoadFromFileAsync(FindDefaultArtifactPath(), cancellationToken);

    public bool IsInsideBounds(int x, int y) =>
        x >= 0 && y >= 0 && x < Map.Bounds.Width && y < Map.Bounds.Height;

    public bool IsInsideBounds(double x, double y) =>
        x >= 0 && y >= 0 && x < Map.Bounds.Width && y < Map.Bounds.Height;

    public bool IsBlocked(int x, int y) => _blockedCells.Contains((x, y));

    public bool IsBlocked(double x, double y)
    {
        if (!IsInsideBounds(x, y))
        {
            return false;
        }

        return IsBlocked((int)Math.Floor(x), (int)Math.Floor(y));
    }

    public bool IsNavigable(double x, double y) =>
        IsInsideBounds(x, y) && !IsBlocked(x, y);

    public bool SegmentTouchesBlockedOrOutOfBounds(double startX, double startY, double endX, double endY)
    {
        var deltaX = endX - startX;
        var deltaY = endY - startY;
        var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        var steps = Math.Max(1, (int)Math.Ceiling(distance / 0.1d));

        for (var index = 0; index <= steps; index++)
        {
            var t = (double)index / steps;
            var x = startX + deltaX * t;
            var y = startY + deltaY * t;
            if (!IsInsideBounds(x, y) || IsBlocked(x, y))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsInsideSafeSpawn(int x, int y) =>
        Map.Regions.Any(region => region.Kind == RegionKind.SafeSpawn && Contains(region.Bounds, x, y));

    private static string FindDefaultArtifactPath()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, DefaultArtifactRelativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Unable to locate {DefaultArtifactRelativePath} from {Directory.GetCurrentDirectory()}.");
    }

    private static bool Contains(GridRect rect, int x, int y) =>
        x >= rect.X && y >= rect.Y && x < rect.X + rect.Width && y < rect.Y + rect.Height;
}
