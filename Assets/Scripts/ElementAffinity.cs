/// <summary>
/// 6属性間の相性(ダメージ倍率)を計算するユーティリティ。
///
/// ElementType の定義順を円環として扱い、
/// 「次の2つの属性に強く、前の2つの属性に弱い」という関係を持たせています。
/// 例: Fire(炎) は Wind(風)・Thunder(雷) に強く、Light(光)・Earth(土) に弱い。
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
}
