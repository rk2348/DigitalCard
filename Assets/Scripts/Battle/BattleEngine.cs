using System;
using System.Linq;
using Orisamo.Data;

namespace Orisamo.Battle
{
    /// <summary>
    /// ターン制バトルのコアロジック（初期化→ターン開始→ダメージ計算→HP更新・判定→ターン交代→結果送信）。
    /// MonoBehaviourに依存しないPure C#クラスとして実装し、Unityエディタを起動せずに
    /// UnitTestできるようにする。演出との結合はBattlePresenterが仲介する（MVPパターン）。
    /// </summary>
    public class BattleEngine
    {
        private readonly BattleSession _session;
        private readonly AttributeMatrixSO _attributeMatrix;
        private int _turnCursor;

        /// <summary>ターン開始（行動主体のcardId）</summary>
        public event Action<string> OnTurnStart;
        /// <summary>ダメージ発生（攻撃者ID, 対象ID, ダメージ量）</summary>
        public event Action<string, string, int> OnDamageDealt;
        /// <summary>HP変化（対象ID, 現在HP）</summary>
        public event Action<string, int> OnHpChanged;
        /// <summary>対戦終了（結果, 生存カードID）</summary>
        public event Action<BattleResult, string> OnBattleFinished;

        public BattleEngine(BattleSession session, AttributeMatrixSO attributeMatrix)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _attributeMatrix = attributeMatrix ?? throw new ArgumentNullException(nameof(attributeMatrix));
        }

        /// <summary>①初期化：素早さ順にturnOrderを決定する。</summary>
        public void Initialize()
        {
            _session.DetermineTurnOrder();
            _turnCursor = 0;
            _session.Log($"対戦開始: {string.Join(",", _session.TurnOrder)}");
        }

        /// <summary>
        /// 1ターン分（②〜⑤）を進める。バトルが継続する間はtrue、終了したらfalseを返す。
        /// </summary>
        public bool AdvanceTurn()
        {
            if (_session.IsFinished)
            {
                FinishBattle();
                return false;
            }

            string attackerId = GetNextAliveAttacker();
            if (attackerId == null)
            {
                FinishBattle();
                return false;
            }

            var attacker = _session.GetCard(attackerId);
            var target = SelectTarget(attacker);
            if (target == null)
            {
                FinishBattle();
                return false;
            }

            // ②ターン開始
            OnTurnStart?.Invoke(attackerId);
            _session.Log($"ターン開始: {attackerId}");

            // ③ダメージ計算（基礎攻撃力 × 属性相性倍率 ± スキル効果）
            int damage = CalculateDamage(attacker, target);

            // ④HP更新・判定
            ApplyDamage(target, damage);
            OnDamageDealt?.Invoke(attacker.cardId, target.cardId, damage);
            OnHpChanged?.Invoke(target.cardId, target.CurrentHp);
            _session.Log($"{attacker.cardId} が {target.cardId} に {damage} ダメージ（残HP:{target.CurrentHp}）");

            // ⑤ターン交代
            AdvanceCursor();

            if (_session.IsFinished)
            {
                FinishBattle();
                return false;
            }

            return true;
        }

        /// <summary>演出なしで終了までAdvanceTurnを回す（テスト・シミュレーション用）。</summary>
        public void RunToCompletion(int maxTurns = 1000)
        {
            int turns = 0;
            while (AdvanceTurn())
            {
                turns++;
                if (turns >= maxTurns)
                {
                    _session.Log("最大ターン数に到達したため強制終了します。");
                    break;
                }
            }
        }

        private int CalculateDamage(CardData attacker, CardData target)
        {
            float multiplier = _attributeMatrix.GetMultiplier(attacker.attribute, target.attribute);
            int baseDamage = Math.Max(0, (int)Math.Round(attacker.attackPower * multiplier));
            int finalDamage = SkillEffectResolver.Resolve(attacker, target, attacker.skill, baseDamage);
            return Math.Max(0, finalDamage);
        }

        private void ApplyDamage(CardData target, int damage)
        {
            target.CurrentHp = Math.Max(0, target.CurrentHp - damage);
        }

        private string GetNextAliveAttacker()
        {
            int count = _session.TurnOrder.Count;
            for (int i = 0; i < count; i++)
            {
                string candidateId = _session.TurnOrder[_turnCursor % count];
                var card = _session.GetCard(candidateId);
                if (card != null && card.CurrentHp > 0)
                    return candidateId;

                AdvanceCursor();
            }
            return null;
        }

        private CardData SelectTarget(CardData attacker)
        {
            // 現状は「攻撃者以外で生存している最初のカード」を対象とする単純ルール。
            // 3枚対戦時のターゲット選択仕様（ランダム/プレイヤー選択等）が確定次第、ここを拡張する。
            return _session.Participants
                .Where(c => c.cardId != attacker.cardId && c.CurrentHp > 0)
                .FirstOrDefault();
        }

        private void AdvanceCursor() => _turnCursor++;

        private void FinishBattle()
        {
            var survivor = _session.GetSurvivor();
            int aliveCount = _session.Participants.Count(c => c.CurrentHp > 0);

            _session.Result = aliveCount == 0 ? BattleResult.Draw : BattleResult.Win;
            // 注: ここでのWinは「セッション内の生存者がいる」という意味。
            // 特定プレイヤー視点でのWin/Loseへの変換はBattlePresenter側で行う。

            _session.Log($"対戦終了: result={_session.Result}, survivor={survivor?.cardId}");
            OnBattleFinished?.Invoke(_session.Result, survivor?.cardId);
        }
    }
}
