using Cysharp.Threading.Tasks;
using LitMotion;
using TMPro;
using UnityEngine;

public class FadeModule : MonoBehaviour
{
    private CanvasGroup canvasGroup;

    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0;
        canvasGroup.blocksRaycasts = false;
        OnLoad().Forget();
    }

    // Update is called once per frame
    void Update()
    {

    }

    /// <summary>
    /// ロード開始時
    /// </summary>
    public async UniTask OnLoad()
    {
        // Animate canvasGroup.alpha from 0 to 1
        await LMotion.Create(0f, 1f, 0.3f)
            .WithOnComplete(() => { canvasGroup.blocksRaycasts = true; })
            .WithEase(Ease.InCirc)
            .Bind(value => canvasGroup.alpha = value)
            .AddTo(gameObject);
    }

    /// <summary>
    /// ロード完了時
    /// </summary>
    public void OnComplete()
    {
        LMotion.Create(1f, 0f, 0.25f)
       .WithOnComplete(() => { canvasGroup.blocksRaycasts = false; canvasGroup.alpha = 0; })
       .WithEase(Ease.OutCirc)
       .Bind(value => canvasGroup.alpha = value)
       .AddTo(gameObject);
    }
}
