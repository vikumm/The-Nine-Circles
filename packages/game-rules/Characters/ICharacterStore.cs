namespace Divinity.GameRules.Characters;

public interface ICharacterStore
{
    Task<CharacterStoreMutationResult> CreateKnightAsync(
        string accountId,
        string displayName,
        string normalizedName,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<CharacterRecord?> GetByAccountAsync(string accountId, CancellationToken cancellationToken);

    Task<CharacterRecord?> GetByIdAsync(string characterId, CancellationToken cancellationToken);

    Task<CharacterCheckpointRecord?> GetCheckpointAsync(string characterId, CancellationToken cancellationToken);

    Task StoreCheckpointAsync(CharacterCheckpointRecord checkpoint, CancellationToken cancellationToken);
}
