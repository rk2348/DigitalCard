using System;
using UnityEngine;
using Orisamo.Data;
using Orisamo.QR;

namespace Orisamo.CardGeneration
{
    /// <summary>
    /// スキャンしたイラスト画像（Texture2D）からカードを生成するフロー制御。
    /// 生成後はLocalCardStoreへ保存し、対戦時にCardRepository経由で参照できるようにする。
    /// QRコード生成はIQrEncoder実装（例: ZXingQrEncoder）を注入して使用する。
    /// </summary>
    public class CardGenerationController : MonoBehaviour
    {
        [Header("設定")]
        [SerializeField, Range(0f, 100f)] private float rareChancePercent = 3f;

        [Header("参照コンポーネント")]
        [SerializeField] private LocalCardStore localCardStore;
        [SerializeField] private IllustrationImageStore illustrationImageStore;

        private CardGenerator _cardGenerator;
        private IQrEncoder _qrEncoder;

        /// <summary>カード生成完了</summary>
        public event Action<CardData> OnCardGenerated;
        /// <summary>生成失敗（エラーメッセージ）</summary>
        public event Action<string> OnGenerationFailed;

        private void Awake()
        {
            IImageFeatureExtractor extractor = new SimpleImageFeatureExtractor();
            _cardGenerator = new CardGenerator(extractor, rareChancePercent: rareChancePercent);
        }

        /// <summary>QRコード生成の実装を注入する（未設定でも生成自体は可能）。</summary>
        public void InitializeQrEncoder(IQrEncoder encoder)
        {
            _qrEncoder = encoder;
        }

        /// <summary>
        /// スキャン端末／カメラで取得したイラスト画像からカードを生成する。
        /// illustrationTextureはImport Settingsで Read/Write Enabled = true にしておくこと。
        /// </summary>
        public CardData GenerateFromTexture(Texture2D illustrationTexture, string ownerName, CardAttribute attribute, string illustrationUrl = null)
        {
            if (illustrationTexture == null)
            {
                const string err = "イラスト画像がnullです。";
                Debug.LogError($"CardGenerationController: {err}");
                OnGenerationFailed?.Invoke(err);
                return null;
            }

            Color32[] pixels;
            try
            {
                pixels = illustrationTexture.GetPixels32();
            }
            catch (UnityException e)
            {
                string err = $"画像の読み取りに失敗しました（Texture Import SettingsのRead/Write Enabledを確認してください）: {e.Message}";
                Debug.LogError($"CardGenerationController: {err}");
                OnGenerationFailed?.Invoke(err);
                return null;
            }

            var card = _cardGenerator.Generate(pixels, illustrationTexture.width, illustrationTexture.height, ownerName, attribute, illustrationUrl);

            // イラストの実画像を保存する（対戦時の召喚演出でcardIdから呼び出すため）
            illustrationImageStore?.SaveTexture(card.cardId, illustrationTexture);

            localCardStore?.Save(card);
            OnCardGenerated?.Invoke(card);
            return card;
        }

        /// <summary>生成済みカードのcardIdをQRコード画像に変換する（印刷・確認表示用）。</summary>
        public Texture2D GenerateQrTexture(CardData card, int size = 256)
        {
            if (_qrEncoder == null)
            {
                Debug.LogWarning("CardGenerationController: IQrEncoderが未設定のためQRコードを生成できません。InitializeQrEncoderを先に呼び出してください。");
                return null;
            }
            return _qrEncoder.Encode(card.cardId, size);
        }
    }
}
