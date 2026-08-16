using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro; // TextMeshProを使用。通常のUI.Textを使う場合は using UnityEngine.UI; に変更してください

/// <summary>
/// Unity（パソコン側）を簡易HTTPサーバーとして動かし、スマホのブラウザ
/// （GitHub Pagesで公開したQRスキャンページ）から送られてくるQRコードの
/// 読み取り結果(QRCardData のJSON)を受け取るコンポーネント。
///
/// 【全体の流れ】
/// 1. スマホでGitHub Pages上のスキャンページ(qr-scan-site)を開き、カメラでQRコードを読み取る
/// 2. スキャンページがQRの中身(JSON文字列)をこのUnityサーバーへ POST /scan で送信する
/// 3. このスクリプトが受信し、ProcessCardJson デリゲートを呼び出してキャラクターを生成する
///    (Unityのメインスレッドから呼ばれる)
/// 4. 生成結果(ステータスのJSON)をそのままHTTPレスポンスとしてスマホへ返す
/// 5. スマホ側(app.js)がレスポンスを受け取り、ステータス画面を表示する
///
/// つまりQRコードそのものを読み取るカメラ処理も、キャラクターのステータス表示も
/// スマホのブラウザ側が担当し、Unity（パソコン）はキャラクター生成の計算だけを行う
/// 「バックエンドサーバー」になる。
///
/// 【セットアップ方法】
/// 1. QRScanシーンに空のGameObjectを作成し、このスクリプトをアタッチ
/// 2. port は初期値のままでもOK（他のアプリとポートが衝突する場合のみ変更）
/// 3. serverInfoText（任意）: TextMeshProUGUIをドラッグすると、スマホに入力してもらう
///    サーバーアドレス（例: http://192.168.1.10:8080）を画面に表示できる
/// 4. scanPageUrl（任意）: GitHub Pagesで公開したスキャンページのURLを入力しておくと、
///    serverUrlQrImage に「スキャンページ＋サーバーアドレス」を埋め込んだQRコードを
///    表示できる。スマホでこのQRコードを最初に一度読み取れば、
///    サーバーアドレスの手入力が不要になる
/// 5. ProcessCardJson に、キャラクター生成処理(QRCharacterRegistrarなど)を設定する
///
/// 【重要：Windowsでの注意点】
/// localhostOnly が false（LAN内の他端末からのアクセスを受け付ける設定）の場合、
/// HttpListenerで外部（スマホ）からのアクセスを受け付けるには、管理者権限で実行するか、
/// 事前に以下のコマンドをコマンドプロンプト（管理者権限で起動）で一度だけ実行しておく必要があります。
///     netsh http add urlacl url=http://+:8080/ user=Everyone
/// （portを変更した場合は8080の部分も合わせて変更してください）
///
/// もし対象のUnityのAPI Compatibility LevelでSystem.Net.HttpListenerが見つからない
/// というコンパイルエラーが出た場合は、
/// Project Settings > Player > Other Settings > Api Compatibility Level を
/// ".NET Framework"（または.NET Standard 2.1以降）に変更してください。
///
/// 【重要：ネットワークと「混合コンテンツ」について】
/// パソコンとスマホは同じWi-Fi（同一ローカルネットワーク）に接続している必要があります。
/// また、GitHub Pages(https)のページから、この http のサーバーへ通信しようとすると、
/// ブラウザの「混合コンテンツ(Mixed Content)」制限でブロックされることがあります
/// （特にiPhoneのSafari等）。その場合は localhostOnly をオンにしたうえで、
/// ngrok や Cloudflare Tunnel 等でこのサーバーをhttps化して公開し、
/// 発行されたhttps URLをスマホ側に入力する方法に切り替えてください。
/// </summary>
public class QRWebServer : MonoBehaviour
{
    [Tooltip("サーバーが待ち受けるポート番号")]
    [SerializeField] private int port = 8080;

    [Tooltip("trueならlocalhost限定で待ち受ける(ngrok等でトンネルする場合。管理者権限やnetsh設定は不要)。\n" +
             "falseならLAN内の全アクセスを受け付ける(スマホから直接アクセスする場合。管理者権限 or netsh設定が必要)")]
    [SerializeField] private bool localhostOnly = false;

    [Tooltip("サーバーの状態・アドレスを表示するテキスト（任意）")]
    [SerializeField] private TextMeshProUGUI serverInfoText;

    [Header("接続用QRコード（任意）")]
    [Tooltip("スキャンページのURL（GitHub Pagesで公開したURL）。設定すると接続用QRコードを表示できる")]
    [SerializeField] private string scanPageUrl = "";

    [Tooltip("「スキャンページURL＋このサーバーのアドレス」を埋め込んだQRコードの表示先")]
    [SerializeField] private RawImage serverUrlQrImage;

    [Tooltip("接続用QRコードの画像サイズ（ピクセル）")]
    [SerializeField] private int qrCodeSize = 384;

    [Tooltip("スマホからのリクエストを処理する際、メインスレッドでの処理完了を待つ最大秒数")]
    [SerializeField] private float processTimeoutSeconds = 5f;

    /// <summary>
    /// QRコードの中身(JSON文字列)を受け取り、スマホへ返すレスポンスのJSON文字列を返すデリゲート。
    /// 必ずUnityのメインスレッド(Update)から呼び出される。
    /// nullまたは空文字を返した場合は、汎用の {"status":"ok"} が返される。
    /// 例外を投げた場合は {"status":"error"} が返される。
    /// </summary>
    public Func<string, string> ProcessCardJson;

    private HttpListener listener;
    private Thread listenerThread;
    private readonly ConcurrentQueue<PendingScan> receivedQueue = new ConcurrentQueue<PendingScan>();
    private volatile bool isRunning = false;

    /// <summary>1件のスキャンリクエストと、その処理結果を橋渡しするためのオブジェクト。</summary>
    private class PendingScan
    {
        public string RequestJson;
        public string ResponseJson;
        public readonly ManualResetEventSlim WaitHandle = new ManualResetEventSlim(false);
    }

    private void Start()
    {
        StartServer();
    }

    private void Update()
    {
        // 受信したデータをメインスレッドで処理する(Unity APIはメインスレッド以外から呼べないため、
        // HttpListenerのバックグラウンドスレッドではキャラクター生成処理を直接呼ばず、キューに貯めておく)
        while (receivedQueue.TryDequeue(out PendingScan pending))
        {
            string responseJson = null;
            try
            {
                responseJson = ProcessCardJson?.Invoke(pending.RequestJson);
            }
            catch (Exception e)
            {
                Debug.LogError("QRWebServer: カード処理中にエラーが発生しました: " + e.Message);
            }

            pending.ResponseJson = string.IsNullOrEmpty(responseJson) ? "{\"status\":\"ok\"}" : responseJson;
            pending.WaitHandle.Set(); // HTTPスレッド側の待機を解除し、レスポンスを返させる
        }
    }

    private void StartServer()
    {
        string prefix = localhostOnly ? $"http://localhost:{port}/" : $"http://+:{port}/";

        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();
            isRunning = true;

            listenerThread = new Thread(ListenLoop) { IsBackground = true };
            listenerThread.Start();

            if (localhostOnly)
            {
                Debug.Log($"QRWebServerをlocalhost限定で起動しました(ポート:{port})。ngrok等でこのポートをトンネルし、発行されたhttps URLをスマホ側に入力してください。");
                SetInfoText($"サーバー起動中（localhost限定）\nポート: {port}\n\nngrok等でこのポートを公開し、\n発行されたURLをスマホに入力してください");
            }
            else
            {
                string localIp = GetLocalIPAddress();
                string serverUrl = $"http://{localIp}:{port}";
                Debug.Log($"QRWebServerを起動しました: {serverUrl}");
                SetInfoText($"サーバー起動中\n{serverUrl}\n\nスマホでこのアドレスを入力してください");
                DisplayConnectionQRCode(serverUrl);
            }
        }
        catch (HttpListenerException e)
        {
            Debug.LogError(
                "HTTPサーバーの起動に失敗しました。管理者権限で実行するか、コマンドプロンプト(管理者権限)で以下を一度だけ実行してください:\n" +
                $"netsh http add urlacl url=http://+:{port}/ user=Everyone\n詳細: " + e.Message);
            SetInfoText("サーバー起動に失敗しました\n（管理者権限 or netsh設定が必要です。コンソールログを確認してください）");
        }
        catch (Exception e)
        {
            Debug.LogError("QRWebServerの起動に失敗しました: " + e.Message);
            SetInfoText("サーバー起動に失敗しました: " + e.Message);
        }
    }

    private void ListenLoop()
    {
        while (isRunning)
        {
            try
            {
                HttpListenerContext context = listener.GetContext(); // リクエストが来るまでブロックする
                HandleRequest(context);
            }
            catch (HttpListenerException)
            {
                // Stop()時にGetContext()が例外を投げるのは正常な終了パスなので無視する
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception e)
            {
                Debug.LogError("QRWebServer: リクエスト処理中にエラーが発生しました: " + e.Message);
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        // どのオリジン（GitHub Pagesを含む）からのアクセスも許可する
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

        try
        {
            if (request.HttpMethod == "OPTIONS")
            {
                // ブラウザが送るプリフライトリクエストへの応答
                response.StatusCode = 204;
                response.Close();
                return;
            }

            string path = request.Url.AbsolutePath.TrimEnd('/');

            if (request.HttpMethod == "POST" && path == "/scan")
            {
                string body;
                using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
                {
                    body = reader.ReadToEnd();
                }

                var pending = new PendingScan { RequestJson = body };
                receivedQueue.Enqueue(pending);

                // メインスレッド(Update)でキャラクター生成が終わるまで、このHTTPスレッドで待つ
                bool completed = pending.WaitHandle.Wait(TimeSpan.FromSeconds(processTimeoutSeconds));

                if (completed)
                {
                    WriteResponse(response, 200, pending.ResponseJson);
                }
                else
                {
                    Debug.LogWarning("QRWebServer: カード処理がタイムアウトしました。ProcessCardJsonが設定されているか確認してください。");
                    WriteResponse(response, 504, "{\"status\":\"timeout\"}");
                }
                return;
            }

            if (request.HttpMethod == "GET")
            {
                // 動作確認用（ブラウザで http://IP:PORT/ を開いた時にサーバーが生きているか確認できる）
                WriteResponse(response, 200, "QRWebServer is running.");
                return;
            }

            WriteResponse(response, 404, "{\"status\":\"not_found\"}");
        }
        catch (Exception e)
        {
            Debug.LogError("QRWebServer: リクエスト応答中にエラーが発生しました: " + e.Message);
            try { WriteResponse(response, 500, "{\"status\":\"error\"}"); } catch { /* 応答済みの場合などは無視 */ }
        }
    }

    private void WriteResponse(HttpListenerResponse response, int statusCode, string body)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(body);
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }

    /// <summary>
    /// scanPageUrlが設定されていれば、「スキャンページURL?server=このサーバーのアドレス」を
    /// 埋め込んだQRコードを表示する。スマホで一度読み取れば、サーバーアドレスの手入力が不要になる。
    /// </summary>
    private void DisplayConnectionQRCode(string serverUrl)
    {
        if (serverUrlQrImage == null || string.IsNullOrEmpty(scanPageUrl))
        {
            return;
        }

        string separator = scanPageUrl.Contains("?") ? "&" : "?";
        string connectUrl = $"{scanPageUrl}{separator}server={UnityWebRequest.EscapeURL(serverUrl)}";

        Texture2D qrTexture = QRCodeGenerator.GenerateTexture(connectUrl, qrCodeSize);
        if (qrTexture != null)
        {
            serverUrlQrImage.texture = qrTexture;
            serverUrlQrImage.gameObject.SetActive(true);
        }
    }

    /// <summary>このPCのローカルIPv4アドレスを取得する（複数ある場合は最初の1つ）。</summary>
    private string GetLocalIPAddress()
    {
        try
        {
            foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("ローカルIPアドレスの取得に失敗しました: " + e.Message);
        }

        return "127.0.0.1"; // 取得できなかった場合のフォールバック
    }

    private void SetInfoText(string text)
    {
        if (serverInfoText != null)
        {
            serverInfoText.text = text;
        }
    }

    private void OnDestroy()
    {
        StopServer();
    }

    private void StopServer()
    {
        isRunning = false;

        if (listener != null)
        {
            try
            {
                listener.Stop();
                listener.Close();
            }
            catch (Exception e)
            {
                Debug.LogWarning("QRWebServerの停止中にエラーが発生しました: " + e.Message);
            }
        }
    }
}
