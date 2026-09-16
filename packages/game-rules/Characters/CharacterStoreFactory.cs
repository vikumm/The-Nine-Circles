namespace Divinity.GameRules.Characters;

public static class CharacterStoreFactory
{
    public static ICharacterStore CreateFromEnvironment() => FileCharacterStore.FromEnvironment();
}
