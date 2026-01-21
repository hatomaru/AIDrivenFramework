using UnityEngine;

public class NavigationBarUIManager : MonoBehaviour
{
    [SerializeField] SetupGuideInstance[] steps;
    public GameObject onBoardUI;
    public GameObject stepNavigationUI;

    public void ActivateStep(int stepIndex)
    {
        for (int i = 0; i < steps.Length; i++)
        {
            if (i == stepIndex)
            {
                steps[i].OnEnableStep();
            }
            else
            {
                steps[i].OnDisableStep();
            }
        }
    }
}
