using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// QRコード作成シーンの制御（開発者用／カード印刷用）。
///
/// QRコードに埋め込むのはカードID+シード値(QRCardData)だけで、キャラクターの中身は
/// スマホで読み取った瞬間に決まる(docs/app.js)。そのため、ここでは何枚でも量産できる。
///
/// 画面はすべてコードで組み立てる(シーンにCanvasがなければ作る。シーンに置かれた旧UIは隠す)。
///   左: 選択中のカードのQRコード(大きく表示)とID・シード値
///   中: 操作ボタン(キーボードのショートカット付き)
///       1枚生成[Space] / N枚まとめて生成[B] / PNG保存[S] / 印刷シート書き出し[P] / JSONコピー[C] / 保存フォルダを開く[O]
///   右: このシーンで作ったカードの一覧(押すと左に表示) [↑↓で選択]
///
/// 保存先はプロジェクト(ビルドでは実行ファイル)と同じ階層の QRCards フォルダ。
/// 作ったカードは生成した時点で QRCards/cards.csv に1行ずつ追記するので、保存し忘れても記録は残る。
/// 印刷シートは A4・300dpi の PNG に6面(2列x3段)で並べ、切り取り線を入れる。
///
/// 【セットアップ方法】
/// このシーンに空のGameObjectを作成し、このスクリプトをアタッチするだけでよい。
/// </summary>
public class CharacterCreationManager : MonoBehaviour
{
    [Tooltip("「タイトルへ戻る」の遷移先シーン名")]
    [SerializeField] private string titleSceneName = "Title";

    [Tooltip("画面に表示するQRコードの画像サイズ（ピクセル）")]
    [SerializeField] private int previewSize = 1024;

    [Tooltip("印刷シートに並べるQRコード1つの大きさ（ピクセル、300dpi基準。900pxで約7.6cm）")]
    [SerializeField] private int printQrSize = 900;

    private const int MaxBatchCount = 60;
    private const int HistoryRowsPerPage = 12;
    private const int SheetWidth = 2480;   // A4 縦 300dpi
    private const int SheetHeight = 3508;
    private const int SheetColumns = 2;
    private const int SheetRows = 3;

    private static readonly Color PanelColor = new Color(0.09f, 0.075f, 0.16f, 0.96f);
    private static readonly Color RowColor = new Color(0.14f, 0.11f, 0.24f, 1f);
    private static readonly Color RowSelectedColor = new Color(0.62f, 0.47f, 0.18f, 1f);

    private sealed class CardEntry
    {
        public QRCardData data;
        public DateTime createdAt;
        public bool saved;
    }

    private readonly List<CardEntry> cards = new List<CardEntry>();
    private int selected = -1;
    private int batchCount = 6;
    private int historyPage;
    private Texture2D previewTexture;

    private RawImage previewImage;
    private TextMeshProUGUI previewPlaceholder;
    private TextMeshProUGUI idText;
    private TextMeshProUGUI seedText;
    private TextMeshProUGUI jsonText;
    private TextMeshProUGUI batchCountText;
    private TextMeshProUGUI batchButtonLabel;
    private TextMeshProUGUI historyTitle;
    private TextMeshProUGUI historyPageText;
    private TextMeshProUGUI statusText;
    private RectTransform historyList;
    private readonly List<Button> selectionButtons = new List<Button>();

    private static string OutputFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "QRCards"));

    private void Start()
    {
        Canvas canvas = OrisamoUI.EnsureSceneCanvas();
        UIAnimationDirector.ConfigureScaler(canvas);
        foreach (Transform child in canvas.transform) child.gameObject.SetActive(false); // シーンに置かれた旧UI

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = OrisamoUI.Ink;
        }

        Build(canvas.transform);
        RefreshAll();

        if (QRCodeGenerator.GenerateTexture("test", 32) is Texture2D probe) Destroy(probe);
        else SetStatus("ZXing.Net が導入されていないため、QRコードを生成できません（ORISAMO_ZXING を確認してください）", true);
    }

    private void OnDestroy()
    {
        if (previewTexture != null) Destroy(previewTexture);
    }

    private void Update()
    {
        // ボタンを押した後にフォーカスが残っていると、Spaceキーでそのボタンまで押されてしまうため外す
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null && eventSystem.currentSelectedGameObject != null) eventSystem.SetSelectedGameObject(null);

        bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (Input.GetKeyDown(KeyCode.Space)) CreateCards(1);
        else if (Input.GetKeyDown(KeyCode.B)) CreateCards(batchCount);
        else if (Input.GetKeyDown(KeyCode.S)) SaveSelectedPng();
        else if (Input.GetKeyDown(KeyCode.P)) ExportPrintSheets();
        else if (Input.GetKeyDown(KeyCode.C)) CopySelectedJson();
        else if (Input.GetKeyDown(KeyCode.O) && !control) OpenOutputFolder();
        else if (Input.GetKeyDown(KeyCode.UpArrow)) Select(selected - 1);
        else if (Input.GetKeyDown(KeyCode.DownArrow)) Select(selected + 1);
        else if (Input.GetKeyDown(KeyCode.LeftArrow)) ChangeBatchCount(-1);
        else if (Input.GetKeyDown(KeyCode.RightArrow)) ChangeBatchCount(1);
    }

    // ==================== 画面の組み立て ====================

    private void Build(Transform canvas)
    {
        RectTransform root = OrisamoUI.Stretch(OrisamoUI.CreateRect("QRCreationView", canvas));

        TextMeshProUGUI title = OrisamoUI.CreateText("Title", root, "QRコード作成", 84f, OrisamoUI.Gold, TextAlignmentOptions.Left, FontStyles.Bold);
        OrisamoUI.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(80f, -50f), new Vector2(1400f, 110f));
        TextMeshProUGUI subtitle = OrisamoUI.CreateText("Subtitle", root,
            "カード印刷用（開発者向け）　QRにはカードIDとシード値だけが入り、キャラクターはスマホで読み取った時に決まります", 40f, OrisamoUI.Muted, TextAlignmentOptions.Left);
        OrisamoUI.Place(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(80f, -160f), new Vector2(2800f, 60f));

        Button back = CreateButton(root, "タイトルへ戻る  [Esc]", 48f, () => SceneManager.LoadScene(titleSceneName));
        OrisamoUI.Place((RectTransform)back.transform, new Vector2(1f, 1f), new Vector2(-80f, -50f), new Vector2(680f, 120f));

        BuildPreviewPanel(root);
        BuildActionPanel(root);
        BuildHistoryPanel(root);

        statusText = OrisamoUI.CreateText("Status", root, "", 46f, OrisamoUI.Parchment, TextAlignmentOptions.Left);
        OrisamoUI.Place(statusText.rectTransform, new Vector2(0f, 0f), new Vector2(80f, 40f), new Vector2(3680f, 70f));
        statusText.overflowMode = TextOverflowModes.Ellipsis;
    }

    private RectTransform CreatePanel(Transform parent, string name, Vector2 position, Vector2 size, string heading)
    {
        Image panel = OrisamoUI.CreateImage(name, parent, OrisamoUI.RoundedFlat, PanelColor, true);
        RectTransform rect = OrisamoUI.Place(panel.rectTransform, new Vector2(0f, 1f), position, size);
        Image frame = OrisamoUI.CreateImage("Frame", rect, OrisamoUI.RoundedFrame, OrisamoUI.WithAlpha(OrisamoUI.Gold, 0.35f), true);
        OrisamoUI.Stretch(frame.rectTransform);
        if (!string.IsNullOrEmpty(heading))
        {
            TextMeshProUGUI label = OrisamoUI.CreateText("Heading", rect, heading, 50f, OrisamoUI.Gold, TextAlignmentOptions.Left, FontStyles.Bold);
            OrisamoUI.Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(50f, -36f), new Vector2(size.x - 100f, 70f));
        }
        return rect;
    }

    private void BuildPreviewPanel(Transform root)
    {
        RectTransform panel = CreatePanel(root, "PreviewPanel", new Vector2(80f, -250f), new Vector2(1500f, 1770f), "選択中のカード");

        Image paper = OrisamoUI.CreateImage("Paper", panel, OrisamoUI.RoundedFlat, Color.white, true);
        OrisamoUI.Place(paper.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(1220f, 1220f));
        GameObject qr = new GameObject("QR", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        qr.transform.SetParent(paper.transform, false);
        previewImage = qr.GetComponent<RawImage>();
        previewImage.raycastTarget = false;
        OrisamoUI.Stretch(previewImage.rectTransform, 30f);
        previewPlaceholder = OrisamoUI.CreateText("Placeholder", paper.transform, "Space キーか「1枚生成」で\nQRコードを作成します", 60f, new Color(0.35f, 0.33f, 0.42f, 1f));
        OrisamoUI.Stretch(previewPlaceholder.rectTransform);

        idText = OrisamoUI.CreateText("Id", panel, "", 46f, OrisamoUI.Parchment, TextAlignmentOptions.Left);
        OrisamoUI.Place(idText.rectTransform, new Vector2(0f, 1f), new Vector2(140f, -1395f), new Vector2(1300f, 64f));
        seedText = OrisamoUI.CreateText("Seed", panel, "", 46f, OrisamoUI.Parchment, TextAlignmentOptions.Left);
        OrisamoUI.Place(seedText.rectTransform, new Vector2(0f, 1f), new Vector2(140f, -1470f), new Vector2(1300f, 64f));
        jsonText = OrisamoUI.CreateText("Json", panel, "", 32f, OrisamoUI.Muted, TextAlignmentOptions.TopLeft);
        jsonText.textWrappingMode = TextWrappingModes.Normal;
        OrisamoUI.Place(jsonText.rectTransform, new Vector2(0f, 1f), new Vector2(140f, -1560f), new Vector2(1240f, 160f));
    }

    private void BuildActionPanel(Transform root)
    {
        RectTransform panel = CreatePanel(root, "ActionPanel", new Vector2(1640f, -250f), new Vector2(1000f, 1770f), "操作");
        const float width = 880f;
        const float height = 130f;
        float y = -150f;

        AddAction(panel, "1枚生成  [Space]", ref y, () => CreateCards(1), true);

        y -= 20f;
        TextMeshProUGUI countLabel = OrisamoUI.CreateText("BatchLabel", panel, "まとめて生成する枚数  [← →]", 40f, OrisamoUI.Muted, TextAlignmentOptions.Left);
        OrisamoUI.Place(countLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(width, 56f));
        y -= 70f;
        Button minus = CreateButton(panel, "－", 70f, () => ChangeBatchCount(-1));
        OrisamoUI.Place((RectTransform)minus.transform, new Vector2(0.5f, 1f), new Vector2(-width / 2f + 75f, y), new Vector2(150f, height));
        Button plus = CreateButton(panel, "+", 70f, () => ChangeBatchCount(1));
        OrisamoUI.Place((RectTransform)plus.transform, new Vector2(0.5f, 1f), new Vector2(width / 2f - 75f, y), new Vector2(150f, height));
        batchCountText = OrisamoUI.CreateText("BatchCount", panel, "", 72f, OrisamoUI.Parchment, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Place(batchCountText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(500f, height));
        y -= height + 24f;
        Button batch = AddAction(panel, "", ref y, () => CreateCards(batchCount), false);
        batchButtonLabel = batch.GetComponentInChildren<TextMeshProUGUI>();

        y -= 50f;
        AddAction(panel, "選択中のQRをPNG保存  [S]", ref y, SaveSelectedPng, false);
        AddAction(panel, "印刷シート書き出し（A4・6面）  [P]", ref y, ExportPrintSheets, false);
        AddAction(panel, "JSONをコピー  [C]", ref y, CopySelectedJson, false);
        AddAction(panel, "保存フォルダを開く  [O]", ref y, OpenOutputFolder, false);

        TextMeshProUGUI folder = OrisamoUI.CreateText("Folder", panel, $"保存先: {OutputFolder}", 32f, OrisamoUI.Muted, TextAlignmentOptions.TopLeft);
        folder.textWrappingMode = TextWrappingModes.Normal;
        OrisamoUI.Place(folder.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(width, 150f));
    }

    private Button AddAction(Transform panel, string text, ref float y, UnityEngine.Events.UnityAction onClick, bool primary)
    {
        Button button = CreateButton(panel, text, 48f, onClick);
        OrisamoUI.Place((RectTransform)button.transform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(880f, 130f));
        if (primary) button.GetComponent<UIProductButtonStyle>().SetPrimary(true);
        y -= 154f;
        return button;
    }

    private void BuildHistoryPanel(Transform root)
    {
        RectTransform panel = CreatePanel(root, "HistoryPanel", new Vector2(2700f, -250f), new Vector2(1060f, 1770f), null);
        historyTitle = OrisamoUI.CreateText("Heading", panel, "", 50f, OrisamoUI.Gold, TextAlignmentOptions.Left, FontStyles.Bold);
        OrisamoUI.Place(historyTitle.rectTransform, new Vector2(0f, 1f), new Vector2(50f, -36f), new Vector2(960f, 70f));
        TextMeshProUGUI hint = OrisamoUI.CreateText("Hint", panel, "押すと左に表示  [↑ ↓]　／　● は未保存", 34f, OrisamoUI.Muted, TextAlignmentOptions.Left);
        OrisamoUI.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(50f, -108f), new Vector2(960f, 50f));

        historyList = OrisamoUI.CreateRect("Rows", panel);
        OrisamoUI.Place(historyList, new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(960f, HistoryRowsPerPage * 110f));

        Button prev = CreateButton(panel, "前へ", 44f, () => { historyPage--; RefreshHistory(); });
        OrisamoUI.Place((RectTransform)prev.transform, new Vector2(0f, 0f), new Vector2(50f, 40f), new Vector2(260f, 110f));
        Button next = CreateButton(panel, "次へ", 44f, () => { historyPage++; RefreshHistory(); });
        OrisamoUI.Place((RectTransform)next.transform, new Vector2(1f, 0f), new Vector2(-50f, 40f), new Vector2(260f, 110f));
        historyPageText = OrisamoUI.CreateText("Page", panel, "", 44f, OrisamoUI.Parchment);
        OrisamoUI.Place(historyPageText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 55f), new Vector2(400f, 80f));
    }

    private static Button CreateButton(Transform parent, string text, float fontSize, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(text, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI label = OrisamoUI.CreateText("Label", go.transform, text, fontSize, OrisamoUI.Parchment, TextAlignmentOptions.Center, FontStyles.Bold);
        OrisamoUI.Stretch(label.rectTransform);
        Button button = go.GetComponent<Button>();
        button.onClick.AddListener(onClick);
        go.AddComponent<UIProductButtonStyle>();
        go.AddComponent<UIButtonMotion>();
        return button;
    }

    // ==================== 操作 ====================

    /// <summary>
    /// 新しいカード（QRコード）を count 枚作成する。
    /// キャラクターの中身（名前・ステータス・属性・スキル）はここでは一切決めない。
    /// 決めるのはあくまで「カードID」と「ステータス生成用のシード値」だけ。
    /// </summary>
    private void CreateCards(int count)
    {
        List<string> csvLines = new List<string>();
        for (int i = 0; i < count; i++)
        {
            // カードを一意に識別するID（印刷管理用。ゲームロジックには使わない）
            string cardId = Guid.NewGuid().ToString("N");
            // ステータス生成用のシード値。スマホで読み取った瞬間、この値から中身が決まる。
            int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            CardEntry entry = new CardEntry { data = new QRCardData(cardId, seed), createdAt = DateTime.Now };
            cards.Insert(0, entry);
            csvLines.Add($"{entry.createdAt:yyyy-MM-dd HH:mm:ss},{cardId},{seed}");
        }
        AppendCsv(csvLines);
        OrisamoAudio.Play(OrisamoAudio.Sfx.CardCreated);

        historyPage = 0;
        selected = -1;
        Select(0);
        SetStatus(count == 1 ? "カードを1枚生成しました" : $"カードを{count}枚生成しました（印刷シートは [P] で書き出せます）");
    }

    private void ChangeBatchCount(int delta)
    {
        batchCount = Mathf.Clamp(batchCount + delta, 1, MaxBatchCount);
        RefreshBatch();
    }

    private void Select(int index)
    {
        if (cards.Count == 0) return;
        index = Mathf.Clamp(index, 0, cards.Count - 1);
        if (index == selected) return;
        selected = index;
        historyPage = selected / HistoryRowsPerPage;

        if (previewTexture != null) Destroy(previewTexture);
        previewTexture = QRCodeGenerator.GenerateTexture(cards[selected].data.ToJson(), previewSize);
        if (previewTexture != null) previewTexture.filterMode = FilterMode.Point; // QRのセルをくっきり表示する
        RefreshAll();
    }

    private void SaveSelectedPng()
    {
        if (!HasSelection()) return;
        CardEntry entry = cards[selected];
        Texture2D texture = QRCodeGenerator.GenerateTexture(entry.data.ToJson(), printQrSize);
        if (texture == null)
        {
            SetStatus("QRコードを生成できませんでした（ZXing.Net を確認してください）", true);
            return;
        }

        string path = Path.Combine(OutputFolder, $"card_{entry.createdAt:yyyyMMdd_HHmmss}_{ShortId(entry.data.cardId)}.png");
        if (TryWrite(path, texture.EncodeToPNG()))
        {
            entry.saved = true;
            SetStatus($"保存しました: {path}");
            RefreshHistory();
        }
        Destroy(texture);
    }

    /// <summary>このシーンで作った全カードを、作った順に A4・6面の印刷シート(PNG)へ書き出す。</summary>
    private void ExportPrintSheets()
    {
        if (cards.Count == 0)
        {
            SetStatus("先にカードを生成してください", true);
            return;
        }

        List<CardEntry> ordered = new List<CardEntry>(cards);
        ordered.Reverse();
        int perSheet = SheetColumns * SheetRows;
        int sheetCount = Mathf.CeilToInt(ordered.Count / (float)perSheet);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string firstPath = null;

        for (int sheet = 0; sheet < sheetCount; sheet++)
        {
            Texture2D page = BuildSheet(ordered.GetRange(sheet * perSheet, Mathf.Min(perSheet, ordered.Count - sheet * perSheet)));
            if (page == null) return;
            string path = Path.Combine(OutputFolder, $"sheet_{stamp}_{sheet + 1:00}.png");
            bool ok = TryWrite(path, page.EncodeToPNG());
            Destroy(page);
            if (!ok) return;
            if (firstPath == null) firstPath = path;
        }

        foreach (CardEntry entry in ordered) entry.saved = true;
        RefreshHistory();
        SetStatus($"印刷シートを{sheetCount}枚書き出しました（{ordered.Count}枚分・A4 300dpi）: {Path.GetDirectoryName(firstPath)}");
    }

    private Texture2D BuildSheet(List<CardEntry> entries)
    {
        Texture2D page = new Texture2D(SheetWidth, SheetHeight, TextureFormat.RGB24, false);
        Color32[] pixels = new Color32[SheetWidth * SheetHeight];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);

        int cellWidth = SheetWidth / SheetColumns;
        int cellHeight = SheetHeight / SheetRows;
        Color32 cutLine = new Color32(200, 200, 200, 255);
        for (int c = 1; c < SheetColumns; c++) DrawRect(pixels, c * cellWidth - 1, 0, 2, SheetHeight, cutLine);
        for (int r = 1; r < SheetRows; r++) DrawRect(pixels, 0, r * cellHeight - 1, SheetWidth, 2, cutLine);

        for (int i = 0; i < entries.Count; i++)
        {
            Texture2D qr = QRCodeGenerator.GenerateTexture(entries[i].data.ToJson(), printQrSize);
            if (qr == null)
            {
                Destroy(page);
                SetStatus("QRコードを生成できませんでした（ZXing.Net を確認してください）", true);
                return null;
            }
            // 上の段から順に並べる(テクスチャのyは下から数える)
            int column = i % SheetColumns;
            int row = SheetRows - 1 - i / SheetColumns;
            int size = Mathf.Min(qr.width, Mathf.Min(cellWidth, cellHeight));
            int x0 = column * cellWidth + (cellWidth - size) / 2;
            int y0 = row * cellHeight + (cellHeight - size) / 2;
            Color32[] qrPixels = qr.GetPixels32();
            for (int y = 0; y < size; y++)
            {
                Array.Copy(qrPixels, y * qr.width, pixels, (y0 + y) * SheetWidth + x0, size);
            }
            Destroy(qr);
        }

        page.SetPixels32(pixels);
        page.Apply();
        return page;
    }

    private static void DrawRect(Color32[] pixels, int x, int y, int width, int height, Color32 color)
    {
        for (int py = Mathf.Max(0, y); py < Mathf.Min(SheetHeight, y + height); py++)
        {
            for (int px = Mathf.Max(0, x); px < Mathf.Min(SheetWidth, x + width); px++) pixels[py * SheetWidth + px] = color;
        }
    }

    private void CopySelectedJson()
    {
        if (!HasSelection()) return;
        GUIUtility.systemCopyBuffer = cards[selected].data.ToJson();
        SetStatus("QRコードの中身(JSON)をクリップボードにコピーしました");
    }

    private void OpenOutputFolder()
    {
        Directory.CreateDirectory(OutputFolder);
        Application.OpenURL(new Uri(OutputFolder).AbsoluteUri);
    }

    private bool HasSelection()
    {
        if (selected >= 0 && selected < cards.Count) return true;
        SetStatus("先にカードを生成してください", true);
        return false;
    }

    private bool TryWrite(string path, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, bytes);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"CharacterCreationManager: {path} に保存できませんでした: {exception}");
            SetStatus($"保存できませんでした: {exception.Message}", true);
            return false;
        }
    }

    /// <summary>作ったカードを QRCards/cards.csv に追記する(画像を保存し忘れても、IDとシード値の記録が残るように)。</summary>
    private void AppendCsv(List<string> lines)
    {
        string path = Path.Combine(OutputFolder, "cards.csv");
        try
        {
            Directory.CreateDirectory(OutputFolder);
            if (!File.Exists(path)) File.WriteAllText(path, "created_at,card_id,seed\n");
            File.AppendAllLines(path, lines);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"CharacterCreationManager: cards.csv に記録できませんでした: {exception.Message}");
        }
    }

    // ==================== 表示の更新 ====================

    private void RefreshAll()
    {
        bool hasCard = selected >= 0 && selected < cards.Count;
        previewImage.texture = previewTexture;
        previewImage.gameObject.SetActive(hasCard && previewTexture != null);
        previewPlaceholder.gameObject.SetActive(!hasCard || previewTexture == null);
        if (hasCard && previewTexture == null) previewPlaceholder.text = "QRコードを生成できません\n（ZXing.Net 未導入）";

        QRCardData data = hasCard ? cards[selected].data : null;
        idText.text = data != null ? $"<color=#F8CF70>カードID</color>　{data.cardId}" : "<color=#F8CF70>カードID</color>　-";
        seedText.text = data != null ? $"<color=#F8CF70>シード値</color>　{data.seed}" : "<color=#F8CF70>シード値</color>　-";
        jsonText.text = data != null ? data.ToJson() : "";

        RefreshBatch();
        RefreshHistory();
    }

    private void RefreshBatch()
    {
        batchCountText.text = $"{batchCount} 枚";
        batchButtonLabel.text = $"{batchCount}枚まとめて生成  [B]";
    }

    private void RefreshHistory()
    {
        int pageCount = Mathf.Max(1, Mathf.CeilToInt(cards.Count / (float)HistoryRowsPerPage));
        historyPage = Mathf.Clamp(historyPage, 0, pageCount - 1);
        int unsaved = cards.FindAll(c => !c.saved).Count;
        historyTitle.text = $"今回作成したカード　{cards.Count}枚" + (unsaved > 0 ? $"<size=75%><color=#FF9C7A>（未保存 {unsaved}）</color></size>" : "");
        historyPageText.text = $"{historyPage + 1} / {pageCount}";

        foreach (Button button in selectionButtons) Destroy(button.gameObject);
        selectionButtons.Clear();

        int start = historyPage * HistoryRowsPerPage;
        for (int i = start; i < Mathf.Min(start + HistoryRowsPerPage, cards.Count); i++)
        {
            int index = i;
            CardEntry entry = cards[i];
            GameObject go = new GameObject("Row", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(historyList, false);
            OrisamoUI.Place((RectTransform)go.transform, new Vector2(0.5f, 1f), new Vector2(0f, -(i - start) * 110f), new Vector2(960f, 96f));
            Image background = go.GetComponent<Image>();
            background.sprite = OrisamoUI.RoundedFlat;
            background.type = Image.Type.Sliced;
            background.pixelsPerUnitMultiplier = 0.5f;
            background.color = index == selected ? RowSelectedColor : RowColor;
            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(() => Select(index));

            string mark = entry.saved ? "　" : "<color=#FF9C7A>●</color>";
            TextMeshProUGUI label = OrisamoUI.CreateText("Label", go.transform,
                $"{mark} <color=#B8B0D0>{cards.Count - index:000}</color>　{ShortId(entry.data.cardId)}　<size=80%><color=#B8B0D0>{entry.createdAt:HH:mm:ss}</color></size>",
                44f, OrisamoUI.Parchment, TextAlignmentOptions.Left);
            OrisamoUI.Stretch(label.rectTransform, 0f);
            label.margin = new Vector4(30f, 0f, 30f, 0f);
            selectionButtons.Add(button);
        }
    }

    private void SetStatus(string message, bool warning = false)
    {
        if (statusText == null) return;
        statusText.text = message;
        statusText.color = warning ? new Color(1f, 0.6f, 0.48f, 1f) : OrisamoUI.Parchment;
    }

    private static string ShortId(string cardId) => cardId.Length > 8 ? cardId.Substring(0, 8) : cardId;
}
