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

    /// <summary>
    /// スマホで撮影・背景切り抜きした実物の写真(バトル画面などでの表示用)。
    /// QRコードやFirebaseへのJSONシリアライズ対象ではなく、あくまで実行時にだけ
    /// (BattleCardIntakeなどが)セットする表示用データなので、[NonSerialized]にして
    /// ToJson()/FromJson()の対象から除外している。
    /// </summary>
    [NonSerialized]
    public Sprite photoSprite;

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
        attack = UnityEngine.Random.Range(10, 31);
        defense = UnityEngine.Random.Range(5, 21);
        speed = UnityEngine.Random.Range(5, 21);
        maxHp = 100;
        hp = maxHp;

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

        attack = rng.Next(10, 31);
        defense = rng.Next(5, 21);
        speed = rng.Next(5, 21);
        maxHp = 100;
        hp = maxHp;

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
    /// スキル(PowerBoost)を加味した実質攻撃力。バトル時のダメージ計算に使用。
    /// </summary>
    public int GetEffectiveAttack()
    {
        int value = attack;
        if (skill != null && skill.skillType == SkillType.PowerBoost)
        {
            value += Mathf.RoundToInt(speed * skill.ratio);
        }
        return value;
    }

    /// <summary>
    /// スキル(GuardBoost)を加味した実質防御力。バトル時のダメージ計算に使用。
    /// </summary>
    public int GetEffectiveDefense()
    {
        int value = defense;
        if (skill != null && skill.skillType == SkillType.GuardBoost)
        {
            value += Mathf.RoundToInt(speed * skill.ratio);
        }
        return value;
    }

    public override string ToString()
    {
        string mutationTag = isMutation ? "[突然変異] " : "";
        return $"{mutationTag}{characterName}\n" +
               $"属性:{element}  ATK:{attack} DEF:{defense} SPD:{speed} HP:{hp}\n" +
               $"スキル「{skill.skillName}」:{skill.GetDescription()}";
    }

    /// <summary>
    /// カード印刷用QRコードに埋め込むJSON文字列に変換する。
    /// </summary>
    public string ToJson()
    {
        return JsonUtility.ToJson(this);
    }

    /// <summary>
    /// QRコードから読み取ったJSON文字列をCharacterStatsに変換する。
    /// 形式が不正な場合はnullを返す(呼び出し側でnullチェックを行うこと)。
    /// </summary>
    public static CharacterStats FromJson(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            CharacterStats stats = JsonUtility.FromJson<CharacterStats>(json);

            if (stats == null || string.IsNullOrEmpty(stats.characterName))
            {
                return null;
            }

            if (stats.dataVersion > CurrentDataVersion)
            {
                Debug.LogWarning($"未知のデータバージョンです(dataVersion:{stats.dataVersion})。読み取り結果が正しく表示されない可能性があります。");
            }

            return stats;
        }
        catch (Exception e)
        {
            Debug.LogError("QRコードのデータ解析に失敗しました: " + e.Message);
            return null;
        }
    }
}
