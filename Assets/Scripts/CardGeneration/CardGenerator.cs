using System;
using UnityEngine;
using Orisamo.Data;

namespace Orisamo.CardGeneration
{
    /// <summary>
    /// イラスト画像からCardData（ステータス・スキル込み）を生成するオーケストレーター。
    /// 特徴量抽出（IImageFeatureExtractor）・ステータス生成・スキル生成・レアリティ判定を統括する。
    /// MonoBehaviourに依存しないPure C#クラスとして実装し、UnitTest可能にする。
    /// </summary>
    public class CardGenerator
    {
        private readonly IImageFeatureExtractor _extractor;
        private readonly StatGenerator _statGenerator;
        private readonly SkillGenerator _skillGenerator;
        private readonly RarityJudge _rarityJudge;

        /// <param name="extractor">画像特徴量抽出の実装</param>
        /// <param name="rng">
        /// 乱数生成器。省略時は非決定的な乱数を使用する。
        /// テストで再現性を持たせたい場合はシード指定のSystem.Randomを渡す。
        /// </param>
        /// <param name="rareChancePercent">レア（突然変異）が出現する確率（%）</param>
        public CardGenerator(IImageFeatureExtractor extractor, System.Random rng = null, float rareChancePercent = 3f)
        {
            _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
            var randomSource = rng ?? new System.Random();
            _statGenerator = new StatGenerator(randomSource);
            _skillGenerator = new SkillGenerator(randomSource);
            _rarityJudge = new RarityJudge(rareChancePercent, randomSource);
        }

        public CardData Generate(
            Color32[] pixels,
            int width,
            int height,
            string ownerName,
            CardAttribute attribute,
            string illustrationUrl = null)
        {
            var features = _extractor.Extract(pixels, width, height);
            var rarity = _rarityJudge.Judge();
            var (hp, speed, attackPower) = _statGenerator.GenerateStats(features, rarity);
            var skill = _skillGenerator.GenerateSkill(features, attribute);

            return new CardData
            {
                cardId = Guid.NewGuid().ToString("N"),
                ownerName = ownerName,
                illustrationUrl = illustrationUrl,
                attribute = attribute,
                hp = hp,
                speed = speed,
                attackPower = attackPower,
                skill = skill,
                rarity = rarity,
                generatedAt = DateTime.UtcNow.ToString("o")
            };
        }
    }
}
