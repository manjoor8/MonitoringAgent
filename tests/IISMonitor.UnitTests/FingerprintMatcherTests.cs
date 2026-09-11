using IISMonitor.Core.Fingerprinting;
using IISMonitor.Core.Models;
using Xunit;

namespace IISMonitor.UnitTests;

public class FingerprintMatcherTests
{
    [Fact]
    public void SameIncident_ReturnsNearPerfectSimilarity()
    {
        var fp1 = new IncidentFingerprint
        {
            PrimaryAppPool = "PatientPortalPool",
            PeakCpu = 98.0,
            CpuProfileBuckets = new[] { 0.4, 0.5, 0.7, 0.95, 0.98, 0.98, 0.95, 0.8, 0.6, 0.45 },
            MemoryDeltaMb = 400,
            GcActivityScore = 0.8,
            RequestRatePerSec = 250
        };

        var fp2 = new IncidentFingerprint
        {
            PrimaryAppPool = "PatientPortalPool",
            PeakCpu = 98.0,
            CpuProfileBuckets = new[] { 0.4, 0.5, 0.7, 0.95, 0.98, 0.98, 0.95, 0.8, 0.6, 0.45 },
            MemoryDeltaMb = 400,
            GcActivityScore = 0.8,
            RequestRatePerSec = 250
        };

        double sim = IncidentFingerprintMatcher.CalculateSimilarity(fp1, fp2);
        Assert.True(sim >= 0.99, $"Expected >= 0.99, got {sim}");
    }

    [Fact]
    public void DifferentAppPool_LowersSimilarityScore()
    {
        var buckets = new[] { 0.4, 0.5, 0.7, 0.95, 0.98, 0.98, 0.95, 0.8, 0.6, 0.45 };

        var fp1 = new IncidentFingerprint
        {
            PrimaryAppPool = "PatientPortalPool",
            CpuProfileBuckets = buckets,
            MemoryDeltaMb = 100,
            GcActivityScore = 0.5,
            RequestRatePerSec = 50
        };

        var fp2 = new IncidentFingerprint
        {
            PrimaryAppPool = "ReportingServicePool",
            CpuProfileBuckets = buckets,
            MemoryDeltaMb = 100,
            GcActivityScore = 0.5,
            RequestRatePerSec = 50
        };

        double sim = IncidentFingerprintMatcher.CalculateSimilarity(fp1, fp2);
        Assert.True(sim < 0.80, $"Expected < 0.80 due to app pool mismatch, got {sim}");
    }

    [Fact]
    public void ResampleCpuProfile_NormalizesCorrectly()
    {
        var samples = new List<double> { 40.0, 50.0, 70.0, 95.0, 100.0, 90.0, 60.0, 45.0 };
        var buckets = IncidentFingerprintMatcher.ResampleCpuProfile(samples, 10);

        Assert.Equal(10, buckets.Length);
        Assert.All(buckets, b => Assert.InRange(b, 0.0, 1.0));
        Assert.Equal(0.40, buckets[0], 2);
        Assert.Equal(0.45, buckets[9], 2);
    }

    [Fact]
    public void ComputeFingerprintHash_IsDeterministic()
    {
        var fp = new IncidentFingerprint
        {
            PrimaryAppPool = "AppPool1",
            PeakCpu = 92.5,
            MemoryDeltaMb = 250,
            RequestRatePerSec = 120,
            CpuProfileBuckets = new[] { 0.5, 0.9, 0.9, 0.5 }
        };

        string hash1 = IncidentFingerprintMatcher.ComputeFingerprintHash(fp);
        string hash2 = IncidentFingerprintMatcher.ComputeFingerprintHash(fp);

        Assert.Equal(hash1, hash2);
        Assert.NotEmpty(hash1);
    }
}
