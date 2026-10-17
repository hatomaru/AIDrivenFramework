using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AIDrivenFW.Config;
using UnityEngine;

namespace AIDrivenFW.Setup
{
    [Serializable]
    public sealed class PlayerSetupPlan
    {
        public string Model;
        public string Mode = "Ollama";
        public string ModelTerms;
        public string ModelSource;
        public string RuntimeTerms = RuntimeRelease.TermsUrl;
        public long ModelBytes;
        public ModelLevel Level;
        public string RuntimePath = "";
        public string ModelPath = "";
        public bool Installed = false;
        // For a player-owned GGUF whose terms must be reviewed at its original source.
        public bool ReviewModelTermsAtSource;

        public PlayerSetupPlan()
        {
            var entry = AIDrivenConfig.CreateRecommendedModels(true)[0];
            Model = entry.ModelName; ModelSource = entry.DownloadUrl;
            ModelTerms = entry.LicenseUrl; ModelBytes = entry.EstimatedDownloadBytes; Level = entry.Level;
        }

        public PlayerSetupPlan Snapshot() => JsonUtility.FromJson<PlayerSetupPlan>(JsonUtility.ToJson(this));

        public void Validate()
        {
            if (Mode != "Ollama" && Mode != "Llama") throw new ArgumentException("Unsupported runtime.");
            if (string.IsNullOrWhiteSpace(Model) || ModelBytes <= 0)
                throw new ArgumentException("A model and its estimated size are required.");
            if (Mode != "Llama" || !ReviewModelTermsAtSource || !string.IsNullOrWhiteSpace(ModelTerms)) ValidateHttps(ModelTerms);
            ValidateHttps(RuntimeTerms);
            if (Mode == "Ollama")
            {
                if (!Regex.IsMatch(Model, @"^[a-zA-Z0-9][a-zA-Z0-9._:/-]{0,159}$") ||
                    Model.IndexOf("cloud", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new ArgumentException("Choose a local Ollama model tag.");
                ValidateHttps(ModelSource);
            }
            else if (!File.Exists(RuntimePath) || !File.Exists(ModelPath) ||
                     !ModelPath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ||
                     !(Path.GetFileName(RuntimePath).Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase) ||
                       Path.GetFileName(RuntimePath).Equals("llama-server", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Select an existing llama-server executable and GGUF model.");
        }

        public static void ValidateHttps(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                !string.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException("A public HTTPS terms/source link is required.");
        }

        public ModelInfo ToSettings(string consentUtc) => new ModelInfo
        {
            Name = Model, Mode = Mode, RuntimePath = RuntimePath, ModelPath = ModelPath,
            ConsentRevision = ModelInfo.CurrentConsentRevision, ConsentUtc = consentUtc,
            ModelTermsUrl = ModelTerms, RuntimeTermsUrl = RuntimeTerms, LocalModelTermsReviewed = Mode == "Llama" && ReviewModelTermsAtSource
        };

        public static string OllamaModelPage(string tag) => "https://ollama.com/" + (tag.Contains("/") ? "" : "library/") + tag;

        // The game config is the catalog; no second model list lives in the screen or setup layer.
        public static PlayerSetupPlan[] Choices() => FromConfig(AIDrivenConfig.Instance?.OllamaRecommendModelInfos ?? AIDrivenConfig.CreateRecommendedModels(true));
        public static PlayerSetupPlan[] FromConfig(ModelInfoConfig[] entries) => (entries ?? Array.Empty<ModelInfoConfig>())
            .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.ModelName))
            .Select(entry => new PlayerSetupPlan { Model = entry.ModelName, ModelSource = entry.DownloadUrl,
                ModelTerms = entry.LicenseUrl, ModelBytes = entry.EstimatedDownloadBytes, Level = entry.Level }).ToArray();
    }

    public static class RuntimeRelease
    {
        // Pinned official release. Upgrade deliberately, together with size and SHA-256.
        public const string Version = "v0.40.5";
        public const long Bytes = 1471226485;
        public const string Sha256 = "5134ce7e3bc8e94d0494721c799ec293b429450d0c52461b0ee3647dd3bb4bb7";
        public const string Url = "https://github.com/ollama/ollama/releases/download/" + Version + "/ollama-windows-amd64.zip";
        public const string TermsUrl = "https://github.com/ollama/ollama/blob/" + Version + "/LICENSE";
        public static string DirectoryPath => Path.Combine(Application.persistentDataPath,
            "AIDrivenFramework", "Runtime", Version);
        public static string ExecutablePath => Path.Combine(DirectoryPath, "ollama.exe");
        public static bool CanAcquire => Application.platform == RuntimePlatform.WindowsPlayer ||
            Application.platform == RuntimePlatform.WindowsEditor
            ? System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.X64
            : false;
    }
}
