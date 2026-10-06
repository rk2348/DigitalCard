using System.Collections;
using UnityEngine;

/// <summary>
/// PC(Unity)の画面が開いていることを、スマホ側(docs/app.js)に知らせる生存信号。
/// /tables/{tableId}/display/heartbeat に数秒ごとにサーバー時刻を書き込む。
/// スマホは この時刻が古い(=Unityが起動していない)間、QRコードを読み取らせない。
///
/// シーンに配置する必要はない(起動時に自動で生成し、シーンをまたいで動き続ける)。
/// </summary>
public sealed class DisplayPresence : MonoBehaviour
{
    private const float IntervalSeconds = 5f;

    private string HeartbeatPath => $"{FirebaseRest.TablePath}/display";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        GameObject go = new GameObject("DisplayPresence");
        DontDestroyOnLoad(go);
        go.AddComponent<DisplayPresence>();
    }

    private IEnumerator Start()
    {
        WaitForSecondsRealtime wait = new WaitForSecondsRealtime(IntervalSeconds);
        while (true)
        {
            yield return FirebaseRest.Write("PUT", HeartbeatPath, "{\"heartbeat\":{\".sv\":\"timestamp\"}}");
            yield return wait;
        }
    }

    private void OnApplicationQuit()
    {
        // 終了したことをすぐに伝える(届かなくても、スマホ側は時刻が古くなった時点で読み取りを止める)
        FirebaseRest.WriteFireAndForget("PUT", HeartbeatPath, "null");
    }
}
