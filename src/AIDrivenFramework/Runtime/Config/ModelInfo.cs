using System.IO;
using System.Text;
using UnityEngine;
// Using Unity's built-in JsonUtility to avoid adding external dependencies.

namespace AIDrivenFW.Config
{
    [System.Serializable]
    public class ModelInfo
    {
        const string FileName = "AISetup.json";

        // JsonUtility serializes fields, so use a public field instead of a property
        public string Name = "";
        // 保存する実行モード: "Ollama" または "Llama"
        public string Mode = "Llama";

        public ModelInfo() { }

        public string ToJson()
        {
            return JsonUtility.ToJson(this, true);
        }

        public static ModelInfo FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonUtility.FromJson<ModelInfo>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 設定ファイルを保存する
        /// </summary>
        public void SaveToFile()
        {
            // 設定は persistentDataPath ではなく Data フォルダ側に保存する
            string path = Path.Combine(Application.dataPath, AIDrivenConfig.Instance.BaseFilePath, FileName);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, ToJson(), Encoding.UTF8);
        }

        /// <summary>
        /// 設定ファイルを読み込む
        /// </summary>
        /// <returns>読み込み後の設定ファイル</returns>
        public static ModelInfo LoadFromFile()
        {
            // 設定は persistentDataPath ではなく Data フォルダ側から読み込む
            string path = Path.Combine(Application.dataPath, AIDrivenConfig.Instance.BaseFilePath, FileName);

            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                return FromJson(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
