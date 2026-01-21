using UnityEngine;
using LitMotion;
using Cysharp.Threading.Tasks;
using TMPro;

public class LoadingModule : MonoBehaviour
{
    private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI reasonText;

    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0;
        canvasGroup.blocksRaycasts = false;
        //OnLoad("初期化中...").Forget();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    /// <summary>
    /// ロード開始時
    /// </summary>
    /// <param name="reason"></param>
    public async UniTask OnLoad(string reason)
    {
        reasonText.text = reason;
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
