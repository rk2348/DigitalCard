// ZXingQrDecoder.cs と同様、ZXing.Net導入時のみ有効化される。
// 導入手順は Assets/Scripts/QR/ZXingQrDecoder.cs のコメントを参照。
#if ORISAMO_ZXING
using UnityEngine;
using ZXing;
using ZXing.QrCode;

namespace Orisamo.QR
{
    /// <summary>
    /// ZXing.Netを用いたQRコード生成実装。印刷用データ出力・カード確認画面での表示に使用する。
    /// </summary>
    public class ZXingQrEncoder : IQrEncoder
    {
        public Texture2D Encode(string content, int size = 256)
        {
            var writer = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new QrCodeEncodingOptions
                {
                    Height = size,
                    Width = size,
                    Margin = 1
                }
            };

            var pixelData = writer.Write(content);
            var texture = new Texture2D(pixelData.Width, pixelData.Height, TextureFormat.RGBA32, false);
            texture.LoadRawTextureData(pixelData.Pixels);
            texture.Apply();
            return texture;
        }
    }
}
#endif
