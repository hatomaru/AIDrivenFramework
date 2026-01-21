using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SetupGuideInstance : MonoBehaviour
{
    [SerializeField] Image fill;
    [SerializeField] TextMeshProUGUI guideText;

    public void OnEnableStep()
    {
        fill.color = new Color(1,1,1,0.71f);
        guideText.color = new Color(0.5921569f, 0.5647059f, 0.9686275f, 1);
    }

    public void OnDisableStep()
    {
        fill.color = new Color(1,1,1,1f);
        guideText.color = new Color(0.43f,0.43f,0.43f,1f);
    }
}
