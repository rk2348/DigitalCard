using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// バトル開始前に「プレイヤーが自分のカードをQRコードでスキャンして参戦する」フェーズを担当する。
///
/// 【全体の流れ】
/// 1. バトルシーン開始時、このコンポーネントがQRスキャン用パネルを表示する
///    (実際のカメラ映像・QR解析はQRCodeScannerが担当し、このスクリプトはその結果を受け取るだけ)
/// 2. プレイヤーが自分のカードのQRコード(cardId＋seedのJSON)をカメラにかざす
/// 3. 読み取ったcardIdをキーに、Firebase Realtime Databaseの
///    /characterByCard/{cardId} を検索する。
///    (スマホ側(app.js)がカード登録時に、そのカードの最新の確定ステータス＋
///     背景切り抜き済みの実物写真(PNGのdata URL)をここへ書き込んでいる想定)
/// 4. 見つかったデータからCharacterStatsを組み立て、写真もSpriteに変換したうえで
///    OnCharacterReadyイベントで通知する。
/// 5. まだ登録されていないカードだった場合はエラーメッセージを表示し、再スキャンを待つ。
///
/// ステータス・スキルはスマホ(JavaScript)側で既に確定させた値をそのまま使う。
/// C#のCharacterStats.AssignRandomStats(seed)で同じseedから再計算すると、
/// 乱数アルゴリズムの違い(JS:mulberry32 / C#:System.Random)により結果が
/// 一致しなくなるため、seedからの再計算はあえて行わない。
///
/// 【セットアップ方法】
/// 1. バトルシーンに空のGameObjectを作成し、このスクリプトをアタッチ
/// 2. 同じシーンにQRCodeScannerを設置し(previewImage等はQRCodeScanner側の設定に従う)、
///    qrScannerにドラッグ
/// 3. Canvas上にスキャン中だけ表示しておくパネル(intakePanel)を用意してドラッグ
///    (QRCodeScannerのプレビューなどをこのパネルの中に配置しておくとよい)
/// 4. statusText(任意)にスキャン状況を表示するTextMeshProUGUIをドラッグ
/// 5. databaseUrlにFirebaseのdatabaseURL(例: https://xxxx-default-rtdb.firebaseio.com)を設定
/// 6. BattleManager側でこのコンポーネントを参照し、OnCharacterReadyを購読して
///    受け取ったCharacterStatsをプレイヤーキャラクターとして戦闘を開始する
/// </summary>
public class BattleCardIntake : MonoBehaviour
{
    [Tooltip("FirebaseコンソールのdatabaseURL（例: https://digitalcard-b825d-default-rtdb.firebaseio.com）")]
    [SerializeField] private string databaseUrl = "";

    [Tooltip("QRコード読み取りを行うコンポーネント")]
    [SerializeField] private QRCodeScanner qrScanner;

    [Tooltip("スキャン待ち中に表示しておくパネル。読み込み完了時に自動でSetActive(false)する(任意)")]
    [SerializeField] private GameObject intakePanel;

    [Tooltip("状況メッセージの表示先(任意)")]
    [SerializeField] private TextMeshProUGUI statusText;

    [Tooltip("未登録カード等でエラーになった場合、次のスキャンを受け付けるまでのクールダウン秒数")]
    [SerializeField] private float retryCooldown = 1.5f;

    /// <summary>カードの読み込みが完了した時に発火。引数は確定したプレイヤーキャラクター(写真つき)。</summary>
    public event Action<CharacterStats> OnCharacterReady;

    private bool isProcessing = false;

    private void Awake()
    {
        if (qrScanner != null)
        {
            qrScanner.OnQRCodeScanned += HandleQrScanned;
        }
        else
        {
            Debug.LogError("BattleCardIntake: qrScannerが設定されていません。");
        }
    }

    private void OnDestroy()
    {
        if (qrScanner != null)
        {
            qrScanner.OnQRCodeScanned -= HandleQrScanned;
        }
    }

    private void HandleQrScanned(string decodedText)
    {
        if (isProcessing) return;

        QRCardData cardData = QRCardData.FromJson(decodedText);
        if (cardData == null)
        {
            SetStatus("カードの形式が正しくありません。もう一度読み取ってください");
            return;
        }

        StartCoroutine(FetchCharacterByCard(cardData.cardId));
    }

    private IEnumerator FetchCharacterByCard(string cardId)
    {
        isProcessing = true;
        SetStatus("カード情報を読み込んでいます...");

        string url = $"{databaseUrl.TrimEnd('/')}/characterByCard/{cardId}.json";

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("BattleCardIntake: カード情報の取得に失敗しました: " + req.error);
                SetStatus("通信に失敗しました。もう一度お試しください");
                yield return new WaitForSeconds(retryCooldown);
                isProcessing = false;
                yield break;
            }

            string json = req.downloadHandler.text;
            if (string.IsNullOrEmpty(json) || json == "null")
            {
                SetStatus("このカードはまだ登録されていません。スマホで撮影・登録してから読み取ってください");
                yield return new WaitForSeconds(retryCooldown);
                isProcessing = false;
                yield break;
            }

            FirebaseCharacterRecord record = null;
            bool parseFailed = false;
            try
            {
                record = JsonUtility.FromJson<FirebaseCharacterRecord>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("BattleCardIntake: カードデータのパースに失敗しました: " + e.Message);
                parseFailed = true;
            }

            if (parseFailed)
            {
                SetStatus("カードデータの読み込みに失敗しました");
                yield return new WaitForSeconds(retryCooldown);
                isProcessing = false;
                yield break;
            }

            if (record == null || string.IsNullOrEmpty(record.characterName))
            {
                SetStatus("カードデータが不正です。スマホ側で登録し直してください");
                yield return new WaitForSeconds(retryCooldown);
                isProcessing = false;
                yield break;
            }

            CharacterStats stats = BuildCharacterStats(record);

            SetStatus($"{stats.characterName} が参戦！");
            if (intakePanel != null) intakePanel.SetActive(false);

            OnCharacterReady?.Invoke(stats);
            // isProcessingはtrueのまま維持する(このフェーズは一度成功したら再スキャンを受け付けない)
        }
    }

    /// <summary>
    /// Firebaseから取得したレコードをCharacterStatsに変換する。
    /// </summary>
    private CharacterStats BuildCharacterStats(FirebaseCharacterRecord record)
    {
        CharacterStats stats = new CharacterStats(record.characterName)
        {
            attack = record.attack,
            defense = record.defense,
            speed = record.speed,
            hp = record.hp,
            maxHp = record.maxHp,
            isMutation = record.isMutation,
        };

        if (!string.IsNullOrEmpty(record.element) && Enum.TryParse(record.element, out ElementType parsedElement))
        {
            stats.element = parsedElement;
        }
        else
        {
            Debug.LogWarning($"BattleCardIntake: 未知の属性名です({record.element})。既定値を使用します。");
        }

        if (!string.IsNullOrEmpty(record.skillType) && Enum.TryParse(record.skillType, out SkillType parsedSkillType))
        {
            stats.skill = new CharacterSkill(parsedSkillType, record.ratio);
        }
        else
        {
            Debug.LogWarning($"BattleCardIntake: 未知のスキル種別です({record.skillType})。スキル無しとして扱います。");
        }

        stats.photoSprite = BuildSpriteFromDataUrl(record.photoDataUrl);

        return stats;
    }

    /// <summary>
    /// data URL形式("data:image/png;base64,....")の文字列をSpriteに変換する。
    /// 写真が無い/デコードに失敗した場合はnullを返す(呼び出し側でnullチェックすること。
    /// MonsterSpriteBuilder側もnullなら手続き生成にフォールバックするようになっている)。
    /// </summary>
    private Sprite BuildSpriteFromDataUrl(string dataUrl)
    {
        if (string.IsNullOrEmpty(dataUrl)) return null;

        int commaIndex = dataUrl.IndexOf(',');
        string base64Data = commaIndex >= 0 ? dataUrl.Substring(commaIndex + 1) : dataUrl;

        byte[] imageBytes;
        try
        {
            imageBytes = Convert.FromBase64String(base64Data);
        }
        catch (FormatException e)
        {
            Debug.LogWarning("BattleCardIntake: 写真データ(base64)のデコードに失敗しました: " + e.Message);
            return null;
        }

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(imageBytes))
        {
            Debug.LogWarning("BattleCardIntake: 写真データのTexture2Dへの変換に失敗しました。");
            return null;
        }

        return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
    }

    private void SetStatus(string text)
    {
        if (statusText != null) statusText.text = text;
        Debug.Log("[BattleCardIntake] " + text);
    }
}
