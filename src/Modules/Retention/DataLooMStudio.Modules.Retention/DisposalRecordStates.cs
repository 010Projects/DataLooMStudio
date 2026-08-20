namespace DataLooMStudio.Modules.Retention;

public static class DisposalRecordStates
{
    public const string Requested = "Requested";

    public const string Approved = "Approved";

    public const string Queued = "Queued";

    public const string Executing = "Executing";

    public const string StorageDisposed = "StorageDisposed";

    public const string Reconciled = "Reconciled";

    public const string Completed = "Completed";

    public const string Denied = "Denied";

    public const string Failed = "Failed";

    public const string Suspended = "Suspended";

    public const string Cancelled = "Cancelled";

    public static bool IsSupported(string state)
    {
        return state.Equals(Requested, StringComparison.Ordinal)
            || state.Equals(Approved, StringComparison.Ordinal)
            || state.Equals(Queued, StringComparison.Ordinal)
            || state.Equals(Executing, StringComparison.Ordinal)
            || state.Equals(StorageDisposed, StringComparison.Ordinal)
            || state.Equals(Reconciled, StringComparison.Ordinal)
            || state.Equals(Completed, StringComparison.Ordinal)
            || state.Equals(Denied, StringComparison.Ordinal)
            || state.Equals(Failed, StringComparison.Ordinal)
            || state.Equals(Suspended, StringComparison.Ordinal)
            || state.Equals(Cancelled, StringComparison.Ordinal);
    }
}