using LitMotion;
using LitMotion.Extensions;
using UnityEngine;
using UnityEngine.UI;

public class HoverButtonManager : MonoBehaviour
{
    [SerializeField] Vector3 defaultScale;
    [SerializeField] float multiplier = 1.05f;
    Button button;
    RectTransform rect;
    MotionHandle motionHandle;

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        if(GetComponent<Button>() != null)
        {
            button = GetComponent<Button>();
        }
    }

    /// <summary>
    /// 表示する
    /// </summary>
    public void Show()
    {
        if (button != null && !button.interactable)
            return;
        motionHandle = LMotion.Create(defaultScale, defaultScale * multiplier, 0.25f)
            .WithEase(Ease.InOutSine)
            .BindToLocalScale(rect);
    }

    /// <summary>
    /// 非表示にする
    /// </summary>
    public void Hide()
    {
        if (button != null && !button.interactable)
            return;
        motionHandle.TryComplete();
        motionHandle = LMotion.Create(defaultScale * multiplier, defaultScale, 0.22f)
            .WithEase(Ease.InOutSine)
            .BindToLocalScale(rect);
    }

    private void OnDestroy()
    {
        motionHandle.TryCancel();
    }
}