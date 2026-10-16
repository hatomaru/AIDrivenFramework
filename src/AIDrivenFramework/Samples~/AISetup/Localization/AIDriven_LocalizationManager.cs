using UnityEngine;

public class AIDriven_LocalizationManager : MonoBehaviour
{
    [SerializeField]private AIDriven_LocalizationData data;
    private SystemLanguage currentLanguage; // デフォルト: デバイス言語
    public System.Action OnLanguageChanged;

    private void Awake()
    {
        currentLanguage = Application.systemLanguage;
    }

    /// <summary>
    /// 翻訳の取得
    /// </summary>
    /// <param name="key">エントリ名</param>
    /// <returns>翻訳後の文章</returns>
    public string Get(string key)
    {
        var entry = data.entries.Find(e => e.key == key);
        if (entry == null) return key;
        switch (currentLanguage)
        {
            case SystemLanguage.Japanese:
                return string.IsNullOrEmpty(entry.japanese) ? entry.english : entry.japanese;
            default:
                return string.IsNullOrEmpty(entry.english) ? entry.japanese : entry.english;
        }
    }

    /// <summary>
    /// 言語設定を変更
    /// </summary>
    /// <param name="lang">新しい言語</param>
    public void SetLanguage(SystemLanguage lang)
    {
        currentLanguage = lang;
        OnLanguageChanged?.Invoke();
    }
}
