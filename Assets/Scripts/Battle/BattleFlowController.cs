using System.Collections.Generic;
using UnityEngine;
using Orisamo.Data;
using Orisamo.Network;
using Orisamo.QR;

namespace Orisamo.Battle
{
    /// <summary>
    /// 画面遷移案の「QRスキャン画面→対戦確認画面→バトル演出画面→結果画面」の流れを繋ぐ
    /// フローコントローラー。QrScanController・CardRepository・BattlePresenterを仲介する。
    /// シーン上に1つ配置し、各コンポーネントの参照をインスペクタで設定する想定。
    /// </summary>
    public class BattleFlowController : MonoBehaviour
    {
        [Header("参照コンポーネント")]
        [SerializeField] private QrScanController qrScanController;
        [SerializeField] private CardRepository cardRepository;
        [SerializeField] private BattlePresenter battlePresenter;
        [SerializeField] private ResultReporter resultReporter;
        [SerializeField] private SummonEffectController summonEffectController;

        [Header("設定")]
        [SerializeField] private int requiredCardCount = 2; // 2枚 or 3枚

        private readonly List<CardData> _confirmedCards = new List<CardData>();
        private readonly HashSet<string> _pendingCardIds = new HashSet<string>();

        /// <summary>全カードのステータス取得が完了し、対戦確認画面へ遷移可能になった</summary>
        public event System.Action<List<CardData>> OnAllCardsReady;
        /// <summary>いずれかのカード取得に失敗した（cardId, エラーメッセージ）</summary>
        public event System.Action<string, string> OnCardFetchError;

        private void Awake()
        {
            if (qrScanController != null)
            {
                qrScanController.OnCardScanned += HandleCardScanned;
                qrScanController.OnReadyCountReached += HandleReadyCountReached;
            }

            if (battlePresenter != null && resultReporter != null)
            {
                // 対戦終了後、自動でサーバーへ結果送信したい場合はここでBattleEngineのイベントを購読してもよい。
                // 現状はBattlePresenter.HandleBattleFinished内のTODOから明示的に呼ぶ想定。
            }
        }

        private void OnDestroy()
        {
            if (qrScanController != null)
            {
                qrScanController.OnCardScanned -= HandleCardScanned;
                qrScanController.OnReadyCountReached -= HandleReadyCountReached;
            }
        }

        /// <summary>QRスキャン画面に入ったタイミングで呼び出す。</summary>
        public void BeginScanPhase()
        {
            _confirmedCards.Clear();
            _pendingCardIds.Clear();
            summonEffectController?.DespawnAll();
            qrScanController.ResetScan();
            qrScanController.StartScanning();
        }

        private void HandleCardScanned(string cardId)
        {
            _pendingCardIds.Add(cardId);

            cardRepository.GetCardData(
                cardId,
                onSuccess: card =>
                {
                    _pendingCardIds.Remove(cardId);

                    // QRが確定した時点で、そのカードのイラストをキャラクターとして画面に登場させる
                    int slotIndex = _confirmedCards.Count;
                    summonEffectController?.Summon(card, slotIndex);

                    _confirmedCards.Add(card);
                    TryNotifyReady();
                },
                onError: err =>
                {
                    _pendingCardIds.Remove(cardId);
                    Debug.LogError($"BattleFlowController: カード取得失敗 cardId={cardId}, error={err}");
                    OnCardFetchError?.Invoke(cardId, err);
                    // TODO: 再スキャンを促すUI表示など
                }
            );
        }

        private void HandleReadyCountReached()
        {
            qrScanController.StopScanning();
        }

        private void TryNotifyReady()
        {
            if (_confirmedCards.Count < requiredCardCount) return;
            if (_pendingCardIds.Count > 0) return; // 取得中のカードが残っていれば待つ

            OnAllCardsReady?.Invoke(new List<CardData>(_confirmedCards));
        }

        /// <summary>対戦確認画面で「対戦開始」ボタンが押されたときに呼び出す。</summary>
        public void ConfirmAndStartBattle(string sessionId)
        {
            battlePresenter.SetupBattle(_confirmedCards, sessionId);
            battlePresenter.StartPresentedBattle();
        }

        /// <summary>結果画面から呼び出し、対戦結果をサーバーへ送信する。</summary>
        public void ReportResult()
        {
            if (battlePresenter.CurrentSession == null)
            {
                Debug.LogWarning("BattleFlowController: 送信対象のセッションがありません。");
                return;
            }
            resultReporter.Report(battlePresenter.CurrentSession);
        }
    }
}
