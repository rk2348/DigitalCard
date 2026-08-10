using System.Collections.Generic;
using UnityEngine;

namespace Orisamo.Data
{
    /// <summary>
    /// スキルのeffectFormula種別に応じてダメージ補正を計算する。
    /// 新しいスキル種別が増えてもRegisterで追加でき、BattleEngine本体の変更は不要。
    /// </summary>
    public static class SkillEffectResolver
    {
        public delegate int EffectFunc(CardData caster, CardData target, SkillData skill, int baseDamage);

        private static readonly Dictionary<string, EffectFunc> _effects = new Dictionary<string, EffectFunc>();

        static SkillEffectResolver()
        {
            RegisterDefaults();
        }

        /// <summary>新しいeffectFormula種別を登録する。同名IDは上書きされる。</summary>
        public static void Register(string effectFormulaId, EffectFunc func)
        {
            _effects[effectFormulaId] = func;
        }

        public static int Resolve(CardData caster, CardData target, SkillData skill, int baseDamage)
        {
            if (skill == null || string.IsNullOrEmpty(skill.effectFormula))
                return baseDamage;

            if (_effects.TryGetValue(skill.effectFormula, out var func))
                return func(caster, target, skill, baseDamage);

            Debug.LogWarning($"SkillEffectResolver: 未登録のeffectFormula '{skill.effectFormula}' です。基礎ダメージを返します。");
            return baseDamage;
        }

        /// <summary>
        /// 暫定のサンプル効果種別。実際の計算式はサーバー側のスキル生成ロジック確定後に
        /// effectFormula IDを合わせて追加・調整する。
        /// </summary>
        private static void RegisterDefaults()
        {
            // 参照ステータス値の一部を固定ダメージとして加算
            Register("add_ref_stat_flat", (caster, target, skill, baseDamage) =>
            {
                int refValue = GetStatValue(caster, skill.referenceStat);
                return baseDamage + refValue / 4;
            });

            // 参照ステータスが閾値以上なら倍率補正
            Register("multiply_if_ref_stat_high", (caster, target, skill, baseDamage) =>
            {
                int refValue = GetStatValue(caster, skill.referenceStat);
                return refValue >= 50 ? Mathf.RoundToInt(baseDamage * 1.3f) : baseDamage;
            });
        }

        private static int GetStatValue(CardData card, StatType stat)
        {
            switch (stat)
            {
                case StatType.Hp: return card.CurrentHp;
                case StatType.Speed: return card.speed;
                case StatType.AttackPower: return card.attackPower;
                default: return 0;
            }
        }
    }
}
