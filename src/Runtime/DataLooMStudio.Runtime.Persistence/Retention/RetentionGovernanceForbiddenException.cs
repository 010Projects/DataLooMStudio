namespace DataLooMStudio.Runtime.Persistence.Retention;

public sealed class RetentionGovernanceForbiddenException : Exception
{
    public RetentionGovernanceForbiddenException()
        : base("Retention governance operation is not authorized.")
    {
    }
}