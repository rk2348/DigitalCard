using UnityEngine;
#if ORISAMO_ZXING
using ZXing;
using ZXing.QrCode;
#endif

/// <summary>
/// キャラクターステータスをQRコード画像(Texture2D)として生成するユーティリティ。
///
/// ZXing.Netがプロジェクトに導入されている場合のみ動作する(ORISAMO_ZXINGシンボルで制御)。
/// オリサモ本編プロジェクトと同じ方式(ORISAMO_ZXINGシンボルでZXing統合をゲート)に揃えてある。
///
/// 【事前準備】
/// 1. NuGetForUnity等でZXing.Netパッケージをプロジェクトに導入
/// 2. Edit > Project Settings > Player > Scripting Define Symbols に "ORISAMO_ZXING" を追加
/// </summary>
public static class QRCodeGenerator
{
    /// <summary>
    /// 指定した文字列からQRコードのTexture2Dを生成する。
    /// ZXing.Net未導入の場合はnullを返す(呼び出し側でnullチェックすること)。
    /// </summary>
    public static Texture2D GenerateTexture(string content, int size = 512)
    {
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

#if ORISAMO_ZXING
        var writer = new BarcodeWriter
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                Height = size,
                Width = size,
                Margin = 1,
                ErrorCorrection = ZXing.QrCode.Internal.ErrorCorrectionLevel.M
            }
        };

        Color32[] pixelData = writer.Write(content);

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixelData);
        texture.Apply();
        return texture;
#else
        Debug.LogWarning("ZXing.Netが導入されていないため、QRコードを生成できません。ORISAMO_ZXINGシンボルを追加してください。");
        return null;
#endif
    }
}
