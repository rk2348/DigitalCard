// このファイルはZXing.Net（またはZXingベースのUnityアセット）を導入した場合のみ有効化される。
// 導入手順:
//   1. ZXing.Net（NuGet/UPM/Asset Store等）をプロジェクトに追加
//   2. Project Settings > Player > Scripting Define Symbols に ORISAMO_ZXING を追加
// 未導入の間はコンパイル対象から除外されるため、プロジェクトはエラーなくビルドできる。
#if ORISAMO_ZXING
using UnityEngine;
using ZXing;
using ZXing.Common;

namespace Orisamo.QR
{
    /// <summary>
    /// ZXing.Netを用いたQRコードデコーダー実装。
    /// </summary>
    public class ZXingQrDecoder : IQrDecoder
    {
        private readonly BarcodeReader _reader;

        public ZXingQrDecoder()
        {
            _reader = new BarcodeReader
            {
                AutoRotate = false,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = new[] { BarcodeFormat.QR_CODE }
                }
            };
        }

        public bool TryDecode(Color32[] pixels, int width, int height, out string decodedText)
        {
            var result = _reader.Decode(pixels, width, height);
            decodedText = result?.Text;
            return result != null;
        }
    }
}
#endif
