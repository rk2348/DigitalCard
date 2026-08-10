using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orisamo.QR
{
    /// <summary>
    /// カメラ映像からQRコードを読み取り、cardIdを発行するイベントを送出する。
    /// 同時読取数は2枚（基本ルール）または3枚（多人数対戦）に対応。
    /// 実際のデコード処理はIQrDecoder実装（例: ZXingQrDecoder）に委譲する。
    /// </summary>
    public class QrScanController : MonoBehaviour
    {
        [Header("読取設定")]
        [SerializeField] private int maxSimultaneousCards = 2; // 2枚 or 3枚
        [SerializeField] private float scanIntervalSeconds = 0.3f;

        private WebCamTexture _webCamTexture;
        private IQrDecoder _decoder;
        private Color32[] _pixelBuffer;
        private float _scanTimer;
        private readonly HashSet<string> _scannedCardIds = new HashSet<string>();

        /// <summary>新規cardIdを読み取った</summary>
        public event Action<string> OnCardScanned;
        /// <summary>同一カードの重複読取</summary>
        public event Action<string> OnDuplicateScan;
        /// <summary>無効なcardId（空文字等）を検出</summary>
        public event Action OnInvalidCardId;
        /// <summary>必要枚数（maxSimultaneousCards）が揃った</summary>
        public event Action OnReadyCountReached;

        public IReadOnlyCollection<string> ScannedCardIds => _scannedCardIds;

        /// <summary>QRデコーダー実装を注入する（DI）。ZXing等の実装差し替えに対応。</summary>
        public void Initialize(IQrDecoder decoder)
        {
            _decoder = decoder;
        }

        public void StartScanning(string deviceName = null)
        {
            _scannedCardIds.Clear();

            if (WebCamTexture.devices.Length == 0)
            {
                Debug.LogError("QrScanController: 利用可能なカメラが見つかりません。");
                return;
            }

            _webCamTexture = string.IsNullOrEmpty(deviceName)
                ? new WebCamTexture()
                : new WebCamTexture(deviceName);

            _webCamTexture.Play();
        }

        public void StopScanning()
        {
            if (_webCamTexture != null && _webCamTexture.isPlaying)
                _webCamTexture.Stop();
        }

        /// <summary>プレビュー表示用（RawImage等のtextureに割り当てる）。</summary>
        public Texture GetPreviewTexture() => _webCamTexture;

        public void ResetScan()
        {
            _scannedCardIds.Clear();
        }

        private void Update()
        {
            if (_webCamTexture == null || !_webCamTexture.isPlaying || _decoder == null)
                return;

            if (_scannedCardIds.Count >= maxSimultaneousCards)
                return;

            _scanTimer += Time.deltaTime;
            if (_scanTimer < scanIntervalSeconds)
                return;

            _scanTimer = 0f;
            TryDecodeCurrentFrame();
        }

        private void TryDecodeCurrentFrame()
        {
            int width = _webCamTexture.width;
            int height = _webCamTexture.height;

            if (_pixelBuffer == null || _pixelBuffer.Length != width * height)
                _pixelBuffer = new Color32[width * height];

            _webCamTexture.GetPixels32(_pixelBuffer);

            if (_decoder.TryDecode(_pixelBuffer, width, height, out var decodedText))
                HandleDecodedText(decodedText);
        }

        private void HandleDecodedText(string decodedText)
        {
            if (string.IsNullOrWhiteSpace(decodedText))
            {
                OnInvalidCardId?.Invoke();
                return;
            }

            string cardId = decodedText.Trim();

            if (_scannedCardIds.Contains(cardId))
            {
                OnDuplicateScan?.Invoke(cardId);
                return;
            }

            _scannedCardIds.Add(cardId);
            OnCardScanned?.Invoke(cardId);

            if (_scannedCardIds.Count >= maxSimultaneousCards)
                OnReadyCountReached?.Invoke();
        }

        private void OnDestroy() => StopScanning();
    }
}
