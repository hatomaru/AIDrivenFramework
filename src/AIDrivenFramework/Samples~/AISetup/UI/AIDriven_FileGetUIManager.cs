using TMPro;
using UnityEngine;

public class AIDriven_FileGetUIManager : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI guideText;
    [SerializeField] TextMeshProUGUI linkText;
    [SerializeField] AIDriven_TMPAutoLinkify tMPAutoLinkify;

    public void Init(string text,string link)
    {
        guideText.text = text;
        linkText.text = link;
        tMPAutoLinkify.ConvertUrlsToLinks();
    }
}
