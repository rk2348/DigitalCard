using System;
using System.Collections.Generic;

/// <summary>
/// 登録カードどうしのトーナメント(勝ち抜き戦)の組み合わせと進行状況。試合はすべてCPU同士で行う。
///
/// ・人数は 4 / 8 / 16 人。登録カードが足りない時は、全員が入る一番小さい人数に縮め、空いた枠は不戦勝(シード)にする
/// ・出場カードは「一覧で選んだカード」＋「足りない分を残りの登録カードからランダムに補充」。同じカードは2回出ない
/// ・組み合わせは抽選(シャッフル)で決める。不戦勝の枠どうしが当たらないよう、1回戦の各試合に最大1つだけ置く
/// ・バトルシーンは1試合ごとに読み込み直すので、状態は Current(static)に置いてシーンをまたいで引き継ぐ
///
/// Rounds[r][i] は r 回戦(0始まり)の i 番目の枠に入っている出場者の番号(Entrants の添字)。
/// 最後の Rounds[RoundCount][0] が優勝者。Undecided はまだ決まっていない枠、Bye は不戦勝の空き枠。
/// </summary>
public sealed class Tournament
{
    public const int Undecided = -1;
    public const int Bye = -2;
    public static readonly int[] Sizes = { 4, 8, 16 };

    /// <summary>進行中のトーナメント(なければnull)。</summary>
    public static Tournament Current;

    public readonly int Size;
    public readonly List<FirebaseCharacterRecord> Entrants;
    public readonly int[][] Rounds;

    /// <summary>直前に決着した試合(組み合わせ表で勝ち上がりを見せるため)。なければ round = -1。</summary>
    public int LastRound { get; private set; } = -1;
    public int LastMatch { get; private set; } = -1;

    public int RoundCount => Rounds.Length - 1;
    public bool IsFinished => Rounds[RoundCount][0] >= 0;
    public FirebaseCharacterRecord Champion => IsFinished ? Entrants[Rounds[RoundCount][0]] : null;

    private Tournament(int size, List<FirebaseCharacterRecord> entrants, int[] firstRound)
    {
        Size = size;
        Entrants = entrants;
        int rounds = 0;
        while ((1 << rounds) < size) rounds++;
        Rounds = new int[rounds + 1][];
        Rounds[0] = firstRound;
        for (int r = 1; r <= rounds; r++)
        {
            Rounds[r] = new int[size >> r];
            for (int i = 0; i < Rounds[r].Length; i++) Rounds[r][i] = Undecided;
        }
        AdvanceByes();
    }

    /// <summary>
    /// トーナメントを作る。picked は一覧で選んだカード、pool は登録カード全体(補充用)。
    /// 出場者が2人に満たない時は null。
    /// </summary>
    public static Tournament Create(List<FirebaseCharacterRecord> picked, List<FirebaseCharacterRecord> pool, int requestedSize, Random random)
    {
        // 選んだカード(重複を除く)
        List<FirebaseCharacterRecord> entrants = new List<FirebaseCharacterRecord>();
        HashSet<string> used = new HashSet<string>();
        foreach (FirebaseCharacterRecord record in picked)
        {
            if (record != null && used.Add(record.cardId) && entrants.Count < requestedSize) entrants.Add(record);
        }

        // 足りない分は、まだ選ばれていない登録カードからランダムに補充する
        List<FirebaseCharacterRecord> rest = pool.FindAll(r => r != null && !used.Contains(r.cardId));
        Shuffle(rest, random);
        foreach (FirebaseCharacterRecord record in rest)
        {
            if (entrants.Count >= requestedSize) break;
            if (used.Add(record.cardId)) entrants.Add(record);
        }
        if (entrants.Count < 2) return null;

        // 全員が入る一番小さい人数に合わせる(例: 5人しかいなければ8人の表にして3枠を不戦勝にする)
        int size = requestedSize;
        foreach (int candidate in Sizes)
        {
            if (candidate >= entrants.Count && candidate < size) size = candidate;
        }

        // 抽選: 出場者を並べ替え、不戦勝は1回戦の各試合に1つまでになるよう、先頭の試合から順に置く
        Shuffle(entrants, random);
        int byes = size - entrants.Count;
        int[] firstRound = new int[size];
        int next = 0;
        for (int match = 0; match < size / 2; match++)
        {
            firstRound[match * 2] = next++;
            firstRound[match * 2 + 1] = match < byes ? Bye : next++;
        }
        // 不戦勝の試合が表の上半分に偏らないよう、試合の並びもシャッフルする
        int[] order = new int[size / 2];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        Shuffle(order, random);
        int[] shuffled = new int[size];
        for (int i = 0; i < order.Length; i++)
        {
            shuffled[i * 2] = firstRound[order[i] * 2];
            shuffled[i * 2 + 1] = firstRound[order[i] * 2 + 1];
        }
        return new Tournament(size, entrants, shuffled);
    }

    /// <summary>次に行う試合。すべて終わっていれば false。</summary>
    public bool TryGetNextMatch(out int round, out int match)
    {
        for (round = 0; round < RoundCount; round++)
        {
            int[] slots = Rounds[round];
            for (match = 0; match < slots.Length / 2; match++)
            {
                if (Rounds[round + 1][match] != Undecided) continue;
                if (slots[match * 2] >= 0 && slots[match * 2 + 1] >= 0) return true;
            }
        }
        round = match = -1;
        return false;
    }

    public FirebaseCharacterRecord EntrantAt(int round, int slot)
    {
        int index = Rounds[round][slot];
        return index >= 0 ? Entrants[index] : null;
    }

    /// <summary>試合の結果を記録する。firstWon は表の上側(偶数番目の枠)の出場者が勝ったかどうか。</summary>
    public void ReportWinner(int round, int match, bool firstWon)
    {
        Rounds[round + 1][match] = Rounds[round][match * 2 + (firstWon ? 0 : 1)];
        LastRound = round;
        LastMatch = match;
        AdvanceByes();
    }

    /// <summary>相手が不戦勝の空き枠なら、試合をせずに勝ち上がらせる。</summary>
    private void AdvanceByes()
    {
        for (int r = 0; r < RoundCount; r++)
        {
            for (int m = 0; m < Rounds[r].Length / 2; m++)
            {
                if (Rounds[r + 1][m] != Undecided) continue;
                int a = Rounds[r][m * 2], b = Rounds[r][m * 2 + 1];
                if (a == Bye && b == Bye) Rounds[r + 1][m] = Bye;
                else if (a >= 0 && b == Bye) Rounds[r + 1][m] = a;
                else if (b >= 0 && a == Bye) Rounds[r + 1][m] = b;
            }
        }
    }

    /// <summary>その回戦の呼び名(決勝・準決勝・準々決勝・○回戦)。</summary>
    public string RoundName(int round)
    {
        switch (RoundCount - 1 - round)
        {
            case 0: return "決勝";
            case 1: return "準決勝";
            case 2: return "準々決勝";
            default: return $"{round + 1}回戦";
        }
    }

    /// <summary>その回戦の中で、実際に戦う試合の通し番号(1始まり。不戦勝の組は数えない)。</summary>
    public int MatchNumber(int round, int match)
    {
        int number = 0;
        for (int m = 0; m <= match; m++)
        {
            if (Rounds[round][m * 2] != Bye && Rounds[round][m * 2 + 1] != Bye) number++;
        }
        return Math.Max(1, number);
    }

    private static void Shuffle<T>(IList<T> list, Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
