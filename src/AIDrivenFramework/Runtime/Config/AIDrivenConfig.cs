using UnityEngine;

namespace AIDrivenFW.Config
{
    public class AIDrivenConfig : MonoBehaviour
    {
        public const bool isDeepDebug = true;
        // Auto Detect Constant
        public const string autoDetect = "Auto";
        public static string defaultArguments => $"--gpu-layers {RecommendedGpuLayers} --batch-size {RecommendedBatchSize} --prio 2 --keep 0 -cnv";

        // File Paths
        public static readonly string baseFilePath = "AIDrivenFreameWork/";
        public static string aiSoftwareFileName = "llama-cli";
        public static readonly string tempFilePath = "Temp/";
        public static readonly string modelSubPath = "Models/";
        public static readonly string[] aiSoftwareFileFilters = {"*.zip", "*.tar.gz", "*.tar" };
        public static readonly string[] modelFileFilters = { "*.gguf" };
        // Link Settings
        public static readonly string softwareLink = "https://github.com/ggml-org/llama.cpp/releases/";
        // Model Settings
        public static readonly ModelInfoConfig defaultModelInfo = new ModelInfoConfig(
            modelName: "Llama-3-ELYZA-JP:8B-Q4_K_M",
            downloadUrl: "https://huggingface.co/shirubei/Llama-3-ELYZA-JP-8B-Q4_K_M-GGUF/tree/main",
            minVRAM: 4096,
            maxVRAM: 16384,
            level: ModelLevel.Balanced
        );
        /// <summary>
        /// GPU メモリに応じた推奨 GPU レイヤー数を返す
        /// Apple Silicon Mac では統合メモリを OS と共有するため余裕を持って設定
        /// </summary>
        public static int RecommendedGpuLayers
        {
            get
            {
                int vramMB = UnityEngine.SystemInfo.graphicsMemorySize; // MB 単位
                if (vramMB <= 0) return 0;

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                if (vramMB < 8192)  return 0;   // 8GB 未満は CPU のみ
                if (vramMB < 16384) return 10;  // 8-16GB: 最小限の GPU オフロード
                if (vramMB < 32768) return 30;  // 16-32GB
                return 60;                      // 32GB 以上
#else
                // Windows/Linux: 専用 VRAM
                if (vramMB < 4096) return 40;
                if (vramMB < 8192) return 60;
                if (vramMB < 16384) return 80;
                return 100;
#endif
            }
        }

        /// <summary>
        /// GPU メモリに応じた推奨バッチサイズを返す
        /// </summary>
        public static int RecommendedBatchSize
        {
            get
            {
                int vramMB = UnityEngine.SystemInfo.graphicsMemorySize;
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                if (vramMB < 16384) return 8;
#endif
                return 16;
            }
        }
    }
}