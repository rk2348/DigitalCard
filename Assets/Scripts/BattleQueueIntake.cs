using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// バトル開始条件を「スマホ側で2台分の対戦登録が揃うこと」に一本化するための待受コンポーネント。
///
/// 【全体の流れ】
/// 1. プレイヤーはそれぞれ自分のスマホでQRスキャン→実物撮影→背景切り抜き→名前入力→登録を行う
///    (app.js側)。登録が完了すると、app.js側がFirebaseの
///    /battleSlots/player1 または /battleSlots/player2 の空いている方に
///    (transaction()で競合を避けつつ)自分のデータを書き込む。
/// 2. PC(Unity)側はこのコンポーネントが /battleSlots を定期的にポーリングし、
///    player1・player2の両方が埋まったら試合成立とみなす。
/// 3. 試合成立したら両者のCharacterStats(実物の写真つき)を組み立ててOnMatchReadyで通知し、
///    /battleSlotsをクリアする(次の組がまた入れるようにする)。
///
/// 【重要】PC(Unity)側でQRコードを直接読み取ることは想定していない。
/// カメラ・QRコード解析はすべてスマホ(html5-qrcode)側で完結させ、PC側はFirebaseの
/// battleSlotsを見るだけ、という役割分担にしている。
///
/// ステータス・スキルはスマホ(JavaScript)側で既に確定させた値をそのまま使う。
/// C#のCharacterStats.AssignRandomStats(seed)で同じseedから再計算すると、
/// 乱数アルゴリズムの違い(JS:mulberry32 / C#:System.Random)により結果が
/// 一致しなくなるため、seedからの再計算はあえて行わない。
///
/// 【セットアップ方法】
/// 1. バトルシーンに空のGameObjectを作成し、このスクリプトをアタッチ
/// 2. databaseUrlにFirebaseのdatabaseURL(例: https://xxxx-default-rtdb.firebaseio.com)を設定
/// 3. waitingPanel(任意)に「対戦相手を待っています」等を表示するUIパネルをドラッグ
///    (試合成立時に自動でSetActive(false)される)
/// 4. statusText(任意)に状況テキスト(プレイヤー1/2の参加状況)を表示するTextMeshProUGUIをドラッグ
/// 5. BattleManager側のbattleQueueIntakeにこのコンポーネントをドラッグし、
///    OnMatchReadyを購読してもらう
/// </summary>
public class BattleQueueIntake : MonoBehaviour
{
    [Tooltip("FirebaseコンソールのdatabaseURL（例: https://digitalcard-b825d-default-rtdb.firebaseio.com）")]
    [SerializeField] private string databaseUrl = "";

    [Tooltip("ポーリング間隔（秒）")]
    [SerializeField] private float pollIntervalSeconds = 1.5f;

    [Tooltip("対戦相手を待っている間だけ表示しておくパネル(任意)。試合成立時に自動でSetActive(false)する")]
    [SerializeField] private GameObject waitingPanel;

    [Tooltip("状況メッセージ(プレイヤー1/2の参加状況)の表示先(任意)")]
    [SerializeField] private TextMeshProUGUI statusText;

    /// <summary>2人分の登録が揃った時に発火。引数は(プレイヤー1, プレイヤー2)。</summary>
    public event Action<CharacterStats, CharacterStats> OnMatchReady;

    private bool matchStarted = false;

    private void Start()
    {
        if (string.IsNullOrEmpty(databaseUrl))
        {
            Debug.LogError("BattleQueueIntake: databaseUrlが設定されていません。");
            return;
        }

        databaseUrl = databaseUrl.TrimEnd('/');
        StartCoroutine(PollLoop());
    }

    private IEnumerator PollLoop()
    {
        while (!matchStarted)
        {
            yield return StartCoroutine(PollOnce());

            if (!matchStarted)
            {
                yield return new WaitForSeconds(pollIntervalSeconds);
            }
        }
    }

    private IEnumerator PollOnce()
    {
        string url = $"{databaseUrl}/battleSlots.json";

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("BattleQueueIntake: battleSlotsの取得に失敗しました: " + req.error);
                yield break;
            }

            string json = req.downloadHandler.text;
            if (string.IsNullOrEmpty(json) || json == "null")
            {
                SetStatus("プレイヤー1: 待機中 / プレイヤー2: 待機中");
                yield break;
            }

            BattleSlotsRecord slots = null;
            bool parseFailed = false;
            try
            {
                slots = JsonUtility.FromJson<BattleSlotsRecord>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("BattleQueueIntake: battleSlotsのパースに失敗しました: " + e.Message);
                parseFailed = true;
            }

            if (parseFailed || slots == null)
            {
                yield break;
            }

            bool player1Ready = slots.player1 != null && !string.IsNullOrEmpty(slots.player1.characterName);
            bool player2Ready = slots.player2 != null && !string.IsNullOrEmpty(slots.player2.characterName);

            SetStatus(
                $"プレイヤー1: {(player1Ready ? slots.player1.characterName + " 参加済み" : "待機中")} / " +
                $"プレイヤー2: {(player2Ready ? slots.player2.characterName + " 参加済み" : "待機中")}");

            if (player1Ready && player2Ready)
            {
                matchStarted = true;

                CharacterStats p1 = BuildCharacterStats(slots.player1);
                CharacterStats p2 = BuildCharacterStats(slots.player2);

                // 次の組がまた登録できるよう、先にサーバー側をクリアしておく
                yield return StartCoroutine(ClearSlots());

                if (waitingPanel != null) waitingPanel.SetActive(false);

                OnMatchReady?.Invoke(p1, p2);
            }
        }
    }

    /// <summary>
    /// 試合成立後、次の組がまた登録できるようbattleSlotsを空にする(nullで上書き)。
    /// </summary>
    private IEnumerator ClearSlots()
    {
        string url = $"{databaseUrl}/battleSlots.json";
        byte[] body = Encoding.UTF8.GetBytes("null");

        using (UnityWebRequest req = new UnityWebRequest(url, "PUT"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("BattleQueueIntake: battleSlotsのクリアに失敗しました: " + req.error);
            }
        }
    }

    /// <summary>
    /// Firebaseから取得したレコードをCharacterStatsに変換する(BattleCardIntakeと同じロジック)。
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
            Debug.LogWarning($"BattleQueueIntake: 未知の属性名です({record.element})。既定値を使用します。");
        }

        if (!string.IsNullOrEmpty(record.skillType) && Enum.TryParse(record.skillType, out SkillType parsedSkillType))
        {
            stats.skill = new CharacterSkill(parsedSkillType, record.ratio);
        }
        else
        {
            Debug.LogWarning($"BattleQueueIntake: 未知のスキル種別です({record.skillType})。スキル無しとして扱います。");
        }

        stats.photoSprite = BuildSpriteFromDataUrl(record.photoDataUrl);

        return stats;
    }

    /// <summary>
    /// data URL形式("data:image/png;base64,....")の文字列をSpriteに変換する。
    /// 写真が無い/デコードに失敗した場合はnullを返す(MonsterSpriteBuilder側で
    /// 手続き生成にフォールバックする)。
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
            Debug.LogWarning("BattleQueueIntake: 写真データ(base64)のデコードに失敗しました: " + e.Message);
            return null;
        }

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(imageBytes))
        {
            Debug.LogWarning("BattleQueueIntake: 写真データのTexture2Dへの変換に失敗しました。");
            return null;
        }

        return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
    }

    private void SetStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }
}
