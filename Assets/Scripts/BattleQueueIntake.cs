using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// バトル開始条件を「スマホ側で2台分の対戦登録が揃うこと」に一本化するための待受コンポーネント。
///
/// 【全体の流れ】
/// 1. プレイヤーはスマホ(docs/app.js)で登録済みカードを読み取り、この卓の
///    /tables/{tableId}/battleSlots/player1 または player2 の空いている方に(transactionで)参加する。
/// 2. PC(Unity)側はこのコンポーネントが battleSlots を shallow 取得(埋まっている枠のキーだけ)で
///    定期的にポーリングする。写真を含む全データは、2人が揃った時に1回だけ取得する。
/// 3. 試合成立時は、まず /activeBattle を新しい matchId で初期化し、その後で battleSlots を空にする。
///    スマホは「自分の枠が消えた」ことを合図に activeBattle の購読を始めるので、
///    この順序により前の試合の結果(status:"finished")を読んでしまうことがない。
///
/// 【重要】PC(Unity)側でQRコードを直接読み取ることは想定していない。
///
/// ステータス・スキルはスマホ(JavaScript)側で既に確定させた値をそのまま使う。
/// C#のCharacterStats.AssignRandomStats(seed)で同じseedから再計算すると、
/// 乱数アルゴリズムの違い(JS:mulberry32 / C#:System.Random)により結果が
/// 一致しなくなるため、seedからの再計算はあえて行わない。
///
/// 接続先と卓IDは FirebaseRest(Resources/FirebaseSettings.json)で一元管理する。
///
/// 【セットアップ方法】
/// 1. バトルシーンに空のGameObjectを作成し、このスクリプトをアタッチ
/// 2. waitingPanel(任意)に「対戦相手を待っています」等を表示するUIパネルをドラッグ
/// 3. statusText(任意)に状況テキストを表示するTextMeshProUGUIをドラッグ
/// 4. BattleManager側のbattleQueueIntakeにこのコンポーネントをドラッグする
/// </summary>
public class BattleQueueIntake : MonoBehaviour
{
    /// <summary>試合成立時に渡す情報。</summary>
    public class MatchInfo
    {
        public string matchId;
        public CharacterStats player1;
        public CharacterStats player2;
    }

    [Tooltip("ポーリング間隔（秒）")]
    [SerializeField] private float pollIntervalSeconds = 1.5f;

    [Tooltip("対戦相手を待っている間だけ表示しておくパネル(任意)。試合成立時に自動でSetActive(false)する")]
    [SerializeField] private GameObject waitingPanel;

    [Tooltip("状況メッセージ(プレイヤー1/2の参加状況)の表示先(任意)")]
    [SerializeField] private TextMeshProUGUI statusText;

    /// <summary>2人分の登録が揃い、activeBattleの初期化と枠のクリアが済んだ時に発火。</summary>
    public event Action<MatchInfo> OnMatchReady;

    /// <summary>参加枠の状況が取得できるたびに発火(プレイヤー1参加済みか, その名前, プレイヤー2参加済みか, その名前)。</summary>
    public event Action<bool, string, bool, string> OnSlotsChanged;

    private bool matchStarted;

    // 参加者名の表示用キャッシュ(枠が埋まった時に名前だけを1回取得する)
    private string player1Name;
    private string player2Name;

    private string SlotsPath => $"{FirebaseRest.TablePath}/battleSlots";

    private void Start()
    {
        if (string.IsNullOrEmpty(FirebaseRest.DatabaseUrl))
        {
            Debug.LogError("BattleQueueIntake: FirebaseSettings.json の databaseUrl が設定されていません。");
            return;
        }

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
        BattleSlotsPresence presence = null;
        yield return FirebaseRest.Get(SlotsPath, req =>
        {
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("BattleQueueIntake: battleSlotsの取得に失敗しました: " + req.error);
                return;
            }
            presence = ParseOrDefault<BattleSlotsPresence>(req.downloadHandler.text) ?? new BattleSlotsPresence();
        }, "shallow=true");

        if (presence == null) yield break;

        if (!presence.player1) player1Name = null;
        else if (player1Name == null) yield return StartCoroutine(FetchName("player1", n => player1Name = n));

        if (!presence.player2) player2Name = null;
        else if (player2Name == null) yield return StartCoroutine(FetchName("player2", n => player2Name = n));

        SetStatus(
            $"卓 {FirebaseRest.TableId}　" +
            $"プレイヤー1: {(presence.player1 ? (player1Name ?? "") + " 参加済み" : "待機中")} / " +
            $"プレイヤー2: {(presence.player2 ? (player2Name ?? "") + " 参加済み" : "待機中")}");
        OnSlotsChanged?.Invoke(presence.player1, player1Name, presence.player2, player2Name);

        if (presence.player1 && presence.player2)
        {
            yield return StartCoroutine(StartMatch());
        }
    }

    private IEnumerator FetchName(string slot, Action<string> onName)
    {
        yield return FirebaseRest.Get($"{SlotsPath}/{slot}/characterName", req =>
        {
            if (req.result != UnityWebRequest.Result.Success) return;
            string json = req.downloadHandler.text;
            if (json.Length >= 2 && json[0] == '"') onName(json.Substring(1, json.Length - 2));
        });
    }

    /// <summary>
    /// 2人分の全データを取得し、activeBattleを初期化してから枠を空にする。
    /// どこかで失敗した場合は何もせず、次回のポーリングでやり直す。
    /// </summary>
    private IEnumerator StartMatch()
    {
        BattleSlotsRecord slots = null;
        yield return FirebaseRest.Get(SlotsPath, req =>
        {
            if (req.result == UnityWebRequest.Result.Success)
                slots = ParseOrDefault<BattleSlotsRecord>(req.downloadHandler.text);
        });

        bool player1Ready = slots?.player1 != null && !string.IsNullOrEmpty(slots.player1.characterName);
        bool player2Ready = slots?.player2 != null && !string.IsNullOrEmpty(slots.player2.characterName);
        if (!player1Ready || !player2Ready) yield break;

        string matchId = Guid.NewGuid().ToString("N");
        string initJson =
            "{\"matchId\":\"" + matchId + "\"" +
            ",\"status\":\"starting\"" +
            ",\"player1Uid\":" + JsonString(slots.player1.uid) +
            ",\"player2Uid\":" + JsonString(slots.player2.uid) +
            ",\"player1Name\":" + JsonString(slots.player1.characterName) +
            ",\"player2Name\":" + JsonString(slots.player2.characterName) + "}";

        bool initialized = false;
        yield return FirebaseRest.Write("PUT", $"{FirebaseRest.TablePath}/activeBattle", initJson, ok => initialized = ok);
        if (!initialized) yield break;

        // 次の組がまた登録できるよう、試合データの初期化が済んでから枠を空にする
        bool cleared = false;
        yield return FirebaseRest.Write("PUT", SlotsPath, "null", ok => cleared = ok);
        if (!cleared) yield break;

        matchStarted = true;
        if (waitingPanel != null) waitingPanel.SetActive(false);

        OnMatchReady?.Invoke(new MatchInfo
        {
            matchId = matchId,
            player1 = FirebaseCharacterMapper.ToCharacterStats(slots.player1, nameof(BattleQueueIntake)),
            player2 = FirebaseCharacterMapper.ToCharacterStats(slots.player2, nameof(BattleQueueIntake)),
        });
    }

    /// <summary>
    /// 受付を止める(開発者用のデモ対戦をPCだけで始める時に使う)。
    /// Firebase上の枠には触れないので、スマホから参加済みの人がいれば次の試合の受付で拾われる。
    /// </summary>
    public void StopWaiting()
    {
        matchStarted = true;
        StopAllCoroutines();
    }

    private static T ParseOrDefault<T>(string json) where T : class
    {
        if (string.IsNullOrEmpty(json) || json == "null") return null;
        try
        {
            return JsonUtility.FromJson<T>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"BattleQueueIntake: {typeof(T).Name}のパースに失敗しました: {e.Message}");
            return null;
        }
    }

    private static string JsonString(string value)
    {
        if (value == null) return "null";
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private void SetStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }
}
