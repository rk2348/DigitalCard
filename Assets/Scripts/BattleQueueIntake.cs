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
/// 4. 立ち去った人の枠が埋まったままにならないよう、次の枠は自動で空ける:
///    ・スマホの生存信号(/queuePresence/{slot}、数秒ごと)が presenceTimeoutSeconds 途絶えた枠
///    ・maxWaitSeconds 以上、対戦相手が来ないまま待っている枠
///    (スマホ側も、通信が切れた時点で自分の枠を自動で消す。運営者は ResetQueue で全枠を空けられる)
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

    [Tooltip("待機中のスマホの生存信号がこの秒数途絶えたら、その枠を空ける")]
    [SerializeField] private float presenceTimeoutSeconds = 20f;

    [Tooltip("対戦相手が来ないまま、この秒数待っている枠は空ける")]
    [SerializeField] private float maxWaitSeconds = 300f;

    /// <summary>2人分の登録が揃い、activeBattleの初期化と枠のクリアが済んだ時に発火。</summary>
    public event Action<MatchInfo> OnMatchReady;

    /// <summary>参加枠の状況が取得できるたびに発火(プレイヤー1参加済みか, その名前, プレイヤー2参加済みか, その名前)。</summary>
    public event Action<bool, string, bool, string> OnSlotsChanged;

    private bool matchStarted;

    // 参加者名の表示用キャッシュ(枠が埋まった時に名前だけを1回取得する)
    private string player1Name;
    private string player2Name;

    private string SlotsPath => $"{FirebaseRest.TablePath}/battleSlots";
    private string QueuePresencePath => $"{FirebaseRest.TablePath}/queuePresence";

    /// <summary>枠ごとの待機状況(誰が・いつから・最後に生存信号が届いたのはいつか)。</summary>
    private sealed class SlotWatch
    {
        public string slot;
        public string occupant;      // 枠に入っている人(uid + 参加時刻)。変わったら待ち時間を数え直す
        public string uid;
        public float since;          // この人を最初に見た時刻(Time.time)
        public long lastPresenceAt;  // 最後に受け取った生存信号の値
        public float lastPresenceChange;
        public bool presenceSeen;    // 一度でも生存信号を受け取ったか(古い版のスマホは送らないので、その場合は待ち時間の上限だけで判断する)

        public void Clear()
        {
            occupant = null;
            uid = null;
            presenceSeen = false;
            lastPresenceAt = 0;
        }
    }

    private readonly SlotWatch watch1 = new SlotWatch { slot = "player1" };
    private readonly SlotWatch watch2 = new SlotWatch { slot = "player2" };

    [Serializable]
    private class QueuePresenceEntry
    {
        public string uid;
        public long at;
    }

    [Serializable]
    private class QueuePresenceRecord
    {
        public QueuePresenceEntry player1;
        public QueuePresenceEntry player2;
    }

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

        // 立ち去った人の枠を空ける(空けた場合は、次回のポーリングで最新の状態を取り直す)
        bool expired = false;
        yield return StartCoroutine(ExpireAbandonedSlots(presence, e => expired = e));
        if (expired) yield break;

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

    /// <summary>
    /// 生存信号が途絶えた枠・待ち時間の上限を超えた枠を空ける。1つでも空けたら onDone(true)。
    /// </summary>
    private IEnumerator ExpireAbandonedSlots(BattleSlotsPresence presence, Action<bool> onDone)
    {
        // 枠に入っている人の確認(新しく入った人がいれば uid と参加時刻を取得する)
        yield return StartCoroutine(Track(watch1, presence.player1));
        yield return StartCoroutine(Track(watch2, presence.player2));

        if (watch1.occupant != null || watch2.occupant != null)
        {
            yield return FirebaseRest.Get(QueuePresencePath, req =>
            {
                if (req.result != UnityWebRequest.Result.Success) return; // こちらの通信不良で枠を空けないよう、失敗時は何もしない
                QueuePresenceRecord record = ParseOrDefault<QueuePresenceRecord>(req.downloadHandler.text);
                ObservePresence(watch1, record?.player1);
                ObservePresence(watch2, record?.player2);
            });
        }

        bool expiredAny = false;
        foreach (SlotWatch w in new[] { watch1, watch2 })
        {
            if (w.occupant == null) continue;
            string reason = null;
            if (w.presenceSeen && Time.time - w.lastPresenceChange > presenceTimeoutSeconds) reason = "スマホの通信が途絶えたため";
            else if (Time.time - w.since > maxWaitSeconds) reason = "待ち時間の上限を超えたため";
            if (reason == null) continue;

            Debug.Log($"BattleQueueIntake: {reason}、{w.slot} の枠を空けます。");
            yield return StartCoroutine(ClearSlot(w.slot));
            if (w.slot == "player1") player1Name = null; else player2Name = null;
            w.Clear();
            expiredAny = true;
        }
        onDone(expiredAny);
    }

    /// <summary>枠に入っている人を確認し、入れ替わっていれば待ち時間を数え直す。</summary>
    private IEnumerator Track(SlotWatch w, bool occupied)
    {
        if (!occupied)
        {
            w.Clear();
            yield break;
        }

        // 参加時刻が前回と同じなら同じ人とみなす(uidの取得は入れ替わった時だけにして、通信を減らす)
        string joinedAt = null;
        yield return FirebaseRest.Get($"{SlotsPath}/{w.slot}/timestamp", req =>
        {
            if (req.result == UnityWebRequest.Result.Success) joinedAt = req.downloadHandler.text;
        });
        if (joinedAt == null) yield break; // 取得できなかった時は前回の状態のまま
        if (w.occupant != null && w.occupant.EndsWith("@" + joinedAt)) yield break;

        string uid = null;
        yield return FirebaseRest.Get($"{SlotsPath}/{w.slot}/uid", req =>
        {
            if (req.result == UnityWebRequest.Result.Success) uid = req.downloadHandler.text.Trim('"');
        });
        if (uid == null) yield break;

        string occupant = uid + "@" + joinedAt;
        w.Clear();
        w.occupant = occupant;
        w.uid = uid;
        w.since = Time.time;
        w.lastPresenceChange = Time.time;
    }

    private static void ObservePresence(SlotWatch w, QueuePresenceEntry entry)
    {
        if (w.occupant == null || entry == null || entry.at == 0 || entry.uid != w.uid) return;
        if (entry.at == w.lastPresenceAt) return;
        w.lastPresenceAt = entry.at;
        w.lastPresenceChange = Time.time;
        w.presenceSeen = true;
    }

    private IEnumerator ClearSlot(string slot)
    {
        yield return FirebaseRest.Write("PUT", $"{SlotsPath}/{slot}", "null");
        yield return FirebaseRest.Write("PUT", $"{QueuePresencePath}/{slot}", "null");
    }

    /// <summary>
    /// 運営者用: 待機中の枠をすべて空ける(試合が始まる前だけ)。
    /// 待っていた人のスマホには「対戦待ちが解除されました」と表示される。
    /// </summary>
    public void ResetQueue()
    {
        if (matchStarted) return;
        Debug.Log("BattleQueueIntake: 運営者の操作で、待機中の枠をすべて空けます。");
        StartCoroutine(ResetQueueRoutine());
    }

    private IEnumerator ResetQueueRoutine()
    {
        yield return FirebaseRest.Write("PUT", SlotsPath, "null");
        yield return FirebaseRest.Write("PUT", QueuePresencePath, "null");
        watch1.Clear();
        watch2.Clear();
        player1Name = player2Name = null;
        OnSlotsChanged?.Invoke(false, null, false, null);
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
        FirebaseRest.WriteFireAndForget("PUT", QueuePresencePath, "null");

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
