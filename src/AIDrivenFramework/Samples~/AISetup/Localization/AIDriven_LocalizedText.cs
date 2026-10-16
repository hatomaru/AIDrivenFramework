using TMPro;
using UnityEngine;

[RequireComponent(typeof(TextMeshProUGUI))]

public class AIDriven_LocalizedText : MonoBehaviour
{
    AIDriven_LocalizationManager localizationManager;
    [SerializeField] private string localizationKey;

    private TextMeshProUGUI textComponent;

    void Start()
    {
        localizationManager = GameObject.Find("Localization").GetComponent<AIDriven_LocalizationManager>();
        textComponent = GetComponent<TextMeshProUGUI>();
        UpdateText();
    }

    public void UpdateText()
    {
        if (!string.IsNullOrEmpty(localizationKey))
        {
            textComponent.text = localizationManager.Get(localizationKey);
            localizationManager.OnLanguageChanged += UpdateText;
        }
    }
}
