using AIDrivenFW.Setup;
using NUnit.Framework;

namespace AIDrivenFW.Tests.Unit
{
    public class SetupCompletionTests
    {
        [Test]
        public void ProgressReflectsRuntimeModelLayersThenVerification()
        {
            var value = new SetupCompletion(100);
            Assert.AreEqual(0, value.Observe(new SetupProgress { Stage = SetupStage.Runtime }));
            Assert.AreEqual(12.5, value.Observe(new SetupProgress { Stage = SetupStage.Runtime, CompletedBytes = 50, TotalBytes = 100 }));
            Assert.AreEqual(25, value.Observe(new SetupProgress { Stage = SetupStage.Model }));
            Assert.AreEqual(55, value.Observe(new SetupProgress { Stage = SetupStage.Model, Item = "a", CompletedBytes = 50, TotalBytes = 50 }));
            Assert.AreEqual(70, value.Observe(new SetupProgress { Stage = SetupStage.Model, Item = "b", CompletedBytes = 25, TotalBytes = 50 }));
            Assert.AreEqual(85, value.Observe(new SetupProgress { Stage = SetupStage.Verify }));
            Assert.AreEqual(100, value.Observe(new SetupProgress { Stage = SetupStage.Complete }));
        }

        [Test]
        public void RepeatedAndOutOfOrderBytesCannotMoveProgressBackOrDoubleCount()
        {
            var value = new SetupCompletion(100);
            value.Observe(new SetupProgress { Stage = SetupStage.Model, Item = "a", CompletedBytes = 50, TotalBytes = 100 });
            Assert.AreEqual(55, value.Observe(new SetupProgress { Stage = SetupStage.Model, Item = "a", CompletedBytes = 25, TotalBytes = 100 }));
            Assert.AreEqual(55, value.Observe(new SetupProgress { Stage = SetupStage.Model, Item = "a", CompletedBytes = 50, TotalBytes = 100 }));
            Assert.AreEqual(55, value.Observe(new SetupProgress { Stage = SetupStage.Model, Item = "b", TotalBytes = 100 }));
        }

        [Test]
        public void FailedOrPausedVerificationNeverShowsOneHundredPercent()
        {
            var value = new SetupCompletion(10);
            value.Observe(new SetupProgress { Stage = SetupStage.Model, CompletedBytes = 1000, TotalBytes = 10 });
            Assert.AreEqual(85, value.Percent);
            Assert.AreEqual(85, value.Observe(new SetupProgress { Stage = SetupStage.Failed }));
            Assert.AreEqual(85, value.Observe(new SetupProgress { Stage = SetupStage.Cancelled }));
        }

        [Test]
        public void CapacityAggregatesDistinctLayersAndLabelsTheRemainingEstimate()
        {
            var value = new SetupCompletion(100);
            value.Observe(new SetupProgress { Stage = SetupStage.Runtime, CompletedBytes = 30, TotalBytes = 60 });
            Assert.AreEqual(30, value.RemainingBytes); Assert.IsFalse(value.IsEstimate);
            value.Observe(new SetupProgress { Stage = SetupStage.Model, Item = "a", CompletedBytes = 20, TotalBytes = 40 });
            value.Observe(new SetupProgress { Stage = SetupStage.Model, Item = "b", CompletedBytes = 10, TotalBytes = 50 });
            value.Observe(new SetupProgress { Stage = SetupStage.Model, Item = "a", CompletedBytes = 15, TotalBytes = 40 });
            Assert.AreEqual(30, value.DownloadedBytes); Assert.AreEqual(100, value.TotalBytes);
            Assert.AreEqual(70, value.RemainingBytes); Assert.IsTrue(value.IsEstimate);
            value.Observe(new SetupProgress { Stage = SetupStage.Model, Detail = "success" });
            Assert.AreEqual(90, value.DownloadedBytes); Assert.AreEqual(90, value.TotalBytes);
            Assert.AreEqual(0, value.RemainingBytes); Assert.IsFalse(value.IsEstimate);
            Assert.Less(value.Percent, 100);
        }

        [Test]
        public void InstalledAndByteLessStatesDoNotInventDownloadAmounts()
        {
            var value = new SetupCompletion(100);
            value.Observe(new SetupProgress { Stage = SetupStage.Runtime });
            Assert.IsFalse(value.HasDownloadBytes);
            value.Observe(new SetupProgress { Stage = SetupStage.Model, Detail = "installed" });
            Assert.IsFalse(value.HasDownloadBytes);
            value.Observe(new SetupProgress { Stage = SetupStage.Verify });
            Assert.AreEqual(0, value.RemainingBytes);
        }
    }
}
