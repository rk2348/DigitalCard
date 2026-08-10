using UnityEngine;

namespace Orisamo.QR
{
    /// <summary>
    /// QRコード生成（印刷・表示用）の抽象インターフェース。
    /// IQrDecoderと同様、実装をZXing.Net等の外部アセットに委譲する。
    /// </summary>
    public interface IQrEncoder
    {
        /// <summary>指定文字列（cardId）をエンコードしたQRコード画像を生成する。</summary>
        Texture2D Encode(string content, int size = 256);
    }
}
