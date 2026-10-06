using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 本番の2台対戦モード専用。各ターンの「強/普/弱」選択を、PC側のボタンではなく
/// 両プレイヤーのスマホ(docs/app.js)からFirebase経由で受け取るための同期役。
/// 対戦の意思決定はすべてスマホ側で完結し、PC(Unity)は結果を演出して表示するだけの役割になる。
///
/// Firebase上の /tables/{tableId}/activeBattle を使う:
///   { matchId, status, turn, attackerSlot, defenderSlot, timeoutSeconds,
///     player1Uid, player2Uid, player1Name, player2Name, winnerSlot, endReason,
///     choices: { "{turn}": { attacker, defender } } }
///   status: starting → choosing → finished / aborted
///
/// ・スマホは activeBattle/choices/{turn}/attacker または defender に選択を書き込む。
///   ターン番号ごとに書き込み先が分かれるため、遅れて届いた書き込みが次のターンに混ざることはない。
/// ・制限時間(timeoutSeconds)内に選ばれなかった側は「普」を選んだものとして扱う。
/// ・スマホは対戦中、activeBattle/presence/{slot} に数秒ごとに生存信号(サーバー時刻)を書き込む。
///   信号が disconnectGraceSeconds 以上途絶えた側は「通信が切れた」とみなし、制限時間を待たずに
///   BattleManager がその側を自動で操作する(信号が戻れば、またスマホで選べるようになる)。
///
/// 【セットアップ方法】
/// 1. バトルシーンに空のGameObjectを作成し、このスクリプトをアタッチ
/// 2. BattleManager側のbattleTurnSyncにこのコンポーネントをドラッグ
///    (battleQueueIntakeとセットで使う。どちらも未設定ならPCボタン+AIのフォールバック動作になる)
/// </summary>
public class BattleTurnSync : MonoBehaviour
{
    /// <summary>1ターン分の選択結果。</summary>
    public struct TurnChoices
    {
        public AttackLevel attacker;
        public AttackLevel defender;
        public bool attackerTimedOut;
        public bool defenderTimedOut;
        /// <summary>通信が切れていて選べなかった(自動で操作する)側。</summary>
        public bool attackerDisconnected;
        public bool defenderDisconnected;
    }

    [Tooltip("ポーリング間隔（秒）")]
    [SerializeField] private float pollIntervalSeconds = 1f;

    [Tooltip("1回の選択の制限時間（秒）。過ぎたら未選択の側は「普」を選んだものとして扱う")]
    [SerializeField] private float choiceTimeoutSeconds = 30f;

    [Tooltip("スマホの生存信号がこの秒数途絶えたら通信切れとみなし、自動で操作する")]
    [SerializeField] private float disconnectGraceSeconds = 6f;

    private int turnNumber;
    private bool matchInProgress;
    private float choiceDeadline;

    /// <summary>スロットごとの生存信号の受信状況。</summary>
    private sealed class PresenceState
    {
        public long lastValue;
        public float lastChangeTime;
        public bool seen; // 一度でも信号を受け取ったか(古い版のスマホは信号を送らないため、その場合は通信切れ扱いにしない)
    }

    private readonly PresenceState player1Presence = new PresenceState();
    private readonly PresenceState player2Presence = new PresenceState();
    private Coroutine presenceRoutine;

    /// <summary>選択を待っている最中か。</summary>
    public bool IsWaitingForChoices { get; private set; }

    /// <summary>今のターンで攻撃側/防御側がスマホから選択済みか(内容は公開前なので持たない)。</summary>
    public bool AttackerChosen { get; private set; }
    public bool DefenderChosen { get; private set; }

    /// <summary>選択の制限時間の残り秒数。</summary>
    public float SecondsRemaining => IsWaitingForChoices ? Mathf.Max(0f, choiceDeadline - Time.time) : 0f;

    public float ChoiceTimeoutSeconds => choiceTimeoutSeconds;

    private string BattlePath => $"{FirebaseRest.TablePath}/activeBattle";

    [Serializable]
    private class ChoiceRecord
    {
        public string attacker;
        public string defender;
    }

    [Serializable]
    private class PresenceRecord
    {
        public long player1;
        public long player2;
    }

    /// <summary>試合の開始(BattleQueueIntakeがactiveBattleを初期化した後)に呼ぶ。</summary>
    public void BeginMatch()
    {
        turnNumber = 0;
        matchInProgress = true;
        foreach (PresenceState state in new[] { player1Presence, player2Presence })
        {
            state.lastValue = 0;
            state.lastChangeTime = Time.time;
            state.seen = false;
        }
        if (presenceRoutine != null) StopCoroutine(presenceRoutine);
        presenceRoutine = StartCoroutine(PollPresence());
    }

    /// <summary>そのスロット("player1" / "player2")のスマホの通信が切れているか。</summary>
    public bool IsDisconnected(string slot)
    {
        PresenceState state = slot == "player1" ? player1Presence : player2Presence;
        return state.seen && Time.time - state.lastChangeTime > disconnectGraceSeconds;
    }

    /// <summary>試合中ずっと、両スマホの生存信号を見張る(演出中に途切れても気づけるように、ターンとは独立して回す)。</summary>
    private IEnumerator PollPresence()
    {
        WaitForSeconds wait = new WaitForSeconds(pollIntervalSeconds);
        while (matchInProgress)
        {
            yield return FirebaseRest.Get($"{BattlePath}/presence", req =>
            {
                if (req.result != UnityWebRequest.Result.Success) return; // こちらの通信不良でスマホを切断扱いにしないよう、失敗時は何もしない
                string json = req.downloadHandler.text;
                if (string.IsNullOrEmpty(json) || json == "null") return;
                PresenceRecord record;
                try
                {
                    record = JsonUtility.FromJson<PresenceRecord>(json);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("BattleTurnSync: 生存信号のパースに失敗しました: " + e.Message);
                    return;
                }
                Observe(player1Presence, record.player1);
                Observe(player2Presence, record.player2);
            });
            yield return wait;
        }
        presenceRoutine = null;
    }

    private static void Observe(PresenceState state, long value)
    {
        if (value == 0 || value == state.lastValue) return;
        state.lastValue = value;
        state.lastChangeTime = Time.time;
        state.seen = true;
    }

    /// <summary>
    /// 新しいターンの攻撃側/防御側スロットをFirebaseに書き込み、
    /// 両者の選択が揃うか制限時間が過ぎるまで待つ。
    /// </summary>
    public IEnumerator WaitForChoices(string attackerSlot, string defenderSlot, Action<TurnChoices> onResolved)
    {
        turnNumber++;
        string turnJson =
            "{\"turn\":" + turnNumber +
            ",\"turnKey\":\"" + turnNumber + "\"" + // Database Rulesで choices/{turn} と照合するための文字列版
            ",\"attackerSlot\":\"" + attackerSlot + "\"" +
            ",\"defenderSlot\":\"" + defenderSlot + "\"" +
            ",\"timeoutSeconds\":" + Mathf.RoundToInt(choiceTimeoutSeconds) +
            ",\"status\":\"choosing\"}";

        // ターン情報の書き込みに失敗するとスマホにボタンが出ないため、成功するまで繰り返す
        bool written = false;
        while (!written)
        {
            yield return FirebaseRest.Write("PATCH", BattlePath, turnJson, ok => written = ok);
            if (!written) yield return new WaitForSeconds(pollIntervalSeconds);
        }

        float deadline = Time.time + choiceTimeoutSeconds;
        choiceDeadline = deadline;
        AttackerChosen = DefenderChosen = false;
        IsWaitingForChoices = true;
        ChoiceRecord latest = null;

        while (Time.time < deadline)
        {
            yield return new WaitForSeconds(pollIntervalSeconds);

            yield return FirebaseRest.Get($"{BattlePath}/choices/{turnNumber}", req =>
            {
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("BattleTurnSync: 選択の取得に失敗しました: " + req.error);
                    return;
                }
                string json = req.downloadHandler.text;
                if (string.IsNullOrEmpty(json) || json == "null") return;
                try
                {
                    latest = JsonUtility.FromJson<ChoiceRecord>(json);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("BattleTurnSync: 選択のパースに失敗しました: " + e.Message);
                }
            });

            AttackerChosen = !string.IsNullOrEmpty(latest?.attacker);
            DefenderChosen = !string.IsNullOrEmpty(latest?.defender);
            // 選び終えたか、通信が切れて(数秒待っても戻らず)自動操作になる側だけになったら締め切る
            if ((AttackerChosen || IsDisconnected(attackerSlot)) && (DefenderChosen || IsDisconnected(defenderSlot)))
            {
                break;
            }
        }
        IsWaitingForChoices = false;

        bool attackerMissing = string.IsNullOrEmpty(latest?.attacker);
        bool defenderMissing = string.IsNullOrEmpty(latest?.defender);
        bool attackerOffline = attackerMissing && IsDisconnected(attackerSlot);
        bool defenderOffline = defenderMissing && IsDisconnected(defenderSlot);
        TurnChoices result = new TurnChoices
        {
            attackerTimedOut = attackerMissing && !attackerOffline,
            defenderTimedOut = defenderMissing && !defenderOffline,
            attackerDisconnected = attackerOffline,
            defenderDisconnected = defenderOffline,
        };
        result.attacker = ParseLevel(latest?.attacker);
        result.defender = ParseLevel(latest?.defender);
        onResolved(result);
    }

    /// <summary>
    /// 対戦終了をスマホ側に伝える。winnerSlot は player1 / player2 / draw。
    /// endReason は knockout(HP0) / judgement(攻防回数の上限で判定) / forfeit(未選択が続いたため)。
    /// </summary>
    public IEnumerator WriteFinished(string winnerSlot, string endReason)
    {
        matchInProgress = false;
        string json = "{\"status\":\"finished\",\"winnerSlot\":\"" + winnerSlot + "\",\"endReason\":\"" + endReason + "\"}";
        yield return FirebaseRest.Write("PATCH", BattlePath, json);
    }

    /// <summary>
    /// 試合の途中でバトル画面を離れる時(Escやシーンの再読み込み)に呼ぶ。
    /// スマホ側に中断を伝え、「選択待ち」のまま止まらないようにする。
    /// </summary>
    public void AbortIfInProgress()
    {
        if (!matchInProgress) return;
        matchInProgress = false;
        FirebaseRest.WriteFireAndForget("PATCH", BattlePath, "{\"status\":\"aborted\"}");
    }

    private void OnDestroy()
    {
        AbortIfInProgress();
    }

    private static AttackLevel ParseLevel(string value)
    {
        switch (value)
        {
            case "Strong": return AttackLevel.Strong;
            case "Weak": return AttackLevel.Weak;
            default: return AttackLevel.Normal;
        }
    }
}
