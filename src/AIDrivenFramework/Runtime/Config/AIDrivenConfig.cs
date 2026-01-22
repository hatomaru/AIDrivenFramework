using AIDrivenFW;
using UnityEngine;

/// <summary>
/// AI設定クラス
/// </summary>
[CreateAssetMenu(fileName = "GenAIConfig", menuName = "AIDrivenFrameWork/GenAIConfig")]
public class GenAIConfig
{
    public string ModelName = AIDrivenConfig.autoDetect;
    public string sysPrompt = "";
    public string arguments = AIDrivenConfig.autoDetect;
    public bool AutoStartProcess = true;
    public bool AutoKillProcess = true;

    public GenAIConfig()
    {
        if (arguments == AIDrivenConfig.autoDetect)
        {
            arguments = AIDrivenConfig.defaultArguments;
        }
    }
}

public class AIDrivenConfig : MonoBehaviour
{
    public const bool isDeepDebug = false;
    // Auto Detect Constant
    public const string autoDetect = "Auto";
    public const string defaultArguments = "--gpu-layers 80 --batch-size 16 --prio 2 -cnv";
    // File Paths
    public static readonly string baseFilePath = "AIDrivenFreameWork/";
    public static readonly string aiSoftwareFileName = "llama-cli.exe";
    public static readonly string tempFilePath = "Temp/";
    public static readonly string modelSubPath = "Models/";
    // Link Settings
    public static readonly string softwareLink = "https://github.com/ggml-org/llama.cpp/releases/";
    public static readonly string modelink = "https://huggingface.co/elyza/Llama-3-ELYZA-JP-8B-GGUF/tree/main";


}
