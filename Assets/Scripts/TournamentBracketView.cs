using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// トーナメントの組み合わせ表と、優勝者の表彰をバトル画面に重ねて表示する(4K基準の座標)。
///
/// 組み合わせ表は左から 1回戦 → … → 決勝 → 優勝 の列で並べ、勝ち上がった線を金色にする。
/// 次に行う試合の2枠は点滅させ、直前の試合で勝ち上がった枠は弾むように光らせる。
/// 演出はスローモーションの影響を受けないよう unscaledDeltaTime で動かす。
/// </summary>
public sealed class TournamentBracketView : MonoBehaviour
{
    private const float BracketTop = 400f;
    private const float BracketHeight = 1620f;
    private const float SideMargin = 140f;
    private const float LineThickness = 6f;

    private static readonly Color LineIdle = new Color(0.45f, 0.42f, 0.55f, 0.7f);
    private static readonly Color BoxFill = new Color(0.10f, 0.08f, 0.18f, 0.95f);

    private RectTransform root;
    private RectTransform content;
    private readonly List<Image> pulsing = new List<Image>();
    private readonly List<Texture2D> textures = new List<Texture2D>();

    public static TournamentBracketView Create(Canvas canvas)
    {
        TournamentBracketView view = OrisamoUI.CreateRect("TournamentBracket", canvas.transform).gameObject.AddComponent<TournamentBracketView>();
        view.root = OrisamoUI.Stretch((RectTransform)view.transform);
        view.gameObject.AddComponent<UIAnimationIgnore>();
        view.root.gameObject.SetActive(false);
        return view;
    }

    private void OnDestroy()
    {
        foreach (Texture2D texture in textures) if (texture != null) Destroy(texture);
    }

    private void Update()
    {
        float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 6f);
        foreach (Image image in pulsing)
        {
            if (image != null) image.color = OrisamoUI.WithAlpha(OrisamoUI.GoldLight, pulse);
        }
    }

    // ==================== 組み合わせ表 ====================

    /// <summary>組み合わせ表を出す。nextRound/nextMatch は次に行う試合(なければ -1)。</summary>
    public void Show(Tournament tournament, int nextRound, int nextMatch, string caption, string subCaption)
    {
        Clear();
        root.SetAsLastSibling();
        root.gameObject.SetActive(true);

        Image shade = OrisamoUI.CreateImage("Shade", content, null, new Color(0.02f, 0.01f, 0.05f, 0.88f));
        OrisamoUI.Stretch(shade.rectTransform);
        Image glow = OrisamoUI.CreateImage("Glow", content, OrisamoUI.SoftGlow, OrisamoUI.WithAlpha(OrisamoUI.Arcane, 0.3f));
        OrisamoUI.PlaceCenter(glow.rectTransform, new Vector2(0f, 700f), new Vector2(3600f, 1100f));

        TextMeshProUGUI title = OrisamoUI.CreateText("Caption", content, caption, 120f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(3400f, 150f));
        OrisamoUI.ApplyGoldGradient(title);
        OrisamoUI.ApplyOutline(title, new Color(0.2f, 0.07f, 0.02f, 1f), 0.18f, new Color(0f, 0f, 0f, 0.8f), 1.2f);
        if (!string.IsNullOrEmpty(subCaption))
        {
            TextMeshProUGUI sub = OrisamoUI.CreateText("SubCaption", content, subCaption, 72f, OrisamoUI.Parchment, TextAlignmentOptions.Center, FontStyles.Bold);
            OrisamoUI.Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -205f), new Vector2(3400f, 100f));
            OrisamoUI.ApplyOutline(sub, OrisamoUI.Ink, 0.22f, new Color(0f, 0f, 0f, 0.7f));
        }

        int columns = tournament.RoundCount + 1;
        float columnWidth = (OrisamoUI.ReferenceResolution.x - SideMargin * 2f) / columns;
        float boxWidth = Mathf.Min(640f, columnWidth - 110f);
        float pitch = BracketHeight / tournament.Size;
        float boxHeight = Mathf.Min(130f, pitch * 0.78f);
        float fontSize = Mathf.Clamp(boxHeight * 0.48f, 36f, 58f);

        // 各枠の中心の高さ(上からの距離。下へ行くほどマイナス)
        float[][] y = new float[columns][];
        y[0] = new float[tournament.Size];
        for (int i = 0; i < tournament.Size; i++) y[0][i] = -(BracketTop + pitch * (i + 0.5f));
        for (int r = 1; r < columns; r++)
        {
            y[r] = new float[tournament.Rounds[r].Length];
            for (int i = 0; i < y[r].Length; i++) y[r][i] = (y[r - 1][i * 2] + y[r - 1][i * 2 + 1]) * 0.5f;
        }
        float ColumnX(int r) => SideMargin + r * columnWidth + (columnWidth - boxWidth) * 0.5f;

        // 回戦の見出し
        for (int r = 0; r < columns; r++)
        {
            string name = r < tournament.RoundCount ? tournament.RoundName(r) : "優勝";
            TextMeshProUGUI heading = OrisamoUI.CreateText("Round", content, name, 50f,
                r == tournament.RoundCount ? OrisamoUI.Gold : OrisamoUI.Muted, TextAlignmentOptions.Center, FontStyles.Bold);
            OrisamoUI.Place(heading.rectTransform, new Vector2(0f, 1f), new Vector2(ColumnX(r), -BracketTop + 80f), new Vector2(boxWidth, 70f));
        }

        // 線(枠より先に描いて、枠の下に隠す)
        for (int r = 0; r < tournament.RoundCount; r++)
        {
            float xRight = ColumnX(r) + boxWidth;
            float xNext = ColumnX(r + 1);
            float xMid = (xRight + xNext) * 0.5f;
            for (int m = 0; m < tournament.Rounds[r + 1].Length; m++)
            {
                int winner = tournament.Rounds[r + 1][m];
                for (int side = 0; side < 2; side++)
                {
                    int child = tournament.Rounds[r][m * 2 + side];
                    bool won = winner >= 0 && child == winner;
                    Color color = won ? OrisamoUI.Gold : LineIdle;
                    float childY = y[r][m * 2 + side];
                    Line(xRight, childY, xMid, childY, color);
                    Line(xMid, childY, xMid, y[r + 1][m], color);
                }
                Line(xMid, y[r + 1][m], xNext, y[r + 1][m], winner >= 0 ? OrisamoUI.Gold : LineIdle);
            }
        }

        // 枠
        for (int r = 0; r < columns; r++)
        {
            for (int i = 0; i < tournament.Rounds[r].Length; i++)
            {
                int entrant = tournament.Rounds[r][i];
                bool eliminated = r < tournament.RoundCount && entrant >= 0
                    && tournament.Rounds[r + 1][i / 2] >= 0 && tournament.Rounds[r + 1][i / 2] != entrant;
                bool isNext = r == nextRound && i / 2 == nextMatch;
                bool justAdvanced = r == tournament.LastRound + 1 && i == tournament.LastMatch;
                bool champion = r == tournament.RoundCount && entrant >= 0;
                RectTransform box = Box(tournament, entrant, new Vector2(ColumnX(r), y[r][i] + boxHeight * 0.5f),
                    new Vector2(boxWidth, champion ? boxHeight * 1.25f : boxHeight), fontSize, eliminated, isNext, champion);
                if (justAdvanced) StartCoroutine(Punch(box));
            }
        }
    }

    public void Hide()
    {
        Clear();
        root.gameObject.SetActive(false);
    }

    private void Clear()
    {
        pulsing.Clear();
        if (content != null) Destroy(content.gameObject);
        content = OrisamoUI.Stretch(OrisamoUI.CreateRect("Content", root));
        foreach (Texture2D texture in textures) if (texture != null) Destroy(texture);
        textures.Clear();
    }

    private RectTransform Box(Tournament tournament, int entrant, Vector2 topLeft, Vector2 size, float fontSize,
        bool eliminated, bool isNext, bool champion)
    {
        FirebaseCharacterRecord record = entrant >= 0 ? tournament.Entrants[entrant] : null;
        ElementType element = ElementType.Fire;
        bool hasElement = record != null && System.Enum.TryParse(record.element, out element);
        Color elementColor = hasElement ? ElementAffinity.GetElementColor(element) : OrisamoUI.Muted;

        Image fill = OrisamoUI.CreateImage("Box", content, OrisamoUI.RoundedPanel,
            champion ? Color.Lerp(BoxFill, OrisamoUI.Gold, 0.35f) : record != null ? Color.Lerp(BoxFill, elementColor, 0.12f) : BoxFill, true);
        RectTransform box = fill.rectTransform;
        box.anchorMin = box.anchorMax = new Vector2(0f, 1f);
        box.pivot = new Vector2(0.5f, 0.5f);
        box.sizeDelta = size;
        box.anchoredPosition = new Vector2(topLeft.x + size.x * 0.5f, topLeft.y - size.y * 0.5f);

        Color borderColor = champion ? OrisamoUI.GoldLight : isNext ? OrisamoUI.GoldLight : OrisamoUI.WithAlpha(OrisamoUI.Gold, record != null ? 0.6f : 0.25f);
        Image border = OrisamoUI.CreateImage("Border", box, OrisamoUI.RoundedFrame, borderColor, true);
        OrisamoUI.Stretch(border.rectTransform);
        if (isNext) pulsing.Add(border);

        if (hasElement)
        {
            Image accent = OrisamoUI.CreateImage("Accent", box, OrisamoUI.RoundedFlat, elementColor, true);
            OrisamoUI.Place(accent.rectTransform, new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(10f, size.y - 24f));
        }

        string text;
        if (record != null)
        {
            string kanji = hasElement ? $"<color=#{ColorUtility.ToHtmlStringRGB(elementColor)}>{OrisamoUI.ElementKanji(element)}</color> " : "";
            string rare = record.isMutation ? " <color=#FFE08A>★</color>" : "";
            text = kanji + record.characterName + rare;
        }
        else
        {
            text = entrant == Tournament.Bye ? "<color=#6E6884>（不戦勝）</color>" : "<color=#8A84A0>？</color>";
        }
        TextMeshProUGUI label = OrisamoUI.CreateText("Name", box, text, champion ? fontSize * 1.2f : fontSize,
            OrisamoUI.Parchment, TextAlignmentOptions.Left, champion ? FontStyles.Bold : FontStyles.Normal);
        OrisamoUI.Stretch(label.rectTransform);
        label.margin = new Vector4(36f, 0f, 20f, 0f);
        label.overflowMode = TextOverflowModes.Ellipsis;
        if (eliminated)
        {
            label.alpha = 0.4f;
            fill.color = OrisamoUI.WithAlpha(fill.color, 0.6f);
        }
        return box;
    }

    /// <summary>軸に沿った線(水平か垂直)。座標は左上基準で、y は下へ行くほどマイナス。</summary>
    private void Line(float x1, float y1, float x2, float y2, Color color)
    {
        Image line = OrisamoUI.CreateImage("Line", content, null, color);
        RectTransform r = line.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, 1f);
        if (Mathf.Abs(y1 - y2) < 0.5f)
        {
            r.anchoredPosition = new Vector2(Mathf.Min(x1, x2), y1 + LineThickness * 0.5f);
            r.sizeDelta = new Vector2(Mathf.Abs(x2 - x1) + LineThickness * 0.5f, LineThickness);
        }
        else
        {
            r.anchoredPosition = new Vector2(x1 - LineThickness * 0.5f, Mathf.Max(y1, y2) + LineThickness * 0.5f);
            r.sizeDelta = new Vector2(LineThickness, Mathf.Abs(y2 - y1) + LineThickness);
        }
    }

    private IEnumerator Punch(RectTransform box)
    {
        yield return new WaitForSecondsRealtime(0.5f);
        OrisamoAudio.Play(OrisamoAudio.Sfx.Join);
        Image flash = OrisamoUI.CreateImage("Flash", box, OrisamoUI.SoftGlow, OrisamoUI.WithAlpha(OrisamoUI.Gold, 0.9f));
        OrisamoUI.Stretch(flash.rectTransform, -60f);
        for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime)
        {
            if (box == null) yield break;
            float p = t / 0.6f;
            box.localScale = Vector3.one * (1f + Mathf.Sin(p * Mathf.PI) * 0.12f);
            flash.color = OrisamoUI.WithAlpha(OrisamoUI.Gold, 0.9f * (1f - p));
            yield return null;
        }
        if (box != null) box.localScale = Vector3.one;
    }

    // ==================== 優勝の表彰 ====================

    /// <summary>優勝者を大きく表示する(写真・名前・何人のトーナメントか)。</summary>
    public void ShowChampion(CharacterStats champion, string subtitle)
    {
        Clear();
        root.SetAsLastSibling();
        root.gameObject.SetActive(true);

        RectTransform panel = OrisamoUI.CreateOrnatePanel("Champion", content, new Color(0.06f, 0.04f, 0.12f, 0.9f), OrisamoUI.GoldLight);
        OrisamoUI.Place(panel, new Vector2(1f, 0.5f), new Vector2(-120f, 40f), new Vector2(1500f, 1700f));

        TextMeshProUGUI crown = OrisamoUI.CreateText("Label", panel, "CHAMPION", 110f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Place(crown.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(1400f, 140f));
        crown.characterSpacing = 14f;
        OrisamoUI.ApplyGoldGradient(crown);
        OrisamoUI.ApplyOutline(crown, new Color(0.25f, 0.08f, 0.02f, 1f), 0.2f, new Color(0f, 0f, 0f, 0.8f), 1.4f);
        TextMeshProUGUI title = OrisamoUI.CreateText("Title", panel, "優勝", 150f, OrisamoUI.GoldLight, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(1400f, 180f));
        OrisamoUI.ApplyOutline(title, new Color(0.25f, 0.08f, 0.02f, 1f), 0.2f, new Color(0f, 0f, 0f, 0.8f), 1.2f);

        RectTransform art = OrisamoUI.CreateRect("Art", panel);
        OrisamoUI.Place(art, new Vector2(0.5f, 1f), new Vector2(0f, -420f), new Vector2(860f, 760f));
        Image halo = OrisamoUI.CreateImage("Halo", art, OrisamoUI.SoftGlow, OrisamoUI.WithAlpha(OrisamoUI.Gold, 0.55f));
        OrisamoUI.Stretch(halo.rectTransform, -80f);
        if (champion.photoSprite != null)
        {
            Image photo = OrisamoUI.CreateImage("Photo", art, champion.photoSprite, Color.white);
            OrisamoUI.Stretch(photo.rectTransform);
            photo.preserveAspect = true;
        }
        else
        {
            RectTransform emblem = OrisamoUI.CreateElementEmblem(art, champion.element, 420f);
            OrisamoUI.PlaceCenter(emblem, Vector2.zero, emblem.sizeDelta);
        }

        TextMeshProUGUI name = OrisamoUI.CreateText("Name", panel, champion.characterName, 130f, OrisamoUI.Parchment, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Place(name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 230f), new Vector2(1400f, 170f));
        name.enableAutoSizing = true;
        name.fontSizeMin = 60f;
        name.fontSizeMax = 130f;
        OrisamoUI.ApplyOutline(name, OrisamoUI.Ink, 0.2f, new Color(0f, 0f, 0f, 0.6f));
        TextMeshProUGUI sub = OrisamoUI.CreateText("Subtitle", panel, subtitle, 56f, OrisamoUI.Muted, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Place(sub.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(1400f, 80f));

        StartCoroutine(SlideIn(panel));
    }

    /// <summary>表彰パネルの下の一行(タイトルへ戻るまでの秒数など)。</summary>
    public void SetFooter(string text)
    {
        if (content == null) return;
        Transform existing = content.Find("Footer");
        TextMeshProUGUI footer = existing != null ? existing.GetComponent<TextMeshProUGUI>() : null;
        if (footer == null)
        {
            footer = OrisamoUI.CreateText("Footer", content, "", 52f, OrisamoUI.Parchment, TextAlignmentOptions.Right);
            footer.name = "Footer";
            OrisamoUI.Place(footer.rectTransform, new Vector2(1f, 0f), new Vector2(-140f, 50f), new Vector2(1600f, 70f));
            OrisamoUI.ApplyOutline(footer, OrisamoUI.Ink, 0.25f, new Color(0f, 0f, 0f, 0.6f));
        }
        footer.text = text;
    }

    private static IEnumerator SlideIn(RectTransform panel)
    {
        Vector2 target = panel.anchoredPosition;
        for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime)
        {
            if (panel == null) yield break;
            float e = OrisamoUI.EaseOutCubic(t / 0.6f);
            panel.anchoredPosition = target + new Vector2(1800f * (1f - e), 0f);
            yield return null;
        }
        if (panel != null) panel.anchoredPosition = target;
    }
}
