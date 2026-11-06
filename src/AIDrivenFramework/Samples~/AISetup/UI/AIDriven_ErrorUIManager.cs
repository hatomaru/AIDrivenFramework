using TMPro;
using UnityEngine;

public class AIDriven_ErrorUIManager : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI messageText;

    public void Init(string message)
    {
        messageText.text = message;
    }
}
