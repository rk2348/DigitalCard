using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Orisamo.Data;

namespace Orisamo.Battle
{
    /// <summary>
    /// BattleEngineの状態変化をUI／演出へ反映するプレゼンター層（MVP）。
    /// ロジック本体（BattleEngine）には手を加えず、演出タイミングの制御に専念する。
    /// HPバー・召喚演出・SE再生等は各TODO箇所に実装を追加していく想定。
    /// </summary>
    public class BattlePresenter : MonoBehaviour
    {
        [SerializeField] private AttributeMatrixSO attributeMatrix;
        [SerializeField] private float turnIntervalSeconds = 1.2f;

        private BattleSession _session;
        private BattleEngine _engine;

        public BattleSession CurrentSession => _session;

        /// <summary>QRスキャン＋カード取得が完了した後、対戦確認画面から呼び出す。</summary>
        public void SetupBattle(List<CardData> participants, string sessionId)
        {
            _session = new BattleSession(sessionId, participants);
            _engine = new BattleEngine(_session, attributeMatrix);

            _engine.OnTurnStart += HandleTurnStart;
            _engine.OnDamageDealt += HandleDamageDealt;
            _engine.OnHpChanged += HandleHpChanged;
            _engine.OnBattleFinished += HandleBattleFinished;

            _engine.Initialize();
        }

        /// <summary>バトル演出画面で対戦開始ボタンが押されたときに呼び出す。</summary>
        public void StartPresentedBattle()
        {
            StartCoroutine(RunBattleWithPacing());
        }

        private IEnumerator RunBattleWithPacing()
        {
            bool continuing = true;
            while (continuing)
            {
                continuing = _engine.AdvanceTurn();
                yield return new WaitForSeconds(turnIntervalSeconds);
            }
        }

        private void HandleTurnStart(string cardId)
        {
            Debug.Log($"[BattlePresenter] ターン開始: {cardId}");
            // TODO: 行動順表示・行動主体のハイライト演出をここで呼び出す
        }

        private void HandleDamageDealt(string attackerId, string targetId, int damage)
        {
            Debug.Log($"[BattlePresenter] {attackerId} -> {targetId} : {damage}ダメージ");
            // TODO: 攻撃エフェクト・SE・ダメージ数値のポップアップ表示
        }

        private void HandleHpChanged(string cardId, int currentHp)
        {
            Debug.Log($"[BattlePresenter] {cardId} 残HP: {currentHp}");
            // TODO: HPバーのアニメーション更新
        }

        private void HandleBattleFinished(BattleResult result, string survivorCardId)
        {
            Debug.Log($"[BattlePresenter] 対戦終了: result={result}, survivor={survivorCardId}");
            // TODO: 勝敗演出（エフェクト・SE）の再生、結果画面への遷移
            // TODO: ResultReporter.Report(_session) をここから呼び出し、サーバーへ結果送信する
        }

        private void OnDestroy()
        {
            if (_engine == null) return;
            _engine.OnTurnStart -= HandleTurnStart;
            _engine.OnDamageDealt -= HandleDamageDealt;
            _engine.OnHpChanged -= HandleHpChanged;
            _engine.OnBattleFinished -= HandleBattleFinished;
        }
    }
}
