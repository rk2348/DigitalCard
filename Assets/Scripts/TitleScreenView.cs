using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// タイトル画面の見た目。背景に3Dスタジアムを置いてカメラをゆっくり周回させ、
/// 手前にORISAMOのロゴ・「バトルをはじめる」ボタン・参加方法の案内を重ねる。
///
/// ボタンはすべてコードで生成する(シーンにCanvasがなければCanvasごと作る):
///   「バトルをはじめる」(中央の主役ボタン) / 「QRコード作成」(右下) / 「登録カード一覧」「トーナメント」(右上)
/// シーンにボタンなどのUIが残っていても重複しないよう、Canvas直下の既存の要素は非表示にする。
/// 開発者モード(TitleManager)では、QRコードもスマホも使わないデモ対戦のボタンも表示する。
/// </summary>
public sealed class TitleScreenView : MonoBehaviour
{
    private TitleManager titleManager;
    private RectTransform developerPanel;

    private void Start()
    {
        titleManager = GetComponent<TitleManager>();
        Tournament.Current = null; // 途中でタイトルに戻ってきた場合、進行中のトーナメントは打ち切る

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            Stadium stadium = Stadium.Build(mainCamera);
            stadium.SetScreens("ORISAMO", "WELCOME");
            StadiumCamera stadiumCamera = mainCamera.GetComponent<StadiumCamera>();
            if (stadiumCamera == null) stadiumCamera = mainCamera.gameObject.AddComponent<StadiumCamera>();
            stadiumCamera.Orbit(new Vector3(0f, 3f, 0f), 40f, 13f, 3f, 44f, -90f, true);
        }

        Canvas canvas = OrisamoUI.EnsureSceneCanvas();
        UIAnimationDirector.ConfigureScaler(canvas);
        HideSceneUi(canvas);
        BuildMainButtons(canvas.transform);
        BuildBranding(canvas.transform);
        BuildDeveloperPanel(canvas.transform);
        BuildGalleryButton(canvas);
        OrisamoAudio.PlayBgm(OrisamoAudio.Bgm.Title);

        if (titleManager != null)
        {
            titleManager.DeveloperModeChanged += OnDeveloperModeChanged;
            OnDeveloperModeChanged(titleManager.IsDeveloperMode);
        }
    }

    private void OnDestroy()
    {
        if (titleManager != null) titleManager.DeveloperModeChanged -= OnDeveloperModeChanged;
    }

    /// <summary>シーンに置かれた旧UI(仮のタイトル文字や以前のボタン)を隠す。</summary>
    private static void HideSceneUi(Canvas canvas)
    {
        foreach (Transform child in canvas.transform) child.gameObject.SetActive(false);
    }

    /// <summary>「バトルをはじめる」(主役)と「QRコード作成」(右下)のボタン。</summary>
    private void BuildMainButtons(Transform canvas)
    {
        if (titleManager == null) return;

        Button battle = CreateButton(canvas, "StartBattleButton", "バトルをはじめる", 96f, titleManager.GoToBattle);
        OrisamoUI.PlaceCenter((RectTransform)battle.transform, new Vector2(0f, -330f), new Vector2(1200f, 240f));
        Style(battle).SetPrimary(true);

        Button qr = CreateButton(canvas, "QRCreationButton", "QRコード作成", 52f, titleManager.GoToCharacterCreation);
        OrisamoUI.Place((RectTransform)qr.transform, new Vector2(1f, 0f), new Vector2(-70f, 70f), new Vector2(640f, 140f));
        Style(qr);
    }

    private static Button CreateButton(Transform parent, string name, string text, float fontSize, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI label = OrisamoUI.CreateText("Label", go.transform, text, fontSize, OrisamoUI.Parchment, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Stretch(label.rectTransform);
        Button button = go.GetComponent<Button>();
        button.onClick.AddListener(onClick);
        return button;
    }

    private static UIProductButtonStyle Style(Button button)
    {
        UIProductButtonStyle style = button.GetComponent<UIProductButtonStyle>();
        if (style == null) style = button.gameObject.AddComponent<UIProductButtonStyle>();
        if (button.GetComponent<UIButtonMotion>() == null) button.gameObject.AddComponent<UIButtonMotion>();
        return style;
    }

    /// <summary>ロゴ・キャッチコピー・参加方法の案内。</summary>
    private void BuildBranding(Transform canvas)
    {
        RectTransform brand = OrisamoUI.Stretch(OrisamoUI.CreateRect("Branding", canvas));
        brand.SetAsFirstSibling();
        brand.gameObject.AddComponent<UIAnimationIgnore>();

        Image shade = OrisamoUI.CreateImage("Shade", brand, OrisamoUI.Vignette, new Color(0.02f, 0.01f, 0.05f, 0.85f));
        OrisamoUI.Stretch(shade.rectTransform, -400f);
        Image glow = OrisamoUI.CreateImage("LogoGlow", brand, OrisamoUI.SoftGlow, OrisamoUI.WithAlpha(OrisamoUI.Arcane, 0.55f));
        OrisamoUI.PlaceCenter(glow.rectTransform, new Vector2(0f, 360f), new Vector2(3000f, 1100f));

        // ロゴの背後でゆっくり回る魔法陣
        RectTransform rings = OrisamoUI.CreateRect("Rings", brand);
        OrisamoUI.PlaceCenter(rings, new Vector2(0f, 360f), new Vector2(1400f, 1400f));
        Image outer = OrisamoUI.CreateImage("Outer", rings, OrisamoUI.ThinRing, OrisamoUI.WithAlpha(OrisamoUI.Gold, 0.35f));
        OrisamoUI.Stretch(outer.rectTransform);
        Image inner = OrisamoUI.CreateImage("Inner", rings, OrisamoUI.ThinRing, OrisamoUI.WithAlpha(OrisamoUI.Gold, 0.22f));
        OrisamoUI.Stretch(inner.rectTransform, 170f);
        ElementType[] elements = { ElementType.Fire, ElementType.Wind, ElementType.Dark, ElementType.Water, ElementType.Earth, ElementType.Light };
        for (int i = 0; i < elements.Length; i++)
        {
            float angle = Mathf.PI * 2f * i / elements.Length + Mathf.PI / 2f;
            Image orb = OrisamoUI.CreateImage("Orb", rings, OrisamoUI.SoftGlow, ElementAffinity.GetElementColor(elements[i]));
            OrisamoUI.PlaceCenter(orb.rectTransform, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 700f, new Vector2(170f, 170f));
            Image core = OrisamoUI.CreateImage("Core", orb.rectTransform, OrisamoUI.Circle, Color.Lerp(ElementAffinity.GetElementColor(elements[i]), Color.white, 0.5f));
            OrisamoUI.PlaceCenter(core.rectTransform, Vector2.zero, new Vector2(34f, 34f));
        }
        StartCoroutine(Spin(rings, 5f));

        TextMeshProUGUI logo = OrisamoUI.CreateText("Logo", brand, "ORISAMO", 330f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.PlaceCenter(logo.rectTransform, new Vector2(0f, 430f), new Vector2(3000f, 400f));
        logo.characterSpacing = 22f;
        OrisamoUI.ApplyGoldGradient(logo);
        OrisamoUI.ApplyOutline(logo, new Color(0.2f, 0.07f, 0.02f, 1f), 0.16f, new Color(0f, 0f, 0f, 0.85f), 1.4f);

        TextMeshProUGUI kana = OrisamoUI.CreateText("Kana", brand, "オ リ サ モ", 96f, OrisamoUI.Parchment, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.PlaceCenter(kana.rectTransform, new Vector2(0f, 210f), new Vector2(1600f, 120f));
        OrisamoUI.ApplyOutline(kana, OrisamoUI.Ink, 0.22f, new Color(0f, 0f, 0f, 0.7f));

        RectTransform divider = OrisamoUI.CreateDivider(brand, 1300f, OrisamoUI.Gold);
        OrisamoUI.PlaceCenter(divider, new Vector2(0f, 120f), divider.sizeDelta);

        TextMeshProUGUI tagline = OrisamoUI.CreateText("Tagline", brand, "描いたカードが、スタジアムで動き出す。", 72f, OrisamoUI.GoldLight);
        OrisamoUI.PlaceCenter(tagline.rectTransform, new Vector2(0f, 30f), new Vector2(2400f, 100f));
        OrisamoUI.ApplyOutline(tagline, OrisamoUI.Ink, 0.25f, new Color(0f, 0f, 0f, 0.7f));

        TextMeshProUGUI guide = OrisamoUI.CreateText("Guide", brand,
            "参加はスマートフォンから　／　カードのQRコードを読み取って、キャラクターを登録しよう", 50f, OrisamoUI.Muted);
        OrisamoUI.Place(guide.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(2800f, 70f));
        OrisamoUI.ApplyOutline(guide, OrisamoUI.Ink, 0.25f, new Color(0f, 0f, 0f, 0.6f));

        TextMeshProUGUI credit = OrisamoUI.CreateText("Credit", brand, "© ORISAMO Project", 38f, OrisamoUI.WithAlpha(OrisamoUI.Muted, 0.7f), TextAlignmentOptions.Left);
        OrisamoUI.Place(credit.rectTransform, Vector2.zero, new Vector2(70f, 70f), new Vector2(800f, 60f));
    }

    /// <summary>開発者モードで表示するデモ対戦のボタン(QRコードもスマホも使わずにPCだけで遊べる)。</summary>
    private void BuildDeveloperPanel(Transform canvas)
    {
        developerPanel = OrisamoUI.CreateOrnatePanel("DeveloperPanel", canvas, OrisamoUI.PanelFill, OrisamoUI.WithAlpha(OrisamoUI.Gold, 0.8f), false);
        OrisamoUI.Place(developerPanel, new Vector2(1f, 0f), new Vector2(-70f, 250f), new Vector2(980f, 560f));
        developerPanel.gameObject.AddComponent<UIAnimationIgnore>();

        TextMeshProUGUI title = OrisamoUI.CreateText("Title", developerPanel, "開発者モード", 54f, OrisamoUI.Gold, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(900f, 70f));
        TextMeshProUGUI note = OrisamoUI.CreateText("Note", developerPanel, "QRコード・スマホなしで対戦できます（Ctrl+Shift+D で表示切替）", 34f, OrisamoUI.Muted);
        OrisamoUI.Place(note.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(900f, 50f));

        CreateDeveloperButton("デモ対戦（CPU同士）", new Vector2(0f, -210f), () => titleManager.StartDemoBattle(BattleManager.DemoMode.CpuVsCpu));
        CreateDeveloperButton("デモ対戦（キーボード操作）", new Vector2(0f, -360f), () => titleManager.StartDemoBattle(BattleManager.DemoMode.PlayerVsCpu));
    }

    /// <summary>右上の「登録カード一覧」ボタン。押すと登録済みのカードを一覧表示する(CardGalleryView)。</summary>
    private void BuildGalleryButton(Canvas canvas)
    {
        CardGalleryView gallery = CardGalleryView.Create(canvas);
        gallery.StartCpuMatch = (first, second) =>
        {
            BattleManager.PendingCardMatch = new[] { first, second };
            titleManager.GoToBattle();
        };
        gallery.StartTournament = (picked, pool, size) =>
        {
            Tournament tournament = Tournament.Create(picked, pool, size, new System.Random());
            if (tournament == null) return;
            Tournament.Current = tournament;
            titleManager.GoToBattle();
        };

        Button button = CreateButton(canvas.transform, "GalleryButton", "登録カード一覧", 52f, gallery.Open);
        OrisamoUI.Place((RectTransform)button.transform, new Vector2(1f, 1f), new Vector2(-70f, -70f), new Vector2(640f, 140f));
        Style(button);

        Button tournamentButton = CreateButton(canvas.transform, "TournamentButton", "トーナメント", 52f, gallery.OpenForTournament);
        OrisamoUI.Place((RectTransform)tournamentButton.transform, new Vector2(1f, 1f), new Vector2(-70f, -240f), new Vector2(640f, 140f));
        Style(tournamentButton);
        gallery.transform.SetAsLastSibling();
    }

    private void CreateDeveloperButton(string text, Vector2 position, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(text, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(developerPanel, false);
        OrisamoUI.Place((RectTransform)go.transform, new Vector2(0.5f, 1f), position, new Vector2(860f, 120f));
        TextMeshProUGUI label = OrisamoUI.CreateText("Label", go.transform, text, 48f, OrisamoUI.Parchment, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Stretch(label.rectTransform);
        Button button = go.GetComponent<Button>();
        button.onClick.AddListener(onClick);
        Style(button);
    }

    private void OnDeveloperModeChanged(bool enabled)
    {
        if (developerPanel != null) developerPanel.gameObject.SetActive(enabled && titleManager != null);
    }

    private static IEnumerator Spin(RectTransform target, float degreesPerSecond)
    {
        while (target != null)
        {
            target.Rotate(0f, 0f, degreesPerSecond * Time.deltaTime);
            yield return null;
        }
    }
}
