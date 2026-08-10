using NUnit.Framework;
using UnityEngine;
using Orisamo.CardGeneration;

namespace Orisamo.Tests
{
    /// <summary>
    /// IllustrationImageStoreが保存した画像を、cardIdから正しく読み戻せることを検証する。
    /// </summary>
    public class IllustrationImageStoreTests
    {
        private GameObject _hostObject;
        private IllustrationImageStore _store;

        [SetUp]
        public void SetUp()
        {
            _hostObject = new GameObject("IllustrationImageStoreTestHost");
            _store = _hostObject.AddComponent<IllustrationImageStore>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_hostObject);
        }

        [Test]
        public void SavedTexture_CanBeLoadedBack_ByCardId()
        {
            string cardId = "test-card-" + System.Guid.NewGuid().ToString("N");
            var original = new Texture2D(4, 4);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    original.SetPixel(x, y, x == y ? Color.red : Color.blue);
            original.Apply();

            _store.SaveTexture(cardId, original);

            bool found = _store.TryLoadTexture(cardId, out var loaded);

            Assert.IsTrue(found);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(4, loaded.width);
            Assert.AreEqual(4, loaded.height);
        }

        [Test]
        public void UnknownCardId_ReturnsFalse()
        {
            bool found = _store.TryLoadTexture("does-not-exist", out var loaded);

            Assert.IsFalse(found);
            Assert.IsNull(loaded);
        }
    }
}
