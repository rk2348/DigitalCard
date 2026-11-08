using System;
using UnityEngine;

/// <summary>
/// スキルの種類。いずれも「他のステータスを参照して効果を発揮する」設計書の要件を反映。
/// 名称・効果内容は仮のものなので、実際の仕様に合わせて自由に追加/変更してください。
/// ※Firebaseには名前(文字列)で保存されるので、項目名を変えると登録済みカードが読めなくなる。
///   追加は末尾に足していけば既存カードに影響しない。スマホ側(docs/app.js の SKILL_TYPES)にも同じ名前で足すこと。
/// </summary>
public enum SkillType
{
    PowerBoost,     // 素早さの一部を攻撃力に上乗せ(常時)
    GuardBoost,     // 素早さの一部を防御力に上乗せ(常時)
    LifeDrain,      // 必殺技で与えたダメージの一部を体力に回復
    Overdrive,      // 必殺技で攻撃力に応じた追加ダメージ
    IronFist,       // 鉄拳の構え: 防御力の一部を攻撃力に上乗せ(常時)
    Desperation,    // 背水の陣: 減ったHPの一部を攻撃力に上乗せ(常時)
    ElementMastery, // 属性の極み: 有利属性への攻撃のダメージアップ
    FirstStrike,    // 先手必勝: 最初に当てた攻撃のダメージアップ(1回)
    Evasion,        // 見切り: 相手より素早いとき受けるダメージを軽減
    Counter,        // 反撃の構え: 受けたダメージの一部を相手に返す
    Shield,         // 守護結界: 戦闘開始時、防御力に応じたダメージを防ぐ結界を張る
    Regeneration,   // 自然治癒: 自分の攻撃ターンの終わりに防御力に応じて回復
    Intimidate,     // 威圧: 戦闘開始時、攻撃力に応じて相手の防御力を下げる
    Endurance       // 不屈の魂: 倒れる攻撃を1回だけ耐え、最大HPの一部で踏みとどまる
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
        skillName = GetSkillName(type);
    }

    public static string GetSkillName(SkillType type)
    {
        switch (type)
        {
            case SkillType.PowerBoost: return "疾風の一撃";
            case SkillType.GuardBoost: return "俊敏なる守り";
            case SkillType.LifeDrain: return "生命吸収";
            case SkillType.Overdrive: return "渾身の一打";
            case SkillType.IronFist: return "鉄拳の構え";
            case SkillType.Desperation: return "背水の陣";
            case SkillType.ElementMastery: return "属性の極み";
            case SkillType.FirstStrike: return "先手必勝";
            case SkillType.Evasion: return "見切り";
            case SkillType.Counter: return "反撃の構え";
            case SkillType.Shield: return "守護結界";
            case SkillType.Regeneration: return "自然治癒";
            case SkillType.Intimidate: return "威圧";
            case SkillType.Endurance: return "不屈の魂";
            default: return "スキル";
        }
    }

    /// <summary>
    /// 実際に効果へ掛ける倍率。ratio(0.2〜0.5)のままだと強すぎる/弱すぎるスキルはここで調整する。
    /// 説明文の%表示もこの値から作るので、表示と実際の効果は常に一致する。
    /// (スマホ側 docs/app.js の skillEffectRatio と同じ式にしておくこと)
    /// </summary>
    public float EffectRatio
    {
        get
        {
            switch (skillType)
            {
                case SkillType.Desperation:
                case SkillType.Evasion:
                case SkillType.Endurance:
                    return ratio * 0.5f;  // 10〜25%
                case SkillType.FirstStrike:
                    return ratio * 2f;    // 40〜100%(1回限りなので大きめ)
                case SkillType.Shield:
                    return ratio * 3f;    // 防御力の60〜150%
                default:
                    return ratio;         // 20〜50%
            }
        }
    }

    /// <summary>
    /// UIやログ表示用の説明文を生成する。
    /// </summary>
    public string GetDescription()
    {
        int percent = Mathf.RoundToInt(EffectRatio * 100);
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
            case SkillType.IronFist:
                return $"防御力の{percent}%を攻撃力に加算";
            case SkillType.Desperation:
                return $"減ったHPの{percent}%を攻撃力に加算";
            case SkillType.ElementMastery:
                return $"有利な属性への攻撃のダメージが{percent}%アップ";
            case SkillType.FirstStrike:
                return $"最初に当てた攻撃のダメージが{percent}%アップ";
            case SkillType.Evasion:
                return $"相手より素早いとき、受けるダメージを{percent}%軽減";
            case SkillType.Counter:
                return $"受けたダメージの{percent}%を相手に返す";
            case SkillType.Shield:
                return $"戦闘開始時、防御力の{percent}%分のダメージを防ぐ結界を張る";
            case SkillType.Regeneration:
                return $"自分の攻撃ターンの終わりに、防御力の{percent}%回復";
            case SkillType.Intimidate:
                return $"戦闘開始時、攻撃力の{percent}%分だけ相手の防御力を下げる";
            case SkillType.Endurance:
                return $"倒れる攻撃を1回だけ耐え、最大HPの{percent}%で踏みとどまる";
            default:
                return "";
        }
    }
}
