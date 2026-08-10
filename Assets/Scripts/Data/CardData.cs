using System;

namespace Orisamo.Data
{
    /// <summary>
    /// バトルに使用する1枚のカードのデータ。
    /// サーバーからJSONで取得し、JsonUtility.FromJsonでそのままデシリアライズする想定。
    /// CurrentHpはバトル中の一時状態のためシリアライズ対象に含めていない。
    /// </summary>
    [Serializable]
    public class CardData
    {
        public string cardId;
        public string ownerName;
        public string illustrationUrl;
        public CardAttribute attribute;
        public int hp;
        public int speed;
        public int attackPower;
        public SkillData skill;
        public Rarity rarity;

        /// <summary>ISO8601文字列。DateTimeが必要な場合はGetGeneratedAt()を使う。</summary>
        public string generatedAt;

        // --- バトル中のみ使用する一時状態（JsonUtilityの対象外） ---
        [NonSerialized] private int _currentHp;
        [NonSerialized] private bool _initialized;

        public int CurrentHp
        {
            get => _initialized ? _currentHp : hp;
            set
            {
                _currentHp = value;
                _initialized = true;
            }
        }

        /// <summary>対戦開始時にCurrentHpをhpへリセットする。</summary>
        public void ResetForBattle()
        {
            CurrentHp = hp;
        }

        public DateTime GetGeneratedAt()
        {
            return DateTime.TryParse(generatedAt, out var dt) ? dt : DateTime.MinValue;
        }
    }
}
