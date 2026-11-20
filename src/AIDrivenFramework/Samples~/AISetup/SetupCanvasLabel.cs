using UnityEngine;
using UnityEngine.UI;

/// <summary>Editable bilingual copy, stored with each Text object in the scene.</summary>
[RequireComponent(typeof(Text))]
public sealed class SetupCanvasLabel : MonoBehaviour
{
    [TextArea(1, 5)] public string Japanese;
    [TextArea(1, 5)] public string English;
    public void Apply(bool japanese) => GetComponent<Text>().text = japanese ? Japanese : English;
}
