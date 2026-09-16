namespace Divinity.GameRules.Characters;

public sealed class CharacterService
{
    private readonly ICharacterStore _store;
    private readonly TimeProvider _timeProvider;

    public CharacterService(ICharacterStore store, TimeProvider? timeProvider = null)
    {
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CharacterServiceResult> CreateKnightAsync(CreateKnightCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.AccountId))
        {
            return new CharacterServiceResult(CharacterServiceStatus.NotAuthenticated, null, "Authenticated account is required to create a Knight.");
        }

        var name = CharacterName.Normalize(command.Name);
        if (!name.Success)
        {
            return new CharacterServiceResult(CharacterServiceStatus.InvalidName, null, name.Message);
        }

        var result = await _store.CreateKnightAsync(
            command.AccountId.Trim(),
            name.DisplayName!,
            name.NormalizedName!,
            _timeProvider.GetUtcNow(),
            cancellationToken);

        return new CharacterServiceResult(result.Status, result.Character, result.Message);
    }

    public async Task<CharacterServiceResult> SelectKnightAsync(string accountId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return new CharacterServiceResult(CharacterServiceStatus.NotAuthenticated, null, "Authenticated account is required to select a Knight.");
        }

        var character = await _store.GetByAccountAsync(accountId.Trim(), cancellationToken);
        return character is null
            ? new CharacterServiceResult(CharacterServiceStatus.NotFound, null, "Account has no VS-008 Knight.")
            : new CharacterServiceResult(CharacterServiceStatus.Selected, character, "Knight selected.");
    }

    public async Task<CharacterServiceResult> GetOwnedCharacterAsync(string accountId, string characterId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return new CharacterServiceResult(CharacterServiceStatus.NotAuthenticated, null, "Authenticated account is required to join world.");
        }

        if (string.IsNullOrWhiteSpace(characterId))
        {
            return new CharacterServiceResult(CharacterServiceStatus.NotFound, null, "JoinWorld requires character_id.");
        }

        var character = await _store.GetByIdAsync(characterId.Trim(), cancellationToken);
        if (character is null)
        {
            return new CharacterServiceResult(CharacterServiceStatus.NotFound, null, "Character does not exist.");
        }

        return string.Equals(character.AccountId, accountId.Trim(), StringComparison.Ordinal)
            ? new CharacterServiceResult(CharacterServiceStatus.Selected, character, "Character ownership verified.")
            : new CharacterServiceResult(CharacterServiceStatus.NotOwned, null, "character_id is not owned by the authenticated account.");
    }
}
