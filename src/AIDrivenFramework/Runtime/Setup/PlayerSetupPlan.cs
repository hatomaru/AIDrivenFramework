using System;
using System.IO;
using System.Text.RegularExpressions;
using AIDrivenFW.Config;
using UnityEngine;

namespace AIDrivenFW.Setup
{
    [Serializable]
    public sealed class PlayerSetupPlan
    {
        public string Model = "qwen3:0.6b";
        public string Mode = "Ollama";
        public string ModelTerms = "https://huggingface.co/Qwen/Qwen3-0.6B/blob/main/LICENSE";
        public string ModelSource = "https://ollama.com/library/qwen3:0.6b";
        public string RuntimeTerms = RuntimeRelease.TermsUrl;
        public long ModelBytes = 523000000;
        public string RuntimePath = "";
        public string ModelPath = "";
        public bool Installed = false;

        public PlayerSetupPlan Snapshot() => JsonUtility.FromJson<PlayerSetupPlan>(JsonUtility.ToJson(this));

        public void Validate()
        {
            if (Mode != "Ollama" && Mode != "Llama") throw new ArgumentException("Unsupported runtime.");
            if (string.IsNullOrWhiteSpace(Model) || ModelBytes <= 0)
                throw new ArgumentException("A model and its estimated size are required.");
            ValidateHttps(ModelTerms);
            ValidateHttps(RuntimeTerms);
            if (Mode == "Ollama")
            {
                if (!Regex.IsMatch(Model, @"^[a-zA-Z0-9][a-zA-Z0-9._:/-]{0,159}$") ||
                    Model.IndexOf("cloud", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new ArgumentException("Choose a local Ollama model tag.");
                ValidateHttps(ModelSource);
            }
            else if (!File.Exists(RuntimePath) || !File.Exists(ModelPath) ||
                     !ModelPath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
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
            ModelTermsUrl = ModelTerms, RuntimeTermsUrl = RuntimeTerms
        };

        // Small, multilingual, Apache-2.0 defaults. Players can opt into larger models.
        public static PlayerSetupPlan[] Choices() => new[]
        {
            new PlayerSetupPlan(),
            new PlayerSetupPlan { Model = "qwen3:1.7b", ModelBytes = 1400000000,
                ModelSource = "https://ollama.com/library/qwen3:1.7b",
                ModelTerms = "https://huggingface.co/Qwen/Qwen3-1.7B/blob/main/LICENSE" },
            new PlayerSetupPlan { Model = "qwen3:4b", ModelBytes = 2500000000,
                ModelSource = "https://ollama.com/library/qwen3:4b",
                ModelTerms = "https://huggingface.co/Qwen/Qwen3-4B/blob/main/LICENSE" }
        };
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
