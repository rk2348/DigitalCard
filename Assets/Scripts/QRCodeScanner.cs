using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro; // TextMeshProを使用。通常のUI.Textを使う場合は using UnityEngine.UI; に変更してください

#if ORISAMO_ZXING
using ZXing;
using ZXing.Common;
#endif

/// <summary>
/// デバイスのカメラを使ってQRコードをリアルタイムでスキャンするコンポーネント。
/// 読み取りに成功すると OnQRCodeScanned イベントでテキストを通知する。
///
/// 「カメラが正しく映っているか確認する画面」として、以下を備えている：
/// ・カメラ映像のプレビュー表示（向き・縦横比を実機に合わせて自動補正）
/// ・カメラの状態テキスト（起動中／カメラが見つからない／スキャン中／読み取り成功）
/// ・QRコードを検出した瞬間に光る枠（任意）
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
/// 2. カメラ映像をプレビュー表示するため、Canvas上にRawImageを配置し
///    previewImage にドラッグ
///    ・映像が伸び縮みして見える場合は、RawImageに AspectRatioFitter
///      コンポーネントを追加してください（自動でアスペクト比を設定します）
/// 3. （任意）カメラの状態を表示するTextMeshProUGUIを配置し cameraStatusText にドラッグ
/// 4. （任意）QRコードを検出した瞬間に色を変えて知らせたい場合、
///    プレビューの周りに枠用のImageを配置し scanIndicatorImage にドラッグ
/// </summary>
public class QRCodeScanner : MonoBehaviour
{
    [Header("カメラ確認画面")]
    [Tooltip("カメラ映像のプレビュー表示先")]
    [SerializeField] private RawImage previewImage;

    [Tooltip("カメラの状態を表示するテキスト（任意）：起動中／見つからない／スキャン中／読み取り成功")]
    [SerializeField] private TextMeshProUGUI cameraStatusText;

    [Tooltip("QRコードを検出した瞬間に色を変える枠（任意）")]
    [SerializeField] private Image scanIndicatorImage;

    [Tooltip("scanIndicatorImageのスキャン中の色")]
    [SerializeField] private Color scanningColor = Color.white;

    [Tooltip("scanIndicatorImageの検出成功時の色")]
    [SerializeField] private Color detectedColor = Color.green;

    [Tooltip("検出成功時、何秒間 detectedColor を表示し続けるか")]
    [SerializeField] private float detectedColorDuration = 0.5f;

    [Header("スキャン設定")]
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
    private bool hasCameraStartedStreaming = false;

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
        SetCameraStatus("カメラを起動中...");

        if (WebCamTexture.devices.Length == 0)
        {
            Debug.LogError("利用可能なカメラが見つかりません。");
            SetCameraStatus("カメラが見つかりません");
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

        SetIndicatorColor(scanningColor);
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

        // カメラ映像が実際に流れ始めたタイミングで、プレビューの向き・縦横比を補正する
        if (!hasCameraStartedStreaming && webCamTexture.width > 16)
        {
            hasCameraStartedStreaming = true;
            AdjustPreviewOrientation();
            SetCameraStatus("スキャン中...カードをカメラに映してください");
        }

        scanTimer += Time.deltaTime;
        if (scanTimer < scanInterval) return;
        scanTimer = 0f;

        TryDecodeFrame();
    }

    /// <summary>
    /// 実機のカメラの向き・ミラー設定に合わせて、プレビュー映像の回転とアスペクト比を補正する。
    /// （これをやらないと、スマホ実機でプレビューが横倒しや左右反転で映ることがある）
    /// </summary>
    private void AdjustPreviewOrientation()
    {
        if (previewImage == null) return;

        // 回転補正
        previewImage.rectTransform.localEulerAngles = new Vector3(0f, 0f, -webCamTexture.videoRotationAngle);

        // 上下反転補正（フロントカメラ等でミラーされている場合）
        float scaleY = webCamTexture.videoVerticallyMirrored ? -1f : 1f;
        previewImage.rectTransform.localScale = new Vector3(1f, scaleY, 1f);

        // 縦横比補正（AspectRatioFitterが付いていれば自動で反映される）
        AspectRatioFitter fitter = previewImage.GetComponent<AspectRatioFitter>();
        if (fitter != null && webCamTexture.height > 0)
        {
            fitter.aspectRatio = (float)webCamTexture.width / webCamTexture.height;
        }
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

        SetCameraStatus("読み取り成功！");
        FlashDetectedIndicator();

        OnQRCodeScanned?.Invoke(text);
    }

    /// <summary>
    /// QRコードを検出した瞬間、枠の色を一瞬変えてフィードバックする。
    /// </summary>
    private void FlashDetectedIndicator()
    {
        if (scanIndicatorImage == null) return;

        StopAllCoroutines();
        StartCoroutine(FlashIndicatorRoutine());
    }

    private IEnumerator FlashIndicatorRoutine()
    {
        SetIndicatorColor(detectedColor);
        yield return new WaitForSeconds(detectedColorDuration);
        SetIndicatorColor(scanningColor);
    }

    private void SetIndicatorColor(Color color)
    {
        if (scanIndicatorImage != null)
        {
            scanIndicatorImage.color = color;
        }
    }

    private void SetCameraStatus(string text)
    {
        if (cameraStatusText != null)
        {
            cameraStatusText.text = text;
        }
    }

    private void OnDestroy()
    {
        if (webCamTexture != null && webCamTexture.isPlaying)
        {
            webCamTexture.Stop();
        }
    }
}