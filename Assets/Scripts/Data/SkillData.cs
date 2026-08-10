using System;

namespace Orisamo.Data
{
    /// <summary>
    /// 自動生成されるスキル1件分のデータ。
    /// JsonUtilityでサーバーからのJSONをそのままデシリアライズできる構造にしている。
    /// </summary>
    [Serializable]
    public class SkillData
    {
        public string skillId;
        public string skillName;
        public StatType referenceStat;

        /// <summary>
        /// 効果計算式の種別ID。SkillEffectResolverの登録キーと対応する。
        /// （例: "add_ref_stat_flat" など。サーバー側生成ロジックが確定させる）
        /// </summary>
        public string effectFormula;

        public string description;
    }
}
