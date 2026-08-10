using System;
using Orisamo.Data;

namespace Orisamo.CardGeneration
{
    /// <summary>
    /// 生成時に低確率でレアカード（突然変異）を付与する判定。
    /// </summary>
    public class RarityJudge
    {
        private readonly float _rareChancePercent;
        private readonly System.Random _rng;

        public RarityJudge(float rareChancePercent, System.Random rng)
        {
            _rareChancePercent = rareChancePercent;
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        public Rarity Judge()
        {
            double roll = _rng.NextDouble() * 100.0;
            return roll < _rareChancePercent ? Rarity.Rare : Rarity.Normal;
        }
    }
}
