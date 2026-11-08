using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// カードごとの戦績(勝ち・負け・引き分け)。Firebase の /cardStats/{cardId} に保存する。
///
/// ・勝ち・負け・引き分けは、スマホ2台で戦った本番の試合だけを数える(CPU対戦・デモ対戦は数えない)
/// ・championships はトーナメント(登録カードのCPU対戦)で優勝した回数
/// ・加算は Firebase のサーバー側の increment で行うので、複数の卓が同時に書いても数がずれない
/// ・読み取りは Database Rules で auth != null に許可している(スマホの結果画面でも通算成績を出すため)
/// </summary>
public static class CardRecords
{
    [Serializable]
    public class Entry
    {
        public string cardId;
        public string characterName;
        public string element;
        public int wins;
        public int losses;
        public int draws;
        public int championships;
        public long lastPlayedAt;

        public int Played => wins + losses + draws;

        public string Summary => draws > 0 ? $"{wins}勝 {losses}敗 {draws}分" : $"{wins}勝 {losses}敗";

        /// <summary>優勝回数を含めた表示(優勝していなければ Summary と同じ)。</summary>
        public string SummaryWithTitles => championships > 0 ? $"{Summary}　<color=#F8CF70>優勝{championships}回</color>" : Summary;
    }

    private const string RootPath = "cardStats";
    private static readonly Regex EntryPattern = new Regex("\"([^\"]+)\"\\s*:\\s*(\\{[^{}]*\\})");

    /// <summary>試合結果を2枚分記録する。winnerSlot は player1 / player2 / draw。</summary>
    public static IEnumerator RecordResult(CharacterStats player1, CharacterStats player2, string winnerSlot)
    {
        yield return Record(player1, winnerSlot == "draw" ? "draws" : winnerSlot == "player1" ? "wins" : "losses");
        yield return Record(player2, winnerSlot == "draw" ? "draws" : winnerSlot == "player2" ? "wins" : "losses");
    }

    private static IEnumerator Record(CharacterStats stats, string field)
    {
        if (stats == null) yield break;
        yield return Increment(stats.cardId, stats.characterName, stats.element.ToString(), field);
    }

    /// <summary>トーナメントの優勝を記録する。</summary>
    public static IEnumerator RecordChampion(FirebaseCharacterRecord champion)
    {
        if (champion == null) yield break;
        yield return Increment(champion.cardId, champion.characterName, champion.element, "championships");
    }

    private static IEnumerator Increment(string cardId, string characterName, string element, string field)
    {
        if (string.IsNullOrEmpty(cardId)) yield break;
        string json =
            "{\"" + field + "\":{\".sv\":{\"increment\":1}}" +
            ",\"cardId\":" + JsonString(cardId) +
            ",\"characterName\":" + JsonString(characterName) +
            ",\"element\":" + JsonString(element) +
            ",\"lastPlayedAt\":{\".sv\":\"timestamp\"}}";
        yield return FirebaseRest.Write("PATCH", $"{RootPath}/{cardId}", json);
    }

    /// <summary>全カードの戦績を取得する(写真を含まないので軽い)。失敗時は onDone(null)。</summary>
    public static IEnumerator LoadAll(Action<List<Entry>> onDone)
    {
        List<Entry> entries = null;
        yield return FirebaseRest.Get(RootPath, req =>
        {
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("CardRecords: 戦績を取得できませんでした: " + req.error);
                return;
            }
            entries = new List<Entry>();
            string json = req.downloadHandler.text;
            if (string.IsNullOrEmpty(json) || json == "null") return;
            foreach (Match match in EntryPattern.Matches(json))
            {
                Entry entry = Parse(match.Groups[2].Value);
                if (entry == null) continue;
                if (string.IsNullOrEmpty(entry.cardId)) entry.cardId = match.Groups[1].Value;
                entries.Add(entry);
            }
        });
        onDone(entries);
    }

    /// <summary>1枚分の戦績を取得する。まだ対戦していなければ0勝0敗の Entry を返す。失敗時は onDone(null)。</summary>
    public static IEnumerator Load(string cardId, Action<Entry> onDone)
    {
        Entry entry = null;
        yield return FirebaseRest.Get($"{RootPath}/{cardId}", req =>
        {
            if (req.result != UnityWebRequest.Result.Success) return;
            string json = req.downloadHandler.text;
            entry = string.IsNullOrEmpty(json) || json == "null" ? new Entry { cardId = cardId } : Parse(json);
        });
        onDone(entry);
    }

    /// <summary>勝利数の多い順(同数なら優勝回数→勝率→対戦数の多い順)に並べる。勝利も優勝もないカードは除く。</summary>
    public static List<Entry> Ranking(List<Entry> entries, int count)
    {
        List<Entry> sorted = new List<Entry>(entries);
        sorted.RemoveAll(e => e.wins <= 0 && e.championships <= 0);
        sorted.Sort((a, b) =>
        {
            int byWins = b.wins.CompareTo(a.wins);
            if (byWins != 0) return byWins;
            int byTitles = b.championships.CompareTo(a.championships);
            if (byTitles != 0) return byTitles;
            int byRate = ((float)b.wins / Mathf.Max(1, b.Played)).CompareTo((float)a.wins / Mathf.Max(1, a.Played));
            return byRate != 0 ? byRate : b.Played.CompareTo(a.Played);
        });
        if (sorted.Count > count) sorted.RemoveRange(count, sorted.Count - count);
        return sorted;
    }

    private static Entry Parse(string json)
    {
        try
        {
            return JsonUtility.FromJson<Entry>(json);
        }
        catch (ArgumentException exception)
        {
            Debug.LogWarning("CardRecords: 戦績のデータを読めませんでした: " + exception.Message);
            return null;
        }
    }

    private static string JsonString(string value)
    {
        if (value == null) return "null";
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
