using UnityEngine;
using UnityEngine.UI;
using TMPro; // TextMeshProを使用。通常のUI.Textを使う場合は using UnityEngine.UI; に変更してください

/// <summary>
/// QRコードスキャナー(QRCodeScanner)からの読み取り結果を受け取り、
/// キャラクターを実際に生成してUIと3Dオブジェクトで表示する。
///
/// 【重要な変更点】
/// QRコードの中身は、以前は完成済みのキャラクターステータス(CharacterStats)の
/// JSONそのものだったが、今回「カードID＋ステータス生成用シード値」(QRCardData)
/// だけを埋め込む形式に変更した。
/// そのため、QRコードを読み取った直後の時点ではまだキャラクターの中身（属性・
/// ステータス・スキル）は決まっておらず、名前だけをその場でユーザーに
/// 入力してもらい、名前が確定した瞬間に CharacterStats.AssignRandomStats(seed)
/// を呼んで初めてキャラクターの中身を確定させる。
///
/// 【セットアップ方法】
/// 1. シーンに空のGameObjectを作成し、QRCodeScanner.cs をアタッチ（カメラ制御・QR解析）
/// 2. 別の空のGameObjectを作成し、このスクリプトをアタッチ
/// 3. Inspectorの qrCodeScanner に、手順1で作成したQRCodeScannerをドラッグ
/// 4. 確定したキャラクターのステータス表示用の TextMeshProUGUI を Canvas 上に配置し
///    statusText にドラッグ
/// 5. （任意）読み取り待ち/エラーメッセージ表示用の TextMeshProUGUI を messageText にドラッグ
/// 6. 名前入力用に、TMP_InputField と 確定ボタン(Button) を含むパネルをCanvas上に作成し、
///    パネル全体を nameInputPanel に、入力欄を nameInputField に、ボタンを confirmNameButton に
///    ドラッグしてください（パネルは最初は非アクティブでOK。QR読み取り後に自動表示されます）
/// 7. （任意）3Dオブジェクトの表示位置を modelSpawnPoint に設定
///    （characterModelPrefab は未設定でOK。仮のカプセルが自動表示される）
/// 8. 読み取ったキャラクターをそのままバトルで使いたい場合は
///    saveScannedCharacterToGameManager をオンにする
///    （オンにすると、名前確定後にGameManagerのプレイヤーキャラクターとして上書き保存される）
/// 9. Canvas上に「もう一度登録する」ボタンを作成し、registerAgainButton にドラッグしてください
///    （最初は非アクティブでOK。1体登録が完了すると自動的に表示され、押すと表示をクリアして
///    次のQRコードを読み取れる状態に戻ります）
/// </summary>
public class QRCharacterStatusDisplay : MonoBehaviour
{
    [Tooltip("QRコードスキャナー（同一シーン内に配置したQRCodeScannerをドラッグ）")]
    [SerializeField] private QRCodeScanner qrCodeScanner;

    [Tooltip("確定したキャラクターのステータス表示先")]
    [SerializeField] private TextMeshProUGUI statusText;

    [Tooltip("読み取り待ち・エラー時のメッセージ表示先（任意）")]
    [SerializeField] private TextMeshProUGUI messageText;

    [Header("名前入力（QR読み取り後に表示）")]
    [Tooltip("名前入力欄一式を含むパネル。最初は非アクティブにしておいてください")]
    [SerializeField] private GameObject nameInputPanel;

    [Tooltip("キャラクターの名前を入力するフィールド")]
    [SerializeField] private TMP_InputField nameInputField;

    [Tooltip("名前を確定してキャラクターを生成するボタン")]
    [SerializeField] private Button confirmNameButton;

    [Tooltip("名前が未入力のまま確定された場合に使う名前")]
    [SerializeField] private string defaultCharacterName = "名無しの相棒";

    [Header("再登録（キャラクター確定後に表示）")]
    [Tooltip("表示中のキャラクターをクリアして、次のQRコードを読み取れる状態に戻すボタン。最初は非アクティブにしておいてください")]
    [SerializeField] private Button registerAgainButton;

    [Header("3Dモデル表示（任意）")]
    [SerializeField] private Transform modelSpawnPoint;
    [SerializeField] private GameObject characterModelPrefab;

    [Header("ゲーム連携（任意）")]
    [Tooltip("オンにすると、名前確定後のキャラクターをGameManagerにプレイヤーキャラクターとして保存する")]
    [SerializeField] private bool saveScannedCharacterToGameManager = false;

    private GameObject spawnedModelInstance;

    // 名前入力待ちのカード情報（まだCharacterStatsになっていない状態）
    private QRCardData pendingCardData;

    private void OnEnable()
    {
        if (qrCodeScanner != null)
        {
            qrCodeScanner.OnQRCodeScanned += HandleQRCodeScanned;
        }
        else
        {
            Debug.LogError("QRCodeScannerが設定されていません。Inspectorでドラッグしてください。");
        }

        if (confirmNameButton != null)
        {
            confirmNameButton.onClick.AddListener(ConfirmCharacterName);
        }

        if (registerAgainButton != null)
        {
            registerAgainButton.onClick.AddListener(ResetForNextRegistration);
        }
    }

    private void OnDisable()
    {
        if (qrCodeScanner != null)
        {
            qrCodeScanner.OnQRCodeScanned -= HandleQRCodeScanned;
        }

        if (confirmNameButton != null)
        {
            confirmNameButton.onClick.RemoveListener(ConfirmCharacterName);
        }

        if (registerAgainButton != null)
        {
            registerAgainButton.onClick.RemoveListener(ResetForNextRegistration);
        }
    }

    private void Start()
    {
        if (nameInputPanel != null)
        {
            nameInputPanel.SetActive(false);
        }
        if (registerAgainButton != null)
        {
            registerAgainButton.gameObject.SetActive(false);
        }
        SetMessage("QRコードをカメラにかざしてください");
    }

    /// <summary>
    /// QRコードの読み取りに成功した時に呼ばれる（QRCodeScannerのイベント経由）。
    /// この時点ではまだカードID・シード値しか分からず、キャラクターの中身は未確定。
    /// </summary>
    private void HandleQRCodeScanned(string json)
    {
        QRCardData cardData = QRCardData.FromJson(json);

        if (cardData == null)
        {
            SetMessage("読み取れましたが、カードの形式が正しくありません");
            return;
        }

        pendingCardData = cardData;

        // 名前入力待ちに移行する
        if (nameInputField != null)
        {
            nameInputField.text = "";
        }
        if (nameInputPanel != null)
        {
            nameInputPanel.SetActive(true);
        }

        SetMessage("読み取り成功！名前を入力してください");
    }

    /// <summary>
    /// 名前入力パネルの「確定」ボタンから呼ばれる。
    /// ここで初めて CharacterStats.AssignRandomStats(seed) を呼び出し、
    /// キャラクターの属性・ステータス・スキルを確定させる。
    /// </summary>
    private void ConfirmCharacterName()
    {
        if (pendingCardData == null)
        {
            return; // 読み取り前に押された等、想定外の呼び出しは無視
        }

        string enteredName = nameInputField != null ? nameInputField.text.Trim() : "";
        string finalName = string.IsNullOrEmpty(enteredName) ? defaultCharacterName : enteredName;

        CharacterStats stats = new CharacterStats(finalName);
        stats.AssignRandomStats(pendingCardData.seed);

        if (nameInputPanel != null)
        {
            nameInputPanel.SetActive(false);
        }

        DisplayStatus(stats);
        spawnedModelInstance = CharacterModelUtility.SpawnModel(stats, modelSpawnPoint, characterModelPrefab, spawnedModelInstance);
        SetMessage($"{stats.characterName} が誕生した！");

        if (saveScannedCharacterToGameManager && GameManager.Instance != null)
        {
            GameManager.Instance.SavePlayerCharacter(stats);
        }

        pendingCardData = null;

        // 登録完了。続けて別のカードを登録できるよう、ボタンを表示しておく
        if (registerAgainButton != null)
        {
            registerAgainButton.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// 「もう一度登録する」ボタンから呼ばれる。
    /// 表示中のキャラクター情報をクリアし、次のQRコードを読み取れる状態に戻す。
    /// </summary>
    private void ResetForNextRegistration()
    {
        if (statusText != null)
        {
            statusText.text = "";
        }

        if (spawnedModelInstance != null)
        {
            Destroy(spawnedModelInstance);
            spawnedModelInstance = null;
        }

        pendingCardData = null;

        if (nameInputPanel != null)
        {
            nameInputPanel.SetActive(false);
        }

        if (registerAgainButton != null)
        {
            registerAgainButton.gameObject.SetActive(false);
        }

        SetMessage("QRコードをカメラにかざしてください");
    }

    private void DisplayStatus(CharacterStats stats)
    {
        if (statusText == null) return;

        string mutationHeader = stats.isMutation ? "★突然変異カード★\n" : "";
        statusText.text =
            $"{mutationHeader}{stats.characterName}\n\n" +
            $"属性: {stats.element}\n" +
            $"攻撃力: {stats.attack}\n" +
            $"防御力: {stats.defense}\n" +
            $"素早さ: {stats.speed}\n" +
            $"HP: {stats.hp}/{stats.maxHp}\n\n" +
            $"スキル「{stats.skill.skillName}」\n{stats.skill.GetDescription()}";
    }

    private void SetMessage(string text)
    {
        if (messageText != null) messageText.text = text;
    }
}