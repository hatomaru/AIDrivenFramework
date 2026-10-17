using System;
using AIDrivenFW.Config;
using AIDrivenFW.Setup;
using NUnit.Framework;

namespace AIDrivenFW.Tests.Unit
{
    public class PlayerSetupCatalogTests
    {
        [Test]
        public void ConfigIsTheSourceOfModelNameSizeSourceLicenseAndTier()
        {
            var entry = new ModelInfoConfig("my-model:small", "https://example.com/model", 1, 2, ModelLevel.Balanced)
            { EstimatedDownloadBytes = 123456, LicenseUrl = "https://example.com/license" };
            var plans = PlayerSetupPlan.FromConfig(new[] { entry });
            Assert.AreEqual(1, plans.Length); Assert.AreEqual(entry.ModelName, plans[0].Model);
            Assert.AreEqual(entry.DownloadUrl, plans[0].ModelSource); Assert.AreEqual(entry.LicenseUrl, plans[0].ModelTerms);
            Assert.AreEqual(entry.EstimatedDownloadBytes, plans[0].ModelBytes); Assert.AreEqual(entry.Level, plans[0].Level);
            plans[0].Validate(); entry.ModelName = "changed";
            Assert.AreEqual("my-model:small", plans[0].Model);
        }
        [Test]
        public void LegacyConfigRequiresSizeAndLicenseReviewRatherThanGuessing()
        {
            var entry = new ModelInfoConfig("custom:old", "https://example.com/model", 1, 2, ModelLevel.Light);
            var plan = PlayerSetupPlan.FromConfig(new[] { entry })[0];
            Assert.AreEqual(0, plan.ModelBytes); Assert.IsTrue(string.IsNullOrEmpty(plan.ModelTerms));
            Assert.Throws<ArgumentException>(() => plan.Validate());
        }
        [Test]
        public void EmptyCatalogDoesNotSilentlyReplaceGameChoices()
        {
            Assert.IsEmpty(PlayerSetupPlan.FromConfig(null));
            Assert.IsEmpty(PlayerSetupPlan.FromConfig(new ModelInfoConfig[] { null }));
        }
        [Test]
        public void NewConfigDefaultsHaveExplicitLocalModelTermsAndCapacity()
        {
            var defaults = AIDrivenConfig.CreateRecommendedModels(true);
            foreach (var plan in PlayerSetupPlan.FromConfig(defaults)) plan.Validate();
            Assert.AreEqual(defaults[0].ModelName, new PlayerSetupPlan().Model);
        }
    }
}
