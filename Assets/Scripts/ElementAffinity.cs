using UnityEngine;

/// <summary>
/// 6属性間の相性(ダメージ倍率)を計算するユーティリティ。
///
/// 相性は2つのグループに分かれる:
///   ・火 → 風 → 地 → 水 → 火 の4すくみ(矢印の先に強い。例: 火は風に強く、水に弱い)
///   ・光 と 闇 はお互いに強い(どちらから攻撃しても1.5倍)
/// それ以外の組み合わせ(同属性、火と地、光と火など)は等倍。
/// 相性表を変える時は Table の中身を書き換えるだけでよい。
/// </summary>
public static class ElementAffinity
{
    public const float AdvantageMultiplier = 1.5f;    // 有利属性へのダメージ倍率
    public const float DisadvantageMultiplier = 0.67f; // 不利属性へのダメージ倍率
    public const float NeutralMultiplier = 1.0f;       // 相性なし

    private const float A = AdvantageMultiplier;
    private const float D = DisadvantageMultiplier;
    private const float N = NeutralMultiplier;

    /// <summary>[攻撃側, 防御側] のダメージ倍率。並びは ElementType の定義順(火・風・闇・水・地・光)。</summary>
    private static readonly float[,] Table =
    {
        //            火  風  闇  水  地  光   ← 防御側
        /* 火 */    { N,  A,  N,  D,  N,  N },
        /* 風 */    { D,  N,  N,  N,  A,  N },
        /* 闇 */    { N,  N,  N,  N,  N,  A },
        /* 水 */    { A,  N,  N,  N,  D,  N },
        /* 地 */    { N,  D,  N,  A,  N,  N },
        /* 光 */    { N,  N,  A,  N,  N,  N },
    };

    /// <summary>
    /// attacker が defender に攻撃する際のダメージ倍率を返す。
    /// </summary>
    public static float GetMultiplier(ElementType attacker, ElementType defender)
    {
        return Table[(int)attacker, (int)defender];
    }

    /// <summary>対戦前の案内用: 「火 は 風 に強い」のような説明。相性がなければ null。</summary>
    public static string DescribeMatchup(ElementType a, ElementType b)
    {
        float ab = GetMultiplier(a, b);
        float ba = GetMultiplier(b, a);
        string ka = OrisamoUI.ElementKanji(a), kb = OrisamoUI.ElementKanji(b);
        if (ab > N && ba > N) return $"{ka} と {kb} はお互いに強い！どちらの攻撃も効果ばつぐん";
        if (ab > N) return $"{ka} は {kb} に強い！";
        if (ba > N) return $"{kb} は {ka} に強い！";
        return null;
    }

    /// <summary>
    /// UI表示用に「有利/不利/互角」のテキストを返す。
    /// </summary>
    public static string GetMultiplierLabel(float multiplier)
    {
        if (multiplier > NeutralMultiplier) return "効果はばつぐんだ！";
        if (multiplier < NeutralMultiplier) return "効果はいまひとつ…";
        return "";
    }

    /// <summary>
    /// 属性ごとの代表色を返す(3Dモデルの着色やHPバーの色分けなどに使用)。
    /// カードデザインの属性イメージ(火・風・闇・水・地・光)に合わせた配色。
    /// </summary>
    public static Color GetElementColor(ElementType element)
    {
        switch (element)
        {
            case ElementType.Fire: return new Color(0.90f, 0.30f, 0.20f); // 赤
            case ElementType.Wind: return new Color(0.40f, 0.80f, 0.45f); // 黄緑
            case ElementType.Dark: return new Color(0.35f, 0.20f, 0.45f); // 紫
            case ElementType.Water: return new Color(0.25f, 0.55f, 0.90f); // 青
            case ElementType.Earth: return new Color(0.60f, 0.45f, 0.25f); // 茶
            case ElementType.Light: return new Color(0.95f, 0.85f, 0.35f); // 黄金
            default: return Color.gray;
        }
    }
}