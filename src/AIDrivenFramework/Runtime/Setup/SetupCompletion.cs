using System;
using System.Collections.Generic;

namespace AIDrivenFW.Setup
{
    /// <summary>Monotonic work estimate, not a prediction of remaining time.</summary>
    public sealed class SetupCompletion
    {
        readonly long modelBytes;
        readonly Dictionary<string, long> completed = new Dictionary<string, long>();
        readonly Dictionary<string, long> totals = new Dictionary<string, long>();
        public float Percent { get; private set; }
        public long DownloadedBytes { get; private set; }
        public long TotalBytes { get; private set; }
        public long RemainingBytes => Math.Max(0, TotalBytes - DownloadedBytes);
        public bool IsEstimate { get; private set; }
        public bool HasDownloadBytes { get; private set; }
        public SetupCompletion(long modelBytes) => this.modelBytes = Math.Max(1, modelBytes);
        public float Observe(SetupProgress progress)
        {
            float next = Percent;
            switch (progress.Stage)
            {
                case SetupStage.Runtime:
                    HasDownloadBytes = progress.TotalBytes > 0;
                    DownloadedBytes = Math.Max(DownloadedBytes, Math.Max(0, progress.CompletedBytes));
                    TotalBytes = Math.Max(TotalBytes, Math.Max(0, progress.TotalBytes));
                    IsEstimate = false;
                    next = progress.TotalBytes > 0 ? 25f * Math.Min(1f, (float)progress.CompletedBytes / progress.TotalBytes) : 0;
                    break;
                case SetupStage.Model:
                    if (completed.Count == 0 && progress.TotalBytes == 0) HasDownloadBytes = false;
                    string item = progress.Item ?? "model";
                    completed[item] = Math.Max(completed.TryGetValue(item, out var old) ? old : 0, Math.Max(0, progress.CompletedBytes));
                    totals[item] = Math.Max(totals.TryGetValue(item, out var total) ? total : 0, progress.TotalBytes);
                    long done = 0, expected = 0;
                    foreach (var pair in completed) done += totals[pair.Key] > 0 ? Math.Min(pair.Value, totals[pair.Key]) : 0;
                    foreach (long value in totals.Values) expected += value;
                    bool downloaded = progress.Detail == "success" || progress.Detail == "verifying sha256 digest" || progress.Detail == "writing manifest";
                    HasDownloadBytes = expected > 0;
                    TotalBytes = downloaded ? expected : Math.Max(modelBytes, expected);
                    DownloadedBytes = downloaded ? expected : done;
                    IsEstimate = !downloaded;
                    next = 25f + 60f * (downloaded ? 1 : Math.Min(1f, (float)done / Math.Max(modelBytes, expected)));
                    break;
                case SetupStage.Verify: next = 85; DownloadedBytes = TotalBytes; IsEstimate = false; break;
                case SetupStage.Complete: next = 100; DownloadedBytes = TotalBytes; IsEstimate = false; break;
            }
            Percent = Math.Max(Percent, next);
            return Percent;
        }
    }
}
