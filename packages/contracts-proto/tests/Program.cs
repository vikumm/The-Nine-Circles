using System.Net;
using Divinity.Contracts.V1;
using Divinity.ContractsProto;
using Divinity.ContractsProto.GameTickets;
using Divinity.GameGateway.Protocol;
using Divinity.WorldRuntime;
using Google.Protobuf;

var checks = new List<ProtocolCheck>();

AddCheck(checks, "contracts compile for world runtime", () => WorldRuntimeInfo.UsesContractsProto);
AddCheck(checks, "client envelope round-trips", ClientEnvelopeRoundTrips);
AddCheck(checks, "server envelope round-trips", ServerEnvelopeRoundTrips);
AddCheck(checks, "invalid protocol version rejected", InvalidProtocolVersionIsRejected);
AddCheck(checks, "payload over 64 KiB rejected", OversizedPayloadIsRejected);
AddCheck(checks, "truncated payload rejected", TruncatedPayloadIsRejected);
AddCheck(checks, "unknown payload type rejected", UnknownPayloadTypeIsRejected);
AddCheck(checks, "move intent contract stays intent-only", MoveIntentContractIsIntentOnly);
AddCheck(checks, "attack intent contract stays intent-only", AttackIntentContractIsIntentOnly);
AddCheck(checks, "cast intent contract stays intent-only", CastIntentContractIsIntentOnly);
AddCheck(checks, "skill state changed contract exists", SkillStateChangedContractExists);
AddCheck(checks, "character progressed contract exists", CharacterProgressedContractExists);
AddCheck(checks, "combat event carries server kill id", CombatEventKillIdExists);
AddCheck(checks, "equipment item carries server durability", EquipmentDurabilityContractExists);
AddCheck(checks, "reward grant contract is server-authored", RewardGrantContractExists);
AddCheck(checks, "inventory intents stay intent-only", InventoryIntentsStayIntentOnly);
AddCheck(checks, "reconnect contract rotates server token", ReconnectContractRotatesServerToken);
await AddCheckAsync(checks, "client to gateway ClientHello handler smoke", ClientHelloSmokeAsync);

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length > 0)
{
    Console.Error.WriteLine($"Protocol tests failed: {failures.Length} check(s) failed.");
    return 1;
}

Console.WriteLine("VS-003 protocol tests passed.");
return 0;

static bool ClientEnvelopeRoundTrips()
{
    var envelope = CreateClientHelloEnvelope();
    var parsed = ClientEnvelope.Parser.ParseFrom(envelope.ToByteArray());

    return parsed.ProtocolVersion == ProtocolConstants.SupportedProtocolVersion
        && parsed.Sequence == 42
        && parsed.ClientTick == 1234
        && parsed.PayloadCase == ClientEnvelope.PayloadOneofCase.ClientHello
        && parsed.ClientHello.BuildId == "vs003-smoke"
        && parsed.ClientHello.GameTicket == "ticket-for-contract-test";
}

static bool ServerEnvelopeRoundTrips()
{
    var envelope = new ServerEnvelope
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        ServerTick = 9,
        AckSequence = 42,
        ServerError = new ServerError
        {
            Code = ErrorCode.ClientHelloAcceptedNoSession,
            Message = "VS-003 controlled response",
            CorrelationId = "test-correlation"
        }
    };

    var parsed = ServerEnvelope.Parser.ParseFrom(envelope.ToByteArray());

    return parsed.ProtocolVersion == ProtocolConstants.SupportedProtocolVersion
        && parsed.ServerTick == 9
        && parsed.AckSequence == 42
        && parsed.PayloadCase == ServerEnvelope.PayloadOneofCase.ServerError
        && parsed.ServerError.Code == ErrorCode.ClientHelloAcceptedNoSession;
}

static bool InvalidProtocolVersionIsRejected()
{
    var envelope = CreateClientHelloEnvelope();
    envelope.ProtocolVersion = ProtocolConstants.SupportedProtocolVersion + 1;

    var result = ProtocolV1Handler.HandleClientEnvelope(envelope.ToByteArray());

    return result.StatusCode == HttpStatusCode.BadRequest
        && result.Envelope.ServerError.Code == ErrorCode.UnsupportedProtocolVersion;
}

static bool TruncatedPayloadIsRejected()
{
    var payload = CreateClientHelloEnvelope().ToByteArray();
    var truncated = payload.Take(payload.Length - 1).ToArray();

    var result = ProtocolV1Handler.HandleClientEnvelope(truncated);

    return result.StatusCode == HttpStatusCode.BadRequest
        && result.Envelope.ServerError.Code == ErrorCode.MalformedPayload;
}

static bool OversizedPayloadIsRejected()
{
    var payload = new byte[ProtocolConstants.MaxEnvelopeBytes + 1];

    var result = ProtocolV1Handler.HandleClientEnvelope(payload);

    return result.StatusCode == HttpStatusCode.RequestEntityTooLarge
        && result.Envelope.ServerError.Code == ErrorCode.PayloadTooLarge;
}

static bool UnknownPayloadTypeIsRejected()
{
    var envelope = new ClientEnvelope
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        Sequence = 99,
        ClientTick = 100
    };

    var result = ProtocolV1Handler.HandleClientEnvelope(envelope.ToByteArray());

    return result.StatusCode == HttpStatusCode.BadRequest
        && result.Envelope.ServerError.Code == ErrorCode.UnknownPayloadType;
}

static bool MoveIntentContractIsIntentOnly()
{
    var envelope = new ClientEnvelope
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        Sequence = 77,
        ClientTick = 8800,
        MoveIntent = new MoveIntent
        {
            Mode = MovementMode.Direction,
            DirectionX = 1,
            DirectionY = 1
        }
    };

    var parsed = ClientEnvelope.Parser.ParseFrom(envelope.ToByteArray());

    return parsed.PayloadCase == ClientEnvelope.PayloadOneofCase.MoveIntent
        && parsed.MoveIntent.Mode == MovementMode.Direction
        && parsed.MoveIntent.DirectionX == 1
        && parsed.MoveIntent.DirectionY == 1
        && ErrorCode.MoveRejected == (ErrorCode)23
        && typeof(MoveIntent).GetProperty("Position") is null
        && typeof(MoveIntent).GetProperty("FinalPosition") is null
        && typeof(MoveIntent).GetProperty("Speed") is null;
}

static bool AttackIntentContractIsIntentOnly()
{
    var envelope = new ClientEnvelope
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        Sequence = 78,
        ClientTick = 8810,
        AttackIntent = new AttackIntent
        {
            TargetEntityId = "monster:moss-slime-spawn-01",
            SkillId = "knight_basic_slash",
            ActionId = "action-vs013"
        }
    };

    var parsed = ClientEnvelope.Parser.ParseFrom(envelope.ToByteArray());

    return parsed.PayloadCase == ClientEnvelope.PayloadOneofCase.AttackIntent
        && parsed.AttackIntent.TargetEntityId == "monster:moss-slime-spawn-01"
        && parsed.AttackIntent.SkillId == "knight_basic_slash"
        && ErrorCode.AttackRejected == (ErrorCode)24
        && typeof(AttackIntent).GetProperty("Damage") is null
        && typeof(AttackIntent).GetProperty("Critical") is null
        && typeof(AttackIntent).GetProperty("TargetHp") is null
        && typeof(AttackIntent).GetProperty("CooldownComplete") is null;
}

static bool SkillStateChangedContractExists()
{
    var envelope = new ServerEnvelope
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        AckSequence = 78,
        SkillStateChanged = new SkillStateChanged
        {
            CharacterId = "character-vs013",
            SkillId = "knight_basic_slash",
            CooldownStartedServerMs = 1000,
            CooldownEndsServerMs = 1800,
            Available = false
        }
    };

    var parsed = ServerEnvelope.Parser.ParseFrom(envelope.ToByteArray());

    return parsed.PayloadCase == ServerEnvelope.PayloadOneofCase.SkillStateChanged
        && parsed.SkillStateChanged.SkillId == "knight_basic_slash"
        && parsed.SkillStateChanged.CooldownEndsServerMs == 1800
        && !parsed.SkillStateChanged.Available;
}

static bool CastIntentContractIsIntentOnly()
{
    var envelope = new ClientEnvelope
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        Sequence = 79,
        ClientTick = 8820,
        CastIntent = new CastIntent
        {
            SkillId = "knight_shield_bash_r1",
            TargetEntityId = "monster:moss-slime-spawn-01",
            ActionId = "action-vs014"
        }
    };

    var parsed = ClientEnvelope.Parser.ParseFrom(envelope.ToByteArray());

    return parsed.PayloadCase == ClientEnvelope.PayloadOneofCase.CastIntent
        && parsed.CastIntent.SkillId == "knight_shield_bash_r1"
        && parsed.CastIntent.TargetEntityId == "monster:moss-slime-spawn-01"
        && ErrorCode.CastRejected == (ErrorCode)25
        && typeof(CastIntent).GetProperty("Damage") is null
        && typeof(CastIntent).GetProperty("StunDuration") is null
        && typeof(CastIntent).GetProperty("SkillXp") is null
        && typeof(CastIntent).GetProperty("SkillRank") is null
        && typeof(CastIntent).GetProperty("CooldownEndsServerMs") is null
        && typeof(CastIntent).GetProperty("Mp") is null;
}

static bool CharacterProgressedContractExists()
{
    var envelope = new ServerEnvelope
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        AckSequence = 79,
        CharacterProgressed = new CharacterProgressed
        {
            CharacterId = "character-vs014",
            SkillId = "knight_shield_bash_r1",
            SkillXp = 20,
            SkillRank = 2,
            MaxRank = 2
        }
    };

    var parsed = ServerEnvelope.Parser.ParseFrom(envelope.ToByteArray());

    return parsed.PayloadCase == ServerEnvelope.PayloadOneofCase.CharacterProgressed
        && parsed.CharacterProgressed.SkillId == "knight_shield_bash_r1"
        && parsed.CharacterProgressed.SkillXp == 20
        && parsed.CharacterProgressed.SkillRank == 2
        && parsed.CharacterProgressed.MaxRank == 2;
}

static bool CombatEventKillIdExists()
{
    var envelope = new ServerEnvelope
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        AckSequence = 80,
        CombatEvent = new CombatEvent
        {
            EventId = "combat:vs015",
            SourceEntityId = "character-vs015",
            TargetEntityId = "monster:moss-slime-spawn-01",
            SkillId = "knight_basic_slash",
            Result = CombatResult.Hit,
            Damage = 45,
            TargetHp = 0,
            KillId = "kill:1"
        }
    };

    var parsed = ServerEnvelope.Parser.ParseFrom(envelope.ToByteArray());

    return parsed.PayloadCase == ServerEnvelope.PayloadOneofCase.CombatEvent
        && parsed.CombatEvent.KillId == "kill:1"
        && typeof(AttackIntent).GetProperty("KillId") is null
        && typeof(CastIntent).GetProperty("KillId") is null;
}

static bool EquipmentDurabilityContractExists()
{
    var envelope = new ServerEnvelope
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        AckSequence = 81,
        InventoryDelta = new InventoryDelta
        {
            InventoryVersion = 2,
            Equipment =
            {
                new EquipmentItem
                {
                    Slot = EquipmentSlot.OffHand,
                    ItemInstanceId = "item:shield:vs015",
                    Durability = 19,
                    MaxDurability = 20,
                    AttributesActive = true
                }
            }
        }
    };

    var parsed = ServerEnvelope.Parser.ParseFrom(envelope.ToByteArray());
    var equipment = parsed.InventoryDelta.Equipment.Single();

    return parsed.PayloadCase == ServerEnvelope.PayloadOneofCase.InventoryDelta
        && equipment.Durability == 19
        && equipment.MaxDurability == 20
        && equipment.AttributesActive
        && typeof(EquipItemIntent).GetProperty("Durability") is null
        && typeof(UnequipItemIntent).GetProperty("Durability") is null;
}

static bool RewardGrantContractExists()
{
    var envelope = new ServerEnvelope
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        AckSequence = 82,
        RewardGranted = new RewardGranted
        {
            RewardKey = "kill:1:character-vs016",
            CharacterId = "character-vs016",
            Xp = 20,
            SkillXp = 0,
            CurrencyDelta = 2,
            ItemInstanceIds = { "item:shield:vs016" }
        }
    };

    var parsed = ServerEnvelope.Parser.ParseFrom(envelope.ToByteArray());

    return parsed.PayloadCase == ServerEnvelope.PayloadOneofCase.RewardGranted
        && parsed.RewardGranted.RewardKey == "kill:1:character-vs016"
        && parsed.RewardGranted.CurrencyDelta == 2
        && parsed.RewardGranted.ItemInstanceIds.Single() == "item:shield:vs016"
        && typeof(AttackIntent).GetProperty("RewardKey") is null
        && typeof(CastIntent).GetProperty("RewardKey") is null;
}

static bool InventoryIntentsStayIntentOnly()
{
    var envelope = new ClientEnvelope
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        Sequence = 83,
        ClientTick = 8830,
        EquipItemIntent = new EquipItemIntent
        {
            ItemInstanceId = "item:shield:vs017",
            Slot = EquipmentSlot.OffHand,
            InventoryVersion = 7
        }
    };

    var parsed = ClientEnvelope.Parser.ParseFrom(envelope.ToByteArray());
    var unequip = new ClientEnvelope
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        Sequence = 84,
        ClientTick = 8840,
        UnequipItemIntent = new UnequipItemIntent
        {
            Slot = EquipmentSlot.OffHand,
            InventoryVersion = 8
        }
    };
    var parsedUnequip = ClientEnvelope.Parser.ParseFrom(unequip.ToByteArray());

    return parsed.PayloadCase == ClientEnvelope.PayloadOneofCase.EquipItemIntent
        && parsed.EquipItemIntent.ItemInstanceId == "item:shield:vs017"
        && parsed.EquipItemIntent.Slot == EquipmentSlot.OffHand
        && parsed.EquipItemIntent.InventoryVersion == 7
        && parsedUnequip.PayloadCase == ClientEnvelope.PayloadOneofCase.UnequipItemIntent
        && parsedUnequip.UnequipItemIntent.InventoryVersion == 8
        && ErrorCode.InventoryRejected == (ErrorCode)26
        && typeof(EquipItemIntent).GetProperty("OwnerCharacterId") is null
        && typeof(EquipItemIntent).GetProperty("Rarity") is null
        && typeof(EquipItemIntent).GetProperty("Defense") is null
        && typeof(EquipItemIntent).GetProperty("CurrencyBalance") is null
        && typeof(EquipItemIntent).GetProperty("BoundCharacterId") is null
        && typeof(UnequipItemIntent).GetProperty("ItemInstanceId") is null
        && typeof(UnequipItemIntent).GetProperty("Defense") is null
        && typeof(InventoryDelta).GetProperty("OwnerCharacterId") is null;
}

static bool ReconnectContractRotatesServerToken()
{
    var accepted = new ServerEnvelope
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        AckSequence = 85,
        JoinAccepted = new JoinAccepted
        {
            CharacterId = "character-vs018",
            MapId = "training-field-01",
            ChannelId = "gateway-local",
            Position = new Vector2 { X = 12, Y = 8 },
            Stats = new CharacterStats { Level = 1, Hp = 93, Mp = 20 },
            ContentHash = "content-hash",
            ReconnectToken = "rt_contract_token",
            ReconnectTtlSeconds = 30
        }
    };
    var parsedAccepted = ServerEnvelope.Parser.ParseFrom(accepted.ToByteArray());
    var request = new ClientEnvelope
    {
        ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
        Sequence = 86,
        ClientTick = 86,
        ReconnectRequest = new ReconnectRequest
        {
            ReconnectToken = parsedAccepted.JoinAccepted.ReconnectToken,
            PreviousConnectionId = "conn-previous"
        }
    };
    var parsedRequest = ClientEnvelope.Parser.ParseFrom(request.ToByteArray());

    return parsedAccepted.PayloadCase == ServerEnvelope.PayloadOneofCase.JoinAccepted
        && parsedAccepted.JoinAccepted.ReconnectToken == "rt_contract_token"
        && parsedAccepted.JoinAccepted.ReconnectTtlSeconds == 30
        && parsedAccepted.JoinAccepted.Stats.Hp == 93
        && parsedRequest.PayloadCase == ClientEnvelope.PayloadOneofCase.ReconnectRequest
        && parsedRequest.ReconnectRequest.PreviousConnectionId == "conn-previous"
        && ErrorCode.ReconnectRejected == (ErrorCode)27
        && typeof(ReconnectRequest).GetProperty("CharacterId") is null
        && typeof(ReconnectRequest).GetProperty("RewardKey") is null
        && typeof(ReconnectRequest).GetProperty("InventoryVersion") is null
        && typeof(ReconnectRequest).GetProperty("Hp") is null
        && typeof(JoinAccepted).GetProperty("RewardKey") is null;
}

static async Task<bool> ClientHelloSmokeAsync()
{
    var storePath = CreateTempDirectory("vs006-gateway-protocol-smoke");
    var previousStorePath = Environment.GetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH");
    Environment.SetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH", storePath);
    var nonce = "nonce-for-contract-test";

    try
    {
        var ticketService = new GameTicketService(new FileGameTicketStore(storePath));
        var issueResult = await ticketService.IssueAsync(
            new GameTicketIssueCommand("account-protocol-smoke", "vs003-smoke", ProtocolConstants.SupportedProtocolVersion, nonce),
            CancellationToken.None);

        if (!issueResult.Success)
        {
            return false;
        }

        var requestBytes = CreateClientHelloEnvelope(issueResult.GameTicket!, nonce).ToByteArray();
        var response = await ProtocolV1Handler.HandleClientEnvelopeAsync(requestBytes, ticketService, CancellationToken.None);
        var envelope = response.Envelope;

        return response.StatusCode == HttpStatusCode.OK
            && envelope.ProtocolVersion == ProtocolConstants.SupportedProtocolVersion
            && envelope.AckSequence == 42
            && envelope.PayloadCase == ServerEnvelope.PayloadOneofCase.ServerError
            && envelope.ServerError.Code == ErrorCode.ClientHelloAcceptedNoSession;
    }
    finally
    {
        Environment.SetEnvironmentVariable("DIVINITY_GAME_TICKET_STORE_PATH", previousStorePath);
        DeleteDirectory(storePath);
    }
}

static ClientEnvelope CreateClientHelloEnvelope(string gameTicket = "ticket-for-contract-test", string nonce = "nonce-for-contract-test") => new()
{
    ProtocolVersion = ProtocolConstants.SupportedProtocolVersion,
    Sequence = 42,
    ClientTick = 1234,
    ClientHello = new ClientHello
    {
        BuildId = "vs003-smoke",
        GameTicket = gameTicket,
        ClientNonce = nonce
    }
};

static string CreateTempDirectory(string name)
{
    var path = Path.Combine(Path.GetTempPath(), "divinity", name, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static void DeleteDirectory(string path)
{
    if (Directory.Exists(path))
    {
        Directory.Delete(path, recursive: true);
    }
}

static void AddCheck(ICollection<ProtocolCheck> checks, string name, Func<bool> check)
{
    Console.WriteLine($"RUN {name}");
    ProtocolCheck result;
    try
    {
        result = Check(name, check());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
        result = Check(name, false);
    }

    checks.Add(result);
    Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL")} {result.Name}");
}

static async Task AddCheckAsync(ICollection<ProtocolCheck> checks, string name, Func<Task<bool>> check)
{
    Console.WriteLine($"RUN {name}");
    var result = await CheckAsync(name, check());
    checks.Add(result);
    Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL")} {result.Name}");
}

static ProtocolCheck Check(string name, bool passed) => new(name, passed);

static async Task<ProtocolCheck> CheckAsync(string name, Task<bool> check)
{
    try
    {
        return new ProtocolCheck(name, await check);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
        return new ProtocolCheck(name, false);
    }
}

internal readonly record struct ProtocolCheck(string Name, bool Passed);
