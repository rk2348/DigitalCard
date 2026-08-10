using System;
using System.Collections.Generic;
using System.Linq;
using Orisamo.Data;

namespace Orisamo.Battle
{
    /// <summary>
    /// 対戦セッションのデータ本体（参加カード・ターン順・ログ・結果）。
    /// Unityエンジンに依存しないPure C#クラスとして実装する。
    /// </summary>
    public class BattleSession
    {
        public string SessionId { get; }
        public List<CardData> Participants { get; }
        public List<string> TurnOrder { get; private set; } = new List<string>();
        public List<string> BattleLog { get; } = new List<string>();
        public BattleResult Result { get; set; }

        public BattleSession(string sessionId, List<CardData> participants)
        {
            if (string.IsNullOrEmpty(sessionId))
                throw new ArgumentException("sessionIdは必須です。");

            if (participants == null || participants.Count < 2 || participants.Count > 3)
                throw new ArgumentException("対戦は2枚または3枚のカードで構成する必要があります。");

            SessionId = sessionId;
            Participants = participants;

            foreach (var card in Participants)
                card.ResetForBattle();
        }

        /// <summary>素早さ順にturnOrderを決定する。</summary>
        public void DetermineTurnOrder()
        {
            TurnOrder = Participants
                .OrderByDescending(c => c.speed)
                .Select(c => c.cardId)
                .ToList();
        }

        public CardData GetCard(string cardId) => Participants.FirstOrDefault(c => c.cardId == cardId);

        public void Log(string message) => BattleLog.Add(message);

        public string[] GetParticipantIds() => Participants.Select(c => c.cardId).ToArray();

        /// <summary>生存カードが1枚以下になったら終了。</summary>
        public bool IsFinished => Participants.Count(c => c.CurrentHp > 0) <= 1;

        public CardData GetSurvivor() => Participants.FirstOrDefault(c => c.CurrentHp > 0);
    }
}
