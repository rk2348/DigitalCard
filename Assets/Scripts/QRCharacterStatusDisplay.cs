using UnityEngine;
using TMPro; // TextMeshProを使用。通常のUI.Textを使う場合は using UnityEngine.UI; に変更してください

/// <summary>
/// QRコードスキャナー(QRCodeScanner)からの読み取り結果を受け取り、
/// キャラクターステータスをUIと3Dオブジェクトで表示する。
/// QRコード内のデータは CharacterStats.ToJson() で生成したJSON文字列を想定。
///
/// 【セットアップ方法】
/// 1. シーンに空のGameObjectを作成し、QRCodeScanner.cs をアタッチ（カメラ制御・QR解析）
/// 2. 別の空のGameObjectを作成し、このスクリプトをアタッチ
/// 3. Inspectorの qrCodeScanner に、手順1で作成したQRCodeScannerをドラッグ
/// 4. ステータス表示用の TextMeshProUGUI を Canvas 上に配置し statusText にドラッグ
/// 5. （任意）読み取り待ち/エラーメッセージ表示用の TextMeshProUGUI を messageText にドラッグ
/// 6. （任意）3Dオブジェクトの表示位置を modelSpawnPoint に設定
///    （characterModelPrefab は未設定でOK。仮のカプセルが自動表示される）
/// 7. 読み取ったキャラクターをそのままバトルで使いたい場合は
///    saveScannedCharacterToGameManager をオンにする
///    （オンにすると、GameManagerのプレイヤーキャラクターとして上書き保存される）
/// </summary>
public class QRCharacterStatusDisplay : MonoBehaviour
{
    [Tooltip("QRコードスキャナー（同一シーン内に配置したQRCodeScannerをドラッグ）")]
    [SerializeField] private QRCodeScanner qrCodeScanner;

    [Tooltip("読み取ったキャラクターのステータス表示先")]
    [SerializeField] private TextMeshProUGUI statusText;

    [Tooltip("読み取り待ち・エラー時のメッセージ表示先（任意）")]
    [SerializeField] private TextMeshProUGUI messageText;

    [Header("3Dモデル表示（任意）")]
    [SerializeField] private Transform modelSpawnPoint;
    [SerializeField] private GameObject characterModelPrefab;

    [Header("ゲーム連携（任意）")]
    [Tooltip("オンにすると、読み取ったキャラクターをGameManagerにプレイヤーキャラクターとして保存する")]
    [SerializeField] private bool saveScannedCharacterToGameManager = false;

    private GameObject spawnedModelInstance;

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
    }

    private void OnDisable()
    {
        if (qrCodeScanner != null)
        {
            qrCodeScanner.OnQRCodeScanned -= HandleQRCodeScanned;
        }
    }

    private void Start()
    {
        SetMessage("QRコードをカメラにかざしてください");
    }

    /// <summary>
    /// QRコードの読み取りに成功した時に呼ばれる（QRCodeScannerのイベント経由）。
    /// </summary>
    private void HandleQRCodeScanned(string json)
    {
        CharacterStats stats;
        try
        {
            stats = CharacterStats.FromJson(json);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("QRコードは読み取れましたが、キャラクターデータの解析に失敗しました: " + e.Message);
            SetMessage("読み取れましたが、カードの形式が正しくありません");
            return;
        }

        // JSON自体は解析できても中身が空、といったケースを弾く
        if (stats == null || string.IsNullOrEmpty(stats.characterName))
        {
            SetMessage("読み取れましたが、カードの形式が正しくありません");
            return;
        }

        DisplayStatus(stats);
        spawnedModelInstance = CharacterModelUtility.SpawnModel(stats, modelSpawnPoint, characterModelPrefab, spawnedModelInstance);
        SetMessage("読み取り成功！");

        if (saveScannedCharacterToGameManager && GameManager.Instance != null)
        {
            GameManager.Instance.SavePlayerCharacter(stats);
        }
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
