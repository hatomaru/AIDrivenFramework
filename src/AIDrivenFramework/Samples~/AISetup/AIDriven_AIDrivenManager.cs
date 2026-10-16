using UnityEngine;

/// <summary>Compatibility facade for helper references in previously imported projects.</summary>
public sealed class AIDriven_AIDrivenManager : MonoBehaviour
{
    public bool isOllama = true;
    public bool isCheckedOllama;
    void Start() { if (GetComponent<PlayerSetupScreen>() == null) gameObject.AddComponent<PlayerSetupScreen>(); }
    public void OnEnter() { }
    public void OnBack() { }
    public void SetOllamaMode() { isOllama = true; }
    public void SetLlamaMode() { isOllama = false; }
}
