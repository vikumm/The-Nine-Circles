using Divinity.Contracts.V1;

namespace Divinity.WorldRuntime;

public static class WorldRuntimeInfo
{
    public const string ComponentName = "world-runtime";
    public const string Status = "VS-010 authoritative movement";
    public const bool UsesAuthoritativeMapArtifact = true;
    public const bool ImplementsMovement = true;
    public const bool ImplementsCombat = false;
    public static bool UsesContractsProto => GameReflection.Descriptor.Package == "divinity.protocol.v1";
}
