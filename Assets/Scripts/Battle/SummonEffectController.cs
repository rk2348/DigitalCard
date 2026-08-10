using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Orisamo.Data;
using Orisamo.CardGeneration;

namespace Orisamo.Battle
{
    /// <summary>
    /// QR読取で確定したカードのイラストを、対戦画面上にキャラクターとして登場させる演出。
    /// イラスト画像はIllustrationImageStoreからcardIdをキーに取得する。
    /// 設計書5-4節「召喚演出：カードをカメラにかざす（QR読取）と、大画面の左右にキャラクターが
    /// 出現するアニメーション」に対応する。
    /// </summary>
    public class SummonEffectController : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private IllustrationImageStore illustrationImageStore;

        [Header("配置設定")]
        [Tooltip("キャラクターの登場位置（2枚対戦なら2つ、3枚対戦なら3つ配置しておく）")]
        [SerializeField] private Transform[] summonSlots;

        [Tooltip("MeshRenderer（またはSpriteRenderer）を持つプレハブ。Quad等にイラストを貼って表示する")]
        [SerializeField] private GameObject characterDisplayPrefab;

        [SerializeField] private float appearDurationSeconds = 0.6f;

        private readonly Dictionary<string, GameObject> _activeCharacters = new Dictionary<string, GameObject>();

        /// <summary>キャラクターの登場アニメーションが完了した</summary>
        public event System.Action<string> OnCharacterAppeared;

        /// <summary>
        /// 指定したカードのイラストを、slotIndex番目の登場位置にキャラクターとして表示する。
        /// イラストが未保存（別端末で生成された等）の場合は何もせず警告を出す。
        /// </summary>
        public void Summon(CardData card, int slotIndex)
        {
            if (card == null || string.IsNullOrEmpty(card.cardId))
            {
                Debug.LogWarning("SummonEffectController: cardが不正です。");
                return;
            }

            if (illustrationImageStore == null)
            {
                Debug.LogWarning("SummonEffectController: IllustrationImageStoreが未設定です。");
                return;
            }

            if (summonSlots == null || slotIndex < 0 || slotIndex >= summonSlots.Length)
            {
                Debug.LogWarning($"SummonEffectController: 不正なslotIndex({slotIndex})です。summonSlotsの数を確認してください。");
                return;
            }

            if (characterDisplayPrefab == null)
            {
                Debug.LogWarning("SummonEffectController: characterDisplayPrefabが未設定です。");
                return;
            }

            if (!illustrationImageStore.TryLoadTexture(card.cardId, out var texture))
            {
                Debug.LogWarning($"SummonEffectController: cardId={card.cardId} のイラスト画像が見つかりません（別端末で生成されたカードの可能性があります）。");
                return;
            }

            // 同じカードが既に表示されていれば一旦片付ける
            Despawn(card.cardId);

            var slot = summonSlots[slotIndex];
            var instance = Instantiate(characterDisplayPrefab, slot.position, slot.rotation, slot);
            ApplyTexture(instance, texture);

            _activeCharacters[card.cardId] = instance;
            StartCoroutine(PlayAppearAnimation(instance, card.cardId));
        }

        /// <summary>対戦終了後や再スキャン時に、表示中のキャラクターを全て片付ける。</summary>
        public void DespawnAll()
        {
            foreach (var go in _activeCharacters.Values)
                if (go != null) Destroy(go);
            _activeCharacters.Clear();
        }

        public void Despawn(string cardId)
        {
            if (_activeCharacters.TryGetValue(cardId, out var go))
            {
                if (go != null) Destroy(go);
                _activeCharacters.Remove(cardId);
            }
        }

        private static void ApplyTexture(GameObject instance, Texture2D texture)
        {
            var renderer = instance.GetComponentInChildren<Renderer>();
            if (renderer == null)
            {
                Debug.LogWarning("SummonEffectController: characterDisplayPrefabにRendererが見つかりません（Quad等を含む構成にしてください）。");
                return;
            }

            // 他カードとマテリアルを共有しないようインスタンス化してからテクスチャを差し替える
            renderer.material = new Material(renderer.sharedMaterial);
            renderer.material.mainTexture = texture;
        }

        private IEnumerator PlayAppearAnimation(GameObject instance, string cardId)
        {
            Vector3 targetScale = instance.transform.localScale;
            instance.transform.localScale = Vector3.zero;

            float t = 0f;
            while (t < appearDurationSeconds)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / appearDurationSeconds);
                float eased = 1f - Mathf.Pow(1f - p, 3f); // EaseOutCubic風の簡易カーブ
                if (instance != null)
                    instance.transform.localScale = targetScale * eased;
                yield return null;
            }

            if (instance != null)
                instance.transform.localScale = targetScale;

            OnCharacterAppeared?.Invoke(cardId);
        }
    }
}
