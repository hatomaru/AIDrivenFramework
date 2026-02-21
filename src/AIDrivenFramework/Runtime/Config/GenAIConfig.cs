using UnityEngine;

namespace AIFW.Config
{
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
}
