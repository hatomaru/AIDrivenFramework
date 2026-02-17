using UnityEngine;
using UnityEngine.Events;

public class QuitHookBehaviour : MonoBehaviour
{
    public static UnityAction onProcessKill;

    void OnApplicationQuit()
    {
        onProcessKill?.Invoke();
    }
}
