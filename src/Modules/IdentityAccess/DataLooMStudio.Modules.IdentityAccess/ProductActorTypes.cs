namespace DataLooMStudio.Modules.IdentityAccess;

public static class ProductActorTypes
{
    public const string Human = "Human";

    public const string Workload = "Workload";

    public const string Support = "Support";

    public const string Emergency = "Emergency";

    public static bool IsSupported(string actorType)
    {
        return actorType.Equals(Human, StringComparison.Ordinal)
            || actorType.Equals(Workload, StringComparison.Ordinal)
            || actorType.Equals(Support, StringComparison.Ordinal)
            || actorType.Equals(Emergency, StringComparison.Ordinal);
    }
}