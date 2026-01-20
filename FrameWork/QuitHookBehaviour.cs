using UnityEngine;
using UnityEngine.Events;

internal sealed class QuitHookBehaviour : MonoBehaviour
{
    public static UnityAction onProcessKill;

    void OnApplicationQuit()
    {
        onProcessKill?.Invoke();
    }
}
