using UnityEngine;

/// <summary>
/// 2画面展示で、もう一方の画面(印刷用のブラウザなど)を操作してゲームのウィンドウが
/// 選ばれていない状態になっても、ゲームを止めずに動かし続ける。
/// プロジェクト設定の「Run In Background」がオフでも、起動時に必ずオンにする。
/// (シーンに置く必要はない。最初のシーンを読み込む前に自動で実行される)
/// </summary>
public static class KeepRunningInBackground
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        Application.runInBackground = true;
    }
}
