using UnityEngine;
using LitMotion;
using LitMotion.Extensions;
using Cysharp.Threading.Tasks;
using System.Threading;
using AIDrivenFW;

public class CardUIManager : MonoBehaviour
{
    const float kPosStartX = -163f;
    const float kPosEndX = 130f;
    CanvasGroup canvasGroup;
    RectTransform rectTransform;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        rectTransform = GetComponent<RectTransform>();
        canvasGroup.alpha = 0;
    }

    public async UniTask Show()
    {
        gameObject.SetActive(true);
        _ = LMotion.Create(0f, 1f, 0.3f)
            .WithOnComplete(() => { canvasGroup.blocksRaycasts = true; })
            .WithEase(Ease.InCirc)
            .Bind(value => canvasGroup.alpha = value)
            .AddTo(gameObject);
        await LMotion.Create(new Vector2(kPosStartX, 0), Vector2.zero, 0.2f)
            .WithEase(Ease.OutCirc)
            .BindToAnchoredPosition(rectTransform)
            .AddTo(gameObject);

    }

    public async UniTask Hide()
    {
        _ = LMotion.Create(1f, 0f, 0.25f)
           .WithOnComplete(() => { canvasGroup.blocksRaycasts = false; canvasGroup.alpha = 0; gameObject.SetActive(false);})
           .WithEase(Ease.OutCirc)
           .Bind(value => canvasGroup.alpha = value)
           .AddTo(gameObject);
        await LMotion.Create(Vector2.zero, new Vector2(kPosEndX,0), 0.25f)
            .WithEase(Ease.InOutCirc)
            .BindToAnchoredPosition(rectTransform)
            .AddTo(gameObject);

    }
}
