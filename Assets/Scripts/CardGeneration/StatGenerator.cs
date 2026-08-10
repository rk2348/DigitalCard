using System;
using UnityEngine;
using Orisamo.Data;

namespace Orisamo.CardGeneration
{
    /// <summary>
    /// 画像特徴量とレアリティから、HP・素早さ・攻撃力を算出する。
    /// 数値バランスは暫定値。ワークショップでのテストプレイを踏まえて調整すること
    /// （各種定数を調整するだけでよい設計にしている）。
    /// </summary>
    public class StatGenerator
    {
        private readonly System.Random _rng;

        private const int BaseHp = 80;
        private const int BaseSpeed = 40;
        private const int BaseAttack = 30;
        private const int JitterRange = 10; // ±10のランダム補正
        private const float RareStatMultiplier = 1.25f;

        public StatGenerator(System.Random rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        public (int hp, int speed, int attackPower) GenerateStats(ImageFeatures features, Rarity rarity)
        {
            int hp = BaseHp + Mathf.RoundToInt(features.lineComplexity * 40) + Jitter();
            int speed = BaseSpeed + Mathf.RoundToInt((1f - features.lineComplexity) * 30 + features.colorVariety * 20) + Jitter();
            int attack = BaseAttack + Mathf.RoundToInt(features.averageSaturation * 50) + Jitter();

            if (rarity == Rarity.Rare)
            {
                hp = Mathf.RoundToInt(hp * RareStatMultiplier);
                speed = Mathf.RoundToInt(speed * RareStatMultiplier);
                attack = Mathf.RoundToInt(attack * RareStatMultiplier);
            }

            return (Math.Max(1, hp), Math.Max(1, speed), Math.Max(1, attack));
        }

        private int Jitter() => _rng.Next(-JitterRange, JitterRange + 1);
    }
}
