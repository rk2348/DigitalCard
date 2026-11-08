using System;
using UnityEngine;

/// <summary>
/// キャラクター1体分のステータスを保持するクラス。
/// GameManager経由でシーンをまたいで保持される。
///
/// 設計書(オリサモ企画書)の以下の仕様に対応：
/// ・6属性によるダメージ増減(相性)
/// ・イラスト解析によるステータス自動生成(現状は乱数で代替)
/// ・他ステータスを参照するスキルの自動生成
/// ・「突然変異」によるレアカード出現
/// </summary>
[Serializable]
public class CharacterStats
{
    public string characterName;

    // 基本ステータス
    public int attack;   // 攻撃力
    public int defense;  // 防御力
    public int speed;    // 素早さ
    public int hp;        // 現在HP
    public int maxHp;     // 最大HP(回復時の上限として使用)

    // 属性・スキル
    public ElementType element;
    public CharacterSkill skill;

    // 突然変異(レアカード)かどうか
    public bool isMutation;

    // 必殺技レベル(強/普/弱のうち1つ)。このレベルで攻撃が命中すると追加倍率とスキル効果が発動する。
    public AttackLevel specialLevel;

    // ---- バトル中だけ使う状態(保存しない。試合開始時に ResetBattleState で初期化) ----
    [NonSerialized] public int shieldHp;          // 守護結界の残り(ダメージを肩代わりする量)
    [NonSerialized] public int defenseDebuff;     // 相手の威圧で下げられた防御力
    [NonSerialized] public bool firstStrikeUsed;  // 先手必勝を使ったか
    [NonSerialized] public bool enduranceUsed;    // 不屈の魂を使ったか
    [NonSerialized] public bool desperationShown; // 背水の陣の発動演出を見せたか

    /// <summary>試合開始時に、バトル中だけの状態をまっさらに戻す。</summary>
    public void ResetBattleState()
    {
        shieldHp = 0;
        defenseDebuff = 0;
        firstStrikeUsed = false;
        enduranceUsed = false;
        desperationShown = false;
    }

    /// <summary>指定したスキルを持っているか。</summary>
    public bool HasSkill(SkillType type)
    {
        return skill != null && skill.skillType == type;
    }

    /// <summary>
    /// スマホで撮影・背景切り抜きした実物の写真(バトル画面などでの表示用)。
    /// QRコードやFirebaseへのJSONシリアライズ対象ではなく、あくまで実行時にだけ
    /// (BattleCardIntakeなどが)セットする表示用データなので、[NonSerialized]にして
    /// ToJson()/FromJson()の対象から除外している。
    /// </summary>
    [NonSerialized]
    public Sprite photoSprite;

    /// <summary>登録カードのID(Firebaseから呼び出したキャラクターのみ。戦績の記録に使う)。デモ用のキャラクターはnull。</summary>
    [NonSerialized]
    public string cardId;

    /// <summary>
    /// QRコードのデータ形式バージョン。
    /// 将来ステータス構造(属性やスキルの種類など)を変更した際に、
    /// 古いカードのQRを読んだ時の互換性チェックに使用する。
    /// </summary>
    public const int CurrentDataVersion = 1;
    public int dataVersion = CurrentDataVersion;

    private const float MutationChance = 0.05f;   // 突然変異の発生確率(5%)
    private const float MutationMultiplier = 1.5f; // 突然変異時のステータス倍率

    /// <summary>
    /// JsonUtilityでの復元(QRコード読み取り時)に必要なパラメータ無しコンストラクタ。
    /// </summary>
    public CharacterStats() { }

    public CharacterStats(string name) : this()
    {
        characterName = name;
        maxHp = 100;
        hp = maxHp;
    }

    /// <summary>
    /// ステータス・属性・スキルをすべてランダムに割り振る。
    /// 将来的にはここをAI画像解析の結果を使って算出するロジックに差し替える想定。
    /// </summary>
    public void AssignRandomStats()
    {
        // 攻撃・防御・素早さは合計の強さをそろえて配分する(スマホ側 docs/app.js と同じ式)
        speed = UnityEngine.Random.Range(5, 21);
        AllocateAttackDefense(UnityEngine.Random.value);
        maxHp = 100;
        hp = maxHp;
        specialLevel = (AttackLevel)UnityEngine.Random.Range(0, 3);

        // 6属性からランダムに1つ決定
        int elementCount = Enum.GetValues(typeof(ElementType)).Length;
        element = (ElementType)UnityEngine.Random.Range(0, elementCount);

        // 他ステータスを参照するスキルをランダムに生成
        int skillCount = Enum.GetValues(typeof(SkillType)).Length;
        SkillType randomSkillType = (SkillType)UnityEngine.Random.Range(0, skillCount);
        float ratio = UnityEngine.Random.Range(0.2f, 0.5f);
        skill = new CharacterSkill(randomSkillType, ratio);

        // 突然変異(レアカード)判定
        isMutation = UnityEngine.Random.value < MutationChance;
        if (isMutation)
        {
            ApplyMutation();
        }
    }

    // 攻撃・防御に配分する合計(攻撃+1 = 防御+2)。対戦シミュレーションで、どの振り分けでも勝率が
    // ほぼ同じ(大半が40〜60%)になるよう調整した値。素早さは先攻を決めるだけなので少しだけ差し引く
    // (2026-11 スキル・属性込みのシミュレーションで再調整。1試合が平均13回ほどの攻防で決着するように)
    private const int AttackBase = 17;
    private const float StatBudget = 16f;
    private const float DefenseCost = 0.5f;
    private const float SpeedCost = 0.18f;

    /// <summary>speedを決めた後に呼ぶ。roll(0〜1)で攻撃に回す割合(25〜75%)を決め、残りを防御に回す。</summary>
    private void AllocateAttackDefense(float roll)
    {
        float budget = StatBudget - (speed - 5) * SpeedCost;
        float attackShare = 0.25f + roll * 0.5f;
        attack = AttackBase + (int)Math.Round(budget * attackShare, MidpointRounding.AwayFromZero);
        defense = 5 + (int)Math.Round(budget * (1f - attackShare) / DefenseCost, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 突然変異発生時、ランダムな1ステータスを大幅強化する。
    /// </summary>
    private void ApplyMutation()
    {
        int roll = UnityEngine.Random.Range(0, 3);
        switch (roll)
        {
            case 0: attack = Mathf.RoundToInt(attack * MutationMultiplier); break;
            case 1: defense = Mathf.RoundToInt(defense * MutationMultiplier); break;
            case 2: speed = Mathf.RoundToInt(speed * MutationMultiplier); break;
        }
        characterName = "★" + characterName; // レアカードの目印
    }

    /// <summary>
    /// 【QRコード運用の変更に伴い追加】
    /// 指定したseed値をもとにステータス・属性・スキルを決定する。
    /// QRコード作成時点ではキャラクターの中身を一切決めず、QRコードには
    /// このseed値だけを埋め込んでおき、QRコードを読み取った瞬間に初めて
    /// このメソッドでキャラクターの中身を確定させる、という流れを想定している。
    /// 同じseedからは常に同じ結果が再現される(UnityEngine.Randomのグローバルな
    /// 状態には影響を与えないよう、System.Randomを使用している)。
    /// characterNameは事前にコンストラクタ等で設定しておくこと(このメソッドでは変更しない。
    /// ただし突然変異が発生した場合は先頭に「★」が付与される)。
    /// </summary>
    public void AssignRandomStats(int seed)
    {
        System.Random rng = new System.Random(seed);

        // 攻撃・防御・素早さは合計の強さをそろえて配分する(スマホ側 docs/app.js と同じ式)
        speed = rng.Next(5, 21);
        AllocateAttackDefense((float)rng.NextDouble());
        maxHp = 100;
        hp = maxHp;
        specialLevel = BattleRules.ComputeSpecialLevel(seed);

        // 6属性からランダムに1つ決定
        int elementCount = Enum.GetValues(typeof(ElementType)).Length;
        element = (ElementType)rng.Next(0, elementCount);

        // 他ステータスを参照するスキルをランダムに生成
        int skillCount = Enum.GetValues(typeof(SkillType)).Length;
        SkillType randomSkillType = (SkillType)rng.Next(0, skillCount);
        float ratio = 0.2f + (float)rng.NextDouble() * 0.3f; // 0.2〜0.5の範囲
        skill = new CharacterSkill(randomSkillType, ratio);

        // 突然変異(レアカード)判定
        isMutation = rng.NextDouble() < MutationChance;
        if (isMutation)
        {
            ApplyMutation(rng);
        }
    }

    /// <summary>AssignRandomStats(int seed) 用。System.Randomを使うバージョン。</summary>
    private void ApplyMutation(System.Random rng)
    {
        int roll = rng.Next(0, 3);
        switch (roll)
        {
            case 0: attack = Mathf.RoundToInt(attack * MutationMultiplier); break;
            case 1: defense = Mathf.RoundToInt(defense * MutationMultiplier); break;
            case 2: speed = Mathf.RoundToInt(speed * MutationMultiplier); break;
        }
        characterName = "★" + characterName; // レアカードの目印
    }

    /// <summary>
    /// スキル(PowerBoost / IronFist / Desperation)を加味した実質攻撃力。バトル時のダメージ計算に使用。
    /// </summary>
    public int GetEffectiveAttack()
    {
        int value = attack;
        if (HasSkill(SkillType.PowerBoost))
        {
            value += Mathf.RoundToInt(speed * skill.EffectRatio);
        }
        else if (HasSkill(SkillType.IronFist))
        {
            value += Mathf.RoundToInt(defense * skill.EffectRatio);
        }
        else if (HasSkill(SkillType.Desperation))
        {
            value += Mathf.RoundToInt(Mathf.Max(0, maxHp - hp) * skill.EffectRatio);
        }
        return value;
    }

    /// <summary>
    /// スキル(GuardBoost)と相手の威圧を加味した実質防御力。バトル時のダメージ計算に使用。
    /// </summary>
    public int GetEffectiveDefense()
    {
        int value = defense;
        if (HasSkill(SkillType.GuardBoost))
        {
            value += Mathf.RoundToInt(speed * skill.EffectRatio);
        }
        return Mathf.Max(0, value - defenseDebuff); // 威圧で下げられた分を引く
    }

    /// <summary>
    /// バトル用にこのキャラクターの複製を作成する。
    /// HPは最大HPまで全回復した状態になる（コレクション本体のHPは変化しない）。
    /// </summary>
    public CharacterStats Clone()
    {
        CharacterStats clone = new CharacterStats(characterName)
        {
            attack = attack,
            defense = defense,
            speed = speed,
            maxHp = maxHp,
            hp = maxHp,
            element = element,
            skill = skill != null ? new CharacterSkill(skill.skillType, skill.ratio) : null,
            isMutation = isMutation,
            specialLevel = specialLevel,
            dataVersion = dataVersion,
            photoSprite = photoSprite,
            cardId = cardId
        };
        return clone;
    }

    public override string ToString()
    {
        string mutationTag = isMutation ? "[突然変異] " : "";
        string skillText = skill != null ? $"スキル「{skill.skillName}」:{skill.GetDescription()}" : "スキル未設定";
        return $"{mutationTag}{characterName}\n" +
               $"属性:{element}  ATK:{attack} DEF:{defense} SPD:{speed} HP:{hp}\n" +
               $"{skillText}";
    }
}
