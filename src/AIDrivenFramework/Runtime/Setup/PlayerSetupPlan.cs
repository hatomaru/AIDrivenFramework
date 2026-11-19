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
        public string ModelTermsText = "";
        public string ModelSource;
        public string RuntimeTerms = RuntimeRelease.TermsUrl;
        public long ModelBytes;
        public ModelLevel Level;
        public string RuntimePath = "";
        public string RuntimeArchivePath = "";
        public string LlamaRuntimeVersion = "";
        public string RuntimeArchiveSha256 = "";
        public long RuntimeExpandedBytes;
        public string ModelPath = "";
        public bool Installed = false;
        // No registered terms page: require explicit review at the model's original source.
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
            if (!ReviewModelTermsAtSource || !string.IsNullOrWhiteSpace(ModelTerms)) ValidateHttps(ModelTerms);
            ValidateHttps(RuntimeTerms);
            if (Mode == "Ollama")
            {
                if (!Regex.IsMatch(Model, @"^[a-zA-Z0-9][a-zA-Z0-9._:/-]{0,159}$") ||
                    Model.IndexOf("cloud", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new ArgumentException("Choose a local Ollama model tag.");
                ValidateHttps(ModelSource);
            }
            else
            {
                if (!string.IsNullOrEmpty(LlamaRuntimeVersion))
                {
                    if (LlamaRuntimeVersion != LlamaRuntimeDownload.Version ||
                        LlamaRuntimeDownload.Current == null || RuntimeTerms != LlamaRuntimeDownload.TermsUrl ||
                        !string.IsNullOrWhiteSpace(RuntimePath) || !string.IsNullOrWhiteSpace(RuntimeArchivePath) ||
                        !File.Exists(ModelPath) || !ModelPath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("Review the current llama.cpp CPU release and select a GGUF model.");
                    return;
                }
                bool archive = !string.IsNullOrWhiteSpace(RuntimeArchivePath);
                if (!File.Exists(ModelPath) || !ModelPath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ||
                    (archive ? !File.Exists(RuntimeArchivePath) || !LlamaRuntimeArchive.IsZip(RuntimeArchivePath) ||
                        !Regex.IsMatch(RuntimeArchiveSha256 ?? "", "^[a-fA-F0-9]{64}$") || RuntimeExpandedBytes <= 0 :
                        !File.Exists(RuntimePath) || !(Path.GetFileName(RuntimePath).Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(RuntimePath).Equals("llama-server", StringComparison.OrdinalIgnoreCase))))
                    throw new ArgumentException("Select a reviewed llama.cpp ZIP or installed llama-server, and a GGUF model.");
            }
        }

        public static void ValidateHttps(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                !string.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException("A public HTTPS terms/source link is required.");
        }

        public ModelInfo ToSettings(string consentUtc) => new ModelInfo
        {
            Name = Model, Mode = Mode, RuntimePath = RuntimePath, ModelPath = ModelPath,
            RuntimeArchiveSha256 = RuntimeArchiveSha256,
            ConsentRevision = ModelInfo.CurrentConsentRevision, ConsentUtc = consentUtc,
            ModelTermsUrl = ModelTerms, RuntimeTermsUrl = RuntimeTerms,
            ModelTermsReviewedAtSource = ReviewModelTermsAtSource,
            ModelTermsTextSha256 = TermsDigest(ModelTermsText),
            LocalModelTermsReviewed = Mode == "Llama" && ReviewModelTermsAtSource
        };

        public static string OllamaModelPage(string tag) => "https://ollama.com/" + (tag.Contains("/") ? "" : "library/") + tag;

        static string TermsDigest(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            using var sha = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        // The game config is the catalog; no second model list lives in the screen or setup layer.
        public static PlayerSetupPlan[] Choices() => FromConfig(AIDrivenConfig.Instance?.OllamaRecommendModelInfos ?? AIDrivenConfig.CreateRecommendedModels(true));
        public static PlayerSetupPlan[] FromConfig(ModelInfoConfig[] entries) => (entries ?? Array.Empty<ModelInfoConfig>())
            .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.ModelName))
            .Select(entry => new PlayerSetupPlan { Model = entry.ModelName, ModelSource = entry.DownloadUrl,
                ModelTerms = entry.LicenseUrl, ReviewModelTermsAtSource = string.IsNullOrWhiteSpace(entry.LicenseUrl),
                ModelBytes = entry.EstimatedDownloadBytes, Level = entry.Level }).ToArray();

        public static string ConfiguredModelTerms(string model, ModelInfoConfig[] entries)
        {
            string url = entries?.FirstOrDefault(e => e != null && string.Equals(e.ModelName, model?.Trim(), StringComparison.OrdinalIgnoreCase))?.LicenseUrl;
            try { ValidateHttps(url); return url.Trim(); }
            catch (ArgumentException) { return ""; }
        }
    }

    public static class RuntimeRelease
    {
        // Version and asset metadata are pinned together; never resolve "latest" at setup time.
        public const string Version = "v0.40.5";
        public const long Bytes = 1471226485;
        public const string Sha256 = "5134ce7e3bc8e94d0494721c799ec293b429450d0c52461b0ee3647dd3bb4bb7";
        const string DownloadRoot = "https://github.com/ollama/ollama/releases/download/" + Version + "/";
        public const string Url = DownloadRoot + "ollama-windows-amd64.zip";
        public const string TermsUrl = "https://github.com/ollama/ollama/blob/" + Version + "/LICENSE";

        public sealed class Asset
        {
            public string Platform { get; }
            public string Version => RuntimeRelease.Version;
            public string Url { get; }
            public long Bytes { get; }
            public string Sha256 { get; }
            public string TermsUrl => RuntimeRelease.TermsUrl;
            internal string InstallFolder { get; }

            internal Asset(string platform, string url, long bytes, string sha256, string installFolder)
            {
                Platform = platform; Url = url; Bytes = bytes; Sha256 = sha256; InstallFolder = installFolder;
            }
        }

        static readonly Asset WindowsX64 = new Asset("Windows x64", Url, Bytes, Sha256, Version);
        static readonly Asset WindowsArm64 = new Asset("Windows ARM64", DownloadRoot + "ollama-windows-arm64.zip",
            210479146, "c3d469e4410ba9cfe3a8806b7c7c7799c365e77ccd24ab8efe2e9eec1fce8f4e", Version + "-windows-arm64");

        public static Asset Select(RuntimePlatform platform, System.Runtime.InteropServices.Architecture architecture)
        {
            if (platform != RuntimePlatform.WindowsPlayer && platform != RuntimePlatform.WindowsEditor) return null;
            switch (architecture)
            {
                case System.Runtime.InteropServices.Architecture.X64: return WindowsX64;
                case System.Runtime.InteropServices.Architecture.Arm64: return WindowsArm64;
                default: return null;
            }
        }

        public static Asset Current => Select(Application.platform, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
        public static bool CanAcquire => Current != null;
        public static string DirectoryPath => Path.Combine(Application.persistentDataPath,
            "AIDrivenFramework", "Runtime", Current?.InstallFolder ?? Version);
        public static string ExecutablePath => Path.Combine(DirectoryPath, "ollama.exe");

        internal static bool IsManagedPath(string path) =>
            CanAcquire && string.Equals(path, ExecutablePath, StringComparison.OrdinalIgnoreCase);

        internal static bool CanUseExecutable(string path) =>
            File.Exists(path) && (!IsManagedPath(path) || RuntimeAcquisition.IsVerifiedInstall(DirectoryPath, Current));
    }
}
