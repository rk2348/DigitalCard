// Unity Test Framework（NUnit）を使用。
// Package Manager から "Test Framework" を導入し、
// Assets/Tests フォルダ内に .asmdef（テストアセンブリ、Test Assembliesにチェック）を
// 作成した上でこのファイルを配置すること。
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Orisamo.Battle;
using Orisamo.Data;

namespace Orisamo.Tests
{
    /// <summary>
    /// BattleEngineがMonoBehaviourに依存しないPure C#設計であることを活かした
    /// EditModeテストのサンプル。Unityエディタを起動するだけでPlayモード無しに実行できる。
    /// </summary>
    public class BattleEngineTests
    {
        private static AttributeMatrixSO CreateEqualMatrix()
        {
            // デフォルトは全組み合わせ等倍(1.0)
            return ScriptableObject.CreateInstance<AttributeMatrixSO>();
        }

        private static CardData CreateCard(string id, int hp, int speed, int atk, CardAttribute attr = CardAttribute.AttrA)
        {
            var card = new CardData
            {
                cardId = id,
                hp = hp,
                speed = speed,
                attackPower = atk,
                attribute = attr,
                rarity = Rarity.Normal
            };
            card.ResetForBattle();
            return card;
        }

        [Test]
        public void FasterCardActsFirst()
        {
            var a = CreateCard("A", hp: 100, speed: 10, atk: 20);
            var b = CreateCard("B", hp: 100, speed: 5, atk: 20);
            var session = new BattleSession("test-session-1", new List<CardData> { a, b });
            var engine = new BattleEngine(session, CreateEqualMatrix());

            engine.Initialize();

            Assert.AreEqual("A", session.TurnOrder[0]);
        }

        [Test]
        public void BattleEndsWhenOneCardReachesZeroHp()
        {
            var a = CreateCard("A", hp: 1000, speed: 10, atk: 50);
            var b = CreateCard("B", hp: 10, speed: 5, atk: 5);
            var session = new BattleSession("test-session-2", new List<CardData> { a, b });
            var engine = new BattleEngine(session, CreateEqualMatrix());
            engine.Initialize();

            engine.RunToCompletion();

            Assert.IsTrue(session.IsFinished);
            Assert.AreEqual(BattleResult.Win, session.Result);
            Assert.AreEqual("A", session.GetSurvivor().cardId);
        }

        [Test]
        public void AttributeAdvantageIncreasesDamage()
        {
            var matrix = CreateEqualMatrix();
            matrix.SetMultiplier(CardAttribute.AttrA, CardAttribute.AttrB, 1.5f);

            var a = CreateCard("A", hp: 100, speed: 10, atk: 20, attr: CardAttribute.AttrA);
            var b = CreateCard("B", hp: 100, speed: 5, atk: 20, attr: CardAttribute.AttrB);
            var session = new BattleSession("test-session-3", new List<CardData> { a, b });
            var engine = new BattleEngine(session, matrix);
            engine.Initialize();

            engine.AdvanceTurn();

            // 100 - (20 * 1.5) = 70
            Assert.AreEqual(70, b.CurrentHp);
        }
    }
}
