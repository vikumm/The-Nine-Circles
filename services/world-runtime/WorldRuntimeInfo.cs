using Divinity.Contracts.V1;

namespace Divinity.WorldRuntime;

public static class WorldRuntimeInfo
{
    public const string ComponentName = "world-runtime";
    public const string Status = "VS-001 bootstrap only";
    public const bool ImplementsMovement = false;
    public const bool ImplementsCombat = false;
    public static bool UsesContractsProto => GameReflection.Descriptor.Package == "divinity.protocol.v1";
}
