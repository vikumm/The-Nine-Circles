using Divinity.Contracts.V1;

namespace Divinity.WorldRuntime.Map;

public sealed class WorldJoinMapValidator
{
    private readonly WorldMapCatalog _catalog;

    public WorldJoinMapValidator(WorldMapCatalog catalog)
    {
        _catalog = catalog;
    }

    public WorldJoinMapValidationResult Validate(WorldJoinMapRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.RequestedMapId)
            && !string.Equals(request.RequestedMapId, _catalog.Map.MapId, StringComparison.Ordinal)
            && !string.Equals(request.RequestedMapId, _catalog.Map.StableId, StringComparison.Ordinal))
        {
            return WorldJoinMapValidationResult.Rejected(
                WorldJoinMapStatus.MapMismatch,
                ErrorCode.MalformedPayload,
                "JoinWorld requested_map_id does not match the authoritative map.");
        }

        if (!string.Equals(request.ContentHash, _catalog.ContentHash, StringComparison.Ordinal))
        {
            return WorldJoinMapValidationResult.Rejected(
                WorldJoinMapStatus.ContentHashMismatch,
                ErrorCode.MalformedPayload,
                "JoinWorld content_hash does not match the authoritative server map artifact.");
        }

        var safeSpawn = _catalog.PrimarySafeSpawn;
        if (safeSpawn is null)
        {
            return WorldJoinMapValidationResult.Rejected(
                WorldJoinMapStatus.MissingSafeSpawn,
                ErrorCode.MalformedPayload,
                "Authoritative map artifact has no safe spawn.");
        }

        if (!_catalog.IsInsideBounds(safeSpawn.X, safeSpawn.Y))
        {
            return WorldJoinMapValidationResult.Rejected(
                WorldJoinMapStatus.SpawnOutOfBounds,
                ErrorCode.MalformedPayload,
                "Authoritative safe spawn is outside map bounds.");
        }

        if (_catalog.IsBlocked(safeSpawn.X, safeSpawn.Y))
        {
            return WorldJoinMapValidationResult.Rejected(
                WorldJoinMapStatus.SpawnBlocked,
                ErrorCode.MalformedPayload,
                "Authoritative safe spawn is on a blocked cell.");
        }

        if (!_catalog.IsInsideSafeSpawn(safeSpawn.X, safeSpawn.Y))
        {
            return WorldJoinMapValidationResult.Rejected(
                WorldJoinMapStatus.SpawnOutOfSafeRegion,
                ErrorCode.MalformedPayload,
                "Authoritative safe spawn is outside the safe spawn region.");
        }

        return WorldJoinMapValidationResult.Accepted(
            _catalog.Map.MapId,
            _catalog.Map.StableId,
            _catalog.ContentHash,
            safeSpawn.X,
            safeSpawn.Y);
    }
}

public sealed record WorldJoinMapRequest(string RequestedMapId, string ContentHash);

public sealed record WorldJoinMapValidationResult(
    WorldJoinMapStatus Status,
    ErrorCode ProtocolErrorCode,
    string Message,
    string MapId,
    string StableMapId,
    string ContentHash,
    int SpawnX,
    int SpawnY)
{
    public bool Success => Status == WorldJoinMapStatus.Accepted;

    public static WorldJoinMapValidationResult Accepted(
        string mapId,
        string stableMapId,
        string contentHash,
        int spawnX,
        int spawnY) =>
        new(
            WorldJoinMapStatus.Accepted,
            ErrorCode.Unspecified,
            "JoinWorld map content accepted.",
            mapId,
            stableMapId,
            contentHash,
            spawnX,
            spawnY);

    public static WorldJoinMapValidationResult Rejected(WorldJoinMapStatus status, ErrorCode protocolErrorCode, string message) =>
        new(status, protocolErrorCode, message, string.Empty, string.Empty, string.Empty, 0, 0);
}

public enum WorldJoinMapStatus
{
    Accepted,
    MapMismatch,
    ContentHashMismatch,
    MissingSafeSpawn,
    SpawnOutOfBounds,
    SpawnBlocked,
    SpawnOutOfSafeRegion
}
