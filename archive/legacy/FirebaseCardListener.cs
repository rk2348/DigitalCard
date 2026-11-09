using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.Networking;

/// <summary>
/// 物理カードのQRコードを再スキャンした瞬間、そのカード(cardId)に紐づく
/// 確定済みキャラクターをFirebase Realtime Databaseから取得し、
/// プレイヤーの所持キャラクター(GameManager)として呼び出すコンポーネント。
///
/// 【全体の流れ】
/// 1. スマホ(docs/のWebページ、app.js)がQRを読み取り、名前を入力して
///    ステータスをその場で確定させ、/characters/{cardId} に保存する
///    （cardIdをキーにしているので、同じ物理カードは何度登録しても
///    常に同じ場所が上書きされるだけになる）
/// 2. プレイヤーが対戦前にUnity(QRScanシーン)で同じ物理カードのQRを
///    QRCodeScannerでもう一度スキャンする
/// 3. このスクリプトがQRCodeScanner.OnQRCodeScannedを受けて、
///    デコードされたcardIdをもとに /characters/{cardId} をGETする
/// 4. 取得できたステータスからCharacterStatsを再構築し、
///    GameManagerの所持キャラクターコレクションに追加する
///    （seedから再計算はしない。スマホ側で確定済みの値をそのまま使う）
///
/// Firebase Unity SDKは導入せず、UnityWebRequestでREST API
/// （https://＜databaseURL＞/パス.json）を直接叩くことで依存を減らしている。
///
/// 【セットアップ方法】
/// 1. QRScanシーンに空のGameObjectを作成し、このスクリプトをアタッチ
/// 2. Inspectorの databaseUrl に、FirebaseコンソールのdatabaseURL
///    （例: https://digitalcard-b825d-default-rtdb.firebaseio.com）を入力
/// 3. Inspectorの Qr Code Scanner に、同じシーンのQRCodeScanner(QRScanner)をドラッグ
/// 4. Status Text（任意）に検索結果・エラーメッセージ表示用のTextMeshProUGUIをドラッグ
///
/// 【重要：セキュリティルールについて】
/// このスクリプトはFirebase認証を行わず、REST APIへ直接アクセスする。
/// Realtime Databaseのルールが誰でも読み書き可能な設定(テストモード)である前提。
/// イベント本番運用の前には、最低限 /characters 配下だけ読み書きを許可するよう
/// ルールを絞ることを推奨する。
/// </summary>
public class FirebaseCardListener : MonoBehaviour
{
    [Tooltip("FirebaseコンソールのdatabaseURL（例: https://digitalcard-b825d-default-rtdb.firebaseio.com）")]
    [SerializeField] private string databaseUrl = "";

    [Tooltip("QRコードのスキャンを担当するコンポーネント（同じシーン内のQRScanner）")]
    [SerializeField] private QRCodeScanner qrCodeScanner;

    [Tooltip("検索結果・エラーメッセージを表示するUIテキスト（任意）")]
    [SerializeField] private TextMeshProUGUI statusText;

    [Tooltip("オンにすると、見つかったキャラクターをGameManagerの所持キャラクターコレクションに追加する")]
    [SerializeField] private bool saveScannedCharacterToGameManager = true;

    /// <summary>このセッション内で既に取り込み済みのcardId（同じカードの二重登録防止）。</summary>
    private readonly HashSet<string> registeredCardIds = new HashSet<string>();

    private void Start()
    {
        if (string.IsNullOrEmpty(databaseUrl))
        {
            Debug.LogError("FirebaseCardListener: databaseUrlが設定されていません。InspectorでFirebaseのdatabaseURLを設定してください。");
            return;
        }

        databaseUrl = databaseUrl.TrimEnd('/');

        if (qrCodeScanner == null)
        {
            Debug.LogError("FirebaseCardListener: Qr Code Scannerが設定されていません。InspectorでQRScannerをドラッグしてください。");
            return;
        }

        qrCodeScanner.OnQRCodeScanned += HandleQrScanned;
        SetStatus("カードのQRコードをカメラにかざしてください");
    }

    private void OnDestroy()
    {
        if (qrCodeScanner != null)
        {
            qrCodeScanner.OnQRCodeScanned -= HandleQrScanned;
        }
    }

    /// <summary>
    /// QRコードのデコードに成功するたびに呼ばれる。カード情報を解析し、
    /// Firebase上の確定済みキャラクターを検索するコルーチンを開始する。
    /// </summary>
    private void HandleQrScanned(string decodedText)
    {
        QRCardData cardData = QRCardData.FromJson(decodedText);
        if (cardData == null)
        {
            SetStatus("カードの読み取りに失敗しました（データ形式が不正です）");
            return;
        }

        StartCoroutine(LookupCharacter(cardData));
    }

    /// <summary>
    /// cardIdをもとに /characters/{cardId} を取得し、見つかればキャラクターとして登録する。
    /// </summary>
    private IEnumerator LookupCharacter(QRCardData cardData)
    {
        if (registeredCardIds.Contains(cardData.cardId))
        {
            SetStatus("このカードは既に登録済みです");
            yield break;
        }

        string entryUrl = $"{databaseUrl}/characters/{cardData.cardId}.json";

        using (UnityWebRequest getReq = UnityWebRequest.Get(entryUrl))
        {
            yield return getReq.SendWebRequest();

            if (getReq.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"FirebaseCardListener: {cardData.cardId} の取得に失敗しました: " + getReq.error);
                SetStatus("カード情報の取得に失敗しました。通信環境を確認してください");
                yield break;
            }

            string json = getReq.downloadHandler.text;
            if (string.IsNullOrEmpty(json) || json == "null")
            {
                SetStatus("このカードはまだスマホで登録されていません");
                yield break;
            }

            CharacterRecord record;
            try
            {
                record = JsonUtility.FromJson<CharacterRecord>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"FirebaseCardListener: {cardData.cardId} のパースに失敗しました: " + e.Message);
                SetStatus("カード情報の解析に失敗しました");
                yield break;
            }

            if (record == null || string.IsNullOrEmpty(record.characterName))
            {
                Debug.LogWarning($"FirebaseCardListener: {cardData.cardId} のデータが不正です: " + json);
                SetStatus("カード情報が不正です");
                yield break;
            }

            CharacterStats stats = BuildCharacterStats(record);
            registeredCardIds.Add(cardData.cardId);

            Debug.Log($"[FirebaseCardListener] キャラクターを呼び出しました: cardId={cardData.cardId}\n{stats}");

            if (saveScannedCharacterToGameManager && GameManager.Instance != null)
            {
                GameManager.Instance.AddOwnedCharacter(stats);
            }

            SetStatus($"{stats.characterName} が仲間になった！");
        }
    }

    /// <summary>
    /// Firebaseから取得した確定済みの値をもとにCharacterStatsを構築する。
    /// スマホ側(app.js)で既にステータス・属性・スキルが確定済みのため、
    /// AssignRandomStatsは呼ばずそのまま復元するだけにする。
    /// </summary>
    private CharacterStats BuildCharacterStats(CharacterRecord record)
    {
        CharacterStats stats = new CharacterStats(record.characterName)
        {
            attack = record.attack,
            defense = record.defense,
            speed = record.speed,
            maxHp = record.maxHp > 0 ? record.maxHp : 100,
            isMutation = record.isMutation
        };
        stats.hp = stats.maxHp;

        if (Enum.TryParse(record.element, out ElementType element))
        {
            stats.element = element;
        }
        else
        {
            Debug.LogWarning($"FirebaseCardListener: 未知の属性文字列です({record.element})。Fireを既定値として使用します。cardId={record.cardId}");
            stats.element = ElementType.Fire;
        }

        if (Enum.TryParse(record.skillType, out SkillType skillType))
        {
            stats.skill = new CharacterSkill(skillType, record.ratio);
        }
        else
        {
            // 旧バージョンのapp.js(skillType/ratio未送信)が書き込んだ過去データへの後方互換。
            Debug.LogWarning($"FirebaseCardListener: skillTypeが見つかりません(古い形式のデータの可能性)。既定スキルを割り当てます。cardId={record.cardId}");
            stats.skill = new CharacterSkill(SkillType.PowerBoost, 0.3f);
        }

        return stats;
    }

    private void SetStatus(string text)
    {
        if (statusText != null)
        {
            statusText.text = text;
        }
    }

    /// <summary>/characters/{cardId} に保存されているデータの形式。</summary>
    [Serializable]
    private class CharacterRecord
    {
        public string cardId;
        public int seed;
        public long timestamp;
        public string characterName;
        public string element;
        public int attack;
        public int defense;
        public int speed;
        public int hp;
        public int maxHp;
        public bool isMutation;
        public string skillType;
        public float ratio;
    }
}
