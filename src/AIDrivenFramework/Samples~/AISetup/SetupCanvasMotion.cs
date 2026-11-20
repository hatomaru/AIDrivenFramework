using UnityEngine;
using UnityEngine.UI;

/// <summary>Animates existing scene objects only. Text, panels and artwork are authored assets.</summary>
public sealed class SetupCanvasMotion : MonoBehaviour
{
    public Toggle ReduceMotion;
    public RectTransform Backdrop;
    public RectTransform[] FloatingIcons;
    public RectTransform[] Sparks;
    public Image[] CompletionSparks;
    RectTransform current;
    Vector2[] iconOrigins;
    Vector2 backgroundOrigin;
    float changed;
    bool completed;
    void Awake()
    {
        backgroundOrigin = Backdrop.anchoredPosition;
        iconOrigins = new Vector2[FloatingIcons.Length];
        for (int i = 0; i < iconOrigins.Length; i++) iconOrigins[i] = FloatingIcons[i].anchoredPosition;
    }
    public void Arrive(RectTransform slide, bool complete)
    {
        if (current != null) { current.anchoredPosition = Vector2.zero; current.localScale = Vector3.one; }
        current = slide; changed = Time.unscaledTime; completed = complete;
    }
    void Update()
    {
        bool reduced = ReduceMotion != null && ReduceMotion.isOn;
        float time = Time.unscaledTime, age = time - changed;
        if (Backdrop != null) Backdrop.anchoredPosition = backgroundOrigin + (reduced ? Vector2.zero : new Vector2(Mathf.Sin(time * .16f) * 12, Mathf.Sin(time * .12f) * 8));
        if (current != null)
        {
            float t = reduced ? 1 : Mathf.Clamp01(age / .48f), ease = 1 - Mathf.Pow(1 - t, 3);
            current.anchoredPosition = new Vector2(0, (1 - ease) * -16);
            current.localScale = Vector3.one * (.985f + .015f * ease);
        }
        for (int i = 0; i < FloatingIcons.Length; i++)
        {
            FloatingIcons[i].anchoredPosition = iconOrigins[i] + new Vector2(0, reduced ? 0 : Mathf.Sin(time * 1.8f) * 3);
            FloatingIcons[i].localRotation = Quaternion.Euler(0, 0, reduced ? 0 : Mathf.Sin(time * .9f) * 2);
        }
        for (int i = 0; i < Sparks.Length; i++)
        {
            float a = i % 3 * Mathf.PI * 2 / 3 + (reduced ? 0 : time * .38f);
            Sparks[i].anchoredPosition = new Vector2(Mathf.Cos(a) * 65, Mathf.Sin(a) * 43);
        }
        for (int i = 0; i < CompletionSparks.Length; i++)
        {
            float t = Mathf.Clamp01(age / 1.2f), a = i * Mathf.PI / 4;
            CompletionSparks[i].rectTransform.anchoredPosition = new Vector2(Mathf.Cos(a) * (48 + t * 32), Mathf.Sin(a) * (48 + t * 16));
            var c = CompletionSparks[i].color; c.a = !reduced && completed ? Mathf.Sin(t * Mathf.PI) * .8f : 0; CompletionSparks[i].color = c;
        }
    }
}
