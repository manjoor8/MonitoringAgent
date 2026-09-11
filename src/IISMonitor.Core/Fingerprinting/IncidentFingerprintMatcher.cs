using System.Security.Cryptography;
using System.Text;
using IISMonitor.Core.Models;

namespace IISMonitor.Core.Fingerprinting;

/// <summary>
/// Fingerprint generator and hybrid similarity matcher per Section 42.
/// Computes similarity across categorical, time-series shape, and scalar dimensions.
/// </summary>
public static class IncidentFingerprintMatcher
{
    private const double WeightAppPool = 0.25;
    private const double WeightCpuShape = 0.35;
    private const double WeightMemory = 0.15;
    private const double WeightGc = 0.10;
    private const double WeightRequest = 0.15;

    public const int DefaultProfileBuckets = 10;

    /// <summary>
    /// Calculates similarity score between two incident fingerprints (0.0 = completely dissimilar, 1.0 = identical).
    /// </summary>
    public static double CalculateSimilarity(IncidentFingerprint a, IncidentFingerprint b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        // 1. App Pool categorical match
        double appPoolScore = string.Equals(a.PrimaryAppPool, b.PrimaryAppPool, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;

        // 2. CPU profile shape cosine similarity
        double cpuShapeScore = CalculateCosineSimilarity(a.CpuProfileBuckets, b.CpuProfileBuckets);

        // 3. Normalized scalar distances
        double memScore = CalculateScalarSimilarity(a.MemoryDeltaMb, b.MemoryDeltaMb);
        double gcScore = CalculateScalarSimilarity(a.GcActivityScore, b.GcActivityScore);
        double reqScore = CalculateScalarSimilarity(a.RequestRatePerSec, b.RequestRatePerSec);

        double composite = (WeightAppPool * appPoolScore) +
                           (WeightCpuShape * cpuShapeScore) +
                           (WeightMemory * memScore) +
                           (WeightGc * gcScore) +
                           (WeightRequest * reqScore);

        return Math.Clamp(composite, 0.0, 1.0);
    }

    /// <summary>
    /// Resamples a sequence of CPU samples into N normalized buckets (0.0 - 1.0).
    /// </summary>
    public static double[] ResampleCpuProfile(IReadOnlyList<double> cpuValues, int bucketCount = DefaultProfileBuckets)
    {
        if (cpuValues == null || cpuValues.Count == 0)
            return new double[bucketCount];

        if (cpuValues.Count == 1)
        {
            var single = new double[bucketCount];
            Array.Fill(single, Math.Clamp(cpuValues[0] / 100.0, 0.0, 1.0));
            return single;
        }

        var buckets = new double[bucketCount];
        double step = (double)(cpuValues.Count - 1) / (bucketCount - 1);

        for (int i = 0; i < bucketCount; i++)
        {
            double targetIndex = i * step;
            int lowerIndex = (int)Math.Floor(targetIndex);
            int upperIndex = Math.Min(lowerIndex + 1, cpuValues.Count - 1);
            double fraction = targetIndex - lowerIndex;

            double interpolated = cpuValues[lowerIndex] + (cpuValues[upperIndex] - cpuValues[lowerIndex]) * fraction;
            buckets[i] = Math.Clamp(interpolated / 100.0, 0.0, 1.0);
        }

        return buckets;
    }

    /// <summary>
    /// Computes a stable hash for the fingerprint.
    /// </summary>
    public static string ComputeFingerprintHash(IncidentFingerprint fingerprint)
    {
        var sb = new StringBuilder();
        sb.Append(fingerprint.PrimaryAppPool).Append('|');
        sb.Append(Math.Round(fingerprint.PeakCpu, 1)).Append('|');
        sb.Append(Math.Round(fingerprint.MemoryDeltaMb, 0)).Append('|');
        sb.Append(Math.Round(fingerprint.RequestRatePerSec, 1)).Append('|');

        if (fingerprint.CpuProfileBuckets != null)
        {
            foreach (var b in fingerprint.CpuProfileBuckets)
            {
                sb.Append(Math.Round(b, 2)).Append(',');
            }
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash)[..16];
    }

    private static double CalculateCosineSimilarity(double[]? v1, double[]? v2)
    {
        if (v1 == null || v2 == null || v1.Length == 0 || v2.Length == 0 || v1.Length != v2.Length)
            return 0.5; // neutral when missing

        double dot = 0.0, mag1 = 0.0, mag2 = 0.0;
        for (int i = 0; i < v1.Length; i++)
        {
            dot += v1[i] * v2[i];
            mag1 += v1[i] * v1[i];
            mag2 += v2[i] * v2[i];
        }

        double denominator = Math.Sqrt(mag1) * Math.Sqrt(mag2);
        if (denominator < 1e-6)
            return 1.0;

        return Math.Clamp(dot / denominator, 0.0, 1.0);
    }

    private static double CalculateScalarSimilarity(double val1, double val2)
    {
        double max = Math.Max(Math.Abs(val1), Math.Abs(val2));
        if (max < 1e-6)
            return 1.0;

        return Math.Clamp(1.0 - (Math.Abs(val1 - val2) / max), 0.0, 1.0);
    }
}
