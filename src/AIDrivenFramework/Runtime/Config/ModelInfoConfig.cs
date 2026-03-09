using System;
using UnityEngine;

namespace AIDrivenFW.Config
{

    /// <summary>
    /// モデルの性能レベル
    /// </summary>
    [System.Serializable]
    public enum ModelLevel
    {
        Light,
        Balanced,
        Powerful
    }

    [System.Serializable]

    public class ModelInfoConfig
    {
        public string ModelName;       // モデル名

        public string DownloadUrl;     // HuggingFaceなどのモデルのダウンロードURL

        public int MinVRAM;            // 推奨VRAM
        public int MaxVRAM;

        public ModelLevel Level;

        public ModelInfoConfig(string modelName,
            string downloadUrl,int minVRAM,int maxVRAM,
            ModelLevel level)
        {
            ModelName = modelName;
            DownloadUrl = downloadUrl;
            MinVRAM = minVRAM;
            MaxVRAM = maxVRAM;
            Level = level;
        }
    }
}