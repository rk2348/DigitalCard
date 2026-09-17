using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 本番の2台対戦モード専用。各ターンの「強/普/弱」選択を、PC側のボタンではなく
/// 両プレイヤーのスマホ(docs/app.js)からFirebase経由で受け取るための同期役。
/// これにより対戦の意思決定はすべてスマホ側で完結し、PC(Unity)は結果を演出して
/// 表示するだけの役割になる。
///
/// Firebase上の /activeBattle を使う:
///   { turn, attackerSlot, defenderSlot, attackerChoice, defenderChoice, status, winnerSlot }
/// スマホ側は自分のスロット(player1/player2)がattackerSlot/defenderSlotのどちらかに
/// 一致する間だけ「強/普/弱」ボタンを表示し、選んだ内容をattackerChoice/defenderChoiceに書き込む。
///
/// 【セットアップ方法】
/// 1. バトルシーンに空のGameObjectを作成し、このスクリプトをアタッチ
/// 2. databaseUrlにFirebaseのdatabaseURLを設定
/// 3. BattleManager側のbattleTurnSyncにこのコンポーネントをドラッグ
///    (battleQueueIntakeとセットで使う。どちらも未設定ならPCボタン+AIのフォールバック動作になる)
/// </summary>
public class BattleTurnSync : MonoBehaviour
{
    [Tooltip("FirebaseコンソールのdatabaseURL（例: https://digitalcard-b825d-default-rtdb.firebaseio.com）")]
    [SerializeField] private string databaseUrl = "";

    [Tooltip("ポーリング間隔（秒）")]
    [SerializeField] private float pollIntervalSeconds = 1f;

    private int turnNumber;

    private void Awake()
    {
        if (!string.IsNullOrEmpty(databaseUrl))
        {
            databaseUrl = databaseUrl.TrimEnd('/');
        }
    }

    /// <summary>
    /// 新しいターンの攻撃側/防御側スロットをFirebaseに書き込み、
    /// 両者の選択(attackerChoice / defenderChoice)が揃うまで待つ。
    /// </summary>
    public IEnumerator WaitForChoices(string attackerSlot, string defenderSlot, Action<AttackLevel, AttackLevel> onResolved)
    {
        turnNumber++;
        yield return StartCoroutine(PutRaw(BuildTurnStateJson(attackerSlot, defenderSlot)));

        AttackLevel attackerLevel = AttackLevel.Normal;
        AttackLevel defenderLevel = AttackLevel.Normal;
        bool bothReady = false;

        while (!bothReady)
        {
            yield return new WaitForSeconds(pollIntervalSeconds);

            string url = $"{databaseUrl}/activeBattle.json";
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("BattleTurnSync: activeBattleの取得に失敗しました: " + req.error);
                    continue;
                }

                string json = req.downloadHandler.text;
                if (string.IsNullOrEmpty(json) || json == "null") continue;

                ActiveBattleRecord record;
                try
                {
                    record = JsonUtility.FromJson<ActiveBattleRecord>(json);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("BattleTurnSync: activeBattleのパースに失敗しました: " + e.Message);
                    continue;
                }

                if (record == null || record.turn != turnNumber) continue;

                if (!string.IsNullOrEmpty(record.attackerChoice) && !string.IsNullOrEmpty(record.defenderChoice))
                {
                    attackerLevel = ParseLevel(record.attackerChoice);
                    defenderLevel = ParseLevel(record.defenderChoice);
                    bothReady = true;
                }
            }
        }

        onResolved(attackerLevel, defenderLevel);
    }

    /// <summary>対戦終了をスマホ側に伝える(勝者のスロットを書き込む)。</summary>
    public IEnumerator WriteFinished(string winnerSlot)
    {
        string json = "{\"status\":\"finished\",\"winnerSlot\":\"" + winnerSlot + "\"}";
        yield return StartCoroutine(PutRaw(json));
    }

    private string BuildTurnStateJson(string attackerSlot, string defenderSlot)
    {
        return "{\"turn\":" + turnNumber +
               ",\"attackerSlot\":\"" + attackerSlot + "\"" +
               ",\"defenderSlot\":\"" + defenderSlot + "\"" +
               ",\"attackerChoice\":null,\"defenderChoice\":null,\"status\":\"choosing\"}";
    }

    private IEnumerator PutRaw(string json)
    {
        string url = $"{databaseUrl}/activeBattle.json";
        byte[] body = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest req = new UnityWebRequest(url, "PUT"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("BattleTurnSync: activeBattleの書き込みに失敗しました: " + req.error);
            }
        }
    }

    private AttackLevel ParseLevel(string value)
    {
        switch (value)
        {
            case "Strong": return AttackLevel.Strong;
            case "Weak": return AttackLevel.Weak;
            default: return AttackLevel.Normal;
        }
    }

    [Serializable]
    private class ActiveBattleRecord
    {
        public int turn;
        public string attackerSlot;
        public string defenderSlot;
        public string attackerChoice;
        public string defenderChoice;
        public string status;
    }
}
