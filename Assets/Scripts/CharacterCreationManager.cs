using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro; // TextMeshProを使用。通常のUI.Textを使う場合は using UnityEngine.UI; に変更してください

/// <summary>
/// キャラクター作成シーンの制御。
/// スペースキーを押すとキャラクターが生成され、ステータスが自動で割り振られ、
/// GameManagerに保存される。同時に3Dオブジェクト（現在は仮のプリミティブ）と
/// QRコード（ZXing.Net導入時のみ）を表示する。その後バトルシーンへ遷移する。
///
/// 【セットアップ方法】
/// 1. キャラクター作成シーンに空のGameObjectを作成し、このスクリプトをアタッチ
/// 2. ステータス表示用のTextMeshProUGUIをCanvas上に配置し、statusText にドラッグ
/// 3. 3Dモデルを表示したい位置に空のGameObject（例："ModelSpawnPoint"）を配置し、
///    modelSpawnPoint にドラッグ（カメラが映せる位置に置いてください）
/// 4. characterModelPrefab は現時点では未設定でOK。
///    未設定の場合、属性ごとに色分けされた仮のカプセルが自動生成される。
///    後日、実際のキャラクターモデルのプレハブが用意できたら
///    ここにドラッグするだけで表示物が差し替わる。
/// 5. QRコードを表示したい場合は、Canvas上にRawImageを配置し qrCodeImage にドラッグ
///    （ZXing.Net未導入の場合はログに警告が出るだけで、他の機能は問題なく動作します）
/// 6. Build Settingsに "Battle" という名前のシーンを追加しておく
/// </summary>
public class CharacterCreationManager : MonoBehaviour
{
    [Tooltip("生成されるキャラクターの名前（必要であれば入力欄と連携させてください）")]
    [SerializeField] private string newCharacterName = "プレイヤー";

    [Tooltip("ステータス表示用のUIテキスト（任意）")]
    [SerializeField] private TextMeshProUGUI statusText;

    [Tooltip("バトルシーンへの遷移までの待機時間（秒）")]
    [SerializeField] private float delayBeforeSceneChange = 1.5f;

    [Tooltip("遷移先シーン名（Build Settingsに登録されているもの）")]
    [SerializeField] private string battleSceneName = "Battle";

    [Header("3Dモデル表示（後でキャラクターモデルに差し替え予定）")]
    [Tooltip("3Dオブジェクトを表示する位置・回転の基準にするTransform")]
    [SerializeField] private Transform modelSpawnPoint;

    [Tooltip("キャラクターモデルのプレハブ。未設定の場合は仮の3Dプリミティブ（カプセル）を表示する")]
    [SerializeField] private GameObject characterModelPrefab;

    [Header("QRコード表示（カード印刷を見据えた確認用）")]
    [Tooltip("生成したQRコードを表示するRawImage（任意。未設定ならQR生成自体をスキップ）")]
    [SerializeField] private RawImage qrCodeImage;

    [Tooltip("QRコードの画像サイズ（ピクセル）")]
    [SerializeField] private int qrCodeSize = 512;

    private bool isCharacterCreated = false;
    private GameObject spawnedModelInstance;

    private void Update()
    {
        // まだキャラクターが作成されていない場合のみスペースキーを受け付ける
        if (!isCharacterCreated && Input.GetKeyDown(KeyCode.Space))
        {
            CreateCharacter();
        }
    }

    private void CreateCharacter()
    {
        isCharacterCreated = true;

        // 1. キャラクターを生成し、ステータスをランダムに割り振る
        CharacterStats newCharacter = new CharacterStats(newCharacterName);
        newCharacter.AssignRandomStats();

        // 2. GameManagerに保存する（シーンをまたいでも保持される）
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SavePlayerCharacter(newCharacter);
        }
        else
        {
            Debug.LogError("GameManagerが見つかりません。タイトルシーンにGameManagerを配置してください。");
        }

        // 3. 画面にステータスを表示（UIがあれば）
        if (statusText != null)
        {
            string mutationHeader = newCharacter.isMutation ? "★突然変異が発生した！★\n" : "";
            statusText.text =
                $"{mutationHeader}{newCharacter.characterName} が誕生した！\n\n" +
                $"属性: {newCharacter.element}\n" +
                $"攻撃力: {newCharacter.attack}\n" +
                $"防御力: {newCharacter.defense}\n" +
                $"素早さ: {newCharacter.speed}\n\n" +
                $"スキル「{newCharacter.skill.skillName}」\n{newCharacter.skill.GetDescription()}";
        }

        // 4. 3Dオブジェクトを表示（現在は仮のプリミティブ、将来はキャラクターモデルに差し替え）
        SpawnCharacterModel(newCharacter);

        // 5. QRコードを生成して表示（カード印刷前の確認用。ZXing.Net導入時のみ実際に生成される）
        GenerateAndDisplayQRCode(newCharacter);

        // 6. 少し待ってからバトルシーンへ遷移
        Invoke(nameof(GoToBattleScene), delayBeforeSceneChange);
    }

    /// <summary>
    /// キャラクターの3Dオブジェクトを表示する（共通処理はCharacterModelUtilityに委譲）。
    /// </summary>
    private void SpawnCharacterModel(CharacterStats stats)
    {
        spawnedModelInstance = CharacterModelUtility.SpawnModel(stats, modelSpawnPoint, characterModelPrefab, spawnedModelInstance);
    }

    /// <summary>
    /// キャラクターのステータスをJSON化し、QRコード画像として表示する。
    /// この文字列がそのまま、将来カードに印刷するQRコードの中身になる想定。
    /// ZXing.Net未導入の場合はコンソールに警告が出るのみで、他の処理には影響しない。
    /// </summary>
    private void GenerateAndDisplayQRCode(CharacterStats stats)
    {
        if (qrCodeImage == null)
        {
            return; // QR表示を使わない構成
        }

        string json = stats.ToJson();
        Texture2D qrTexture = QRCodeGenerator.GenerateTexture(json, qrCodeSize);

        if (qrTexture != null)
        {
            qrCodeImage.texture = qrTexture;
            qrCodeImage.gameObject.SetActive(true);
        }
    }

    private void GoToBattleScene()
    {
        SceneManager.LoadScene(battleSceneName);
    }
}
