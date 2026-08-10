using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Orisamo.Data;

namespace Orisamo.CardGeneration
{
    /// <summary>
    /// 生成したCardDataを端末のローカルストレージへ保存・読込する。
    /// サーバーレス構成でカードDBの役割を担う。
    /// 保存先: Application.persistentDataPath/Cards/{cardId}.json
    /// </summary>
    public class LocalCardStore : MonoBehaviour
    {
        private string DirectoryPath => Path.Combine(Application.persistentDataPath, "Cards");

        public void Save(CardData card)
        {
            if (card == null || string.IsNullOrEmpty(card.cardId))
            {
                Debug.LogError("LocalCardStore: card または cardId が不正です。");
                return;
            }

            EnsureDirectory();
            string path = Path.Combine(DirectoryPath, $"{card.cardId}.json");
            File.WriteAllText(path, JsonUtility.ToJson(card, true));
        }

        public bool TryLoad(string cardId, out CardData card)
        {
            string path = Path.Combine(DirectoryPath, $"{cardId}.json");
            if (File.Exists(path))
            {
                card = JsonUtility.FromJson<CardData>(File.ReadAllText(path));
                if (card != null)
                {
                    card.ResetForBattle();
                    return true;
                }
            }
            card = null;
            return false;
        }

        /// <summary>保存済みの全カードを読み込む（一覧画面・大会運営用など）。</summary>
        public CardData[] LoadAll()
        {
            EnsureDirectory();
            var files = Directory.GetFiles(DirectoryPath, "*.json");
            var list = new List<CardData>(files.Length);

            foreach (var file in files)
            {
                var card = JsonUtility.FromJson<CardData>(File.ReadAllText(file));
                if (card != null)
                {
                    card.ResetForBattle();
                    list.Add(card);
                }
            }
            return list.ToArray();
        }

        private void EnsureDirectory()
        {
            if (!Directory.Exists(DirectoryPath))
                Directory.CreateDirectory(DirectoryPath);
        }
    }
}
