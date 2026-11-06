using AIDrivenFW.Config;
using TMPro;
using UnityEngine;

public class AIDriven_ModelCardInstance : MonoBehaviour
{
    AIDriven_ModelSelecter fileSelectModule;
    ModelInfoConfig modelInfo;
    [SerializeField] GameObject recommendObj;
    [SerializeField] TextMeshProUGUI levelext;
    [SerializeField] TextMeshProUGUI vramText;
    [SerializeField] TextMeshProUGUI modelNameText;
    [SerializeField] AIDriven_LocalizationManager localizationManager;

    private void Awake()
    {
        recommendObj.SetActive(false);
        fileSelectModule = GameObject.Find("Manager").GetComponent<AIDriven_ModelSelecter>();
    }

    public void Init(ModelInfoConfig modelInfo)
    {
        this.modelInfo = modelInfo;
        levelext.text = localizationManager.Get($"Model{modelInfo.Level.ToString()}Label");
        string levelKey = localizationManager.Get("MoreMemoryLabel").Replace("{a}", FormatVram(modelInfo.MinVRAM));
        vramText.text = levelKey;
        modelNameText.text = modelInfo.ModelName;
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

    /// <summary>
    /// オススメラベルの表示を切り替える
    /// </summary>
    /// <param name="isRecommend">表示状態</param>
    public void SetRecommend(bool isRecommend)
    {
        recommendObj.SetActive(isRecommend);
    }

    public void OnClick()
    {
        fileSelectModule.OnModelCardClicked(modelInfo);
    }
}
