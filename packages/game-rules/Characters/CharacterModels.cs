namespace Divinity.GameRules.Characters;

public sealed record CharacterNameResult(
    bool Success,
    string? DisplayName,
    string? NormalizedName,
    string Message)
{
    public static CharacterNameResult Valid(string displayName, string normalizedName) =>
        new(true, displayName, normalizedName, "Name is valid.");

    public static CharacterNameResult Invalid(string message) =>
        new(false, null, null, message);
}

public sealed record CharacterPosition(decimal X, decimal Y);

public sealed record KnightStats(
    int Level,
    int MaxHp,
    int MaxMp,
    int Attack,
    int Defense,
    decimal CriticalChancePercent,
    decimal CriticalMultiplier,
    decimal SpeedUnitsPerSecond,
    int HpRegenPerSecond,
    int MpRegenPerSecond);

public sealed record CharacterRecord(
    string CharacterId,
    string AccountId,
    string DisplayName,
    string NormalizedName,
    string Vocation,
    string CatalogVersion,
    KnightStats Stats,
    string MapId,
    string ChannelId,
    string ContentHash,
    CharacterPosition SafeSpawn,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateKnightCommand(string AccountId, string Name);

public sealed record CharacterServiceResult(
    CharacterServiceStatus Status,
    CharacterRecord? Character,
    string Message)
{
    public bool Success => Status is CharacterServiceStatus.Created or CharacterServiceStatus.Selected;
}

public enum CharacterServiceStatus
{
    Created,
    Selected,
    NotAuthenticated,
    InvalidName,
    DuplicateName,
    SlotOccupied,
    NotFound,
    NotOwned
}

public sealed record CharacterStoreMutationResult(
    CharacterServiceStatus Status,
    CharacterRecord? Character,
    string Message);

public sealed record CharacterStoreDocument(
    List<AccountProjectionRecord> Accounts,
    List<CharacterRecord> Characters,
    List<CharacterCheckpointRecord> CharacterCheckpoints);

public sealed record AccountProjectionRecord(
    string AccountId,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc);

public sealed record CharacterCheckpointRecord(
    string CharacterId,
    string MapId,
    CharacterPosition Position,
    ulong LastSequence,
    string Reason,
    DateTimeOffset RecordedAtUtc);
