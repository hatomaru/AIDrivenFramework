using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Selects an input backend on the existing EventSystem; never creates visual UI objects.</summary>
[RequireComponent(typeof(EventSystem))]
public sealed class SetupCanvasInputBridge : MonoBehaviour
{
    void Awake()
    {
#if ENABLE_INPUT_SYSTEM
        if (GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null)
            gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        if (GetComponent<StandaloneInputModule>() == null) gameObject.AddComponent<StandaloneInputModule>();
#endif
    }
}
