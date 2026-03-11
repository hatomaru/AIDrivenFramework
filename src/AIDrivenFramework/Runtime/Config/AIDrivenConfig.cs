using UnityEngine;

namespace AIDrivenFW.Config
{
    [System.Serializable]
    public class AIDrivenConfig : ScriptableObject
    {
        public const bool isDeepDebug = true;
        // Auto Detect Constant
        public const string autoDetect = "Auto";
        public static string defaultArguments => $"--gpu-layers {RecommendedGpuLayers} --batch-size {RecommendedBatchSize} --prio 2 --keep 0 -cnv";
        
        // File Paths
        // Static fallbacks to avoid calling Resources.Load during field initialization / constructor time
        private static string s_baseFilePath = "AIDrivenFreameWork/";
        private static string s_aiSoftwareFileName = "llama-cli";
        private static string s_tempFilePath = "Temp/";
        private static string s_modelSubPath = "Models/";
        private static string s_softwareLink = "https://github.com/ggml-org/llama.cpp/releases/";
        private static string[] s_aiSoftwareFileFilters = {"*.zip", "*.tar.gz", "*.tar" };
        private static string[] s_modelFileFilters = { "*.gguf" };
        private static ModelInfoConfig[] s_recommendModelInfos = new ModelInfoConfig[]
        {
            new ModelInfoConfig(
                modelName: "Qwen3:0.6B",
                downloadUrl: "https://huggingface.co/bartowski/Qwen_Qwen3-0.6B-GGUF",
                minVRAM: 2048,
                maxVRAM: 8192,
                level: ModelLevel.Light
            ),
            new ModelInfoConfig(
                modelName: "qwen3.5:4b",
                downloadUrl: "https://huggingface.co/unsloth/Qwen3.5-4B-GGUF/tree/main",
                minVRAM: 4096,
                maxVRAM: 8192,
                level: ModelLevel.Balanced
            ),
            new ModelInfoConfig(
                modelName: "Qwen3.5:35B-A3B",
                downloadUrl: "https://huggingface.co/unidaikon/Qwen3.5-35B-A3B-Q5_K_XXL-GGUF",
                minVRAM: 8192,
                maxVRAM: 32768,
                level: ModelLevel.Powerful
            )
        };

        // Link Settings
        [SerializeField] private string _aiSoftwareLink = "https://github.com/ggml-org/llama.cpp/releases/";
        [SerializeField] private string _baseFilePath = s_baseFilePath;
        [SerializeField] private string _aiSoftwareFileName = s_aiSoftwareFileName;
        [SerializeField] private string _tempFilePath = s_tempFilePath;
        [SerializeField] private string _modelSubPath = s_modelSubPath;
        [Header("File Filter")]
        [SerializeField] private string[] _aiSoftwareFileFilters = s_aiSoftwareFileFilters;
        [SerializeField] private string[] _modelFileFilters = s_modelFileFilters;
        [Header("Model")]
        // Model Settings
        [SerializeField] private ModelInfoConfig[] _recommendModelInfos = s_recommendModelInfos;

        // Instance properties for direct access
        public string BaseFilePath => _baseFilePath;
        public string AiSoftwareFileName
        {
            get => _aiSoftwareFileName;
            set => _aiSoftwareFileName = value;
        }
        public string TempFilePath => _tempFilePath;
        public string ModelSubPath => _modelSubPath;
        public string[] AiSoftwareFileFilters => _aiSoftwareFileFilters;
        public string[] ModelFileFilters => _modelFileFilters;
        public string SoftwareLink => _aiSoftwareLink;
        public ModelInfoConfig[] RecommendModelInfos => _recommendModelInfos;

        // Static accessors for backward compatibility
        public static string baseFilePath => instance != null ? instance._baseFilePath : s_baseFilePath;
        public static string aiSoftwareFileName
        {
            get => instance != null ? instance._aiSoftwareFileName : s_aiSoftwareFileName;
            set
            {
                if (instance != null) instance._aiSoftwareFileName = value;
                else s_aiSoftwareFileName = value;
            }
        }
        public static string tempFilePath => instance != null ? instance._tempFilePath : s_tempFilePath;
        public static string modelSubPath => instance != null ? instance._modelSubPath : s_modelSubPath;
        public static string[] aiSoftwareFileFilters => instance != null ? instance._aiSoftwareFileFilters : s_aiSoftwareFileFilters;
        public static string[] modelFileFilters => instance != null ? instance._modelFileFilters : s_modelFileFilters;
        public static string aiSoftwareLink => instance != null ? instance._aiSoftwareLink : s_softwareLink;
        public static ModelInfoConfig[] recommendModelInfos => instance != null ? instance._recommendModelInfos : s_recommendModelInfos;

        private static AIDrivenConfig instance;

        public static AIDrivenConfig Instance
        {
            get
            {
                if (instance == null)
                {
                    bool loadThrew = false;
                    try
                    {
                        instance = Resources.Load<AIDrivenConfig>("AIDrivenConfig");
                    }
                    catch (UnityEngine.UnityException)
                    {
                        // Resources.Load is not allowed in this context (e.g., during a MonoBehaviour constructor).
                        // Avoid throwing; caller should retry later (e.g., in Awake/Start).
                        loadThrew = true;
                        return null;
                    }

#if UNITY_EDITOR
                    if (instance == null && !loadThrew)
                    {
                        instance = CreateInstance<AIDrivenConfig>();

                        string folder = "Assets/AIDrivenFW/Resources";
                        if (!System.IO.Directory.Exists(folder))
                        {
                            System.IO.Directory.CreateDirectory(folder);
                        }

                        UnityEditor.AssetDatabase.CreateAsset(
                            instance,
                            $"{folder}/AIDrivenConfig.asset"
                        );

                        UnityEditor.AssetDatabase.SaveAssets();
                    }
#endif
                }

                return instance;
            }
        }

        /// <summary>
        /// VRAM を MB 単位で返す
        /// </summary>
        /// <returns>VRAMメモリ</returns>
        public static int GetVRAM()
        {
            return UnityEngine.SystemInfo.graphicsMemorySize;
        }

        /// <summary>
        /// GPU メモリに応じた推奨 GPU レイヤー数を返す
        /// Apple Silicon Mac では統合メモリを OS と共有するため余裕を持って設定
        /// </summary>
        public static int RecommendedGpuLayers
        {
            get
            {
                int vramMB = GetVRAM();
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
                int vramMB = GetVRAM();
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                if (vramMB < 16384) return 8;
#endif
                return 16;
            }
        }

        /// <summary>
        /// 設定をデフォルトにリセット
        /// </summary>
        public void ResetToDefaults()
        {
            _baseFilePath = s_baseFilePath;
            _aiSoftwareFileName = s_aiSoftwareFileName;
            _tempFilePath = s_tempFilePath;
            _modelSubPath = s_modelSubPath;
            _aiSoftwareFileFilters = (string[])s_aiSoftwareFileFilters.Clone();
            _modelFileFilters = (string[])s_modelFileFilters.Clone();
            _aiSoftwareLink = s_softwareLink;
            _recommendModelInfos = new ModelInfoConfig[]
                {
                    new ModelInfoConfig(
                        modelName: "Qwen3:0.6B",
                        downloadUrl: "https://huggingface.co/bartowski/Qwen_Qwen3-0.6B-GGUF",
                        minVRAM: 2048,
                        maxVRAM: 8192,
                        level: ModelLevel.Light
                    ),
                    new ModelInfoConfig(
                        modelName: "Qwen3.5:4b",
                        downloadUrl: "https://huggingface.co/unsloth/Qwen3.5-4B-GGUF/tree/main",
                        minVRAM: 4096,
                        maxVRAM: 8192,
                        level: ModelLevel.Balanced
                    ),
                    new ModelInfoConfig(
                        modelName: "Qwen3.5:35B-A3B",
                        downloadUrl: "https://huggingface.co/unidaikon/Qwen3.5-35B-A3B-Q5_K_XXL-GGUF",
                        minVRAM: 8192,
                        maxVRAM: 32768,
                        level: ModelLevel.Powerful
                    )
                };

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.AssetDatabase.Refresh();
#endif
        }
    }
}