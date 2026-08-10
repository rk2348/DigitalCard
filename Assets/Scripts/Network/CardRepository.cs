using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Orisamo.Data;
using Orisamo.CardGeneration;

namespace Orisamo.Network
{
    /// <summary>
    /// カードIDをもとにカードデータを取得する。参照優先順位は以下の通り：
    ///   1. メモリキャッシュ
    ///   2. LocalCardStore（Unity側でイラスト解析・生成したカード。サーバーレス運用時はここで完結する）
    ///   3. サーバーAPI（GET {apiBaseUrl}/cards/{cardId}）
    ///   4. Resources/CardCache配下のJSONバンドル（オフライン運用フォールバック）
    /// </summary>
    public class CardRepository : MonoBehaviour
    {
        [SerializeField] private string apiBaseUrl = "https://example.com/api";
        [SerializeField] private int timeoutSeconds = 8;

        [Tooltip("Unity側でイラスト解析・カード生成する構成の場合に割り当てる。未割り当てならサーバーAPIのみを使用する。")]
        [SerializeField] private LocalCardStore localCardStore;

        [Tooltip("trueの場合、サーバーAPIへは問い合わせずLocalCardStore／ローカルキャッシュのみで解決する（完全サーバーレス構成）。")]
        [SerializeField] private bool serverless = false;

        private readonly Dictionary<string, CardData> _memoryCache = new Dictionary<string, CardData>();

        public event Action<CardData> OnCardFetched;
        public event Action<string, string> OnFetchFailed; // cardId, errorMessage

        /// <summary>
        /// 事前配布されたJSONバンドル（TextAsset配列）をメモリキャッシュへロードする。
        /// 会場入りする前にオフラインバンドルを読み込んでおく用途を想定。
        /// </summary>
        public void PreloadBundle(TextAsset[] bundledCards)
        {
            if (bundledCards == null) return;

            foreach (var asset in bundledCards)
            {
                var card = JsonUtility.FromJson<CardData>(asset.text);
                if (card != null && !string.IsNullOrEmpty(card.cardId))
                {
                    card.ResetForBattle();
                    _memoryCache[card.cardId] = card;
                }
            }
        }

        public void GetCardData(string cardId, Action<CardData> onSuccess, Action<string> onError)
        {
            if (_memoryCache.TryGetValue(cardId, out var cached))
            {
                onSuccess?.Invoke(cached);
                OnCardFetched?.Invoke(cached);
                return;
            }

            if (localCardStore != null && localCardStore.TryLoad(cardId, out var localGeneratedCard))
            {
                _memoryCache[cardId] = localGeneratedCard;
                onSuccess?.Invoke(localGeneratedCard);
                OnCardFetched?.Invoke(localGeneratedCard);
                return;
            }

            if (serverless)
            {
                string err = $"サーバーレス構成のため、cardId={cardId} のカードがローカルに見つかりませんでした。";
                Debug.LogWarning(err);
                onError?.Invoke(err);
                OnFetchFailed?.Invoke(cardId, err);
                return;
            }

            StartCoroutine(FetchFromServer(cardId, onSuccess, onError));
        }

        private IEnumerator FetchFromServer(string cardId, Action<CardData> onSuccess, Action<string> onError)
        {
            string url = $"{apiBaseUrl}/cards/{UnityWebRequest.EscapeURL(cardId)}";

            using var req = UnityWebRequest.Get(url);
            req.timeout = timeoutSeconds;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = $"通信エラー: {req.error}";

                if (TryLoadFromLocalCacheFile(cardId, out var localCard))
                {
                    Debug.LogWarning($"CardRepository: サーバー取得に失敗したためローカルキャッシュを使用します（{cardId}）");
                    _memoryCache[cardId] = localCard;
                    onSuccess?.Invoke(localCard);
                    OnCardFetched?.Invoke(localCard);
                    yield break;
                }

                Debug.LogError(err);
                onError?.Invoke(err);
                OnFetchFailed?.Invoke(cardId, err);
                yield break;
            }

            CardData card;
            try
            {
                card = JsonUtility.FromJson<CardData>(req.downloadHandler.text);
            }
            catch (Exception e)
            {
                string err = $"JSON解析エラー: {e.Message}";
                Debug.LogError(err);
                onError?.Invoke(err);
                OnFetchFailed?.Invoke(cardId, err);
                yield break;
            }

            if (card == null || string.IsNullOrEmpty(card.cardId))
            {
                string err = "カードデータの解析結果が不正です。";
                onError?.Invoke(err);
                OnFetchFailed?.Invoke(cardId, err);
                yield break;
            }

            card.ResetForBattle();
            _memoryCache[cardId] = card;
            onSuccess?.Invoke(card);
            OnCardFetched?.Invoke(card);
        }

        /// <summary>
        /// Resources/CardCache/{cardId}.json （TextAssetとして配置）からのフォールバック読込。
        /// 会場でオフライン運用する場合は、事前にこのフォルダへ全カードのJSONを配置しておく。
        /// </summary>
        private bool TryLoadFromLocalCacheFile(string cardId, out CardData card)
        {
            var textAsset = Resources.Load<TextAsset>($"CardCache/{cardId}");
            if (textAsset != null)
            {
                card = JsonUtility.FromJson<CardData>(textAsset.text);
                if (card != null)
                {
                    card.ResetForBattle();
                    return true;
                }
            }
            card = null;
            return false;
        }
    }
}
