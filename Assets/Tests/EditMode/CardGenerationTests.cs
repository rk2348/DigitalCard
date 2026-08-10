using System;
using NUnit.Framework;
using UnityEngine;
using Orisamo.CardGeneration;
using Orisamo.Data;

namespace Orisamo.Tests
{
    /// <summary>
    /// 画像特徴量抽出〜ステータス／スキル生成〜カード生成までの一連ロジックのテスト。
    /// すべてPure C#（MonoBehaviour非依存）のため、Playモード無しで検証できる。
    /// </summary>
    public class CardGenerationTests
    {
        // --- SimpleImageFeatureExtractor ---

        [Test]
        public void SolidColorImage_HasLowLineComplexity()
        {
            var extractor = new SimpleImageFeatureExtractor();
            var pixels = CreateSolidColorImage(64, 64, new Color32(120, 120, 120, 255));

            var features = extractor.Extract(pixels, 64, 64);

            Assert.Less(features.lineComplexity, 0.05f, "単色画像はエッジがほぼ無いはず");
        }

        [Test]
        public void NoisyImage_HasHigherLineComplexityThanSolidImage()
        {
            var extractor = new SimpleImageFeatureExtractor();
            var solid = CreateSolidColorImage(64, 64, new Color32(100, 100, 100, 255));
            var noisy = CreateNoisyImage(64, 64, seed: 1);

            var solidFeatures = extractor.Extract(solid, 64, 64);
            var noisyFeatures = extractor.Extract(noisy, 64, 64);

            Assert.Greater(noisyFeatures.lineComplexity, solidFeatures.lineComplexity);
            Assert.Greater(noisyFeatures.colorVariety, solidFeatures.colorVariety);
        }

        // --- StatGenerator ---

        [Test]
        public void StatGenerator_IsDeterministic_WithSameSeed()
        {
            var features = new ImageFeatures { lineComplexity = 0.5f, colorVariety = 0.5f, averageSaturation = 0.5f };

            var gen1 = new StatGenerator(new System.Random(42));
            var gen2 = new StatGenerator(new System.Random(42));

            var result1 = gen1.GenerateStats(features, Rarity.Normal);
            var result2 = gen2.GenerateStats(features, Rarity.Normal);

            Assert.AreEqual(result1, result2);
        }

        [Test]
        public void StatGenerator_RareCard_HasHigherStatsThanNormal()
        {
            var features = new ImageFeatures { lineComplexity = 0.5f, colorVariety = 0.5f, averageSaturation = 0.5f };

            var normalGen = new StatGenerator(new System.Random(7));
            var rareGen = new StatGenerator(new System.Random(7));

            var normalStats = normalGen.GenerateStats(features, Rarity.Normal);
            var rareStats = rareGen.GenerateStats(features, Rarity.Rare);

            Assert.Greater(rareStats.hp, normalStats.hp);
            Assert.Greater(rareStats.speed, normalStats.speed);
            Assert.Greater(rareStats.attackPower, normalStats.attackPower);
        }

        // --- CardGenerator（end-to-end） ---

        [Test]
        public void CardGenerator_ProducesValidCardData()
        {
            var extractor = new SimpleImageFeatureExtractor();
            var generator = new CardGenerator(extractor, new System.Random(123), rareChancePercent: 0f); // レア無しで安定検証

            var pixels = CreateNoisyImage(64, 64, seed: 5);
            var card = generator.Generate(pixels, 64, 64, ownerName: "テスト太郎", attribute: CardAttribute.AttrC);

            Assert.IsNotEmpty(card.cardId);
            Assert.AreEqual("テスト太郎", card.ownerName);
            Assert.AreEqual(CardAttribute.AttrC, card.attribute);
            Assert.Greater(card.hp, 0);
            Assert.Greater(card.speed, 0);
            Assert.Greater(card.attackPower, 0);
            Assert.IsNotNull(card.skill);
            Assert.IsNotEmpty(card.skill.skillName);
            Assert.AreEqual(Rarity.Normal, card.rarity); // rareChancePercent=0のため
        }

        // --- テスト用ヘルパー ---

        private static Color32[] CreateSolidColorImage(int width, int height, Color32 color)
        {
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            return pixels;
        }

        private static Color32[] CreateNoisyImage(int width, int height, int seed)
        {
            var rng = new System.Random(seed);
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(
                    (byte)rng.Next(0, 256),
                    (byte)rng.Next(0, 256),
                    (byte)rng.Next(0, 256),
                    255);
            }
            return pixels;
        }
    }
}
