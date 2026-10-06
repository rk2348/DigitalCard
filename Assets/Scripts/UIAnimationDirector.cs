using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>Provides a consistent, unobtrusive entrance and button response for every UI scene.</summary>
public sealed class UIAnimationDirector : MonoBehaviour
{
    private static bool installed;
    private static readonly System.Collections.Generic.HashSet<int> animatingTargets = new System.Collections.Generic.HashSet<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (!installed)
        {
            installed = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        AttachToCanvases();
    }

    private static void OnSceneLoaded(Scene _, LoadSceneMode __) => AttachToCanvases();

    private static void AttachToCanvases()
    {
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (canvas.transform.parent == null || canvas.transform.parent.GetComponent<Canvas>() == null)
            {
                ConfigureScaler(canvas);
                if (canvas.GetComponent<UIAnimationDirector>() == null) canvas.gameObject.AddComponent<UIAnimationDirector>();
            }
        }
    }

    /// <summary>
    /// 各シーンのUIは 3840x2160(4K) を基準に配置されている。CanvasScalerが「固定ピクセル」のままだと
    /// フルHDなど4K以外の画面ではみ出すため、画面サイズに合わせて拡大縮小する設定に揃える。
    /// </summary>
    public static void ConfigureScaler(Canvas canvas)
    {
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = OrisamoUI.ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    private void Start()
    {
        StartCoroutine(PlayEntrance());
        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            if (button.GetComponent<UIButtonMotion>() == null) button.gameObject.AddComponent<UIButtonMotion>();
            if (button.GetComponent<UIProductButtonStyle>() == null) button.gameObject.AddComponent<UIProductButtonStyle>();
        }

        ApplyTypography();
    }

    private void ApplyTypography()
    {
        foreach (TextMeshProUGUI text in GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            text.raycastTarget = false;
            if (text.GetComponent<UIStyledText>() != null) continue; // ORISAMOのデザインで装飾済み

            Shadow shadow = text.GetComponent<Shadow>() ?? text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.02f, 0.04f, 0.10f, 0.72f);
            shadow.effectDistance = text.fontSize >= 28f ? new Vector2(2f, -3f) : new Vector2(1f, -2f);
            shadow.useGraphicAlpha = true;
        }
    }

    private IEnumerator PlayEntrance()
    {
        yield return null; // lets scene-specific Start methods finish constructing UI first
        int index = 0;
        foreach (Transform child in transform)
        {
            if (!child.gameObject.activeInHierarchy || child.GetComponent<UIAnimationIgnore>() != null) continue;
            StartCoroutine(AnimateIn(child as RectTransform, index++ * 0.045f));
        }
    }

    private static IEnumerator AnimateIn(RectTransform target, float delay)
    {
        if (target == null) yield break;
        int targetId = target.GetInstanceID();
        if (!animatingTargets.Add(targetId)) yield break;

        try
        {
            yield return new WaitForSecondsRealtime(delay);
            if (target == null || !target.gameObject.activeInHierarchy) yield break;

            if (!target.TryGetComponent(out CanvasGroup group))
            {
                group = target.gameObject.AddComponent<CanvasGroup>();
            }
            if (group == null) yield break;

            float initialAlpha = group.alpha;
            Vector3 finalScale = target.localScale;
            Vector2 finalPosition = target.anchoredPosition;
            group.alpha = 0f;
            target.localScale = finalScale * 0.94f;
            target.anchoredPosition = finalPosition + Vector2.up * 18f;
            const float duration = 0.34f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                if (target == null || group == null) yield break;

                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                group.alpha = Mathf.Lerp(0f, initialAlpha, eased);
                target.localScale = Vector3.LerpUnclamped(finalScale * 0.94f, finalScale, 1f - Mathf.Pow(1f - t, 4f));
                target.anchoredPosition = Vector2.Lerp(finalPosition + Vector2.up * 18f, finalPosition, eased);
                yield return null;
            }
            if (target != null && group != null)
            {
                group.alpha = initialAlpha;
                target.localScale = finalScale;
                target.anchoredPosition = finalPosition;
            }
        }
        finally
        {
            animatingTargets.Remove(targetId);
        }
    }
}

/// <summary>Opt out marker for UI that must never be moved or faded by the scene entrance.</summary>
public sealed class UIAnimationIgnore : MonoBehaviour { }

/// <summary>Small hover and press feedback added automatically to Unity UI Buttons.</summary>
public sealed class UIButtonMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private Vector3 restingScale;
    private float targetScale = 1f;
    private bool initialized;

    private void Awake()
    {
        restingScale = transform.localScale;
        initialized = true;
    }

    private void OnEnable()
    {
        if (!initialized) return;
        targetScale = 1f;
    }

    private void Update()
    {
        transform.localScale = Vector3.Lerp(transform.localScale, restingScale * targetScale, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
    }

    public void OnPointerEnter(PointerEventData eventData) { if (IsUsable()) targetScale = 1.035f; }
    public void OnPointerExit(PointerEventData eventData) => targetScale = 1f;
    public void OnPointerDown(PointerEventData eventData) { if (IsUsable()) targetScale = 0.955f; }
    public void OnPointerUp(PointerEventData eventData) => targetScale = IsUsable() ? 1.035f : 1f;
    public void Punch()
    {
        if (gameObject.activeInHierarchy) StartCoroutine(PunchRoutine());
    }

    private IEnumerator PunchRoutine()
    {
        targetScale = 1.08f;
        yield return new WaitForSecondsRealtime(0.09f);
        targetScale = 1f;
    }

    private bool IsUsable() => GetComponent<Button>() is Button button && button.interactable;
}

/// <summary>
/// 通常のUnityボタンを、ORISAMOのファンタジー調(濃紫の地に金の縁取り、ホバーで光る)に揃える。
/// SetPrimary(true) にすると金地に濃い文字の「主役ボタン」になる。
/// </summary>
[RequireComponent(typeof(Button))]
public sealed class UIProductButtonStyle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    private static readonly Color Surface = new Color(0.10f, 0.07f, 0.19f, 0.96f);
    private static readonly Color SurfaceHover = new Color(0.17f, 0.11f, 0.30f, 0.98f);
    private static readonly Color PrimarySurface = new Color(0.95f, 0.76f, 0.36f, 1f);
    private static readonly Color PrimaryHover = new Color(1f, 0.86f, 0.52f, 1f);
    private static readonly Color Disabled = new Color(0.12f, 0.11f, 0.16f, 0.6f);

    private Button button;
    private Image background;
    private Image frame;
    private Image glow;
    private TextMeshProUGUI label;
    private bool highlighted;
    private bool primary;

    private void Awake()
    {
        button = GetComponent<Button>();
        background = GetComponent<Image>();
        if (background == null) return;

        // 旧スタイルの影・縁取りが残っていれば外す
        foreach (Shadow effect in GetComponents<Shadow>()) Destroy(effect);

        background.sprite = OrisamoUI.RoundedPanel;
        background.type = Image.Type.Sliced;
        background.pixelsPerUnitMultiplier = 0.5f;

        // 光彩はボタンの子にするとボタンの上に描かれてしまうため、親の中でボタンの直前に置き、毎フレーム位置を合わせる
        if (transform.parent != null)
        {
            glow = OrisamoUI.CreateImage(name + "_Glow", transform.parent, OrisamoUI.SoftGlow, OrisamoUI.WithAlpha(OrisamoUI.Gold, 0f));
            glow.transform.SetSiblingIndex(transform.GetSiblingIndex());
            glow.rectTransform.anchorMin = glow.rectTransform.anchorMax = glow.rectTransform.pivot = Vector2.one * 0.5f;
            glow.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        }

        frame = OrisamoUI.CreateImage("Frame", transform, OrisamoUI.RoundedFrame, OrisamoUI.Gold, true);
        OrisamoUI.Stretch(frame.rectTransform);
        frame.transform.SetSiblingIndex(1);
        Image inner = OrisamoUI.CreateImage("InnerLine", transform, OrisamoUI.RoundedHairline, OrisamoUI.WithAlpha(OrisamoUI.Gold, 0.35f), true);
        OrisamoUI.Stretch(inner.rectTransform, 14f);
        inner.transform.SetSiblingIndex(2);

        label = GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.gameObject.AddComponent<UIStyledText>();
            if (OrisamoUI.Font != null) label.font = OrisamoUI.Font;
            label.fontStyle = FontStyles.Bold;
            Shadow shadow = label.GetComponent<Shadow>();
            if (shadow != null) Destroy(shadow);
        }

        ColorBlock colors = button.colors;
        colors.normalColor = colors.highlightedColor = colors.selectedColor = colors.disabledColor = Color.white;
        colors.pressedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        ApplyLabelColor();
    }

    /// <summary>主役ボタン(金地)にするかどうか。</summary>
    public void SetPrimary(bool value)
    {
        primary = value;
        ApplyLabelColor();
        Refresh(false);
    }

    private void ApplyLabelColor()
    {
        if (label != null) label.color = primary ? new Color(0.16f, 0.08f, 0.03f, 1f) : OrisamoUI.Parchment;
        if (frame != null) frame.color = primary ? OrisamoUI.GoldLight : OrisamoUI.Gold;
    }

    private void OnEnable()
    {
        Refresh(false);
    }

    private void OnDisable()
    {
        if (glow != null) glow.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (glow != null) Destroy(glow.gameObject);
    }

    private void Update()
    {
        Refresh(true);
    }

    private void Refresh(bool animated)
    {
        if (background == null || button == null) return;

        Color target = !button.interactable ? Disabled
            : primary ? (highlighted ? PrimaryHover : PrimarySurface)
            : (highlighted ? SurfaceHover : Surface);
        float rate = animated ? 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime) : 1f;
        background.color = Color.Lerp(background.color, target, rate);

        if (glow != null)
        {
            RectTransform rect = (RectTransform)transform;
            glow.gameObject.SetActive(gameObject.activeInHierarchy);
            glow.rectTransform.position = rect.position;
            glow.rectTransform.sizeDelta = rect.rect.size * (Vector2)rect.localScale + new Vector2(260f, 220f);
            float pulse = primary ? 0.25f + 0.12f * Mathf.Sin(Time.unscaledTime * 2.4f) : 0f;
            float alpha = button.interactable ? (highlighted ? 0.6f : pulse) : 0f;
            glow.color = Color.Lerp(glow.color, OrisamoUI.WithAlpha(OrisamoUI.Gold, alpha), rate);
        }
    }

    public void OnPointerEnter(PointerEventData eventData) => highlighted = true;
    public void OnPointerExit(PointerEventData eventData) => highlighted = false;
    public void OnSelect(BaseEventData eventData) => highlighted = true;
    public void OnDeselect(BaseEventData eventData) => highlighted = false;
}
