using UnityEngine;

/// <summary>Compatibility facade for helper references in previously imported projects.</summary>
public sealed class AIDriven_AIDrivenManager : MonoBehaviour
{
    public bool isOllama = true;
    public bool isCheckedOllama;
    void Start() { if (FindFirstObjectByType<PlayerSetupScreen>() == null) Debug.LogWarning("Open the authored AIDrivenSetup scene to use the setup Canvas."); }
    public void OnEnter() { }
    public void OnBack() { }
    public void SetOllamaMode() { isOllama = true; }
    public void SetLlamaMode() { isOllama = false; }
}
