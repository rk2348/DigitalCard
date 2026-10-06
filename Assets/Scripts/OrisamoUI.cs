using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ORISAMOの画面デザインの共通部品。
/// 配色・手続き生成スプライト(角丸パネル、金枠、光彩、魔法陣のリングなど)・
/// UI要素の生成ヘルパーをまとめ、タイトル/バトルの各画面が同じ見た目になるようにする。
/// 画像素材を用意しなくても製品レベルの見た目になるよう、スプライトはすべて実行時に生成してキャッシュする。
/// 画面はすべて 3840x2160(4K) を基準に配置する(CanvasScalerは UIAnimationDirector が設定する)。
/// </summary>
public static class OrisamoUI
{
    public static readonly Vector2 ReferenceResolution = new Vector2(3840f, 2160f);

    // ==================== 配色 ====================
    public static readonly Color Ink = new Color(0.035f, 0.03f, 0.07f, 1f);
    public static readonly Color Night = new Color(0.08f, 0.065f, 0.15f, 1f);
    public static readonly Color PanelFill = new Color(0.07f, 0.055f, 0.13f, 0.90f);
    public static readonly Color Gold = new Color(0.97f, 0.81f, 0.44f, 1f);
    public static readonly Color GoldLight = new Color(1f, 0.93f, 0.70f, 1f);
    public static readonly Color GoldDeep = new Color(0.62f, 0.43f, 0.16f, 1f);
    public static readonly Color Parchment = new Color(0.99f, 0.95f, 0.86f, 1f);
    public static readonly Color Muted = new Color(0.74f, 0.70f, 0.83f, 1f);
    public static readonly Color AttackColor = new Color(1f, 0.45f, 0.28f, 1f);
    public static readonly Color DefenseColor = new Color(0.36f, 0.68f, 1f, 1f);
    public static readonly Color Arcane = new Color(0.55f, 0.36f, 0.95f, 1f);

    public static Color WithAlpha(Color c, float a) { c.a = a; return c; }

    /// <summary>不透明のまま暗くした色(文字の縁取り用)。</summary>
    public static Color Darken(Color c, float factor) => new Color(c.r * factor, c.g * factor, c.b * factor, 1f);

    // ==================== フォント ====================
    private static TMP_FontAsset font;

    /// <summary>シーンに配置済みのTextMeshProと同じフォント(源暎アンチック)を使う。見つからなければTMPの既定フォント。</summary>
    public static TMP_FontAsset Font
    {
        get
        {
            if (font != null) return font;
            TextMeshProUGUI existing = Object.FindAnyObjectByType<TextMeshProUGUI>(FindObjectsInactive.Include);
            font = existing != null && existing.font != null ? existing.font : TMP_Settings.defaultFontAsset;
            return font;
        }
    }

    // ==================== 手続き生成スプライト ====================
    private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

    /// <summary>9スライス用の角丸パネル(白・上から下へわずかに暗くなる陰影つき)。Image.colorで着色して使う。</summary>
    public static Sprite RoundedPanel => GetOrCreate("panel", () => CreateRoundedRect(128, 36f, 0f, 0.80f));

    /// <summary>陰影なしの角丸パネル。</summary>
    public static Sprite RoundedFlat => GetOrCreate("flat", () => CreateRoundedRect(128, 36f, 0f, 1f));

    /// <summary>9スライス用の角丸の枠線(中は透明)。</summary>
    public static Sprite RoundedFrame => GetOrCreate("frame", () => CreateRoundedRect(128, 36f, 7f, 1f));

    /// <summary>細い角丸の枠線(内側の飾り線用)。</summary>
    public static Sprite RoundedHairline => GetOrCreate("hairline", () => CreateRoundedRect(128, 30f, 3f, 1f));

    /// <summary>中心から外へ柔らかく消える光彩。</summary>
    public static Sprite SoftGlow => GetOrCreate("glow", () => CreateRadial(256, t => Mathf.Pow(1f - t, 2.2f)));

    /// <summary>周辺を暗くするビネット(中心が透明)。</summary>
    public static Sprite Vignette => GetOrCreate("vignette", () => CreateRadial(256, t => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.05f, t))));

    /// <summary>くっきりした円。</summary>
    public static Sprite Circle => GetOrCreate("circle", () => CreateRing(256, 0f));

    /// <summary>細いリング(魔法陣・カウントダウン用)。</summary>
    public static Sprite ThinRing => GetOrCreate("thinring", () => CreateRing(512, 5f));

    /// <summary>太めのリング。</summary>
    public static Sprite ThickRing => GetOrCreate("thickring", () => CreateRing(256, 22f));

    /// <summary>ひし形(装飾用)。</summary>
    public static Sprite Diamond => GetOrCreate("diamond", () => CreateDiamond(128));

    /// <summary>上が不透明で下へ透明になる縦グラデーション(画面上下の陰り用)。</summary>
    public static Sprite FadeDown => GetOrCreate("fadedown", () => CreateVerticalFade(256));

    private static Sprite GetOrCreate(string key, System.Func<Sprite> factory)
    {
        if (spriteCache.TryGetValue(key, out Sprite sprite) && sprite != null) return sprite;
        sprite = factory();
        spriteCache[key] = sprite;
        return sprite;
    }

    /// <summary>角丸四角形を符号付き距離で描く。thickness&gt;0なら枠線、0なら塗り。bottomShadeは下端の明るさ。</summary>
    private static Sprite CreateRoundedRect(int size, float radius, float thickness, float bottomShade)
    {
        Texture2D tex = NewTexture(size, size);
        Color32[] pixels = new Color32[size * size];
        float half = size / 2f;
        for (int y = 0; y < size; y++)
        {
            float shade = Mathf.Lerp(bottomShade, 1f, (y + 0.5f) / size);
            for (int x = 0; x < size; x++)
            {
                float px = Mathf.Abs(x + 0.5f - half) - (half - radius - 1f);
                float py = Mathf.Abs(y + 0.5f - half) - (half - radius - 1f);
                float outside = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                float alpha = Mathf.Clamp01(0.5f - outside);
                if (thickness > 0f) alpha *= Mathf.Clamp01(outside + thickness + 0.5f);
                byte v = (byte)(255f * shade);
                pixels[y * size + x] = new Color32(v, v, v, (byte)(255f * alpha));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        float border = radius + thickness + 4f;
        return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect,
            new Vector4(border, border, border, border));
    }

    private static Sprite CreateRadial(int size, System.Func<float, float> alphaByDistance)
    {
        Texture2D tex = NewTexture(size, size);
        Color32[] pixels = new Color32[size * size];
        float half = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float t = Mathf.Clamp01(new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude / half);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(alphaByDistance(t))));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
    }

    private static Sprite CreateRing(int size, float thickness)
    {
        Texture2D tex = NewTexture(size, size);
        Color32[] pixels = new Color32[size * size];
        float outer = size / 2f - 1f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).magnitude;
                float alpha = Mathf.Clamp01(outer - d + 0.5f);
                if (thickness > 0f) alpha *= Mathf.Clamp01(d - (outer - thickness) + 0.5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255f * alpha));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
    }

    private static Sprite CreateDiamond(int size)
    {
        Texture2D tex = NewTexture(size, size);
        Color32[] pixels = new Color32[size * size];
        float half = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = (Mathf.Abs(x + 0.5f - half) + Mathf.Abs(y + 0.5f - half)) / half;
                float alpha = Mathf.Clamp01((1f - d) * half * 0.7f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255f * alpha));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
    }

    private static Sprite CreateVerticalFade(int height)
    {
        Texture2D tex = NewTexture(4, height);
        Color32[] pixels = new Color32[4 * height];
        for (int y = 0; y < height; y++)
        {
            float alpha = Mathf.SmoothStep(0f, 1f, (y + 0.5f) / height);
            for (int x = 0; x < 4; x++) pixels[y * 4 + x] = new Color32(255, 255, 255, (byte)(255f * alpha));
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, height), Vector2.one * 0.5f, 100f);
    }

    private static Texture2D NewTexture(int width, int height)
    {
        return new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
    }

    // ==================== 生成ヘルパー ====================

    public static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    public static Image CreateImage(string name, Transform parent, Sprite sprite, Color color, bool sliced = false)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        if (sliced)
        {
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 0.5f; // 4K基準の画面で枠と角丸が細くなりすぎないようにする
        }
        return image;
    }

    public static TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color,
        TextAlignmentOptions alignment = TextAlignmentOptions.Center, FontStyles style = FontStyles.Normal)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        go.AddComponent<UIStyledText>();
        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        if (Font != null) tmp.font = Font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.fontStyle = style;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        return tmp;
    }

    /// <summary>金色の縦グラデーション文字にする(ロゴや見出し用)。</summary>
    public static void ApplyGoldGradient(TextMeshProUGUI text)
    {
        text.color = Color.white;
        text.enableVertexGradient = true;
        text.colorGradient = new VertexGradient(GoldLight, GoldLight, GoldDeep, GoldDeep);
    }

    /// <summary>文字に縁取りと影(TMPのアウトライン/アンダーレイ)を付ける。マテリアルはこの文字専用の複製になる。</summary>
    public static void ApplyOutline(TextMeshProUGUI text, Color outline, float width, Color shadow, float shadowOffset = 1f)
    {
        Material material = text.fontMaterial;
        text.outlineColor = outline;
        text.outlineWidth = width;
        if (shadow.a > 0f)
        {
            material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            material.SetColor(ShaderUtilities.ID_UnderlayColor, shadow);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -shadowOffset);
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.35f);
        }
    }

    /// <summary>親いっぱいに広げる。</summary>
    public static RectTransform Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one * 0.5f;
        rect.offsetMin = Vector2.one * inset;
        rect.offsetMax = -Vector2.one * inset;
        return rect;
    }

    /// <summary>アンカー(=ピボット)を基準に位置とサイズを決める。</summary>
    public static RectTransform Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    /// <summary>中央基準で配置する。</summary>
    public static RectTransform PlaceCenter(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    /// <summary>
    /// 装飾つきパネル: 影 + 角丸の塗り + 金の外枠 + 細い内枠 + 上下中央のひし形飾り。
    /// 戻り値のRectTransformに中身を追加していく。
    /// </summary>
    public static RectTransform CreateOrnatePanel(string name, Transform parent, Color fill, Color border, bool ornaments = true)
    {
        RectTransform root = CreateRect(name, parent);

        Image shadow = CreateImage("Shadow", root, SoftGlow, new Color(0f, 0f, 0f, 0.55f));
        Stretch(shadow.rectTransform, -60f);
        shadow.rectTransform.anchoredPosition = new Vector2(0f, -18f);

        Stretch(CreateImage("Fill", root, RoundedPanel, fill, true).rectTransform);
        Stretch(CreateImage("Border", root, RoundedFrame, border, true).rectTransform);
        Stretch(CreateImage("InnerLine", root, RoundedHairline, WithAlpha(border, border.a * 0.35f), true).rectTransform, 16f);

        if (ornaments)
        {
            foreach (float y in new[] { 1f, 0f })
            {
                Image gem = CreateImage("Gem", root, Diamond, border);
                gem.rectTransform.anchorMin = gem.rectTransform.anchorMax = new Vector2(0.5f, y);
                gem.rectTransform.sizeDelta = new Vector2(46f, 46f);
                gem.rectTransform.anchoredPosition = Vector2.zero;
            }
        }
        return root;
    }

    /// <summary>中央にひし形を置いた金の飾り罫(見出しの下などに使う)。</summary>
    public static RectTransform CreateDivider(Transform parent, float width, Color color)
    {
        RectTransform root = CreateRect("Divider", parent);
        root.sizeDelta = new Vector2(width, 40f);
        foreach (int side in new[] { -1, 1 })
        {
            Image line = CreateImage("Line", root, RoundedFlat, WithAlpha(color, 0.8f), true);
            line.pixelsPerUnitMultiplier = 8f;
            PlaceCenter(line.rectTransform, new Vector2(side * (width / 4f + 20f), 0f), new Vector2(width / 2f - 60f, 5f));
        }
        Image gem = CreateImage("Gem", root, Diamond, color);
        PlaceCenter(gem.rectTransform, Vector2.zero, new Vector2(40f, 40f));
        return root;
    }

    /// <summary>
    /// 属性の紋章: 属性色の光彩 + 濃い円 + 属性色のリング + 属性の漢字。
    /// </summary>
    public static RectTransform CreateElementEmblem(Transform parent, ElementType element, float size)
    {
        Color color = ElementAffinity.GetElementColor(element);
        RectTransform root = CreateRect("ElementEmblem", parent);
        root.sizeDelta = new Vector2(size, size);

        Image glow = CreateImage("Glow", root, SoftGlow, WithAlpha(color, 0.75f));
        PlaceCenter(glow.rectTransform, Vector2.zero, Vector2.one * size * 1.7f);
        Image disc = CreateImage("Disc", root, Circle, Color.Lerp(Ink, color, 0.35f));
        PlaceCenter(disc.rectTransform, Vector2.zero, Vector2.one * size);
        Image ring = CreateImage("Ring", root, ThickRing, Color.Lerp(color, Color.white, 0.25f));
        PlaceCenter(ring.rectTransform, Vector2.zero, Vector2.one * size);
        Image goldRing = CreateImage("GoldRing", root, ThinRing, Gold);
        PlaceCenter(goldRing.rectTransform, Vector2.zero, Vector2.one * (size + 14f));

        TextMeshProUGUI kanji = CreateText("Kanji", root, ElementKanji(element), size * 0.52f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(kanji.rectTransform);
        ApplyOutline(kanji, Color.Lerp(Ink, color, 0.3f), 0.25f, new Color(0f, 0f, 0f, 0.6f));
        return root;
    }

    // ==================== 表記 ====================

    public static string ElementKanji(ElementType element)
    {
        switch (element)
        {
            case ElementType.Fire: return "火";
            case ElementType.Wind: return "風";
            case ElementType.Dark: return "闇";
            case ElementType.Water: return "水";
            case ElementType.Earth: return "地";
            case ElementType.Light: return "光";
            default: return "?";
        }
    }

    public static string LevelKanji(AttackLevel level)
    {
        switch (level)
        {
            case AttackLevel.Strong: return "強";
            case AttackLevel.Weak: return "弱";
            default: return "普";
        }
    }

    public static Color LevelColor(AttackLevel level)
    {
        switch (level)
        {
            case AttackLevel.Strong: return new Color(1f, 0.38f, 0.30f, 1f);
            case AttackLevel.Weak: return new Color(0.45f, 0.80f, 1f, 1f);
            default: return new Color(1f, 0.85f, 0.40f, 1f);
        }
    }

    /// <summary>
    /// Imageの色を時間とともに一律に変えたいときのための簡易イージング(0〜1)。
    /// </summary>
    public static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    public static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
}

/// <summary>
/// ORISAMOのデザインで装飾済みのテキストの目印。
/// UIAnimationDirectorの共通タイポグラフィ(Shadow付与)の対象から外すために使う。
/// </summary>
public sealed class UIStyledText : MonoBehaviour { }
