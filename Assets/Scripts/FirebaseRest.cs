using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 会場PC(Unity)から Firebase Realtime Database を REST API で読み書きするための共通入口。
///
/// ・接続先(databaseUrl / apiKey / tableId)は Resources/FirebaseSettings.json の1か所で管理する。
///   tableId はコマンドライン引数 -table &lt;卓ID&gt; で上書きできる(複数卓の同時運用用)。
/// ・Firebase Authentication の匿名認証でIDトークンを取得し、すべてのリクエストに付ける。
///   リフレッシュトークンを PlayerPrefs に保存するので、同じPCは常に同じUIDになる。
///   このUIDを Database の /admins/{uid} に登録すると、Database Rules 上で会場PCとして扱われる。
/// ・匿名認証が無効などでトークンを取得できない場合は、トークンなしで続行する
///   (ルールを公開設定のまま運用している間も動作させるため)。
/// </summary>
public static class FirebaseRest
{
    [Serializable]
    private class Settings
    {
        public string databaseUrl;
        public string apiKey;
        public string tableId;
    }

    [Serializable]
    private class SignUpResponse
    {
        public string idToken;
        public string refreshToken;
        public string expiresIn;
        public string localId;
    }

    [Serializable]
    private class RefreshResponse
    {
        public string id_token;
        public string refresh_token;
        public string expires_in;
        public string user_id;
    }

    private const string RefreshTokenPrefsKey = "Orisamo.FirebaseRefreshToken";

    private static Settings settings;
    private static string idToken;
    private static DateTime idTokenExpiresAt;
    private static bool authUnavailable;
    private static float authRetryAt;               // 認証に失敗した後、次に再試行してよい時刻(Time.realtimeSinceStartup)
    private const float AuthRetryIntervalSeconds = 30f;

    /// <summary>直近の認証エラーの説明(画面表示用)。認証できていれば null。</summary>
    public static string LastAuthError { get; private set; }

    /// <summary>認証済みのトークンを持っているか。持っていない場合、Database Rules により卓のデータは読み書きできない。</summary>
    public static bool IsAuthenticated => !string.IsNullOrEmpty(idToken);

    public static string DatabaseUrl => LoadSettings().databaseUrl;
    public static string TableId => LoadSettings().tableId;
    public static string Uid { get; private set; }

    /// <summary>この卓のデータを置くパス(例: tables/table1)。</summary>
    public static string TablePath => $"tables/{TableId}";

    private static Settings LoadSettings()
    {
        if (settings != null) return settings;

        TextAsset asset = Resources.Load<TextAsset>("FirebaseSettings");
        settings = asset != null ? JsonUtility.FromJson<Settings>(asset.text) : new Settings();
        if (asset == null) Debug.LogError("FirebaseRest: Resources/FirebaseSettings.json が見つかりません。");

        settings.databaseUrl = (settings.databaseUrl ?? "").TrimEnd('/');
        if (string.IsNullOrEmpty(settings.tableId)) settings.tableId = "table1";

        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-table" && !string.IsNullOrEmpty(args[i + 1])) settings.tableId = args[i + 1];
        }
        return settings;
    }

    /// <summary>パス(先頭スラッシュなし)とクエリ(省略可)からリクエストURLを組み立てる。認証トークンも付ける。</summary>
    public static string BuildUrl(string path, string query = null)
    {
        string url = $"{DatabaseUrl}/{path}.json";
        string sep = "?";
        if (!string.IsNullOrEmpty(query))
        {
            url += sep + query;
            sep = "&";
        }
        if (!string.IsNullOrEmpty(idToken)) url += sep + "auth=" + UnityWebRequest.EscapeURL(idToken);
        return url;
    }

    /// <summary>有効なIDトークンを用意する。取得できなかった場合もエラーにはせず、トークンなしで続行する。</summary>
    public static IEnumerator EnsureAuth()
    {
        if (authUnavailable) yield break;
        if (!string.IsNullOrEmpty(idToken) && DateTime.UtcNow < idTokenExpiresAt) yield break;
        // 失敗直後は毎回のリクエストで認証を叩かない(一定時間おいて再試行する。以前は一度の失敗で二度と認証しなかった)
        if (string.IsNullOrEmpty(idToken) && Time.realtimeSinceStartup < authRetryAt) yield break;

        string apiKey = LoadSettings().apiKey;
        if (string.IsNullOrEmpty(apiKey))
        {
            authUnavailable = true;
            Debug.LogWarning("FirebaseRest: apiKey が未設定のため、認証なしで接続します。");
            yield break;
        }

        string refreshToken = PlayerPrefs.GetString(RefreshTokenPrefsKey, "");
        if (!string.IsNullOrEmpty(refreshToken))
        {
            string form = "grant_type=refresh_token&refresh_token=" + UnityWebRequest.EscapeURL(refreshToken);
            using (UnityWebRequest req = CreateRequest("https://securetoken.googleapis.com/v1/token?key=" + apiKey,
                       "POST", form, "application/x-www-form-urlencoded"))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    RefreshResponse res = JsonUtility.FromJson<RefreshResponse>(req.downloadHandler.text);
                    StoreToken(res.id_token, res.refresh_token, res.expires_in, res.user_id);
                    yield break;
                }
                Debug.LogWarning("FirebaseRest: トークンの更新に失敗したため、匿名サインインをやり直します: " + req.error + " " + req.downloadHandler.text);
            }
        }

        using (UnityWebRequest req = CreateRequest("https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=" + apiKey,
                   "POST", "{\"returnSecureToken\":true}", "application/json"))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                string body = req.downloadHandler != null ? req.downloadHandler.text : "";
                LastAuthError = DescribeAuthError(body, req.error);
                authRetryAt = Time.realtimeSinceStartup + AuthRetryIntervalSeconds;
                Debug.LogError("FirebaseRest: 匿名認証に失敗しました。認証なしでは卓のデータを読み書きできないため、オンライン対戦は始まりません。\n" +
                               LastAuthError + "\n応答: " + req.error + " " + body);
                yield break;
            }

            SignUpResponse res = JsonUtility.FromJson<SignUpResponse>(req.downloadHandler.text);
            StoreToken(res.idToken, res.refreshToken, res.expiresIn, res.localId);
            Debug.LogWarning($"FirebaseRest: 会場PCのUIDは {Uid} です。Database の /admins/{Uid} に true を登録してください。");
        }
    }

    /// <summary>Firebase Auth のエラー応答から、運営者が何をすればよいかの説明を作る。</summary>
    private static string DescribeAuthError(string body, string error)
    {
        string b = body ?? "";
        if (b.Contains("OPERATION_NOT_ALLOWED") || b.Contains("ADMIN_ONLY_OPERATION"))
            return "Firebaseコンソール > Authentication > ログイン方法 で「匿名」を有効にしてください。";
        if (b.Contains("API_KEY_INVALID") || b.Contains("API key not valid"))
            return "FirebaseSettings.json の apiKey が正しくありません。Firebaseコンソールのプロジェクト設定からウェブAPIキーをコピーし直してください。";
        if (b.Contains("referer") || b.Contains("REFERRER") || b.Contains("API_KEY_SERVICE_BLOCKED") || b.Contains("blocked"))
            return "APIキーに制限(HTTPリファラー/アプリ制限)がかかっていて会場PCから使えません。Google Cloud コンソール > 認証情報 で、" +
                   "会場PC用に制限なし(またはIdentity Toolkit / Token Service APIのみ許可)のキーを作り FirebaseSettings.json に設定してください。";
        if (b.Contains("TOO_MANY_ATTEMPTS"))
            return "匿名アカウントの作成回数の上限に達しました。しばらく待ってから再起動してください。";
        return "匿名認証に失敗しました(" + error + ")。ネットワーク・プロキシ設定と Firebase の設定を確認してください。";
    }

    private static void StoreToken(string token, string refreshToken, string expiresIn, string uid)
    {
        LastAuthError = null;
        idToken = token;
        Uid = uid;
        int seconds = int.TryParse(expiresIn, out int s) ? s : 3600;
        idTokenExpiresAt = DateTime.UtcNow.AddSeconds(Math.Max(seconds - 300, 60));
        PlayerPrefs.SetString(RefreshTokenPrefsKey, refreshToken);
        PlayerPrefs.Save();
    }

    public static IEnumerator Get(string path, Action<UnityWebRequest> onDone, string query = null)
    {
        yield return EnsureAuth();
        using (UnityWebRequest req = UnityWebRequest.Get(BuildUrl(path, query)))
        {
            yield return req.SendWebRequest();
            onDone?.Invoke(req);
        }
    }

    /// <summary>PUT(置換) または PATCH(部分更新)。json に "null" を渡すと削除になる。</summary>
    public static IEnumerator Write(string method, string path, string json, Action<bool> onDone = null)
    {
        yield return EnsureAuth();
        using (UnityWebRequest req = CreateRequest(BuildUrl(path), method, json, "application/json"))
        {
            yield return req.SendWebRequest();
            bool ok = req.result == UnityWebRequest.Result.Success;
            if (!ok) Debug.LogWarning($"FirebaseRest: {method} {path} に失敗しました: {req.error} {req.downloadHandler.text}");
            onDone?.Invoke(ok);
        }
    }

    /// <summary>
    /// 完了を待たずに書き込みを送る(シーン破棄の直前など、コルーチンを回せない場面用)。
    /// 既に取得済みのトークンを使う。
    /// </summary>
    public static void WriteFireAndForget(string method, string path, string json)
    {
        UnityWebRequest req = CreateRequest(BuildUrl(path), method, json, "application/json");
        UnityWebRequestAsyncOperation op = req.SendWebRequest();
        op.completed += _ => req.Dispose();
    }

    private static UnityWebRequest CreateRequest(string url, string method, string body, string contentType)
    {
        UnityWebRequest req = new UnityWebRequest(url, method)
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
            downloadHandler = new DownloadHandlerBuffer()
        };
        req.SetRequestHeader("Content-Type", contentType);
        return req;
    }
}
