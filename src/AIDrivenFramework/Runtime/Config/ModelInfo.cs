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
        public string RuntimePath = "";
        public string ModelPath = "";
        public string ConsentRevision = "";
        public string ConsentUtc = "";
        public string ModelTermsUrl = "";
        public string RuntimeTermsUrl = "";

        public const string CurrentConsentRevision = "player-setup-1";
        public bool HasPlayerConsent => ConsentRevision == CurrentConsentRevision &&
            !string.IsNullOrEmpty(ConsentUtc) && !string.IsNullOrEmpty(ModelTermsUrl);

        public static string SettingsPath => Path.Combine(Application.persistentDataPath,
            "AIDrivenFramework", FileName);

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
            WriteAtomic(SettingsPath, ToJson());
        }

        public static void WriteAtomic(string path, string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        /// <summary>
        /// 設定ファイルを読み込む
        /// </summary>
        /// <returns>読み込み後の設定ファイル</returns>
        public static ModelInfo LoadFromFile()
        {
            string path = SettingsPath;
            // Read old installs without writing into a potentially read-only game directory.
            if (!File.Exists(path))
                path = Path.Combine(Application.dataPath, AIDrivenConfig.baseFilePath, FileName);

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
