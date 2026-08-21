using UnityEngine;

/// <summary>
/// 6属性間の相性(ダメージ倍率)を計算するユーティリティ。
///
/// ElementType の定義順を円環として扱い、
/// 「次の2つの属性に強く、前の2つの属性に弱い」という関係を持たせています。
/// 例: Fire(炎) は Wind(風)・Dark(闇) に強く、Light(光)・Earth(土) に弱い。
/// 同属性同士、および円環上で正反対の位置にある属性同士は等倍(相性なし)。
///
/// 実際の相性表(どの属性がどの属性に強い/弱いか)がデザイン側で確定したら、
/// GetMultiplier の中身を「6x6の相性表」に置き換えるだけで対応できます。
/// </summary>
public static class ElementAffinity
{
    public const float AdvantageMultiplier = 1.5f;    // 有利属性へのダメージ倍率
    public const float DisadvantageMultiplier = 0.67f; // 不利属性へのダメージ倍率
    public const float NeutralMultiplier = 1.0f;       // 相性なし

    /// <summary>
    /// attacker が defender に攻撃する際のダメージ倍率を返す。
    /// </summary>
    public static float GetMultiplier(ElementType attacker, ElementType defender)
    {
        int total = System.Enum.GetValues(typeof(ElementType)).Length; // 6
        int diff = ((int)defender - (int)attacker + total) % total;
        if (diff == 1 || diff == 2) return AdvantageMultiplier;               // 攻撃側が有利
        if (diff == total - 1 || diff == total - 2) return DisadvantageMultiplier; // 攻撃側が不利
        return NeutralMultiplier; // diff == 0(同属性) または diff == 3(対角属性)
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