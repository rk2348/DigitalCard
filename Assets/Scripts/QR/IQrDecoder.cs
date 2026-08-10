using UnityEngine;

namespace Orisamo.QR
{
    /// <summary>
    /// QRコードデコード処理の抽象インターフェース。
    /// 実装をZXing.Net等の外部アセットに委譲することで、
    /// QrScanController本体がライブラリ差し替えの影響を受けないようにする。
    /// </summary>
    public interface IQrDecoder
    {
        /// <summary>
        /// ピクセルデータからQRコードのデコードを試みる。
        /// </summary>
        /// <param name="pixels">Color32[]形式のフレーム画像（WebCamTexture.GetPixels32相当）</param>
        /// <param name="width">画像幅</param>
        /// <param name="height">画像高さ</param>
        /// <param name="decodedText">デコード成功時の文字列（cardId）</param>
        /// <returns>デコードに成功した場合はtrue</returns>
        bool TryDecode(Color32[] pixels, int width, int height, out string decodedText);
    }
}
