using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// スマホで登録されたカード(/characterByCard)を「登録された順」に引くための入口。
///
/// 写真を含む全件を取らずに済むよう、shallow でカードIDの一覧を取り、各カードの timestamp だけを取得して並べる。
/// 中身(写真を含む)は必要になったカードの分だけ取得する。
/// /characterByCard 全体の読み取りは Database Rules 上で会場PC(/admins に登録済みのUID)にしか許可されていない。
/// </summary>
public static class RegisteredCards
{
    private const int ParallelRequests = 6;
    private static readonly Regex ShallowKey = new Regex("\"([^\"]+)\"\\s*:\\s*true");

    /// <summary>
    /// 登録の古い順に並べたカードIDの一覧を取得する。失敗時は onDone(null, エラー内容)。
    /// </summary>
    public static IEnumerator LoadOrderedIds(MonoBehaviour host, Action<List<string>, string> onDone)
    {
        List<string> ids = null;
        string error = null;
        yield return FirebaseRest.Get("characterByCard", req =>
        {
            if (req.result != UnityWebRequest.Result.Success) error = req.error;
            else ids = ParseShallowKeys(req.downloadHandler.text);
        }, "shallow=true");
        if (ids == null)
        {
            onDone(null, error);
            yield break;
        }

        Dictionary<string, long> timestamps = new Dictionary<string, long>();
        Queue<string> pending = new Queue<string>(ids);
        int running = 0;
        while (pending.Count > 0 || running > 0)
        {
            while (pending.Count > 0 && running < ParallelRequests)
            {
                string id = pending.Dequeue();
                running++;
                host.StartCoroutine(FetchTimestamp(id, ts => { timestamps[id] = ts; running--; }));
            }
            yield return null;
        }

        // timestamp のないカード(古い形式)は最後に回す。同じ時刻ならIDで並べて順番を固定する
        ids.Sort((a, b) =>
        {
            long ta = timestamps.TryGetValue(a, out long x) && x > 0 ? x : long.MaxValue;
            long tb = timestamps.TryGetValue(b, out long y) && y > 0 ? y : long.MaxValue;
            int byTime = ta.CompareTo(tb);
            return byTime != 0 ? byTime : string.CompareOrdinal(a, b);
        });
        onDone(ids, null);
    }

    /// <summary>1枚分の登録内容を取得する。見つからない・失敗した時は onDone(null)。</summary>
    public static IEnumerator LoadRecord(string cardId, Action<FirebaseCharacterRecord> onDone)
    {
        FirebaseCharacterRecord record = null;
        yield return FirebaseRest.Get("characterByCard/" + cardId, req =>
        {
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"RegisteredCards: カード {cardId} を取得できませんでした: {req.error}");
                return;
            }
            string json = req.downloadHandler.text;
            if (string.IsNullOrEmpty(json) || json == "null") return;
            try
            {
                record = JsonUtility.FromJson<FirebaseCharacterRecord>(json);
            }
            catch (ArgumentException exception)
            {
                Debug.LogWarning($"RegisteredCards: カード {cardId} のデータを読めませんでした: {exception.Message}");
            }
        });
        onDone(record);
    }

    private static IEnumerator FetchTimestamp(string cardId, Action<long> onDone)
    {
        long timestamp = 0;
        yield return FirebaseRest.Get($"characterByCard/{cardId}/timestamp", req =>
        {
            if (req.result == UnityWebRequest.Result.Success) long.TryParse(req.downloadHandler.text, out timestamp);
        });
        onDone(timestamp);
    }

    private static List<string> ParseShallowKeys(string json)
    {
        List<string> keys = new List<string>();
        if (string.IsNullOrEmpty(json) || json.Trim() == "null") return keys;
        foreach (Match match in ShallowKey.Matches(json)) keys.Add(match.Groups[1].Value);
        return keys;
    }
}
