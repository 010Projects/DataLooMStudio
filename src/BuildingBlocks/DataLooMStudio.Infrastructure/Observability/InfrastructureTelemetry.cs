using System.Diagnostics.Metrics;

using DataLooMStudio.Infrastructure.SecurityScanning;

namespace DataLooMStudio.Infrastructure.Observability;

public static class InfrastructureTelemetry
{
    public const string MeterName = "DataLooMStudio.Infrastructure";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> DependencyFailures = Meter.CreateCounter<long>("dls.dependencies.failures");
    private static readonly Counter<long> MalwareScansCompleted = Meter.CreateCounter<long>("dls.malware.scan.completed");
    private static readonly Counter<long> MalwareScanFailures = Meter.CreateCounter<long>("dls.malware.scan.failures");
    private static readonly Counter<long> EvidenceQuarantined = Meter.CreateCounter<long>("dls.evidence.quarantined");

    public static void RecordDependencyFailure(string dependency, string operation)
    {
        DependencyFailures.Add(
            1,
            new KeyValuePair<string, object?>("dependency", dependency),
            new KeyValuePair<string, object?>("operation", operation));
    }

    public static void RecordMalwareScan(EvidenceMalwareScanOutcome outcome)
    {
        MalwareScansCompleted.Add(1, new KeyValuePair<string, object?>("outcome", outcome.ToString()));
        if (outcome != EvidenceMalwareScanOutcome.Clean)
        {
            MalwareScanFailures.Add(1, new KeyValuePair<string, object?>("outcome", outcome.ToString()));
        }
    }

    public static void RecordEvidenceQuarantine(string reason)
    {
        EvidenceQuarantined.Add(1, new KeyValuePair<string, object?>("reason", reason));
    }
}