using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Firebase Realtime DatabaseをUnityから定期的にポーリングし、スマホ(GitHub Pages)から
/// 書き込まれたカード登録リクエスト(pendingRegistrations)を処理して、
/// 結果(results)をFirebaseへ書き戻すコンポーネント。
///
/// これまでの QRWebServer.cs (HttpListenerによる簡易サーバー) と
/// QRCharacterRegistrar.cs (受信データの処理) を置き換えるもの。
/// LAN内サーバーやCloudflare Tunnel等のトンネルが一切不要になる。
///
/// 【全体の流れ】
/// 1. スマホがQRを読み取り、名前を入力して /pendingRegistrations/{key} に書き込む
///    （{key}はFirebaseのpush()が生成する一意なID）
/// 2. このスクリプトが数秒おきに /pendingRegistrations をポーリングし、新しいエントリを検知する
/// 3. seedからCharacterStats.AssignRandomStats(seed)でキャラクターを確定させる
/// 4. 結果を /results/{key} に書き込む
/// 5. 処理済みのエントリは /pendingRegistrations/{key} から削除する
/// 6. スマホは /results/{key} をリアルタイム購読しており、書き込まれた瞬間にステータスを表示する
///
/// Firebase Unity SDKは導入せず、UnityWebRequestでREST API
/// （https://＜databaseURL＞/パス.json）を直接叩くことで依存を減らしている。
///
/// 【セットアップ方法】
/// 1. QRScanシーンに空のGameObjectを作成し、このスクリプトをアタッチ
/// 2. Inspectorの databaseUrl に、FirebaseコンソールのdatabaseURL
///    （例: https://digitalcard-b825d-default-rtdb.firebaseio.com）を入力
/// 3. 再生すると自動的にポーリングが始まる(Consoleに開始ログが出ます)
///
/// 【重要：セキュリティルールについて】
/// このスクリプトはFirebase認証を行わず、REST APIへ直接アクセスする。
/// Realtime Databaseのルールが誰でも読み書き可能な設定(テストモード)である前提。
/// イベント本番運用の前には、最低限 pendingRegistrations と results 配下だけ
/// 読み書きを許可するようルールを絞ることを推奨する。
///
/// 【JSONパースについて】
/// JsonUtilityは辞書型(キーが可変のJSONオブジェクト)を直接パースできないため、
/// トップレベルのキー一覧の取得だけ簡易的に正規表現で行っている。個々のエントリの
/// 中身(cardId, seedなど)は固定の形なのでJsonUtilityで安全にパースできる。
/// より堅牢にしたい場合は com.unity.nuget.newtonsoft-json パッケージの導入を検討してください。
/// </summary>
public class FirebaseCardListener : MonoBehaviour
{
    [Tooltip("FirebaseコンソールのdatabaseURL（例: https://digitalcard-b825d-default-rtdb.firebaseio.com）")]
    [SerializeField] private string databaseUrl = "";

    [Tooltip("ポーリング間隔（秒）")]
    [SerializeField] private float pollIntervalSeconds = 1.5f;

    [Tooltip("オンにすると、確定したキャラクターをGameManagerにプレイヤーキャラクターとして保存する")]
    [SerializeField] private bool saveScannedCharacterToGameManager = false;

    private readonly HashSet<string> processingKeys = new HashSet<string>();

    private void Start()
    {
        if (string.IsNullOrEmpty(databaseUrl))
        {
            Debug.LogError("FirebaseCardListener: databaseUrlが設定されていません。InspectorでFirebaseのdatabaseURLを設定してください。");
            return;
        }

        databaseUrl = databaseUrl.TrimEnd('/');
        Debug.Log("FirebaseCardListener: ポーリングを開始します: " + databaseUrl);
        StartCoroutine(PollLoop());
    }

    private IEnumerator PollLoop()
    {
        while (true)
        {
            yield return StartCoroutine(PollOnce());
            yield return new WaitForSeconds(pollIntervalSeconds);
        }
    }

    private IEnumerator PollOnce()
    {
        string shallowUrl = $"{databaseUrl}/pendingRegistrations.json?shallow=true";

        using (UnityWebRequest req = UnityWebRequest.Get(shallowUrl))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("FirebaseCardListener: pendingRegistrationsの取得に失敗しました: " + req.error);
                yield break;
            }

            List<string> keys = ExtractTopLevelKeys(req.downloadHandler.text);

            foreach (string key in keys)
            {
                if (processingKeys.Contains(key)) continue;
                processingKeys.Add(key);
                yield return StartCoroutine(ProcessEntry(key));
                processingKeys.Remove(key);
            }
        }
    }

    private IEnumerator ProcessEntry(string key)
    {
        string entryUrl = $"{databaseUrl}/pendingRegistrations/{key}.json";

        using (UnityWebRequest getReq = UnityWebRequest.Get(entryUrl))
        {
            yield return getReq.SendWebRequest();

            if (getReq.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"FirebaseCardListener: {key} の取得に失敗しました: " + getReq.error);
                yield break;
            }

            string json = getReq.downloadHandler.text;
            if (string.IsNullOrEmpty(json) || json == "null")
            {
                yield break; // 既に他の処理で消費済み等
            }

            PendingRegistration pending;
            try
            {
                pending = JsonUtility.FromJson<PendingRegistration>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"FirebaseCardListener: {key} のパースに失敗しました: " + e.Message);
                yield break;
            }

            if (pending == null || string.IsNullOrEmpty(pending.cardId) || string.IsNullOrEmpty(pending.characterName))
            {
                Debug.LogWarning($"FirebaseCardListener: {key} のデータが不正です: " + json);
                yield return StartCoroutine(PutResult(key, BuildErrorResponse("カードIDまたは名前が空です")));
                yield return StartCoroutine(DeletePending(key));
                yield break;
            }

            CharacterStats stats = new CharacterStats(pending.characterName);
            stats.AssignRandomStats(pending.seed);

            Debug.Log($"[FirebaseCardListener] キャラクター登録: cardId={pending.cardId}\n{stats}");

            if (saveScannedCharacterToGameManager && GameManager.Instance != null)
            {
                GameManager.Instance.SavePlayerCharacter(stats);
            }

            string resultJson = BuildStatsResponse(stats);
            yield return StartCoroutine(PutResult(key, resultJson));
            yield return StartCoroutine(DeletePending(key));
        }
    }

    private IEnumerator PutResult(string key, string resultJson)
    {
        string resultUrl = $"{databaseUrl}/results/{key}.json";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(resultJson);

        using (UnityWebRequest putReq = new UnityWebRequest(resultUrl, "PUT"))
        {
            putReq.uploadHandler = new UploadHandlerRaw(bodyRaw);
            putReq.downloadHandler = new DownloadHandlerBuffer();
            putReq.SetRequestHeader("Content-Type", "application/json");

            yield return putReq.SendWebRequest();

            if (putReq.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"FirebaseCardListener: {key} の結果書き込みに失敗しました: " + putReq.error);
            }
        }
    }

    private IEnumerator DeletePending(string key)
    {
        string entryUrl = $"{databaseUrl}/pendingRegistrations/{key}.json";

        using (UnityWebRequest delReq = UnityWebRequest.Delete(entryUrl))
        {
            yield return delReq.SendWebRequest();

            if (delReq.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"FirebaseCardListener: {key} の削除に失敗しました: " + delReq.error);
            }
        }
    }

    /// <summary>
    /// Firebaseの ?shallow=true レスポンス（例: {"-Nabc":true,"-Nxyz":true}）から
    /// トップレベルのキー一覧を取り出す。
    /// </summary>
    private List<string> ExtractTopLevelKeys(string shallowJson)
    {
        var keys = new List<string>();
        if (string.IsNullOrEmpty(shallowJson) || shallowJson == "null")
        {
            return keys;
        }

        MatchCollection matches = Regex.Matches(shallowJson, "\"(-[A-Za-z0-9_]+)\"\\s*:");
        foreach (Match m in matches)
        {
            keys.Add(m.Groups[1].Value);
        }
        return keys;
    }

    private string BuildStatsResponse(CharacterStats stats)
    {
        var res = new CharacterStatsResponse
        {
            status = "ok",
            characterName = stats.characterName,
            element = stats.element.ToString(),
            attack = stats.attack,
            defense = stats.defense,
            speed = stats.speed,
            hp = stats.hp,
            maxHp = stats.maxHp,
            isMutation = stats.isMutation,
            skillName = stats.skill != null ? stats.skill.skillName : "",
            skillDescription = stats.skill != null ? stats.skill.GetDescription() : ""
        };

        return JsonUtility.ToJson(res);
    }

    private string BuildErrorResponse(string message)
    {
        var res = new ErrorResponse { status = "error", message = message };
        return JsonUtility.ToJson(res);
    }

    /// <summary>スマホ側(app.js)がpendingRegistrationsへ書き込むデータの形式。</summary>
    [Serializable]
    private class PendingRegistration
    {
        public string cardId;
        public int seed;
        public string characterName;
        public long timestamp;
    }

    /// <summary>スマホ側へ返す、確定したキャラクターステータスのJSON形式。</summary>
    [Serializable]
    private class CharacterStatsResponse
    {
        public string status;
        public string characterName;
        public string element;
        public int attack;
        public int defense;
        public int speed;
        public int hp;
        public int maxHp;
        public bool isMutation;
        public string skillName;
        public string skillDescription;
    }

    [Serializable]
    private class ErrorResponse
    {
        public string status;
        public string message;
    }
}
