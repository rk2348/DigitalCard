using System;
using Orisamo.Data;

namespace Orisamo.CardGeneration
{
    /// <summary>
    /// 画像特徴量からスキルを自動生成する。
    /// スキル名は暫定のワードバンクから組み合わせる方式。
    /// effectFormulaはSkillEffectResolverに登録済みのIDを指定するため、
    /// 新しい効果種別を増やす場合は両方のクラスに追加すること。
    /// </summary>
    public class SkillGenerator
    {
        private readonly System.Random _rng;

        private static readonly string[] Adjectives =
        {
            "疾風の", "剛力の", "深緑の", "煌めく", "静寂の", "灼熱の", "氷結の", "刹那の"
        };

        private static readonly string[] Nouns =
        {
            "一撃", "加護", "爪", "咆哮", "羽ばたき", "波動", "閃光", "牙"
        };

        // SkillEffectResolver（Data/SkillEffectResolver.cs）に登録済みのeffectFormula ID一覧
        private static readonly string[] EffectFormulas =
        {
            "add_ref_stat_flat",
            "multiply_if_ref_stat_high"
        };

        public SkillGenerator(System.Random rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        public SkillData GenerateSkill(ImageFeatures features, CardAttribute attribute)
        {
            var referenceStat = PickReferenceStat(features);
            var effectFormula = EffectFormulas[_rng.Next(EffectFormulas.Length)];
            var skillName = Adjectives[_rng.Next(Adjectives.Length)] + Nouns[_rng.Next(Nouns.Length)];

            return new SkillData
            {
                skillId = Guid.NewGuid().ToString("N"),
                skillName = skillName,
                referenceStat = referenceStat,
                effectFormula = effectFormula,
                description = BuildDescription(skillName, referenceStat)
            };
        }

        /// <summary>最も強く出た特徴量に対応するステータスを参照ステータスとする。</summary>
        private static StatType PickReferenceStat(ImageFeatures features)
        {
            if (features.lineComplexity >= features.colorVariety && features.lineComplexity >= features.averageSaturation)
                return StatType.AttackPower;

            if (features.colorVariety >= features.averageSaturation)
                return StatType.Speed;

            return StatType.Hp;
        }

        private static string BuildDescription(string skillName, StatType referenceStat)
        {
            string statLabel = referenceStat switch
            {
                StatType.Hp => "体力",
                StatType.Speed => "素早さ",
                StatType.AttackPower => "攻撃力",
                _ => "ステータス"
            };
            return $"{skillName}：自身の{statLabel}に応じて効果が変化する。";
        }
    }
}
