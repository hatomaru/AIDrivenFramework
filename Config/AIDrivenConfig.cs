using UnityEngine;

/// <summary>
/// AI設定クラス
/// </summary>
public class GenAIConfig
{
    public string AISoftwarePath = AIDrivenConfig.autoDetect;   
    public string ModelPath = AIDrivenConfig.autoDetect;
    public string arguments = AIDrivenConfig.autoDetect;
    public bool AutoStartProcess = true;
    public bool AutoKillProcess = true;
}

public class AIDrivenConfig : MonoBehaviour
{
    // Auto Detect Constant
    public const string autoDetect = "Auto";
    // File Paths
    public static readonly string baseFilePath = "AIDrivenFreameWork/";
    public static readonly string tempFilePath = "Temp/";
    public static readonly string modelSubPath = "Models/";
    // Link Settings
    public static readonly string softwareLink = "https://github.com/ggml-org/llama.cpp/releases/";
    public static readonly string modelink = "https://huggingface.co/LiquidAI/LFM2.5-1.2B-Instruct-GGUF/blob/main/";


}
