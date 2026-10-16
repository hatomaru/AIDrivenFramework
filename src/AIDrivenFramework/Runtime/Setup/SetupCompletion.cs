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
        public SetupCompletion(long modelBytes) => this.modelBytes = Math.Max(1, modelBytes);
        public float Observe(SetupProgress progress)
        {
            float next = Percent;
            switch (progress.Stage)
            {
                case SetupStage.Runtime:
                    next = progress.TotalBytes > 0 ? 25f * Math.Min(1f, (float)progress.CompletedBytes / progress.TotalBytes) : 0;
                    break;
                case SetupStage.Model:
                    string item = progress.Item ?? "model";
                    completed[item] = Math.Max(completed.TryGetValue(item, out var old) ? old : 0, progress.CompletedBytes);
                    totals[item] = Math.Max(totals.TryGetValue(item, out var total) ? total : 0, progress.TotalBytes);
                    long done = 0, expected = 0;
                    foreach (long value in completed.Values) done += value;
                    foreach (long value in totals.Values) expected += value;
                    next = 25f + 60f * Math.Min(1f, (float)done / Math.Max(modelBytes, expected));
                    break;
                case SetupStage.Verify: next = 85; break;
                case SetupStage.Complete: next = 100; break;
            }
            Percent = Math.Max(Percent, next);
            return Percent;
        }
    }
}
