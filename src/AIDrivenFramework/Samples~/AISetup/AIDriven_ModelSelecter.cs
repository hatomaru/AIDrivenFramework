using AIDrivenFW.Config;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AIDriven_ModelSelecter : MonoBehaviour
{
    public readonly Color lightModelColor = new Color(0.3058824f, 0.6392157f, 0.3137255f, 1f);
    public readonly Color balanceModelColor = new Color(0.8509805f, 0.5411765f, 0.1882353f, 1f);
    public readonly Color powerfulModelColor = new Color(0.8352942f, 0.372549f, 0.5058824f, 1f);

    [SerializeField] AIDriven_AIDrivenManager manager;
    [SerializeField] AIDriven_ModelCardInstance[] modelCardInstances;

    [SerializeField] Image selectedModelImage;
    [SerializeField] TextMeshProUGUI titleText;
    [SerializeField] TextMeshProUGUI selectedModelText;
    [SerializeField] TextMeshProUGUI recommendModelText;
    [SerializeField] TextMeshProUGUI linkText;
    [SerializeField] TextMeshProUGUI modelNameText;
    [SerializeField] TextMeshProUGUI downloadButtonText;
    [SerializeField] TextMeshProUGUI infoText;
    [SerializeField] GameObject fileSelectButton;
    [SerializeField] AIDriven_LocalizationManager localizationManager;
    public ModelInfoConfig selectedModelInfo;
    ModelInfoConfig recommendModelInfo;

    public void Init()
    {
        ModelInfoConfig[] modelInfo;
        // 推奨モデルの設定
        if (AIDrivenConfig.isUseOllama)
        {
            modelInfo = AIDrivenConfig.Instance.OllamaRecommendModelInfos;
        }
        else
        {
            modelInfo = AIDrivenConfig.Instance.RecommendModelInfos;
        }

        recommendModelInfo = GetRecommendModel(modelInfo);
        selectedModelInfo = recommendModelInfo;

        // 保存されている設定があれば優先して選択
        var saved = AIDrivenFW.Config.ModelInfo.LoadFromFile();
        if (saved != null)
        {
            bool modeMatches = (AIDrivenConfig.isUseOllama && saved.Mode == "Ollama") || (!AIDrivenConfig.isUseOllama && saved.Mode == "Llama");
            if (modeMatches && !string.IsNullOrEmpty(saved.Name))
            {
                var found = System.Array.Find(modelInfo, m => m.ModelName == saved.Name);
                if (found != null)
                {
                    selectedModelInfo = found;
                }
                else
                {
                    //selectedModelInfo = recommendModelInfo;
                }
            }
            else
            {
                // モードが一致しない場合は推奨モデルを自動選択しない
                //selectedModelInfo = null;
            }
        }
        else
        {
            // Ollama モードのときは推奨モデルを自動で選択・表示しない
            if (AIDrivenConfig.isUseOllama)
            {
                //selectedModelInfo = null;
            }
            else
            {
                //selectedModelInfo = recommendModelInfo;
            }
        }
        if (recommendModelInfo == null) return;
        Debug.Log($"Recommended Model: {recommendModelInfo.ModelName} for VRAM: {AIDrivenConfig.GetVRAM()} MB");
        for (int i = 0; i < modelCardInstances.Length && i < modelInfo.Length; i++)
        {
            modelCardInstances[i].Init(modelInfo[i]);
            if (modelInfo[i] == recommendModelInfo)
            {
                modelCardInstances[i].SetRecommend(true);
            }
        }

        // 初期選択がない場合は選択表示を隠す
        if (selectedModelInfo == null)
        {
            HideSelectedModel();
        }

        Refresh();
    }

    public void HideSelectedModel()
    {
        selectedModelImage.gameObject.SetActive(false);
    }

    public void Refresh()
    {
        if (selectedModelInfo == null) { HideSelectedModel(); return; }
        selectedModelImage.gameObject.SetActive(true);
        switch (selectedModelInfo.Level)
        {
            case ModelLevel.Light:
                selectedModelImage.color = lightModelColor;
                break;
            case ModelLevel.Balanced:
                selectedModelImage.color = balanceModelColor;
                break;
            case ModelLevel.Powerful:
                selectedModelImage.color = powerfulModelColor;
                break;
        }
        selectedModelText.text = localizationManager.Get($"Model{selectedModelInfo.Level.ToString()}Label");
        recommendModelText.enabled = recommendModelInfo != null && selectedModelInfo.Level == recommendModelInfo.Level;
        if (manager.isOllama)
        {
            fileSelectButton.SetActive(false);
            downloadButtonText.text = localizationManager.Get("DownloadButton");
            infoText.text = localizationManager.Get("CARD_OllamaDownloadText");
            linkText.enabled = false;
            modelNameText.enabled = true;
            modelNameText.text = selectedModelInfo.ModelName;
        }
        else
        {
            fileSelectButton.SetActive(true);
            downloadButtonText.text = localizationManager.Get("FileSelectButton");
            infoText.text = localizationManager.Get("CARD_ModelDownloadText");
            linkText.enabled = true;
            modelNameText.enabled = false;
        }
    }

    public async void OnModelCardClicked(ModelInfoConfig modelInfo)
    {
        selectedModelInfo = modelInfo;
        manager.OnEnter();
    }

    /// <summary>
    /// オススメモデルをVRAMに応じて返す
    /// </summary>
    /// <returns>オススメモデル</returns>
    public ModelInfoConfig GetRecommendModel(ModelInfoConfig[] modelInfos)
    {
        int vram = AIDrivenConfig.GetVRAM();
        string vramStr = localizationManager.Get("MODAL_MemoryText").Replace("{a}", FormatVram(vram));
        titleText.text = vramStr;
        ModelInfoConfig recommendModel = null;
        foreach (var modelInfo in modelInfos)
        {
            if (modelInfo.MinVRAM <= vram && vram <= modelInfo.MaxVRAM)
            {
                recommendModel = modelInfo;
                break;
            }
        }
        return recommendModel ?? (modelInfos.Length > 0 ? modelInfos[0] : null);
    }

    /// <summary>
    /// フォーマットされたVRAMの文字列を返す
    /// </summary>
    /// <param name="mb">容量</param>
    /// <returns>フォーマットされたVRAMの文字列</returns>
    string FormatVram(int mb)
    {
        if (mb >= 1024)
        {
            return $"{mb / 1024}Gb";
        }
        return $"{mb}mb";
    }
}
