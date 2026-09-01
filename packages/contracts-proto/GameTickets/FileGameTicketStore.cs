using System.Text.Json;

namespace Divinity.ContractsProto.GameTickets;

public sealed class FileGameTicketStore : IGameTicketStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _activeDirectory;
    private readonly string _consumedDirectory;

    public FileGameTicketStore(string rootDirectory)
    {
        _activeDirectory = Path.Combine(rootDirectory, "active");
        _consumedDirectory = Path.Combine(rootDirectory, "consumed");
        Directory.CreateDirectory(_activeDirectory);
        Directory.CreateDirectory(_consumedDirectory);
    }

    public static FileGameTicketStore FromEnvironment()
    {
        var configuredPath = Environment.GetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH");
        var root = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(Path.GetTempPath(), "divinity", "game-tickets")
            : configuredPath;

        return new FileGameTicketStore(root);
    }

    public async Task StoreAsync(string ticketHash, StoredGameTicket ticket, TimeSpan ttl, CancellationToken cancellationToken)
    {
        _ = ttl;
        EnsureHash(ticketHash);
        Directory.CreateDirectory(_activeDirectory);
        Directory.CreateDirectory(_consumedDirectory);

        var activePath = ActivePath(ticketHash);
        var tempPath = activePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var json = JsonSerializer.Serialize(ticket, JsonOptions);
        await File.WriteAllTextAsync(tempPath, json + Environment.NewLine, cancellationToken);

        try
        {
            File.Move(tempPath, activePath, overwrite: false);
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
    }

    public async Task<GameTicketStoreConsumeResult> ConsumeAsync(string ticketHash, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        EnsureHash(ticketHash);
        var activePath = ActivePath(ticketHash);
        var consumedPath = ConsumedPath(ticketHash);

        try
        {
            File.Move(activePath, consumedPath, overwrite: false);
        }
        catch (FileNotFoundException)
        {
            return File.Exists(consumedPath)
                ? new GameTicketStoreConsumeResult(GameTicketStoreConsumeStatus.Reused, await ReadTicketAsync(consumedPath, cancellationToken))
                : new GameTicketStoreConsumeResult(GameTicketStoreConsumeStatus.NotFound, null);
        }
        catch (IOException) when (File.Exists(consumedPath))
        {
            return new GameTicketStoreConsumeResult(GameTicketStoreConsumeStatus.Reused, await ReadTicketAsync(consumedPath, cancellationToken));
        }

        var ticket = await ReadTicketAsync(consumedPath, cancellationToken);
        if (ticket is null)
        {
            return new GameTicketStoreConsumeResult(GameTicketStoreConsumeStatus.NotFound, null);
        }

        if (ticket.ExpiresAtUtc <= nowUtc)
        {
            return new GameTicketStoreConsumeResult(GameTicketStoreConsumeStatus.Expired, ticket);
        }

        return new GameTicketStoreConsumeResult(GameTicketStoreConsumeStatus.Consumed, ticket);
    }

    private async Task<StoredGameTicket?> ReadTicketAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<StoredGameTicket>(json);
    }

    private string ActivePath(string ticketHash) => Path.Combine(_activeDirectory, ticketHash + ".json");

    private string ConsumedPath(string ticketHash) => Path.Combine(_consumedDirectory, ticketHash + ".json");

    private static void EnsureHash(string ticketHash)
    {
        if (ticketHash.Length != 64 || ticketHash.Any(character => character is not (>= 'a' and <= 'f' or >= '0' and <= '9')))
        {
            throw new ArgumentException("Ticket hash must be a lowercase SHA-256 hex string.", nameof(ticketHash));
        }
    }
}
