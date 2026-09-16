using System.Text.Json;

namespace Divinity.GameRules.Characters;

public sealed class FileCharacterStore : ICharacterStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _rootDirectory;
    private readonly string _dataPath;
    private readonly string _lockPath;

    public FileCharacterStore(string rootDirectory)
    {
        _rootDirectory = rootDirectory;
        _dataPath = Path.Combine(_rootDirectory, "characters-vs008.json");
        _lockPath = Path.Combine(_rootDirectory, "characters-vs008.lock");
        Directory.CreateDirectory(_rootDirectory);
    }

    public static FileCharacterStore FromEnvironment()
    {
        var configuredPath = Environment.GetEnvironmentVariable("DIVINITY_CHARACTER_STORE_PATH");
        var root = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(Path.GetTempPath(), "divinity", "characters")
            : configuredPath;

        return new FileCharacterStore(root);
    }

    public Task<CharacterStoreMutationResult> CreateKnightAsync(
        string accountId,
        string displayName,
        string normalizedName,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        WithDocumentLockAsync(document =>
        {
            TouchAccount(document, accountId, nowUtc);

            if (document.Characters.Any(character => string.Equals(character.AccountId, accountId, StringComparison.Ordinal)))
            {
                return new CharacterStoreMutationResult(CharacterServiceStatus.SlotOccupied, null, "Account already owns the VS-008 character slot.");
            }

            if (document.Characters.Any(character => string.Equals(character.NormalizedName, normalizedName, StringComparison.Ordinal)))
            {
                return new CharacterStoreMutationResult(CharacterServiceStatus.DuplicateName, null, "Character name is already reserved.");
            }

            var stats = KnightCatalog.LevelOneStats;
            var character = new CharacterRecord(
                "char_" + Guid.NewGuid().ToString("N"),
                accountId,
                displayName,
                normalizedName,
                KnightCatalog.Vocation,
                KnightCatalog.CatalogVersion,
                stats,
                KnightCatalog.MapId,
                KnightCatalog.ChannelId,
                KnightCatalog.ContentHash,
                KnightCatalog.SafeSpawn,
                nowUtc,
                nowUtc);

            document.Characters.Add(character);
            return new CharacterStoreMutationResult(CharacterServiceStatus.Created, character, "Knight created.");
        }, cancellationToken);

    public Task<CharacterRecord?> GetByAccountAsync(string accountId, CancellationToken cancellationToken) =>
        WithDocumentLockAsync(document =>
        {
            TouchAccount(document, accountId, DateTimeOffset.UtcNow);
            return document.Characters.FirstOrDefault(character => string.Equals(character.AccountId, accountId, StringComparison.Ordinal));
        }, cancellationToken);

    public Task<CharacterRecord?> GetByIdAsync(string characterId, CancellationToken cancellationToken) =>
        WithDocumentLockAsync(
            document => document.Characters.FirstOrDefault(character => string.Equals(character.CharacterId, characterId, StringComparison.Ordinal)),
            cancellationToken);

    public Task<CharacterCheckpointRecord?> GetCheckpointAsync(string characterId, CancellationToken cancellationToken) =>
        WithDocumentLockAsync(
            document => document.CharacterCheckpoints.FirstOrDefault(checkpoint => string.Equals(checkpoint.CharacterId, characterId, StringComparison.Ordinal)),
            cancellationToken);

    public Task StoreCheckpointAsync(CharacterCheckpointRecord checkpoint, CancellationToken cancellationToken) =>
        WithDocumentLockAsync(document =>
        {
            var index = document.CharacterCheckpoints.FindIndex(existing => string.Equals(existing.CharacterId, checkpoint.CharacterId, StringComparison.Ordinal));
            if (index >= 0)
            {
                document.CharacterCheckpoints[index] = checkpoint;
            }
            else
            {
                document.CharacterCheckpoints.Add(checkpoint);
            }

            return true;
        }, cancellationToken);

    private async Task<T> WithDocumentLockAsync<T>(Func<CharacterStoreDocument, T> operation, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_rootDirectory);

        await using var lockStream = await OpenLockAsync(cancellationToken);
        var document = await ReadDocumentAsync(cancellationToken);
        var result = operation(document);
        await WriteDocumentAsync(document, cancellationToken);
        return result;
    }

    private async Task<FileStream> OpenLockAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 99)
            {
                await Task.Delay(10, cancellationToken);
            }
        }

        return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private async Task<CharacterStoreDocument> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_dataPath))
        {
            return EmptyDocument();
        }

        await using var stream = new FileStream(_dataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var document = await JsonSerializer.DeserializeAsync<CharacterStoreDocument>(stream, JsonOptions, cancellationToken)
            ?? EmptyDocument();

        return NormalizeDocument(document);
    }

    private async Task WriteDocumentAsync(CharacterStoreDocument document, CancellationToken cancellationToken)
    {
        var tempPath = _dataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
            await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
        }

        File.Move(tempPath, _dataPath, overwrite: true);
    }

    private static CharacterStoreDocument EmptyDocument() => new([], [], []);

    private static CharacterStoreDocument NormalizeDocument(CharacterStoreDocument document) =>
        new(document.Accounts ?? [], document.Characters ?? [], document.CharacterCheckpoints ?? []);

    private static void TouchAccount(CharacterStoreDocument document, string accountId, DateTimeOffset nowUtc)
    {
        var existing = document.Accounts.Find(account => string.Equals(account.AccountId, accountId, StringComparison.Ordinal));
        if (existing is null)
        {
            document.Accounts.Add(new AccountProjectionRecord(accountId, nowUtc, nowUtc));
            return;
        }

        var index = document.Accounts.IndexOf(existing);
        document.Accounts[index] = existing with { LastSeenAtUtc = nowUtc };
    }
}
