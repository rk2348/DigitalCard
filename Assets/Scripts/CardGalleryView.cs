using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// 登録カード一覧モード。タイトル画面の「登録カード一覧」ボタンから開く全画面の表示。
///
/// ・Firebase の /characterByCard を shallow 取得してカードIDの一覧を作り、各カードの中身を個別に取得する
///   (写真を含むため1件ずつ取得する。一度読んだカードは登録後に変わらないので、開き直しても再取得しない)
/// ・登録日時の新しい順に、1ページ10枚(5列x2段)で並べる。カードを押すと詳細を大きく表示する
/// ・写真のテクスチャは表示中のページの分だけ作り、ページを替えると破棄する
/// ・操作: ←/→ でページ送り、ESC で詳細→一覧の順に閉じる
/// ・詳細ではスマホ対戦の戦績(CardRecords)も表示する
/// ・詳細の「PLAYER 1 / PLAYER 2 に選ぶ」で2枚選ぶと「CPU対戦を始める」が押せるようになり、
///   その2枚をCPU同士で戦わせる(StartCpuMatch。タイトル画面がバトルシーンへの遷移をつなぐ)
/// ・OpenForTournament で開くと「トーナメント出場カードを選ぶ」画面になる。人数(4/8/16)を選び、
///   詳細の「出場させる」で出場カードを選んで「トーナメント開始」(StartTournament)。足りない分は Tournament がランダムに補充する
///
/// /characterByCard 全体の読み取りは Database Rules 上で会場PC(/admins に登録済みのUID)にしか許可されていない。
/// </summary>
public sealed class CardGalleryView : MonoBehaviour
{
    private const int Columns = 5;
    private const int Rows = 2;
    private const int PageSize = Columns * Rows;
    private const int ParallelRequests = 4;
    private static readonly Vector2 CardSize = new Vector2(620f, 740f);
    private static readonly Vector2 CardGap = new Vector2(60f, 60f);
    private static readonly Regex ShallowKey = new Regex("\"([^\"]+)\"\\s*:\\s*true");

    private readonly Dictionary<string, FirebaseCharacterRecord> recordCache = new Dictionary<string, FirebaseCharacterRecord>();
    private readonly List<FirebaseCharacterRecord> records = new List<FirebaseCharacterRecord>();
    private readonly List<Texture2D> pageTextures = new List<Texture2D>();

    private RectTransform root;
    private RectTransform grid;
    private RectTransform detailLayer;
    private RectTransform detailContent;
    private TextMeshProUGUI countText;
    private TextMeshProUGUI statusText;
    private TextMeshProUGUI pageText;
    private Button prevButton;
    private Button nextButton;
    private Button reloadButton;
    private int page;
    private int loadGeneration;
    private bool loading;

    public bool IsOpen => root != null && root.gameObject.activeSelf;

    /// <summary>「CPU対戦を始める」が押された時に、選んだ2枚(PLAYER 1, PLAYER 2)を渡す。</summary>
    public Action<FirebaseCharacterRecord, FirebaseCharacterRecord> StartCpuMatch;

    private FirebaseCharacterRecord pickedPlayer1;
    private FirebaseCharacterRecord pickedPlayer2;
    private Button startMatchButton;

    /// <summary>「トーナメント開始」が押された時に、選んだカード・登録カード全体(補充用)・人数を渡す。</summary>
    public Action<List<FirebaseCharacterRecord>, List<FirebaseCharacterRecord>, int> StartTournament;

    private bool tournamentMode;
    private int tournamentSize = 8;
    private readonly List<FirebaseCharacterRecord> tournamentPicks = new List<FirebaseCharacterRecord>();
    private TextMeshProUGUI titleText;
    private Button sizeButton;
    private Button startTournamentButton;
    private string pickWarning;

    public static CardGalleryView Create(Canvas canvas)
    {
        CardGalleryView view = OrisamoUI.CreateRect("CardGallery", canvas.transform).gameObject.AddComponent<CardGalleryView>();
        view.Build();
        return view;
    }

    private void Build()
    {
        root = OrisamoUI.Stretch((RectTransform)transform);
        root.SetAsLastSibling();
        gameObject.AddComponent<UIAnimationIgnore>();

        // 下のタイトル画面のボタンを押せないよう、背景でクリックを受け止める
        Image backdrop = OrisamoUI.CreateImage("Backdrop", root, null, OrisamoUI.WithAlpha(OrisamoUI.Ink, 0.96f));
        OrisamoUI.Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;
        Image glow = OrisamoUI.CreateImage("Glow", root, OrisamoUI.SoftGlow, OrisamoUI.WithAlpha(OrisamoUI.Arcane, 0.25f));
        OrisamoUI.PlaceCenter(glow.rectTransform, new Vector2(0f, 700f), new Vector2(3600f, 1200f));

        TextMeshProUGUI title = titleText = OrisamoUI.CreateText("Title", root, "登録カード一覧", 120f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(2000f, 150f));
        title.characterSpacing = 8f;
        OrisamoUI.ApplyGoldGradient(title);
        OrisamoUI.ApplyOutline(title, new Color(0.2f, 0.07f, 0.02f, 1f), 0.16f, new Color(0f, 0f, 0f, 0.8f));

        countText = OrisamoUI.CreateText("Count", root, "", 54f, OrisamoUI.Muted);
        OrisamoUI.Place(countText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -205f), new Vector2(2000f, 70f));

        CreateButton(root, "閉じる", new Vector2(1f, 1f), new Vector2(-70f, -70f), new Vector2(460f, 130f), Close);
        reloadButton = CreateButton(root, "再読み込み", new Vector2(0f, 1f), new Vector2(70f, -70f), new Vector2(560f, 130f), () => Reload(true));
        startMatchButton = CreateButton(root, "CPU対戦を始める", new Vector2(0f, 1f), new Vector2(690f, -70f), new Vector2(680f, 130f), StartPickedMatch);
        startMatchButton.GetComponent<UIProductButtonStyle>().SetPrimary(true);
        sizeButton = CreateButton(root, "人数 8人", new Vector2(0f, 1f), new Vector2(690f, -70f), new Vector2(480f, 130f), CycleTournamentSize);
        startTournamentButton = CreateButton(root, "トーナメント開始", new Vector2(1f, 1f), new Vector2(-600f, -70f), new Vector2(640f, 130f), StartPickedTournament);
        startTournamentButton.GetComponent<UIProductButtonStyle>().SetPrimary(true);

        grid = OrisamoUI.CreateRect("Grid", root);
        OrisamoUI.PlaceCenter(grid, new Vector2(0f, -10f),
            new Vector2(Columns * CardSize.x + (Columns - 1) * CardGap.x, Rows * CardSize.y + (Rows - 1) * CardGap.y));

        statusText = OrisamoUI.CreateText("Status", root, "", 64f, OrisamoUI.Parchment);
        OrisamoUI.PlaceCenter(statusText.rectTransform, new Vector2(0f, 0f), new Vector2(3000f, 300f));
        statusText.textWrappingMode = TextWrappingModes.Normal;
        OrisamoUI.ApplyOutline(statusText, OrisamoUI.Ink, 0.25f, new Color(0f, 0f, 0f, 0.6f));

        prevButton = CreateButton(root, "前のページ", new Vector2(0.5f, 0f), new Vector2(-720f, 60f), new Vector2(560f, 130f), () => ShowPage(page - 1));
        nextButton = CreateButton(root, "次のページ", new Vector2(0.5f, 0f), new Vector2(720f, 60f), new Vector2(560f, 130f), () => ShowPage(page + 1));
        pageText = OrisamoUI.CreateText("Page", root, "", 64f, OrisamoUI.Parchment, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Place(pageText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(700f, 80f));

        BuildDetailLayer();
        root.gameObject.SetActive(false);
    }

    private void BuildDetailLayer()
    {
        detailLayer = OrisamoUI.Stretch(OrisamoUI.CreateRect("Detail", root));
        Image shade = OrisamoUI.CreateImage("Shade", detailLayer, null, new Color(0f, 0f, 0f, 0.75f));
        OrisamoUI.Stretch(shade.rectTransform);
        shade.raycastTarget = true;
        // 暗幕はButtonにしない(UIAnimationDirectorがシーン内の全Buttonに装飾を付けるため)。閉じるのはボタンかESC

        detailContent = OrisamoUI.CreateRect("Content", detailLayer);
        OrisamoUI.PlaceCenter(detailContent, Vector2.zero, new Vector2(2600f, 1560f));
        detailLayer.gameObject.SetActive(false);
    }

    private Button CreateButton(Transform parent, string text, Vector2 anchor, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(text, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        OrisamoUI.Place((RectTransform)go.transform, anchor, position, size);
        TextMeshProUGUI label = OrisamoUI.CreateText("Label", go.transform, text, 54f, OrisamoUI.Parchment, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Stretch(label.rectTransform);
        Button button = go.GetComponent<Button>();
        button.onClick.AddListener(onClick);
        go.AddComponent<UIProductButtonStyle>();
        go.AddComponent<UIButtonMotion>();
        return button;
    }

    // ==================== 開閉 ====================

    public void Open() => OpenAs(false);

    /// <summary>トーナメントの出場カードを選ぶ画面として開く。</summary>
    public void OpenForTournament() => OpenAs(true);

    private void OpenAs(bool tournament)
    {
        tournamentMode = tournament;
        pickWarning = null;
        titleText.text = tournament ? "トーナメント出場カード" : "登録カード一覧";
        startMatchButton.gameObject.SetActive(!tournament);
        sizeButton.gameObject.SetActive(tournament);
        startTournamentButton.gameObject.SetActive(tournament);
        sizeButton.GetComponentInChildren<TextMeshProUGUI>().text = $"人数 {tournamentSize}人";
        root.SetAsLastSibling();
        root.gameObject.SetActive(true);
        detailLayer.gameObject.SetActive(false);
        Reload(false);
    }

    public void Close()
    {
        loadGeneration++; // 読み込み中なら打ち切る
        loading = false;
        ClearPage();
        root.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (detailLayer.gameObject.activeSelf) CloseDetail();
            else Close();
        }
        else if (!detailLayer.gameObject.activeSelf && !loading)
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow)) ShowPage(page - 1);
            else if (Input.GetKeyDown(KeyCode.RightArrow)) ShowPage(page + 1);
        }
    }

    private void OnDestroy()
    {
        ClearPage();
    }

    // ==================== 読み込み ====================

    /// <param name="clearCache">true なら取得済みのカードも取り直す。</param>
    private void Reload(bool clearCache)
    {
        if (clearCache) recordCache.Clear();
        StartCoroutine(LoadAll(++loadGeneration));
    }

    private IEnumerator LoadAll(int generation)
    {
        loading = true;
        ClearPage();
        records.Clear();
        SetNavigation(false);
        reloadButton.interactable = false;
        countText.text = "";
        statusText.text = "読み込み中…";

        List<string> cardIds = null;
        string error = null;
        yield return FirebaseRest.Get("characterByCard", req =>
        {
            if (req.result != UnityWebRequest.Result.Success) error = req.error;
            else cardIds = ParseShallowKeys(req.downloadHandler.text);
        }, "shallow=true");
        if (generation != loadGeneration) yield break;

        if (cardIds == null)
        {
            Debug.LogWarning("CardGalleryView: カード一覧を取得できませんでした: " + error);
            FinishLoading("カード一覧を読み込めませんでした。\n<size=75%>ネットワーク接続と、この会場PCが管理者(/admins)に登録されているかを確認してください。</size>");
            yield break;
        }

        Queue<string> missing = new Queue<string>();
        foreach (string id in cardIds)
        {
            if (!recordCache.ContainsKey(id)) missing.Enqueue(id);
        }

        int total = missing.Count;
        int done = 0;
        int running = 0;
        while (missing.Count > 0 || running > 0)
        {
            while (missing.Count > 0 && running < ParallelRequests)
            {
                string id = missing.Dequeue();
                running++;
                StartCoroutine(FetchRecord(id, generation, () => { running--; done++; }));
            }
            statusText.text = $"読み込み中… {done} / {total}";
            yield return null;
            if (generation != loadGeneration) yield break;
        }

        records.Clear();
        foreach (string id in cardIds)
        {
            if (recordCache.TryGetValue(id, out FirebaseCharacterRecord record) && record != null) records.Add(record);
        }
        // 新しく登録されたカードを先頭に並べる
        records.Sort((a, b) => b.timestamp.CompareTo(a.timestamp));

        FinishLoading(records.Count == 0 ? "まだ登録されたカードはありません。" : "");
        ShowPage(0);
    }

    private IEnumerator FetchRecord(string cardId, int generation, Action onDone)
    {
        yield return FirebaseRest.Get("characterByCard/" + cardId, req =>
        {
            if (generation != loadGeneration) return;
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"CardGalleryView: カード {cardId} を取得できませんでした: {req.error}");
                return;
            }
            string json = req.downloadHandler.text;
            if (string.IsNullOrEmpty(json) || json == "null") return;
            try
            {
                FirebaseCharacterRecord record = JsonUtility.FromJson<FirebaseCharacterRecord>(json);
                if (record != null)
                {
                    if (string.IsNullOrEmpty(record.cardId)) record.cardId = cardId;
                    recordCache[cardId] = record;
                }
            }
            catch (ArgumentException exception)
            {
                Debug.LogWarning($"CardGalleryView: カード {cardId} のデータを読めませんでした: {exception.Message}");
            }
        });
        onDone();
    }

    private static List<string> ParseShallowKeys(string json)
    {
        List<string> keys = new List<string>();
        if (string.IsNullOrEmpty(json) || json.Trim() == "null") return keys;
        foreach (Match match in ShallowKey.Matches(json)) keys.Add(match.Groups[1].Value);
        return keys;
    }

    private void FinishLoading(string message)
    {
        loading = false;
        reloadButton.interactable = true;
        statusText.text = message;
        RefreshPicks();
    }

    private void RefreshPicks()
    {
        if (tournamentMode)
        {
            startTournamentButton.interactable = records.Count >= 2 && StartTournament != null;
            string note = pickWarning ?? (tournamentPicks.Count > tournamentSize
                ? $"<color=#FF9C7A>選んだカードが人数より多いので、{tournamentSize}枚までが出場します</color>"
                : "足りない分は、選んでいない登録カードからランダムに選びます");
            countText.text = $"出場カード <color=#F8CF70>{tournamentPicks.Count} / {tournamentSize}枚</color> 選択中　／　{note}";
            return;
        }

        startMatchButton.interactable = pickedPlayer1 != null && pickedPlayer2 != null && StartCpuMatch != null;
        if (pickedPlayer1 == null && pickedPlayer2 == null)
        {
            countText.text = records.Count > 0 ? $"登録数 {records.Count} 枚　／　カードを押すと詳しく見られます（CPU対戦に出す2枚も選べます）" : "";
            return;
        }
        string Name(FirebaseCharacterRecord r) => r != null ? r.characterName : "<color=#8A84A0>未選択</color>";
        countText.text = $"<color=#FF7A7C>PLAYER 1</color> {Name(pickedPlayer1)}　VS　<color=#7AA8FF>PLAYER 2</color> {Name(pickedPlayer2)}";
    }

    private bool IsTournamentPick(FirebaseCharacterRecord record) => tournamentPicks.Exists(r => r.cardId == record.cardId);

    private void ToggleTournamentPick(FirebaseCharacterRecord record)
    {
        pickWarning = null;
        if (IsTournamentPick(record))
        {
            tournamentPicks.RemoveAll(r => r.cardId == record.cardId);
        }
        else if (tournamentPicks.Count >= tournamentSize)
        {
            pickWarning = $"<color=#FF9C7A>{tournamentSize}人のトーナメントには {tournamentSize}枚まで選べます</color>";
        }
        else
        {
            tournamentPicks.Add(record);
        }
        CloseDetail();
        ShowPage(page); // 出場の印を付け直す
        RefreshPicks();
    }

    private void CycleTournamentSize()
    {
        int index = Array.IndexOf(Tournament.Sizes, tournamentSize);
        tournamentSize = Tournament.Sizes[(index + 1) % Tournament.Sizes.Length];
        sizeButton.GetComponentInChildren<TextMeshProUGUI>().text = $"人数 {tournamentSize}人";
        pickWarning = null;
        RefreshPicks();
    }

    private void StartPickedTournament()
    {
        if (StartTournament == null || records.Count < 2) return;
        StartTournament(new List<FirebaseCharacterRecord>(tournamentPicks), new List<FirebaseCharacterRecord>(records), tournamentSize);
    }

    private void Pick(FirebaseCharacterRecord record, bool player1)
    {
        if (player1) pickedPlayer1 = record;
        else pickedPlayer2 = record;
        RefreshPicks();
        CloseDetail();
    }

    private void StartPickedMatch()
    {
        if (pickedPlayer1 == null || pickedPlayer2 == null || StartCpuMatch == null) return;
        StartCpuMatch(pickedPlayer1, pickedPlayer2);
    }

    // ==================== 一覧 ====================

    private int PageCount => Mathf.Max(1, Mathf.CeilToInt(records.Count / (float)PageSize));

    private void ShowPage(int index)
    {
        if (loading) return;
        page = Mathf.Clamp(index, 0, PageCount - 1);
        ClearPage();

        int start = page * PageSize;
        for (int i = start; i < Mathf.Min(start + PageSize, records.Count); i++)
        {
            int slot = i - start;
            Vector2 position = new Vector2(
                (slot % Columns) * (CardSize.x + CardGap.x),
                -(slot / Columns) * (CardSize.y + CardGap.y));
            BuildCard(records[i], position);
        }
        SetNavigation(records.Count > 0);
    }

    private void SetNavigation(bool visible)
    {
        bool multiPage = visible && PageCount > 1;
        prevButton.gameObject.SetActive(multiPage);
        nextButton.gameObject.SetActive(multiPage);
        pageText.gameObject.SetActive(multiPage);
        if (!multiPage) return;
        prevButton.interactable = page > 0;
        nextButton.interactable = page < PageCount - 1;
        pageText.text = $"{page + 1} / {PageCount}";
    }

    private void ClearPage()
    {
        if (grid != null)
        {
            foreach (Transform child in grid) Destroy(child.gameObject);
        }
        if (detailContent != null)
        {
            foreach (Transform child in detailContent) Destroy(child.gameObject);
        }
        foreach (Texture2D texture in pageTextures)
        {
            if (texture != null) Destroy(texture);
        }
        pageTextures.Clear();
    }

    private void BuildCard(FirebaseCharacterRecord record, Vector2 position)
    {
        CharacterStats stats = ToStats(record);
        Color elementColor = ElementAffinity.GetElementColor(stats.element);

        GameObject go = new GameObject(record.cardId, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        RectTransform card = (RectTransform)go.transform;
        card.SetParent(grid, false);
        OrisamoUI.Place(card, new Vector2(0f, 1f), position, CardSize);
        Image background = go.GetComponent<Image>();
        background.sprite = OrisamoUI.RoundedPanel;
        background.type = Image.Type.Sliced;
        background.pixelsPerUnitMultiplier = 0.5f;
        background.color = Color.Lerp(OrisamoUI.Night, elementColor, 0.12f);
        Button button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => ShowDetail(record));
        go.AddComponent<UIButtonMotion>();

        Image border = OrisamoUI.CreateImage("Border", card, OrisamoUI.RoundedFrame, stats.isMutation ? OrisamoUI.GoldLight : OrisamoUI.WithAlpha(OrisamoUI.Gold, 0.85f), true);
        OrisamoUI.Stretch(border.rectTransform);
        bool entered = tournamentMode && IsTournamentPick(record);

        // 写真(なければ属性の紋章)
        RectTransform photoArea = OrisamoUI.CreateRect("PhotoArea", card);
        OrisamoUI.Place(photoArea, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(560f, 440f));
        Image photoBack = OrisamoUI.CreateImage("PhotoBack", photoArea, OrisamoUI.RoundedFlat, Color.Lerp(OrisamoUI.Ink, elementColor, 0.25f), true);
        OrisamoUI.Stretch(photoBack.rectTransform);
        Image halo = OrisamoUI.CreateImage("Halo", photoArea, OrisamoUI.SoftGlow, OrisamoUI.WithAlpha(elementColor, 0.45f));
        OrisamoUI.Stretch(halo.rectTransform, 20f);
        AddPhotoOrEmblem(photoArea, stats, 260f);

        RectTransform emblem = OrisamoUI.CreateElementEmblem(card, stats.element, 110f);
        OrisamoUI.Place(emblem, new Vector2(0f, 1f), new Vector2(20f, -20f), emblem.sizeDelta);

        TextMeshProUGUI name = OrisamoUI.CreateText("Name", card, stats.characterName, 64f, OrisamoUI.Parchment, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -490f), new Vector2(570f, 90f));
        name.enableAutoSizing = true;
        name.fontSizeMin = 36f;
        name.fontSizeMax = 64f;
        OrisamoUI.ApplyOutline(name, OrisamoUI.Ink, 0.2f, new Color(0f, 0f, 0f, 0.5f));

        TextMeshProUGUI statsText = OrisamoUI.CreateText("Stats", card,
            $"HP <b>{stats.maxHp}</b>　ATK <b>{stats.attack}</b>\nDEF <b>{stats.defense}</b>　SPD <b>{stats.speed}</b>",
            40f, OrisamoUI.Muted);
        OrisamoUI.Place(statsText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(570f, 130f));

        if (entered)
        {
            // トーナメントに出場させるカードの印
            border.color = OrisamoUI.GoldLight;
            Image glowFrame = OrisamoUI.CreateImage("EnteredGlow", card, OrisamoUI.SoftGlow, OrisamoUI.WithAlpha(OrisamoUI.Gold, 0.35f));
            OrisamoUI.Stretch(glowFrame.rectTransform, -40f);
            glowFrame.transform.SetAsFirstSibling();
            Image pill = OrisamoUI.CreateImage("Entered", card, OrisamoUI.RoundedPanel, OrisamoUI.Gold, true);
            OrisamoUI.Place(pill.rectTransform, new Vector2(1f, 1f), new Vector2(-20f, -20f), new Vector2(200f, 80f));
            TextMeshProUGUI label = OrisamoUI.CreateText("Text", pill.rectTransform, "出場", 48f, new Color(0.25f, 0.1f, 0.02f), TextAlignmentOptions.Center, FontStyles.Bold);
            OrisamoUI.Stretch(label.rectTransform);
        }
    }

    // ==================== 詳細 ====================

    private void ShowDetail(FirebaseCharacterRecord record)
    {
        foreach (Transform child in detailContent) Destroy(child.gameObject);
        CharacterStats stats = ToStats(record);
        Color elementColor = ElementAffinity.GetElementColor(stats.element);

        RectTransform panel = OrisamoUI.CreateOrnatePanel("Panel", detailContent, OrisamoUI.PanelFill, stats.isMutation ? OrisamoUI.GoldLight : OrisamoUI.Gold);
        OrisamoUI.Stretch(panel);

        RectTransform photoArea = OrisamoUI.CreateRect("PhotoArea", panel);
        OrisamoUI.Place(photoArea, new Vector2(0f, 0.5f), new Vector2(90f, 100f), new Vector2(1100f, 1100f));
        Image photoBack = OrisamoUI.CreateImage("PhotoBack", photoArea, OrisamoUI.RoundedFlat, Color.Lerp(OrisamoUI.Ink, elementColor, 0.25f), true);
        OrisamoUI.Stretch(photoBack.rectTransform);
        Image halo = OrisamoUI.CreateImage("Halo", photoArea, OrisamoUI.SoftGlow, OrisamoUI.WithAlpha(elementColor, 0.5f));
        OrisamoUI.Stretch(halo.rectTransform, 40f);
        AddPhotoOrEmblem(photoArea, stats, 520f);

        TextMeshProUGUI printStatus = OrisamoUI.CreateText("PrintStatus", panel, "", 34f, OrisamoUI.Muted, TextAlignmentOptions.Left);
        OrisamoUI.Place(printStatus.rectTransform, new Vector2(0f, 0f), new Vector2(90f, 200f), new Vector2(1100f, 100f));
        printStatus.textWrappingMode = TextWrappingModes.Normal;
        printStatus.richText = false;
        CreateButton(panel, "カードを見る・印刷", new Vector2(0f, 0f), new Vector2(90f, 60f), new Vector2(1100f, 120f),
            () => OpenPrintableCard(record, printStatus));

        const float textX = 1300f;
        const float textWidth = 1180f;
        RectTransform emblem = OrisamoUI.CreateElementEmblem(panel, stats.element, 170f);
        OrisamoUI.Place(emblem, new Vector2(0f, 1f), new Vector2(textX, -110f), emblem.sizeDelta);

        TextMeshProUGUI name = OrisamoUI.CreateText("Name", panel, stats.characterName, 120f, OrisamoUI.Parchment, TextAlignmentOptions.Left, FontStyles.Bold);
        OrisamoUI.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(textX + 210f, -110f), new Vector2(textWidth - 210f - 380f, 170f));
        name.enableAutoSizing = true;
        name.fontSizeMin = 60f;
        name.fontSizeMax = 120f;
        OrisamoUI.ApplyOutline(name, OrisamoUI.Ink, 0.2f, new Color(0f, 0f, 0f, 0.5f));

        string mutation = stats.isMutation ? "　<color=#FFEDB3>突然変異</color>" : "";
        TextMeshProUGUI element = OrisamoUI.CreateText("Element", panel, $"{OrisamoUI.ElementKanji(stats.element)}属性{mutation}", 56f, elementColor, TextAlignmentOptions.Left, FontStyles.Bold);
        OrisamoUI.Place(element.rectTransform, new Vector2(0f, 1f), new Vector2(textX, -320f), new Vector2(textWidth, 80f));

        string special = OrisamoUI.LevelKanji(stats.specialLevel);
        string specialColor = ColorUtility.ToHtmlStringRGB(OrisamoUI.LevelColor(stats.specialLevel));
        TextMeshProUGUI statLines = OrisamoUI.CreateText("Stats", panel,
            $"HP　<b>{stats.maxHp}</b>\n" +
            $"<color=#{ColorUtility.ToHtmlStringRGB(OrisamoUI.AttackColor)}>攻撃</color>　<b>{stats.attack}</b>\n" +
            $"<color=#{ColorUtility.ToHtmlStringRGB(OrisamoUI.DefenseColor)}>防御</color>　<b>{stats.defense}</b>\n" +
            $"素早さ　<b>{stats.speed}</b>\n" +
            $"必殺技　<color=#{specialColor}><b>{special}</b></color>",
            64f, OrisamoUI.Parchment, TextAlignmentOptions.TopLeft);
        statLines.lineSpacing = 12f;
        OrisamoUI.Place(statLines.rectTransform, new Vector2(0f, 1f), new Vector2(textX, -430f), new Vector2(textWidth, 520f));

        RectTransform divider = OrisamoUI.CreateDivider(panel, textWidth, OrisamoUI.Gold);
        OrisamoUI.Place(divider, new Vector2(0f, 1f), new Vector2(textX, -985f), divider.sizeDelta);

        string skillName = !string.IsNullOrEmpty(record.skillName) ? record.skillName : stats.skill?.skillName ?? "";
        string skillDescription = !string.IsNullOrEmpty(record.skillDescription) ? record.skillDescription : stats.skill?.GetDescription() ?? "";
        TextMeshProUGUI skill = OrisamoUI.CreateText("Skill", panel,
            $"<color=#F8CF70><b>スキル「{skillName}」</b></color>\n<size=80%>{skillDescription}</size>",
            58f, OrisamoUI.Parchment, TextAlignmentOptions.TopLeft);
        skill.textWrappingMode = TextWrappingModes.Normal;
        OrisamoUI.Place(skill.rectTransform, new Vector2(0f, 1f), new Vector2(textX, -1050f), new Vector2(textWidth, 190f));

        TextMeshProUGUI meta = OrisamoUI.CreateText("Meta", panel, $"登録 {FormatTimestamp(record.timestamp)}　／　カードID {ShortId(record.cardId)}",
            40f, OrisamoUI.Muted, TextAlignmentOptions.Left);
        OrisamoUI.Place(meta.rectTransform, new Vector2(0f, 0f), new Vector2(textX, 190f), new Vector2(textWidth, 60f));

        TextMeshProUGUI recordText = OrisamoUI.CreateText("Record", panel, "戦績　読み込み中…", 48f, OrisamoUI.Parchment, TextAlignmentOptions.Left, FontStyles.Bold);
        OrisamoUI.Place(recordText.rectTransform, new Vector2(0f, 0f), new Vector2(textX, 250f), new Vector2(textWidth, 70f));
        StartCoroutine(CardRecords.Load(record.cardId, entry =>
        {
            if (recordText == null) return;
            recordText.text = entry == null ? "戦績　-"
                : entry.Played == 0 && entry.championships == 0 ? "戦績　まだ対戦していません"
                : $"戦績　<color=#F8CF70>{entry.SummaryWithTitles}</color>";
        }));

        if (tournamentMode)
        {
            bool isPicked = IsTournamentPick(record);
            Button toggle = CreateButton(panel, isPicked ? "出場から外す" : "トーナメントに出場させる",
                new Vector2(0f, 0f), new Vector2(textX, 60f), new Vector2(1160f, 120f), () => ToggleTournamentPick(record));
            if (!isPicked) toggle.GetComponent<UIProductButtonStyle>().SetPrimary(true);
        }
        else
        {
            CreateButton(panel, "PLAYER 1 に選ぶ", new Vector2(0f, 0f), new Vector2(textX, 60f), new Vector2(560f, 120f), () => Pick(record, true));
            CreateButton(panel, "PLAYER 2 に選ぶ", new Vector2(0f, 0f), new Vector2(textX + 600f, 60f), new Vector2(560f, 120f), () => Pick(record, false));
        }
        CreateButton(panel, "閉じる", new Vector2(1f, 1f), new Vector2(-70f, -60f), new Vector2(360f, 110f), CloseDetail);
        detailLayer.SetAsLastSibling();
        detailLayer.gameObject.SetActive(true);
    }

    private void CloseDetail()
    {
        detailLayer.gameObject.SetActive(false);
        foreach (Transform child in detailContent) Destroy(child.gameObject);
    }

    private static void OpenPrintableCard(FirebaseCharacterRecord record, TextMeshProUGUI status)
    {
        try
        {
            if (record == null || string.IsNullOrEmpty(record.cardId))
                throw new InvalidOperationException("カードIDがありません。一覧を再読み込みしてください。");
            // Ship the web layout with the player so printing does not depend on a website deployment.
            string folder = Path.Combine(Application.streamingAssetsPath, "CardPrinting");
            string element;
            switch (record.element)
            {
                case "Fire": element = "fire"; break;
                case "Wind": element = "wind"; break;
                case "Dark": element = "dark"; break;
                case "Water": element = "water"; break;
                case "Earth": element = "earth"; break;
                case "Light": element = "light"; break;
                default: throw new InvalidOperationException("カードの属性を確認できません。");
            }
            string art = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(Path.Combine(folder, "card-" + element + ".png")));
            // Escape HTML script delimiters in user-controlled names and skill descriptions.
            string json = JsonUtility.ToJson(record).Replace("<", "\\u003c").Replace(">", "\\u003e")
                .Replace("&", "\\u0026").Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");
            string html = File.ReadAllText(Path.Combine(folder, "template.html"), Encoding.UTF8)
                .Replace("__CARD_ART__", art).Replace("/*CARD_RECORD*/null", json);
            string output = Path.Combine(Application.persistentDataPath, "PrintedCards");
            Directory.CreateDirectory(output);
            string file = Path.Combine(output, "card_" + Guid.NewGuid().ToString("N") + ".html");
            File.WriteAllText(file, html, new UTF8Encoding(false));
            Application.OpenURL(new Uri(file).AbsoluteUri);
            status.text = "ブラウザでカードを開きました。印刷・PDF保存できます。";
        }
        catch (Exception exception)
        {
            Debug.LogWarning("CardGalleryView: 印刷プレビューを開けませんでした: " + exception.Message);
            status.text = "カードを開けませんでした: " + exception.Message;
        }
    }

    // ==================== 共通 ====================

    private CharacterStats ToStats(FirebaseCharacterRecord record)
    {
        CharacterStats stats = FirebaseCharacterMapper.ToCharacterStats(record, "CardGalleryView");
        if (stats.photoSprite != null) pageTextures.Add(stats.photoSprite.texture);
        return stats;
    }

    private static void AddPhotoOrEmblem(RectTransform area, CharacterStats stats, float emblemSize)
    {
        if (stats.photoSprite != null)
        {
            Image photo = OrisamoUI.CreateImage("Photo", area, stats.photoSprite, Color.white);
            OrisamoUI.Stretch(photo.rectTransform, 16f);
            photo.preserveAspect = true;
            return;
        }
        RectTransform emblem = OrisamoUI.CreateElementEmblem(area, stats.element, emblemSize);
        OrisamoUI.PlaceCenter(emblem, Vector2.zero, emblem.sizeDelta);
    }

    private static string FormatTimestamp(long timestamp)
    {
        if (timestamp <= 0) return "-";
        return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToLocalTime().ToString("yyyy/MM/dd HH:mm");
    }

    private static string ShortId(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return "-";
        return cardId.Length > 8 ? cardId.Substring(0, 8) : cardId;
    }
}
