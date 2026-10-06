using System;

/// <summary>
/// 対戦ルールの計算部分(UnityEngineに依存しない純粋な計算)。
/// BattleManagerは演出と進行だけを担い、数値の決め方はすべてここに集約する。
/// ルールの根拠は deliverables/ORISAMO_システム設計書 の第6章を参照。
/// </summary>
public static class BattleRules
{
    /// <summary>基礎ダメージの下限。低ATK対高DEFで試合が終わらなくなるのを防ぐ。</summary>
    public const int MinBaseDamage = 5;

    /// <summary>基礎ダメージの計算で、防御側のDEFに掛ける係数。</summary>
    public const float DefenseFactor = 0.5f;

    /// <summary>この回数の攻防で決着しなければ、残りHPの割合で勝敗を判定する。</summary>
    public const int MaxExchanges = 30;

    /// <summary>必殺技レベルの算出で、能力値用の乱数列と重ならないようにシードに混ぜる値。</summary>
    public const uint SpecialLevelSalt = 0x5bd1e995u;

    /// <summary>
    /// 命中時の最終ダメージを求める(防御成功の判定は呼び出し側で先に行う)。
    /// 基礎ダメージ = max(MinBaseDamage, 実効ATK − 実効DEF × DefenseFactor)
    /// → 属性倍率 → 強さ倍率 → 必殺技倍率 → (必殺技かつOverdriveなら ATK × ratio を加算) → 四捨五入(最低1)
    /// </summary>
    public static int CalculateDamage(int effectiveAttack, int effectiveDefense, float elementMultiplier,
        float levelMultiplier, bool isSpecial, float specialMultiplier, float overdriveBonus)
    {
        float baseDamage = Math.Max(MinBaseDamage, effectiveAttack - effectiveDefense * DefenseFactor);
        float damage = baseDamage * elementMultiplier * levelMultiplier * (isSpecial ? specialMultiplier : 1f);
        if (isSpecial) damage += overdriveBonus;
        return Math.Max((int)Math.Round(damage), 1);
    }

    /// <summary>
    /// 先攻を決める。SPDが高い方が先攻、同値なら tieBreakRoll(0〜1の乱数) で無作為に決める。
    /// 戻り値が true ならプレイヤー1が先攻。
    /// </summary>
    public static bool Player1AttacksFirst(int player1Speed, int player2Speed, double tieBreakRoll)
    {
        if (player1Speed != player2Speed) return player1Speed > player2Speed;
        return tieBreakRoll < 0.5;
    }

    /// <summary>
    /// 攻防回数の上限に達した時の判定。残りHPの割合が高い方が勝ち。
    /// 戻り値: 1 = プレイヤー1の勝ち、2 = プレイヤー2の勝ち、0 = 引き分け
    /// </summary>
    public static int JudgeByHpRatio(int player1Hp, int player1MaxHp, int player2Hp, int player2MaxHp)
    {
        // 割合の比較を整数の交差乗算で行い、浮動小数の誤差で引き分けを取り逃がさないようにする
        long left = (long)Math.Max(player1Hp, 0) * Math.Max(player2MaxHp, 1);
        long right = (long)Math.Max(player2Hp, 0) * Math.Max(player1MaxHp, 1);
        if (left > right) return 1;
        if (right > left) return 2;
        return 0;
    }

    /// <summary>
    /// カードのシード値から必殺技レベルを決める(app.js の computeSpecialLevel と同じ結果になる)。
    /// 名前に依存しないため、同じカードは名前を変えても必殺技レベルが変わらない。
    /// </summary>
    public static AttackLevel ComputeSpecialLevel(int seed)
    {
        SeededRandom rng = new SeededRandom(unchecked((uint)seed) ^ SpecialLevelSalt);
        return (AttackLevel)rng.NextInt(0, 3);
    }
}
