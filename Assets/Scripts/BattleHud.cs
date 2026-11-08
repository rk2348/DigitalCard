using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// バトル画面で3Dスタジアムの上に重ねる2D表示(HUD)。すべてコードで生成する(4K基準の座標)。
///   ・対戦受付: ロゴ、2人分の参加枠、遊び方とルール
///   ・対戦中: 左右のプレイヤーパネル(属性・名前・HP・能力値・攻撃/防御の役割)、ターン数、選択の残り時間、
///             下部のメッセージ欄
///   ・演出: 「VS」の登場、強/普/弱の同時公開、必殺技などの帯、ダメージ数字、短い掛け声
///   ・結果: WINNER と勝者名、決着の理由、次の試合までのカウントダウン
/// BattleManager から進行に合わせて呼び出す。
/// </summary>
public sealed class BattleHud : MonoBehaviour
{
    private static readonly Color Player1Color = new Color(1f, 0.38f, 0.40f, 1f);
    private static readonly Color Player2Color = new Color(0.38f, 0.62f, 1f, 1f);

    private RectTransform root;
    private RectTransform waitingLayer;
    private RectTransform battleLayer;
    private RectTransform overlayLayer;
    private RectTransform effectsLayer;
    private RectTransform resultLayer;

    // 対戦受付
    private SlotCard slot1;
    private SlotCard slot2;

    // 対戦中
    private SidePanel panel1;
    private SidePanel panel2;
    private TextMeshProUGUI turnNumber;
    private RectTransform countdownRoot;
    private Image countdownFill;
    private TextMeshProUGUI countdownText;
    private TextMeshProUGUI messageText;
    private RectTransform messageRoot;

    // 結果
    private TextMeshProUGUI resultCountdown;
    private TextMeshProUGUI playbackLabel;
    private TextMeshProUGUI tieBoostLabel;

    public void SetTieBoost(int consecutiveTies)
    {
        if (tieBoostLabel == null) return;
        tieBoostLabel.gameObject.SetActive(consecutiveTies > 0);
        tieBoostLabel.text = $"あいこ連続　次の攻撃 ×{BattleRules.TieDamageMultiplier(consecutiveTies):0.0}";
    }

    public void ConfigurePlaybackSpeed(UnityEngine.Events.UnityAction toggle)
    {
        Image image = OrisamoUI.CreateImage("PlaybackSpeed", root, OrisamoUI.RoundedPanel, new Color(0.12f, 0.09f, 0.22f, 0.96f));
        RectTransform rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-48f, -32f);
        rect.sizeDelta = new Vector2(320f, 88f);
        image.raycastTarget = true;
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(toggle);
        playbackLabel = OrisamoUI.CreateText("Label", rect, "速度 ×1  [F8]", 38f, OrisamoUI.Gold, TextAlignmentOptions.Center);
        OrisamoUI.Stretch(playbackLabel.rectTransform);
        playbackLabel.raycastTarget = false;
    }

    public void SetPlaybackSpeed(float speed)
    {
        if (playbackLabel != null) playbackLabel.text = speed > 1.5f ? "速度 ×2  [F8]" : "速度 ×1  [F8]";
    }

    public static BattleHud Create(Canvas canvas)
    {
        UIAnimationDirector.ConfigureScaler(canvas);
        BattleHud hud = OrisamoUI.CreateRect("BattleHud", canvas.transform).gameObject.AddComponent<BattleHud>();
        hud.Build();
        return hud;
    }

    private void Build()
    {
        root = OrisamoUI.Stretch((RectTransform)transform);
        gameObject.AddComponent<UIAnimationIgnore>();

        waitingLayer = Layer("Waiting");
        battleLayer = Layer("Battle");
        overlayLayer = Layer("Overlay");
        effectsLayer = Layer("Effects");
        resultLayer = Layer("Result");
        BuildScreenShade();
        BuildWaiting();
        BuildBattleChrome();

        battleLayer.gameObject.SetActive(false);
        resultLayer.gameObject.SetActive(false);
    }

    private RectTransform Layer(string name)
    {
        return OrisamoUI.Stretch(OrisamoUI.CreateRect(name, root));
    }

    /// <summary>画面の上下を少し暗くして、文字を読みやすくする。</summary>
    private void BuildScreenShade()
    {
        // FadeDownは上端が濃いグラデーション。上はそのまま、下は上下反転して使う
        Image top = OrisamoUI.CreateImage("TopShade", root, OrisamoUI.FadeDown, new Color(0f, 0f, 0.02f, 0.55f));
        top.transform.SetAsFirstSibling();
        top.rectTransform.anchorMin = new Vector2(0f, 1f);
        top.rectTransform.anchorMax = Vector2.one;
        top.rectTransform.pivot = new Vector2(0.5f, 1f);
        top.rectTransform.sizeDelta = new Vector2(0f, 520f);
        top.rectTransform.anchoredPosition = Vector2.zero;

        Image bottom = OrisamoUI.CreateImage("BottomShade", root, OrisamoUI.FadeDown, new Color(0f, 0f, 0.02f, 0.6f));
        bottom.transform.SetSiblingIndex(1);
        bottom.rectTransform.anchorMin = Vector2.zero;
        bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
        bottom.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        bottom.rectTransform.sizeDelta = new Vector2(0f, 420f);
        bottom.rectTransform.anchoredPosition = new Vector2(0f, 210f);
        bottom.rectTransform.localScale = new Vector3(1f, -1f, 1f);
    }

    // ==================== 対戦受付 ====================

    private TextMeshProUGUI tableLabel;
    private TextMeshProUGUI devHint;
    private TextMeshProUGUI waitingNotice;

    private void BuildWaiting()
    {
        TextMeshProUGUI logo = OrisamoUI.CreateText("Logo", waitingLayer, "ORISAMO", 200f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Place(logo.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(2000f, 240f));
        logo.characterSpacing = 18f;
        OrisamoUI.ApplyGoldGradient(logo);
        OrisamoUI.ApplyOutline(logo, new Color(0.18f, 0.08f, 0.02f, 1f), 0.18f, new Color(0f, 0f, 0f, 0.7f), 1.2f);

        TextMeshProUGUI sub = OrisamoUI.CreateText("Subtitle", waitingLayer, "スタジアム　対戦受付中", 76f, OrisamoUI.Parchment);
        OrisamoUI.Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -330f), new Vector2(2000f, 100f));
        OrisamoUI.ApplyOutline(sub, OrisamoUI.Ink, 0.2f, new Color(0f, 0f, 0f, 0.6f));
        RectTransform divider = OrisamoUI.CreateDivider(waitingLayer, 1100f, OrisamoUI.Gold);
        OrisamoUI.Place(divider, new Vector2(0.5f, 1f), new Vector2(0f, -440f), divider.sizeDelta);

        tableLabel = OrisamoUI.CreateText("Table", waitingLayer, "", 48f, OrisamoUI.Muted);
        OrisamoUI.Place(tableLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -480f), new Vector2(1200f, 70f));

        slot1 = new SlotCard(waitingLayer, "PLAYER 1", Player1Color, new Vector2(-860f, -230f));
        slot2 = new SlotCard(waitingLayer, "PLAYER 2", Player2Color, new Vector2(860f, -230f));

        TextMeshProUGUI vs = OrisamoUI.CreateText("VS", waitingLayer, "VS", 190f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.Italic);
        OrisamoUI.PlaceCenter(vs.rectTransform, new Vector2(0f, -230f), new Vector2(500f, 240f));
        OrisamoUI.ApplyGoldGradient(vs);
        OrisamoUI.ApplyOutline(vs, new Color(0.2f, 0.08f, 0.02f, 1f), 0.2f, new Color(0f, 0f, 0f, 0.7f), 1.2f);

        RectTransform guide = OrisamoUI.CreateOrnatePanel("Guide", waitingLayer, OrisamoUI.PanelFill, OrisamoUI.Gold);
        OrisamoUI.Place(guide, new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(3000f, 250f));
        TextMeshProUGUI how = OrisamoUI.CreateText("How", guide, "スマホで対戦ページを開いて、カードのQRコードを読み取ろう！　2人そろうとバトル開始！", 64f, OrisamoUI.Parchment);
        OrisamoUI.Place(how.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(2900f, 90f));
        TextMeshProUGUI rule = OrisamoUI.CreateText("Rule", guide,
            "<color=#F8CF70>ルール</color>　攻撃も防御も「強・普・弱」から選ぶ　／　同じなら<color=#7FB8FF>防御成功</color>　／　ちがえば<color=#FF8A6A>命中</color>　／　必殺の強さで当てると大ダメージ",
            48f, OrisamoUI.Muted);
        OrisamoUI.Place(rule.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 45f), new Vector2(2900f, 70f));

        devHint = OrisamoUI.CreateText("DevHint", waitingLayer, "", 40f, OrisamoUI.WithAlpha(OrisamoUI.Muted, 0.75f), TextAlignmentOptions.Left);
        OrisamoUI.Place(devHint.rectTransform, Vector2.zero, new Vector2(60f, 340f), new Vector2(2400f, 60f));

        waitingNotice = OrisamoUI.CreateText("Notice", waitingLayer, "", 56f, OrisamoUI.GoldLight);
        OrisamoUI.Place(waitingNotice.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 420f), new Vector2(3000f, 80f));
        OrisamoUI.ApplyOutline(waitingNotice, OrisamoUI.Ink, 0.25f, new Color(0f, 0f, 0f, 0.7f));
    }

    public void ShowWaiting(string tableId, string developerHint)
    {
        waitingLayer.gameObject.SetActive(true);
        battleLayer.gameObject.SetActive(false);
        resultLayer.gameObject.SetActive(false);
        tableLabel.text = string.IsNullOrEmpty(tableId) ? "" : $"卓 {tableId}";
        devHint.text = developerHint ?? "";
        SetSlots(false, null, false, null);
    }

    public void SetSlots(bool player1Joined, string player1Name, bool player2Joined, string player2Name)
    {
        slot1.Set(player1Joined, player1Name, this);
        slot2.Set(player2Joined, player2Name, this);
    }

    /// <summary>受付画面の案内(登録カードの読み込み状況など)。空文字で消す。</summary>
    public void SetWaitingNotice(string text)
    {
        waitingNotice.text = text ?? "";
    }

    /// <summary>受付画面の枠に、会場PCで呼び出した登録カード(写真・名前)を表示する。</summary>
    public void ShowRegisteredCard(bool player1, CharacterStats stats, string state)
    {
        (player1 ? slot1 : slot2).SetCard(stats, state, this);
    }

    public void HideWaiting()
    {
        waitingLayer.gameObject.SetActive(false);
    }

    private sealed class SlotCard
    {
        private readonly RectTransform root;
        private readonly TextMeshProUGUI state;
        private readonly TextMeshProUGUI name;
        private readonly Image glow;
        private readonly Image photo;
        private bool joined;

        public SlotCard(Transform parent, string label, Color color, Vector2 position)
        {
            root = OrisamoUI.CreateOrnatePanel(label, parent, OrisamoUI.PanelFill, OrisamoUI.Gold);
            OrisamoUI.PlaceCenter(root, position, new Vector2(1100f, 420f));

            glow = OrisamoUI.CreateImage("Glow", root, OrisamoUI.SoftGlow, OrisamoUI.WithAlpha(color, 0f));
            glow.transform.SetAsFirstSibling();
            OrisamoUI.Stretch(glow.rectTransform, -160f);

            Image bar = OrisamoUI.CreateImage("Accent", root, OrisamoUI.RoundedFlat, color, true);
            OrisamoUI.Place(bar.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(320f, 10f));

            TextMeshProUGUI title = OrisamoUI.CreateText("Label", root, label, 58f, color, TextAlignmentOptions.Center, FontStyles.Bold);
            title.characterSpacing = 12f;
            OrisamoUI.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(1000f, 80f));

            name = OrisamoUI.CreateText("Name", root, "", 104f, OrisamoUI.Parchment, TextAlignmentOptions.Center, FontStyles.Bold);
            OrisamoUI.PlaceCenter(name.rectTransform, new Vector2(0f, -20f), new Vector2(1000f, 130f));
            name.enableAutoSizing = true;
            name.fontSizeMin = 50f;
            name.fontSizeMax = 104f;
            OrisamoUI.ApplyOutline(name, OrisamoUI.Ink, 0.2f, new Color(0f, 0f, 0f, 0.5f));

            state = OrisamoUI.CreateText("State", root, "", 60f, OrisamoUI.Muted);
            OrisamoUI.Place(state.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(1000f, 80f));

            photo = OrisamoUI.CreateImage("Photo", root, null, Color.white);
            OrisamoUI.Place(photo.rectTransform, new Vector2(0f, 0.5f), new Vector2(60f, -10f), new Vector2(260f, 260f));
            photo.preserveAspect = true;
            photo.gameObject.SetActive(false);
        }

        /// <summary>登録カードを表示する(写真があれば左に置き、名前を右へずらす)。</summary>
        public void SetCard(CharacterStats stats, string stateText, MonoBehaviour host)
        {
            Set(true, stats.characterName, host);
            state.text = stats.isMutation ? stateText + "　<color=#FFE08A>★ RARE</color>" : stateText;
            bool hasPhoto = stats.photoSprite != null;
            photo.sprite = stats.photoSprite;
            photo.gameObject.SetActive(hasPhoto);
            name.rectTransform.anchoredPosition = new Vector2(hasPhoto ? 150f : 0f, -20f);
            name.rectTransform.sizeDelta = new Vector2(hasPhoto ? 720f : 1000f, 130f);
        }

        public void Set(bool isJoined, string playerName, MonoBehaviour host)
        {
            bool newlyJoined = isJoined && !joined;
            joined = isJoined;
            name.text = isJoined ? (string.IsNullOrEmpty(playerName) ? "…" : playerName) : "？";
            photo.gameObject.SetActive(false);
            name.rectTransform.anchoredPosition = new Vector2(0f, -20f);
            name.rectTransform.sizeDelta = new Vector2(1000f, 130f);
            name.color = isJoined ? OrisamoUI.Parchment : OrisamoUI.WithAlpha(OrisamoUI.Muted, 0.6f);
            state.text = isJoined ? "<color=#F8CF70>参加完了！</color>" : "参加を待っています…";
            if (newlyJoined)
            {
                host.StartCoroutine(Punch());
                OrisamoAudio.Play(OrisamoAudio.Sfx.Join);
            }
        }

        public void Tick(float time)
        {
            if (!joined) state.alpha = 0.55f + 0.45f * Mathf.Sin(time * 3f);
            else state.alpha = 1f;
            Color c = glow.color;
            c.a = Mathf.MoveTowards(c.a, joined ? 0.55f + 0.1f * Mathf.Sin(time * 2f) : 0f, Time.deltaTime * 2f);
            glow.color = c;
        }

        private IEnumerator Punch()
        {
            for (float t = 0f; t < 0.45f; t += Time.deltaTime)
            {
                root.localScale = Vector3.one * (1f + Mathf.Sin(t / 0.45f * Mathf.PI) * 0.08f);
                yield return null;
            }
            root.localScale = Vector3.one;
        }
    }

    // ==================== 対戦中 ====================

    private void BuildBattleChrome()
    {
        // ターン表示
        RectTransform turn = OrisamoUI.CreateOrnatePanel("Turn", battleLayer, OrisamoUI.PanelFill, OrisamoUI.Gold);
        OrisamoUI.Place(turn, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(440f, 170f));
        TextMeshProUGUI turnLabel = OrisamoUI.CreateText("Label", turn, "TURN", 44f, OrisamoUI.Gold, TextAlignmentOptions.Center, FontStyles.Bold);
        turnLabel.characterSpacing = 14f;
        OrisamoUI.Place(turnLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(400f, 60f));
        turnNumber = OrisamoUI.CreateText("Number", turn, "1", 90f, OrisamoUI.Parchment, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Place(turnNumber.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(400f, 100f));
        tieBoostLabel = OrisamoUI.CreateText("TieBoost", battleLayer, "", 42f, OrisamoUI.Gold, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Place(tieBoostLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -225f), new Vector2(820f, 70f));
        tieBoostLabel.raycastTarget = false;
        tieBoostLabel.gameObject.SetActive(false);

        // 選択の残り時間
        countdownRoot = OrisamoUI.CreateRect("Countdown", battleLayer);
        OrisamoUI.Place(countdownRoot, new Vector2(0.5f, 1f), new Vector2(0f, -270f), new Vector2(240f, 240f));
        Image back = OrisamoUI.CreateImage("Back", countdownRoot, OrisamoUI.Circle, new Color(0.04f, 0.03f, 0.09f, 0.8f));
        OrisamoUI.Stretch(back.rectTransform);
        Image track = OrisamoUI.CreateImage("Track", countdownRoot, OrisamoUI.ThickRing, OrisamoUI.WithAlpha(OrisamoUI.Gold, 0.18f));
        OrisamoUI.Stretch(track.rectTransform);
        countdownFill = OrisamoUI.CreateImage("Fill", countdownRoot, OrisamoUI.ThickRing, OrisamoUI.Gold);
        OrisamoUI.Stretch(countdownFill.rectTransform);
        countdownFill.type = Image.Type.Filled;
        countdownFill.fillMethod = Image.FillMethod.Radial360;
        countdownFill.fillOrigin = (int)Image.Origin360.Top;
        countdownFill.fillClockwise = false;
        countdownText = OrisamoUI.CreateText("Seconds", countdownRoot, "30", 100f, OrisamoUI.Parchment, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Stretch(countdownText.rectTransform);
        countdownRoot.gameObject.SetActive(false);

        // メッセージ欄
        messageRoot = OrisamoUI.CreateOrnatePanel("Message", battleLayer, OrisamoUI.PanelFill, OrisamoUI.Gold);
        OrisamoUI.Place(messageRoot, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(2800f, 220f));
        messageText = OrisamoUI.CreateText("Text", messageRoot, "", 76f, OrisamoUI.Parchment);
        OrisamoUI.Stretch(messageText.rectTransform, 40f);
        messageText.textWrappingMode = TextWrappingModes.Normal;
        messageText.enableAutoSizing = true;
        messageText.fontSizeMin = 44f;
        messageText.fontSizeMax = 76f;
    }

    public void ShowBattle(CharacterStats player1, CharacterStats player2)
    {
        waitingLayer.gameObject.SetActive(false);
        resultLayer.gameObject.SetActive(false);
        battleLayer.gameObject.SetActive(true);
        if (panel1 != null) Destroy(panel1.Root.gameObject);
        if (panel2 != null) Destroy(panel2.Root.gameObject);
        panel1 = new SidePanel(battleLayer, player1, "PLAYER 1", Player1Color, false);
        panel2 = new SidePanel(battleLayer, player2, "PLAYER 2", Player2Color, true);
        SetMessage("");
        StartCoroutine(SlideIn(panel1.Root, new Vector2(-1700f, 0f), 0.5f));
        StartCoroutine(SlideIn(panel2.Root, new Vector2(1700f, 0f), 0.5f));
    }

    public void SetTurn(int turn)
    {
        turnNumber.text = turn.ToString();
        StartCoroutine(PunchScale(turnNumber.rectTransform, 0.25f));
    }

    public void SetMessage(string text)
    {
        messageText.text = text;
        if (!string.IsNullOrEmpty(text)) StartCoroutine(FadeText(messageText, 0.18f));
    }

    public void SetHp(bool player1, int hp, int maxHp)
    {
        (player1 ? panel1 : panel2)?.SetHp(hp, maxHp);
    }

    /// <summary>プレイヤーパネルの下に「攻撃/防御」の役割と状態(選択中・決定)を出す。roleがnullなら消す。</summary>
    public void SetRole(bool player1, string role, Color color, string status)
    {
        (player1 ? panel1 : panel2)?.SetRole(role, color, status);
    }

    private int lastCountdownSecond = -1;

    /// <summary>両者の相性(相手への倍率)をパネルに表示する。</summary>
    public void SetAffinity(float player1Multiplier, float player2Multiplier)
    {
        panel1?.SetAffinity(player1Multiplier);
        panel2?.SetAffinity(player2Multiplier);
    }

    public void SetCountdown(float secondsLeft, float totalSeconds)
    {
        if (secondsLeft < 0f)
        {
            countdownRoot.gameObject.SetActive(false);
            return;
        }
        countdownRoot.gameObject.SetActive(true);
        countdownFill.fillAmount = totalSeconds > 0f ? Mathf.Clamp01(secondsLeft / totalSeconds) : 0f;
        int seconds = Mathf.CeilToInt(secondsLeft);
        if (seconds != lastCountdownSecond && seconds <= 5 && seconds > 0) OrisamoAudio.Play(OrisamoAudio.Sfx.Tick, 0.7f, seconds <= 2 ? 1.25f : 1f);
        lastCountdownSecond = seconds;
        countdownText.text = seconds.ToString();
        Color color = seconds <= 5 ? new Color(1f, 0.4f, 0.35f) : OrisamoUI.Gold;
        countdownFill.color = color;
        countdownText.color = seconds <= 5 ? color : OrisamoUI.Parchment;
    }

    private void Update()
    {
        float t = Time.time;
        if (waitingLayer.gameObject.activeSelf)
        {
            slot1.Tick(t);
            slot2.Tick(t);
        }
        panel1?.Tick(Time.deltaTime);
        panel2?.Tick(Time.deltaTime);
    }

    private sealed class SidePanel
    {
        public readonly RectTransform Root;
        private readonly RectTransform fill;
        private readonly RectTransform trail;
        private readonly Image fillImage;
        private readonly TextMeshProUGUI hpText;
        private readonly RectTransform roleRoot;
        private readonly Image rolePill;
        private readonly TextMeshProUGUI roleText;
        private readonly TextMeshProUGUI statusText;
        private readonly TextMeshProUGUI affinityText;
        private readonly CharacterStats stats;
        private readonly TextMeshProUGUI statLine;
        private string statLineCache;
        private const float BarWidth = 1060f;
        private float target = 1f;
        private float shown = 1f;
        private float trailValue = 1f;
        private float trailDelay;

        public SidePanel(Transform parent, CharacterStats stats, string label, Color color, bool rightSide)
        {
            float s = rightSide ? -1f : 1f;
            Vector2 anchor = new Vector2(rightSide ? 1f : 0f, 1f);
            TextAlignmentOptions align = rightSide ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;

            Root = OrisamoUI.CreateOrnatePanel(label, parent, OrisamoUI.PanelFill, OrisamoUI.Gold, false);
            OrisamoUI.Place(Root, anchor, new Vector2(70f * s, -60f), new Vector2(1500f, 490f));

            Image accent = OrisamoUI.CreateImage("Accent", Root, OrisamoUI.RoundedFlat, color, true);
            OrisamoUI.Place(accent.rectTransform, new Vector2(anchor.x, 0.5f), new Vector2(18f * s, 0f), new Vector2(14f, 410f));

            RectTransform emblem = OrisamoUI.CreateElementEmblem(Root, stats.element, 210f);
            OrisamoUI.Place(emblem, new Vector2(anchor.x, 0.5f), new Vector2(60f * s, 0f), emblem.sizeDelta);

            float textX = 310f * s;
            TextMeshProUGUI title = OrisamoUI.CreateText("Label", Root, label, 44f, color, align, FontStyles.Bold);
            title.characterSpacing = 10f;
            OrisamoUI.Place(title.rectTransform, anchor, new Vector2(textX, -34f), new Vector2(700f, 60f));

            affinityText = OrisamoUI.CreateText("Affinity", Root, "", 44f, OrisamoUI.Muted, align, FontStyles.Bold);
            OrisamoUI.Place(affinityText.rectTransform, anchor, new Vector2(textX + 420f * s, -34f), new Vector2(600f, 60f));

            if (stats.isMutation)
            {
                // レアカード(突然変異)の目印: パネルの反対側の上に金色の「RARE」
                Vector2 corner = new Vector2(1f - anchor.x, 1f);
                Image pill = OrisamoUI.CreateImage("Rare", Root, OrisamoUI.RoundedPanel, OrisamoUI.Gold, true);
                OrisamoUI.Place(pill.rectTransform, corner, new Vector2(-40f * s, -26f), new Vector2(250f, 72f));
                TextMeshProUGUI rare = OrisamoUI.CreateText("Text", pill.rectTransform, "★ RARE", 44f, new Color(0.25f, 0.1f, 0.02f), TextAlignmentOptions.Center, FontStyles.Bold);
                OrisamoUI.Stretch(rare.rectTransform);
            }

            TextMeshProUGUI name = OrisamoUI.CreateText("Name", Root, stats.characterName, 96f, OrisamoUI.Parchment, align, FontStyles.Bold);
            OrisamoUI.Place(name.rectTransform, anchor, new Vector2(textX, -86f), new Vector2(1100f, 120f));
            name.enableAutoSizing = true;
            name.fontSizeMin = 50f;
            name.fontSizeMax = 96f;
            OrisamoUI.ApplyOutline(name, OrisamoUI.Ink, 0.2f, new Color(0f, 0f, 0f, 0.5f));

            hpText = OrisamoUI.CreateText("HpText", Root, "", 56f, OrisamoUI.Parchment, rightSide ? TextAlignmentOptions.Left : TextAlignmentOptions.Right, FontStyles.Bold);
            OrisamoUI.Place(hpText.rectTransform, anchor, new Vector2((textX + BarWidth * s) - 460f * s, -196f), new Vector2(460f, 60f));
            TextMeshProUGUI hpLabel = OrisamoUI.CreateText("HpLabel", Root, "HP", 46f, OrisamoUI.Gold, align, FontStyles.Bold);
            OrisamoUI.Place(hpLabel.rectTransform, anchor, new Vector2(textX, -200f), new Vector2(200f, 56f));

            RectTransform bar = OrisamoUI.CreateRect("HpBar", Root);
            OrisamoUI.Place(bar, anchor, new Vector2(textX, -258f), new Vector2(BarWidth, 46f));
            Image trough = OrisamoUI.CreateImage("Trough", bar, OrisamoUI.RoundedFlat, new Color(0.02f, 0.02f, 0.05f, 0.95f), true);
            OrisamoUI.Stretch(trough.rectTransform, -6f);
            trail = OrisamoUI.CreateImage("Trail", bar, OrisamoUI.RoundedFlat, new Color(1f, 0.92f, 0.85f, 0.9f), true).rectTransform;
            fillImage = OrisamoUI.CreateImage("Fill", bar, OrisamoUI.RoundedPanel, HpColor(1f), true);
            fill = fillImage.rectTransform;
            foreach (RectTransform r in new[] { trail, fill })
            {
                r.anchorMin = new Vector2(anchor.x, 0f);
                r.anchorMax = new Vector2(anchor.x, 1f);
                r.pivot = new Vector2(anchor.x, 0.5f);
                r.anchoredPosition = Vector2.zero;
                r.sizeDelta = new Vector2(BarWidth, 0f);
            }
            Image frame = OrisamoUI.CreateImage("Frame", bar, OrisamoUI.RoundedFrame, OrisamoUI.WithAlpha(OrisamoUI.Gold, 0.85f), true);
            OrisamoUI.Stretch(frame.rectTransform, -6f);

            // ステータス(スキルや威圧で変わった分は (+5) のように色付きで出す。試合中も自動で更新)
            this.stats = stats;
            statLine = OrisamoUI.CreateText("Stats", Root, "", 52f, OrisamoUI.Parchment, align, FontStyles.Bold);
            OrisamoUI.Place(statLine.rectTransform, anchor, new Vector2(textX, -300f), new Vector2(1100f, 66f));
            statLine.enableAutoSizing = true;
            statLine.fontSizeMin = 32f;
            statLine.fontSizeMax = 52f;
            OrisamoUI.ApplyOutline(statLine, OrisamoUI.Ink, 0.15f, new Color(0f, 0f, 0f, 0.5f));
            RefreshStats();

            Image divider = OrisamoUI.CreateImage("Divider", Root, OrisamoUI.RoundedFlat, OrisamoUI.WithAlpha(OrisamoUI.Gold, 0.35f), true);
            OrisamoUI.Place(divider.rectTransform, anchor, new Vector2(textX, -378f), new Vector2(1100f, 3f));

            // 必殺技(=スキル)の名前・発動する強さ・効果
            TextMeshProUGUI skillText = OrisamoUI.CreateText("Skill", Root, BuildSkillText(stats), 42f, OrisamoUI.Parchment, align);
            OrisamoUI.Place(skillText.rectTransform, anchor, new Vector2(textX, -388f), new Vector2(1100f, 96f));
            skillText.textWrappingMode = TextWrappingModes.Normal;
            skillText.enableAutoSizing = true;
            skillText.fontSizeMin = 28f;
            skillText.fontSizeMax = 42f;

            // 役割の表示(パネルの下)
            roleRoot = OrisamoUI.CreateRect("Role", Root);
            OrisamoUI.Place(roleRoot, new Vector2(anchor.x, 0f), new Vector2(textX, -30f), new Vector2(900f, 90f));
            roleRoot.pivot = new Vector2(anchor.x, 1f);
            rolePill = OrisamoUI.CreateImage("Pill", roleRoot, OrisamoUI.RoundedPanel, color, true);
            OrisamoUI.Place(rolePill.rectTransform, new Vector2(anchor.x, 0.5f), Vector2.zero, new Vector2(240f, 86f));
            roleText = OrisamoUI.CreateText("Text", rolePill.rectTransform, "", 54f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            OrisamoUI.Stretch(roleText.rectTransform);
            OrisamoUI.ApplyOutline(roleText, new Color(0f, 0f, 0f, 0.6f), 0.18f, new Color(0f, 0f, 0f, 0f));
            statusText = OrisamoUI.CreateText("Status", roleRoot, "", 54f, OrisamoUI.Parchment, align, FontStyles.Bold);
            OrisamoUI.Place(statusText.rectTransform, new Vector2(anchor.x, 0.5f), new Vector2(270f * s, 0f), new Vector2(620f, 80f));
            OrisamoUI.ApplyOutline(statusText, OrisamoUI.Ink, 0.25f, new Color(0f, 0f, 0f, 0.7f));
            roleRoot.gameObject.SetActive(false);

            SetHp(stats.hp, stats.maxHp);
            shown = trailValue = target;
        }

        public void SetHp(int hp, int maxHp)
        {
            float ratio = maxHp > 0 ? Mathf.Clamp01((float)Mathf.Max(hp, 0) / maxHp) : 0f;
            if (ratio < target) trailDelay = 0.45f;
            target = ratio;
            hpText.text = $"{Mathf.Max(hp, 0)} <size=70%>/ {maxHp}</size>";
        }

        public void SetAffinity(float multiplier)
        {
            affinityText.text = multiplier > ElementAffinity.NeutralMultiplier ? "<color=#FF9C6A>相性 有利 ▲</color>"
                : multiplier < ElementAffinity.NeutralMultiplier ? "<color=#8FB8FF>相性 不利 ▼</color>"
                : "相性 互角";
        }

        public void SetRole(string role, Color color, string status)
        {
            roleRoot.gameObject.SetActive(!string.IsNullOrEmpty(role));
            if (string.IsNullOrEmpty(role)) return;
            roleText.text = role;
            rolePill.color = color;
            statusText.text = status;
        }

        /// <summary>現在のステータス表示を作り直す(変化がなければ何もしない)。</summary>
        private void RefreshStats()
        {
            int attack = stats.GetEffectiveAttack();
            int defense = stats.GetEffectiveDefense();
            string tags = "";
            if (stats.shieldHp > 0) tags += $"　<color=#7FC4FF>結界 {stats.shieldHp}</color>";
            if (stats.HasSkill(SkillType.Endurance) && !stats.enduranceUsed) tags += "　<color=#F8CF70>不屈 残1</color>";
            if (stats.HasSkill(SkillType.FirstStrike) && !stats.firstStrikeUsed) tags += "　<color=#F8CF70>先手 残1</color>";
            string text = $"{StatChip("攻撃", stats.attack, attack)}　{StatChip("防御", stats.defense, defense)}　{StatChip("素早さ", stats.speed, stats.speed)}{tags}";
            if (text == statLineCache) return;
            statLineCache = text;
            statLine.text = text;
        }

        private static string StatChip(string label, int baseValue, int effective)
        {
            string diff = effective > baseValue ? $"<color=#7CFF8A><size=70%>(+{effective - baseValue})</size></color>"
                : effective < baseValue ? $"<color=#FF7A6E><size=70%>(-{baseValue - effective})</size></color>"
                : "";
            return $"<color=#F8CF70><size=75%>{label}</size></color> {effective}{diff}";
        }

        private static string BuildSkillText(CharacterStats stats)
        {
            string level = OrisamoUI.LevelKanji(stats.specialLevel);
            if (stats.skill == null)
            {
                return $"<color=#F8CF70><b>必殺技</b></color>　<size=85%>「{level}」で当てると威力アップ</size>";
            }
            return $"<color=#F8CF70><b>必殺技「{stats.skill.skillName}」</b></color>　<size=85%>「{level}」で当てると発動</size>\n" +
                   $"<size=85%><color=#D8D0BC>{stats.skill.GetDescription()}</color></size>";
        }

        public void Tick(float deltaTime)
        {
            RefreshStats();
            shown = Mathf.MoveTowards(shown, target, deltaTime * 1.4f);
            if (trailDelay > 0f) trailDelay -= deltaTime;
            else trailValue = Mathf.MoveTowards(trailValue, shown, deltaTime * 0.6f);
            if (trailValue < shown) trailValue = shown;
            fill.sizeDelta = new Vector2(BarWidth * shown, 0f);
            trail.sizeDelta = new Vector2(BarWidth * trailValue, 0f);
            fillImage.color = HpColor(shown);
            fill.gameObject.SetActive(shown > 0.005f);
        }

        private static Color HpColor(float ratio)
        {
            if (ratio > 0.5f) return new Color(0.36f, 0.95f, 0.48f);
            if (ratio > 0.2f) return new Color(1f, 0.8f, 0.25f);
            return new Color(1f, 0.32f, 0.27f);
        }
    }

    // ==================== 演出 ====================

    /// <summary>試合開始の「VS」演出。</summary>
    public IEnumerator PlayIntro(CharacterStats player1, CharacterStats player2)
    {
        RectTransform intro = OrisamoUI.Stretch(OrisamoUI.CreateRect("Intro", overlayLayer));
        Image band = OrisamoUI.CreateImage("Band", intro, OrisamoUI.RoundedFlat, new Color(0.03f, 0.02f, 0.07f, 0.82f), true);
        OrisamoUI.PlaceCenter(band.rectTransform, Vector2.zero, new Vector2(4200f, 420f));

        TextMeshProUGUI left = IntroName(intro, player1, Player1Color, -1f);
        TextMeshProUGUI right = IntroName(intro, player2, Player2Color, 1f);
        TextMeshProUGUI vs = OrisamoUI.CreateText("VS", intro, "VS", 320f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.Italic);
        OrisamoUI.PlaceCenter(vs.rectTransform, Vector2.zero, new Vector2(700f, 400f));
        OrisamoUI.ApplyGoldGradient(vs);
        OrisamoUI.ApplyOutline(vs, new Color(0.25f, 0.08f, 0.02f, 1f), 0.2f, new Color(0f, 0f, 0f, 0.8f), 1.5f);

        Vector2 leftTarget = left.rectTransform.anchoredPosition, rightTarget = right.rectTransform.anchoredPosition;
        for (float t = 0f; t < 0.45f; t += Time.deltaTime)
        {
            float e = OrisamoUI.EaseOutCubic(t / 0.45f);
            band.rectTransform.localScale = new Vector3(1f, e, 1f);
            left.rectTransform.anchoredPosition = leftTarget + new Vector2(-2400f * (1f - e), 0f);
            right.rectTransform.anchoredPosition = rightTarget + new Vector2(2400f * (1f - e), 0f);
            vs.rectTransform.localScale = Vector3.one * Mathf.Lerp(3f, 1f, e);
            vs.alpha = e;
            yield return null;
        }
        yield return PunchScale(vs.rectTransform, 0.3f);
        yield return new WaitForSeconds(1.2f);
        yield return FadeOut(intro, 0.35f);
        Destroy(intro.gameObject);
    }

    private static TextMeshProUGUI IntroName(Transform parent, CharacterStats stats, Color color, float side)
    {
        TextMeshProUGUI text = OrisamoUI.CreateText("Name", parent, stats.characterName, 150f, OrisamoUI.Parchment,
            side < 0f ? TextAlignmentOptions.Right : TextAlignmentOptions.Left, FontStyles.Bold);
        OrisamoUI.PlaceCenter(text.rectTransform, new Vector2(side * 1000f, 20f), new Vector2(1400f, 200f));
        text.rectTransform.pivot = new Vector2(side < 0f ? 1f : 0f, 0.5f);
        text.rectTransform.anchoredPosition = new Vector2(side * 320f, 20f);
        text.enableAutoSizing = true;
        text.fontSizeMin = 70f;
        text.fontSizeMax = 150f;
        OrisamoUI.ApplyOutline(text, OrisamoUI.Darken(color, 0.4f), 0.22f, new Color(0f, 0f, 0f, 0.6f));

        TextMeshProUGUI element = OrisamoUI.CreateText("Element", text.rectTransform,
            $"<color=#{ColorUtility.ToHtmlStringRGB(ElementAffinity.GetElementColor(stats.element))}>{OrisamoUI.ElementKanji(stats.element)}属性</color>",
            60f, OrisamoUI.Muted, side < 0f ? TextAlignmentOptions.Right : TextAlignmentOptions.Left, FontStyles.Bold);
        OrisamoUI.Place(element.rectTransform, new Vector2(side < 0f ? 1f : 0f, 0f), new Vector2(0f, -70f), new Vector2(800f, 80f));
        if (stats.isMutation) element.text += "　<color=#FFE08A>★ RARE</color>";
        return text;
    }

    /// <summary>
    /// 攻撃側と防御側の「強/普/弱」を同時に公開する。guardedなら「ガード！」、そうでなければ「ヒット！」。
    /// </summary>
    public IEnumerator PlayReveal(AttackLevel attack, AttackLevel defense, bool attackerIsPlayer1, bool guarded)
    {
        OrisamoAudio.Play(OrisamoAudio.Sfx.Reveal);
        RectTransform reveal = OrisamoUI.Stretch(OrisamoUI.CreateRect("Reveal", overlayLayer));
        float attackSide = attackerIsPlayer1 ? -1f : 1f;
        RectTransform attackToken = LevelToken(reveal, attack, "攻撃", OrisamoUI.AttackColor, new Vector2(attackSide * 430f, 160f));
        RectTransform defenseToken = LevelToken(reveal, defense, "防御", OrisamoUI.DefenseColor, new Vector2(-attackSide * 430f, 160f));
        Vector2 aTarget = attackToken.anchoredPosition, dTarget = defenseToken.anchoredPosition;

        for (float t = 0f; t < 0.4f; t += Time.deltaTime)
        {
            float e = OrisamoUI.EaseOutBack(t / 0.4f);
            attackToken.anchoredPosition = Vector2.LerpUnclamped(aTarget + new Vector2(attackSide * 1500f, 0f), aTarget, e);
            defenseToken.anchoredPosition = Vector2.LerpUnclamped(dTarget - new Vector2(attackSide * 1500f, 0f), dTarget, e);
            yield return null;
        }
        attackToken.anchoredPosition = aTarget;
        defenseToken.anchoredPosition = dTarget;
        yield return new WaitForSeconds(0.25f);

        TextMeshProUGUI verdict = OrisamoUI.CreateText("Verdict", reveal, guarded ? "ガード！" : "ヒット！", 200f,
            guarded ? OrisamoUI.DefenseColor : OrisamoUI.AttackColor, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.Italic);
        OrisamoUI.PlaceCenter(verdict.rectTransform, new Vector2(0f, 160f), new Vector2(1200f, 260f));
        OrisamoUI.ApplyOutline(verdict, Color.white, 0.12f, new Color(0f, 0f, 0f, 0.8f), 1.5f);
        StartCoroutine(PunchScale(verdict.rectTransform, 0.3f, 0.4f));
        StartCoroutine(PunchScale(guarded ? defenseToken : attackToken, 0.3f, 0.2f));
        yield return new WaitForSeconds(0.85f);
        yield return FadeOut(reveal, 0.25f);
        Destroy(reveal.gameObject);
    }

    private static RectTransform LevelToken(Transform parent, AttackLevel level, string role, Color roleColor, Vector2 position)
    {
        Color levelColor = OrisamoUI.LevelColor(level);
        RectTransform token = OrisamoUI.CreateRect("Token", parent);
        OrisamoUI.PlaceCenter(token, position, new Vector2(360f, 360f));
        Image glow = OrisamoUI.CreateImage("Glow", token, OrisamoUI.SoftGlow, OrisamoUI.WithAlpha(levelColor, 0.8f));
        OrisamoUI.PlaceCenter(glow.rectTransform, Vector2.zero, new Vector2(680f, 680f));
        Image disc = OrisamoUI.CreateImage("Disc", token, OrisamoUI.Circle, new Color(0.05f, 0.03f, 0.1f, 0.95f));
        OrisamoUI.Stretch(disc.rectTransform);
        Image ring = OrisamoUI.CreateImage("Ring", token, OrisamoUI.ThickRing, levelColor);
        OrisamoUI.Stretch(ring.rectTransform);
        Image gold = OrisamoUI.CreateImage("Gold", token, OrisamoUI.ThinRing, OrisamoUI.Gold);
        OrisamoUI.Stretch(gold.rectTransform, -16f);
        TextMeshProUGUI kanji = OrisamoUI.CreateText("Kanji", token, OrisamoUI.LevelKanji(level), 210f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Stretch(kanji.rectTransform);
        OrisamoUI.ApplyOutline(kanji, OrisamoUI.Darken(levelColor, 0.5f), 0.2f, new Color(0f, 0f, 0f, 0.6f));

        Image pill = OrisamoUI.CreateImage("RolePill", token, OrisamoUI.RoundedPanel, roleColor, true);
        OrisamoUI.Place(pill.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 70f), new Vector2(260f, 86f));
        pill.rectTransform.pivot = new Vector2(0.5f, 0f);
        TextMeshProUGUI label = OrisamoUI.CreateText("Role", pill.rectTransform, role, 54f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Stretch(label.rectTransform);
        return token;
    }

    /// <summary>画面を横切る帯の演出(必殺技名・決着など)。</summary>
    public IEnumerator PlayBanner(string title, string subtitle, Color color, float hold = 0.9f)
    {
        RectTransform banner = OrisamoUI.CreateRect("Banner", overlayLayer);
        OrisamoUI.PlaceCenter(banner, new Vector2(0f, 200f), new Vector2(4400f, 360f));
        Image band = OrisamoUI.CreateImage("Band", banner, OrisamoUI.RoundedFlat, new Color(0.03f, 0.02f, 0.07f, 0.88f), true);
        OrisamoUI.Stretch(band.rectTransform);
        foreach (float y in new[] { 0f, 1f })
        {
            Image edge = OrisamoUI.CreateImage("Edge", banner, OrisamoUI.RoundedFlat, color, true);
            edge.rectTransform.anchorMin = new Vector2(0f, y);
            edge.rectTransform.anchorMax = new Vector2(1f, y);
            edge.rectTransform.sizeDelta = new Vector2(0f, 12f);
            edge.rectTransform.anchoredPosition = Vector2.zero;
        }
        Image glow = OrisamoUI.CreateImage("Glow", banner, OrisamoUI.SoftGlow, OrisamoUI.WithAlpha(color, 0.5f));
        OrisamoUI.PlaceCenter(glow.rectTransform, Vector2.zero, new Vector2(2600f, 700f));

        TextMeshProUGUI main = OrisamoUI.CreateText("Title", banner, title, 170f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.PlaceCenter(main.rectTransform, new Vector2(0f, string.IsNullOrEmpty(subtitle) ? 0f : 30f), new Vector2(3600f, 220f));
        main.enableAutoSizing = true;
        main.fontSizeMin = 80f;
        main.fontSizeMax = 170f;
        OrisamoUI.ApplyGoldGradient(main);
        OrisamoUI.ApplyOutline(main, new Color(0.2f, 0.06f, 0.02f, 1f), 0.2f, new Color(0f, 0f, 0f, 0.8f), 1.5f);
        if (!string.IsNullOrEmpty(subtitle))
        {
            TextMeshProUGUI sub = OrisamoUI.CreateText("Subtitle", banner, subtitle, 64f, color, TextAlignmentOptions.Center, FontStyles.Bold);
            OrisamoUI.PlaceCenter(sub.rectTransform, new Vector2(0f, -115f), new Vector2(3600f, 90f));
        }

        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            float e = OrisamoUI.EaseOutCubic(t / 0.3f);
            banner.anchoredPosition = new Vector2(Mathf.Lerp(-4400f, 0f, e), 200f);
            yield return null;
        }
        banner.anchoredPosition = new Vector2(0f, 200f);
        yield return new WaitForSeconds(hold);
        for (float t = 0f; t < 0.25f; t += Time.deltaTime)
        {
            float e = t / 0.25f;
            banner.anchoredPosition = new Vector2(Mathf.Lerp(0f, 4400f, e * e), 200f);
            yield return null;
        }
        Destroy(banner.gameObject);
    }

    // ==================== とどめの一撃(確定演出) ====================
    // スローモーション中も同じ速さで動くよう、ここの演出はすべて unscaledDeltaTime で進める。

    private RectTransform finisherRoot;
    private Image finisherShade;
    private readonly System.Collections.Generic.List<Image> speedLines = new System.Collections.Generic.List<Image>();

    /// <summary>
    /// この一撃で決着がつく時の予告: 画面の周りが暗くなり、集中線が走り、「FINAL ATTACK」のカットインが入る。
    /// 集中線と暗幕は EndFinisherCue を呼ぶまで残す(突進〜吹き飛ぶまで画面を締めておくため)。
    /// </summary>
    public IEnumerator PlayFinisherCue(string attackerName, Color color)
    {
        EndFinisherCueImmediate();
        OrisamoAudio.Play(OrisamoAudio.Sfx.FinisherCue);
        finisherRoot = OrisamoUI.Stretch(OrisamoUI.CreateRect("Finisher", overlayLayer));
        finisherRoot.SetAsLastSibling();

        finisherShade = OrisamoUI.CreateImage("Shade", finisherRoot, OrisamoUI.Vignette, new Color(0.05f, 0f, 0.02f, 0f));
        OrisamoUI.Stretch(finisherShade.rectTransform, -300f);

        RectTransform lines = OrisamoUI.Stretch(OrisamoUI.CreateRect("SpeedLines", finisherRoot));
        speedLines.Clear();
        for (int i = 0; i < 72; i++)
        {
            Image line = OrisamoUI.CreateImage("Line", lines, null, Color.clear);
            RectTransform r = line.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            float angle = (i + Random.value * 0.6f) / 72f * 360f;
            r.localRotation = Quaternion.Euler(0f, 0f, angle);
            speedLines.Add(line);
        }
        StartCoroutine(AnimateSpeedLines(finisherRoot, color));

        StartCoroutine(FlashRoutine(Color.white, 0.35f));
        TextMeshProUGUI title = OrisamoUI.CreateText("Title", finisherRoot, "FINAL ATTACK", 260f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.Italic);
        OrisamoUI.PlaceCenter(title.rectTransform, new Vector2(0f, 120f), new Vector2(3600f, 320f));
        title.characterSpacing = 6f;
        OrisamoUI.ApplyGoldGradient(title);
        OrisamoUI.ApplyOutline(title, new Color(0.35f, 0.04f, 0.02f, 1f), 0.22f, new Color(0f, 0f, 0f, 0.85f), 1.6f);
        TextMeshProUGUI sub = OrisamoUI.CreateText("Subtitle", finisherRoot, $"{attackerName} の とどめの一撃！", 96f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.PlaceCenter(sub.rectTransform, new Vector2(0f, -110f), new Vector2(3600f, 130f));
        OrisamoUI.ApplyOutline(sub, Color.Lerp(OrisamoUI.Ink, color, 0.4f), 0.25f, new Color(0f, 0f, 0f, 0.8f), 1.2f);

        // 大きく叩きつけるように現れ、少し震えてから上へ抜けていく
        const float total = 1.5f;
        for (float t = 0f; t < total; t += Time.unscaledDeltaTime)
        {
            float slam = OrisamoUI.EaseOutCubic(Mathf.Clamp01(t / 0.18f));
            float shake = t < 0.45f ? (0.45f - t) * 40f : 0f;
            Vector2 jitter = new Vector2(Random.Range(-shake, shake), Random.Range(-shake, shake));
            title.rectTransform.localScale = Vector3.one * Mathf.Lerp(2.6f, 1f, slam);
            title.rectTransform.anchoredPosition = new Vector2(0f, 120f) + jitter;
            sub.alpha = Mathf.Clamp01((t - 0.2f) / 0.2f);
            finisherShade.color = new Color(0.05f, 0f, 0.02f, Mathf.Clamp01(t / 0.25f) * 0.85f);

            float leave = Mathf.Clamp01((t - 1.15f) / 0.35f);
            if (leave > 0f)
            {
                title.alpha = sub.alpha = 1f - leave;
                title.rectTransform.anchoredPosition += Vector2.up * (leave * leave * 300f);
            }
            yield return null;
        }
        Destroy(title.gameObject);
        Destroy(sub.gameObject);
    }

    /// <summary>集中線と暗幕を消す。</summary>
    public void EndFinisherCue()
    {
        if (finisherRoot != null) StartCoroutine(FadeOutFinisher(finisherRoot));
        finisherRoot = null;
    }

    private void EndFinisherCueImmediate()
    {
        if (finisherRoot != null) Destroy(finisherRoot.gameObject);
        finisherRoot = null;
    }

    private IEnumerator FadeOutFinisher(RectTransform target)
    {
        CanvasGroup group = target.gameObject.AddComponent<CanvasGroup>();
        for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
        {
            if (target == null) yield break;
            group.alpha = 1f - t / 0.5f;
            yield return null;
        }
        if (target != null) Destroy(target.gameObject);
    }

    /// <summary>集中線を毎フレーム少しずつ変えて、ちらつかせる。</summary>
    private IEnumerator AnimateSpeedLines(RectTransform owner, Color color)
    {
        Color tint = Color.Lerp(Color.white, color, 0.35f);
        while (owner != null) // 消えるまで(EndFinisherCueのフェードアウト中も)動かし続ける
        {
            foreach (Image line in speedLines)
            {
                if (line == null) continue;
                RectTransform r = line.rectTransform;
                float radius = Random.Range(1050f, 1500f);
                Vector3 dir = r.localRotation * Vector3.right;
                r.anchoredPosition = new Vector2(dir.x, dir.y) * radius;
                r.sizeDelta = new Vector2(Random.Range(900f, 1600f), Random.Range(4f, 16f));
                line.color = OrisamoUI.WithAlpha(tint, Random.Range(0.25f, 0.7f));
            }
            yield return new WaitForSecondsRealtime(0.05f);
        }
    }

    /// <summary>画面全体を一瞬光らせる(命中の瞬間など)。</summary>
    public void FlashScreen(Color color, float duration = 0.4f)
    {
        StartCoroutine(FlashRoutine(color, duration));
    }

    private IEnumerator FlashRoutine(Color color, float duration)
    {
        Image flash = OrisamoUI.CreateImage("Flash", overlayLayer, null, color);
        OrisamoUI.Stretch(flash.rectTransform, -50f);
        flash.transform.SetAsLastSibling();
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float p = t / duration;
            flash.color = OrisamoUI.WithAlpha(color, (1f - p) * (1f - p) * 0.9f);
            yield return null;
        }
        Destroy(flash.gameObject);
    }

    /// <summary>決着の「K.O.」を画面に叩きつける。</summary>
    public IEnumerator PlayKnockoutStamp()
    {
        TextMeshProUGUI ko = OrisamoUI.CreateText("KO", overlayLayer, "K.O.", 520f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.Italic);
        OrisamoUI.PlaceCenter(ko.rectTransform, new Vector2(0f, 120f), new Vector2(3000f, 600f));
        ko.transform.SetAsLastSibling();
        ko.enableVertexGradient = true;
        ko.colorGradient = new VertexGradient(new Color(1f, 0.92f, 0.6f), new Color(1f, 0.92f, 0.6f), new Color(1f, 0.32f, 0.2f), new Color(1f, 0.32f, 0.2f));
        OrisamoUI.ApplyOutline(ko, new Color(0.25f, 0.02f, 0.02f, 1f), 0.24f, new Color(0f, 0f, 0f, 0.85f), 2f);
        StartCoroutine(FlashRoutine(new Color(1f, 0.85f, 0.6f), 0.3f));
        OrisamoAudio.Play(OrisamoAudio.Sfx.KnockOut);

        const float total = 1.6f;
        for (float t = 0f; t < total; t += Time.unscaledDeltaTime)
        {
            float slam = OrisamoUI.EaseOutBack(Mathf.Clamp01(t / 0.22f));
            float shake = t > 0.18f && t < 0.5f ? (0.5f - t) * 60f : 0f;
            ko.rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(3.2f, 1f, slam);
            ko.rectTransform.anchoredPosition = new Vector2(Random.Range(-shake, shake), 120f + Random.Range(-shake, shake));
            ko.alpha = t > 1.25f ? 1f - (t - 1.25f) / 0.35f : 1f;
            yield return null;
        }
        Destroy(ko.gameObject);
    }

    /// <summary>短い掛け声(「効果はばつぐんだ！」など)。待たずに進めてよい。</summary>
    public void Callout(string text, Color color)
    {
        StartCoroutine(CalloutRoutine(text, color));
    }

    private IEnumerator CalloutRoutine(string text, Color color)
    {
        TextMeshProUGUI label = OrisamoUI.CreateText("Callout", overlayLayer, text, 120f, color, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.Italic);
        OrisamoUI.PlaceCenter(label.rectTransform, new Vector2(0f, 420f), new Vector2(2600f, 180f));
        OrisamoUI.ApplyOutline(label, OrisamoUI.Ink, 0.25f, new Color(0f, 0f, 0f, 0.8f), 1.5f);
        for (float t = 0f; t < 1.4f; t += Time.deltaTime)
        {
            float p = t / 1.4f;
            label.rectTransform.localScale = Vector3.one * (p < 0.15f ? Mathf.Lerp(1.6f, 1f, p / 0.15f) : 1f);
            label.rectTransform.anchoredPosition = new Vector2(0f, 420f + p * 60f);
            label.alpha = p > 0.75f ? 1f - (p - 0.75f) / 0.25f : 1f;
            yield return null;
        }
        Destroy(label.gameObject);
    }

    /// <summary>3D空間の位置にダメージ数字を出す。</summary>
    public void SpawnDamageNumber(Vector3 worldPosition, int amount, Color color, bool critical)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 screen = cam.WorldToScreenPoint(worldPosition);
        if (screen.z < 0f) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(effectsLayer, screen, null, out Vector2 local)) return;
        StartCoroutine(DamageRoutine(local, amount, color, critical));
    }

    private IEnumerator DamageRoutine(Vector2 position, int amount, Color color, bool critical)
    {
        TextMeshProUGUI number = OrisamoUI.CreateText("Damage", effectsLayer, amount.ToString(), critical ? 260f : 190f, color, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.Italic);
        number.rectTransform.anchorMin = number.rectTransform.anchorMax = Vector2.one * 0.5f;
        number.rectTransform.sizeDelta = new Vector2(800f, 300f);
        OrisamoUI.ApplyOutline(number, new Color(0.12f, 0.02f, 0.02f, 1f), 0.28f, new Color(0f, 0f, 0f, 0.8f), 1.5f);
        float drift = Random.Range(-60f, 60f);
        for (float t = 0f; t < 1.2f; t += Time.deltaTime)
        {
            float p = t / 1.2f;
            float pop = p < 0.12f ? Mathf.Lerp(2.2f, 1f, OrisamoUI.EaseOutCubic(p / 0.12f)) : 1f;
            number.rectTransform.localScale = Vector3.one * pop;
            number.rectTransform.anchoredPosition = position + new Vector2(drift * p, OrisamoUI.EaseOutCubic(p) * 220f);
            number.alpha = p > 0.7f ? 1f - (p - 0.7f) / 0.3f : 1f;
            yield return null;
        }
        Destroy(number.gameObject);
    }

    // ==================== 結果 ====================

    /// <summary>結果を表示する。winnerがnullなら引き分け。</summary>
    public void ShowResult(CharacterStats winner, bool winnerIsPlayer1, string reason)
    {
        battleLayer.gameObject.SetActive(false);
        foreach (Transform child in resultLayer) Destroy(child.gameObject);
        resultLayer.gameObject.SetActive(true);

        Color sideColor = winner == null ? OrisamoUI.Gold : winnerIsPlayer1 ? Player1Color : Player2Color;

        RectTransform header = OrisamoUI.CreateRect("Header", resultLayer);
        OrisamoUI.Place(header, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(3000f, 360f));
        Image glow = OrisamoUI.CreateImage("Glow", header, OrisamoUI.SoftGlow, OrisamoUI.WithAlpha(OrisamoUI.Gold, 0.55f));
        OrisamoUI.PlaceCenter(glow.rectTransform, Vector2.zero, new Vector2(2600f, 800f));
        TextMeshProUGUI title = OrisamoUI.CreateText("Title", header, winner == null ? "DRAW" : "WINNER", 300f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Stretch(title.rectTransform);
        title.characterSpacing = 24f;
        OrisamoUI.ApplyGoldGradient(title);
        OrisamoUI.ApplyOutline(title, new Color(0.25f, 0.08f, 0.02f, 1f), 0.2f, new Color(0f, 0f, 0f, 0.8f), 1.5f);
        StartCoroutine(PunchScale(header, 0.45f, 0.25f));

        RectTransform panel = OrisamoUI.CreateOrnatePanel("Panel", resultLayer, OrisamoUI.PanelFill, OrisamoUI.Gold);
        OrisamoUI.Place(panel, new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(2400f, 380f));
        TextMeshProUGUI name = OrisamoUI.CreateText("Name", panel, winner == null ? "引き分け" : $"{winner.characterName} の勝利！", 130f,
            OrisamoUI.Parchment, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(2300f, 160f));
        name.enableAutoSizing = true;
        name.fontSizeMin = 60f;
        name.fontSizeMax = 130f;
        OrisamoUI.ApplyOutline(name, OrisamoUI.Darken(sideColor, 0.35f), 0.2f, new Color(0f, 0f, 0f, 0.6f));

        TextMeshProUGUI reasonText = OrisamoUI.CreateText("Reason", panel, reason, 58f, sideColor, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.PlaceCenter(reasonText.rectTransform, new Vector2(0f, -40f), new Vector2(2300f, 80f));
        resultCountdown = OrisamoUI.CreateText("Next", panel, "", 50f, OrisamoUI.Muted);
        OrisamoUI.Place(resultCountdown.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(2300f, 70f));
        StartCoroutine(SlideIn(panel, new Vector2(0f, -600f), 0.5f));
    }

    public void SetResultCountdown(int seconds)
    {
        if (resultCountdown != null) resultCountdown.text = seconds > 0 ? $"次の対戦の受付まで {seconds} 秒" : "";
    }

    // ==================== 補助 ====================

    private static IEnumerator SlideIn(RectTransform target, Vector2 offset, float duration)
    {
        Vector2 end = target.anchoredPosition;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            if (target == null) yield break;
            target.anchoredPosition = end + offset * (1f - OrisamoUI.EaseOutCubic(t / duration));
            yield return null;
        }
        if (target != null) target.anchoredPosition = end;
    }

    private static IEnumerator PunchScale(RectTransform target, float duration, float strength = 0.15f)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            if (target == null) yield break;
            target.localScale = Vector3.one * (1f + Mathf.Sin(t / duration * Mathf.PI) * strength);
            yield return null;
        }
        if (target != null) target.localScale = Vector3.one;
    }

    private static IEnumerator FadeText(TextMeshProUGUI text, float duration)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            text.alpha = t / duration;
            yield return null;
        }
        text.alpha = 1f;
    }

    private static IEnumerator FadeOut(RectTransform target, float duration)
    {
        CanvasGroup group = target.gameObject.AddComponent<CanvasGroup>();
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            group.alpha = 1f - t / duration;
            yield return null;
        }
        group.alpha = 0f;
    }
}
