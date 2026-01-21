using LitMotion;
using LitMotion.Extensions;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class PopupManager : MonoBehaviour
{
    public bool isAccsept = false;
    public bool isPopuped = false;
    [SerializeField] Vector3 popupdScale;
    [SerializeField] Image bg;
    [SerializeField] RectTransform continer;
    [SerializeField] ScrollRect ScrollBar;

    MotionHandle backgroundMotionHandle;
    MotionHandle containerMotionHandle;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        bg.raycastTarget = false;
        continer.localScale = Vector3.zero;
        LMotion.Create(0f, 0f, 0f)
            .BindToColorA(bg);
    }

    public void Accept()
    {
        isAccsept = true;    
    }

    public void Reject()
    {
        isAccsept = false;
    }

    public void Popup()
    {
        isPopuped = true;
        bg.raycastTarget = true;
        if (ScrollBar != null)
        {
            // スクロールバーを一番上に移動
            // レイアウトを強制再構築してから位置をセットし、念のため次フレームでも再セットする
            if (ScrollBar.content != null)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(ScrollBar.content);
            }

            ScrollBar.verticalNormalizedPosition = 1f;
            ScrollBar.velocity = Vector2.zero; // 動きを止める
            StartCoroutine(EnsureScrollTopNextFrame());
        }

        backgroundMotionHandle.TryCancel();
        backgroundMotionHandle = LMotion.Create(0f, 0.5f, 0.2f)
            .WithEase(Ease.OutBack)
            .BindToColorA(bg);
        containerMotionHandle.TryCancel();
        containerMotionHandle = LMotion.Create(Vector3.zero, popupdScale, 0.5f)
            .WithEase(Ease.OutBack)
            .BindToLocalScale(continer);
    }

    /// <summary>
    /// スクロールバーを確実に一番上にするため、次フレームでも再設定する
    /// </summary>
    IEnumerator EnsureScrollTopNextFrame()
    {
        yield return null;
        if (ScrollBar.content != null)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(ScrollBar.content);
        }

        ScrollBar.verticalNormalizedPosition = 1f;
        ScrollBar.velocity = Vector2.zero;
    }

    public void Close()
    {
        if(!isPopuped)
        {
            return;
        }
        isPopuped = false;
        bg.raycastTarget = false;
        backgroundMotionHandle.TryCancel();
        backgroundMotionHandle = LMotion.Create(0.5f, 0f, 0.6f)
            .WithEase(Ease.InBack)
            .BindToColorA(bg);
        containerMotionHandle.TryCancel();
        containerMotionHandle = LMotion.Create(popupdScale, Vector3.zero, 0.4f)
            .WithEase(Ease.InBack)
            .BindToLocalScale(continer);
    }

    private void OnDestroy()
    {
        backgroundMotionHandle.TryCancel();
        containerMotionHandle.TryCancel();
    }
}