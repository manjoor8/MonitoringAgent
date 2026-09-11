namespace IISMonitor.Core.Models;

public record IisLogEntry
{
    public DateTime TimestampUtc { get; init; }
    public string SiteName { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public string UriStem { get; init; } = string.Empty;
    public int StatusCode { get; init; }
    public long TimeTakenMs { get; init; }
    public string ClientIp { get; init; } = string.Empty;
}

public record IisLogIncidentAnalysis
{
    public int TotalRequests { get; init; }
    public double RequestsPerMinute { get; init; }
    public Dictionary<string, int> TopUrlsByCount { get; init; } = new();
    public Dictionary<string, double> TopUrlsByAvgTimeMs { get; init; } = new();
    public Dictionary<int, int> StatusDistribution { get; init; } = new();
    public double AvgResponseTimeMs { get; init; }
    public double MaxResponseTimeMs { get; init; }
}

public record WindowsEventEntry
{
    public DateTime TimestampUtc { get; init; }
    public string ProviderName { get; init; } = string.Empty;
    public int EventId { get; init; }
    public string Level { get; init; } = "Information";
    public string Message { get; init; } = string.Empty;
    public string LogName { get; init; } = "System";
}

public record ScheduledTaskStatus
{
    public string TaskName { get; init; } = string.Empty;
    public string TaskPath { get; init; } = string.Empty;
    public DateTime? LastRunTime { get; init; }
    public DateTime? NextRunTime { get; init; }
    public int LastTaskResult { get; init; }
    public string State { get; init; } = "Ready";
    public bool IsRunning => string.Equals(State, "Running", StringComparison.OrdinalIgnoreCase);
}

public record DotNetRuntimeMetricEntry
{
    public string MetricName { get; init; } = string.Empty;
    public double MetricValue { get; init; }
    public string Source { get; init; } = ".NET CLR Memory";
    public bool IsAvailable { get; init; } = true;
}
