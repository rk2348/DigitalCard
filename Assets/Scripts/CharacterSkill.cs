using System;
using UnityEngine;

/// <summary>
/// スキルの種類。いずれも「他のステータスを参照して効果を発揮する」設計書の要件を反映。
/// 名称・効果内容は仮のものなので、実際の仕様に合わせて自由に追加/変更してください。
/// </summary>
public enum SkillType
{
    PowerBoost, // 素早さの一部を攻撃力に上乗せ
    GuardBoost, // 素早さの一部を防御力に上乗せ
    LifeDrain,  // 与えたダメージの一部を体力に回復
    Overdrive   // 攻撃力に応じて追加ダメージを与える
}

/// <summary>
/// キャラクターが持つスキル1つ分のデータ。
/// イラスト解析からステータスと一緒に自動生成される想定。
/// </summary>
[Serializable]
public class CharacterSkill
{
    public string skillName;
    public SkillType skillType;

    [Range(0.1f, 0.6f)]
    public float ratio; // 参照するステータスに対する倍率(20%〜50%程度を想定)

    /// <summary>
    /// JsonUtilityでの復元(QRコード読み取り時)に必要なパラメータ無しコンストラクタ。
    /// </summary>
    public CharacterSkill() { }

    public CharacterSkill(SkillType type, float ratio)
    {
        this.skillType = type;
        this.ratio = ratio;
        skillName = GenerateSkillName(type);
    }

    private string GenerateSkillName(SkillType type)
    {
        switch (type)
        {
            case SkillType.PowerBoost: return "疾風の一撃";
            case SkillType.GuardBoost: return "俊敏なる守り";
            case SkillType.LifeDrain: return "生命吸収";
            case SkillType.Overdrive: return "渾身の一打";
            default: return "スキル";
        }
    }

    /// <summary>
    /// UIやログ表示用の説明文を生成する。
    /// </summary>
    public string GetDescription()
    {
        int percent = Mathf.RoundToInt(ratio * 100);
        switch (skillType)
        {
            case SkillType.PowerBoost:
                return $"素早さの{percent}%を攻撃力に加算";
            case SkillType.GuardBoost:
                return $"素早さの{percent}%を防御力に加算";
            case SkillType.LifeDrain:
                return $"与えたダメージの{percent}%を体力に回復";
            case SkillType.Overdrive:
                return $"攻撃力の{percent}%分、追加ダメージを与える";
            default:
                return "";
        }
    }
}
