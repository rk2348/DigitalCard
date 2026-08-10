using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Orisamo.CardGeneration
{
    /// <summary>
    /// カードイラストの実画像（Texture2D）をcardId単位で端末へ保存・読込する。
    /// CardData（Data/CardData.cs）はステータス等の数値情報のみを持つ設計のため、
    /// 実際のイラスト画像はこのクラスで別管理する。
    /// 保存先: Application.persistentDataPath/Illustrations/{cardId}.png
    ///
    /// 【重要】この実装はイラストスキャン端末と対戦端末が同一であることを前提にしている
    /// （persistentDataPathは端末ローカルのため、別端末には引き継がれない）。
    /// 別端末構成にする場合は、画像を共有サーバーやローカルネットワーク経由で
    /// 受け渡す仕組みへ拡張する必要がある。
    /// </summary>
    public class IllustrationImageStore : MonoBehaviour
    {
        private string DirectoryPath => Path.Combine(Application.persistentDataPath, "Illustrations");

        private readonly Dictionary<string, Texture2D> _memoryCache = new Dictionary<string, Texture2D>();

        /// <summary>イラスト画像をcardId単位で保存する。CardGenerationController.GenerateFromTextureから呼ばれる。</summary>
        public void SaveTexture(string cardId, Texture2D texture)
        {
            if (string.IsNullOrEmpty(cardId) || texture == null)
            {
                Debug.LogError("IllustrationImageStore: cardId または texture が不正です。");
                return;
            }

            EnsureDirectory();
            byte[] pngBytes = texture.EncodeToPNG();
            string path = Path.Combine(DirectoryPath, $"{cardId}.png");
            File.WriteAllBytes(path, pngBytes);

            _memoryCache[cardId] = texture;
        }

        /// <summary>cardIdからイラスト画像を読み込む。召喚演出（SummonEffectController）等から呼ばれる。</summary>
        public bool TryLoadTexture(string cardId, out Texture2D texture)
        {
            if (string.IsNullOrEmpty(cardId))
            {
                texture = null;
                return false;
            }

            if (_memoryCache.TryGetValue(cardId, out var cached))
            {
                texture = cached;
                return true;
            }

            string path = Path.Combine(DirectoryPath, $"{cardId}.png");
            if (!File.Exists(path))
            {
                texture = null;
                return false;
            }

            byte[] bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2);
            if (!tex.LoadImage(bytes))
            {
                texture = null;
                return false;
            }

            _memoryCache[cardId] = tex;
            texture = tex;
            return true;
        }

        /// <summary>保存済みイラストが存在するか確認する（画面遷移前のバリデーション等に使用）。</summary>
        public bool Exists(string cardId)
        {
            if (_memoryCache.ContainsKey(cardId)) return true;
            string path = Path.Combine(DirectoryPath, $"{cardId}.png");
            return File.Exists(path);
        }

        private void EnsureDirectory()
        {
            if (!Directory.Exists(DirectoryPath))
                Directory.CreateDirectory(DirectoryPath);
        }
    }
}
