using UnityEngine;

/// <summary>
/// AI設定クラス
/// </summary>
[CreateAssetMenu(fileName = "GenAIConfig", menuName = "AIDrivenFrameWork/GenAIConfig")]
public class GenAIConfig : ScriptableObject
{
    public string aiSoftwarePath = "";
    public string modelFilePath = AIDrivenConfig.autoDetect;
    public string sysPrompt = "";
    public string arguments = AIDrivenConfig.autoDetect;

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
    public const bool isDeepDebug = true;
    // Auto Detect Constant
    public const string autoDetect = "Auto";
    public const string defaultArguments = "--gpu-layers 80 --batch-size 16 --prio 2 --keep 0 -cnv";
    // File Paths
    public static readonly string baseFilePath = "AIDrivenFreameWork/";
    public static readonly string aiSoftwareFileName = "llama-cli.exe";
    public static readonly string tempFilePath = "Temp/";
    public static readonly string modelSubPath = "Models/";
    // Link Settings
    public static readonly string softwareLink = "https://github.com/ggml-org/llama.cpp/releases/";
    public static readonly string modelink = "https://huggingface.co/elyza/Llama-3-ELYZA-JP-8B-GGUF/tree/main";


}
