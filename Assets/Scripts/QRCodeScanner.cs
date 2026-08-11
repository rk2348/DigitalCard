using System;
using UnityEngine;
using UnityEngine.UI;

#if ORISAMO_ZXING
using ZXing;
using ZXing.Common;
#endif

/// <summary>
/// デバイスのカメラを使ってQRコードをリアルタイムでスキャンするコンポーネント。
/// 読み取りに成功すると OnQRCodeScanned イベントでテキストを通知する。
///
/// 【必要な準備】
/// 1. ZXing.Net（QR/バーコード読み取りライブラリ）をプロジェクトに導入
///    （NuGetForUnity経由、または zxing.unity 系の .unitypackage を利用）
/// 2. Edit > Project Settings > Player > Scripting Define Symbols に
///    "ORISAMO_ZXING" を追加する
///    ※ 未追加の場合、実機カメラは起動するがQRの解析は行われず、
///       代わりに下記の「開発用テスト機能」でUIの動作確認ができる
/// 3. Android/iOSで実機ビルドする場合はカメラ権限の設定を忘れずに
///    （Player Settings > Android/iOS > Camera Usage Description 等）
///
/// 【セットアップ方法】
/// 1. シーンに空のGameObjectを作成し、このスクリプトをアタッチ
/// 2. カメラ映像をプレビュー表示したい場合は、Canvas上にRawImageを配置し
///    previewImage にドラッグ（任意、無くても解析自体は動作します）
/// </summary>
public class QRCodeScanner : MonoBehaviour
{
    [Tooltip("カメラ映像のプレビュー表示先（任意）")]
    [SerializeField] private RawImage previewImage;

    [Tooltip("スキャン間隔（秒）。負荷軽減のため毎フレームではなく間隔を空けて解析する")]
    [SerializeField] private float scanInterval = 0.3f;

    [Tooltip("同じ内容のQRコードを連続で読み取らないようにするクールダウン時間（秒）")]
    [SerializeField] private float duplicateCooldown = 2f;

#if !ORISAMO_ZXING
    [Header("開発用テスト機能（ZXing.Net未導入時のみ使用）")]
    [Tooltip("このキーを押すと、実際のQRコードの代わりに下のテストJSONを読み取ったことにする")]
    [SerializeField] private KeyCode debugScanKey = KeyCode.T;

    [TextArea(3, 6)]
    [SerializeField]
    private string debugScanJson =
        "{\"characterName\":\"テストキャラ\",\"attack\":22,\"defense\":14,\"speed\":19,\"hp\":100,\"maxHp\":100," +
        "\"element\":0,\"skill\":{\"skillName\":\"疾風の一撃\",\"skillType\":0,\"ratio\":0.3},\"isMutation\":false}";
#endif

    /// <summary>QRコードのデコードに成功した時に発火（引数は読み取った文字列）</summary>
    public event Action<string> OnQRCodeScanned;

    private WebCamTexture webCamTexture;
    private float scanTimer;
    private string lastScannedText;
    private float lastScannedTime = -999f;

#if ORISAMO_ZXING
    private BarcodeReader barcodeReader;
#endif

    private void Start()
    {
        StartCamera();

#if ORISAMO_ZXING
        barcodeReader = new BarcodeReader
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = new System.Collections.Generic.List<BarcodeFormat> { BarcodeFormat.QR_CODE }
            }
        };
#else
        Debug.LogWarning("ORISAMO_ZXING が未定義のため、実際のQRコード解析は行われません。" +
                          "ZXing.Netを導入し、Scripting Define SymbolsにORISAMO_ZXINGを追加してください。" +
                          $"（それまでは{debugScanKey}キーでテスト用のスキャンを疑似的に発生させられます）");
#endif
    }

    private void StartCamera()
    {
        if (WebCamTexture.devices.Length == 0)
        {
            Debug.LogError("利用可能なカメラが見つかりません。");
            return;
        }

        // 背面カメラを優先的に選択（スマホでの読み取りを想定）
        string deviceName = WebCamTexture.devices[0].name;
        foreach (var device in WebCamTexture.devices)
        {
            if (!device.isFrontFacing)
            {
                deviceName = device.name;
                break;
            }
        }

        webCamTexture = new WebCamTexture(deviceName);
        webCamTexture.Play();

        if (previewImage != null)
        {
            previewImage.texture = webCamTexture;
        }
    }

    private void Update()
    {
#if !ORISAMO_ZXING
        // ZXing.Net未導入時：指定キーでテスト用のスキャン結果を疑似発生させる
        if (Input.GetKeyDown(debugScanKey))
        {
            HandleScanResult(debugScanJson);
        }
#endif

        if (webCamTexture == null || !webCamTexture.didUpdateThisFrame) return;

        scanTimer += Time.deltaTime;
        if (scanTimer < scanInterval) return;
        scanTimer = 0f;

        TryDecodeFrame();
    }

    private void TryDecodeFrame()
    {
#if ORISAMO_ZXING
        if (barcodeReader == null) return;

        Color32[] pixels = webCamTexture.GetPixels32();
        var result = barcodeReader.Decode(pixels, webCamTexture.width, webCamTexture.height);

        if (result != null && !string.IsNullOrEmpty(result.Text))
        {
            HandleScanResult(result.Text);
        }
#endif
    }

    private void HandleScanResult(string text)
    {
        // 直前に同じ内容を読み取っていた場合、クールダウン中は無視する（連続反応の防止）
        if (text == lastScannedText && Time.time - lastScannedTime < duplicateCooldown)
        {
            return;
        }

        lastScannedText = text;
        lastScannedTime = Time.time;

        OnQRCodeScanned?.Invoke(text);
    }

    private void OnDestroy()
    {
        if (webCamTexture != null && webCamTexture.isPlaying)
        {
            webCamTexture.Stop();
        }
    }
}
