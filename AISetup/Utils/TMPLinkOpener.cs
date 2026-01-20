using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(TMP_Text))]
public class TMPLinkOpener : MonoBehaviour, IPointerClickHandler
{
    TMP_Text tmpText;

    void Awake()
    {
        tmpText = GetComponent<TMP_Text>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        int linkIndex = TMP_TextUtilities.FindIntersectingLink(
            tmpText,
            eventData.position,
            eventData.pressEventCamera
        );

        if (linkIndex == -1) return;

        var linkInfo = tmpText.textInfo.linkInfo[linkIndex];
        string url = linkInfo.GetLinkID();

        Application.OpenURL(url);
    }
}
