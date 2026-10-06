using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 画面の右下にアプリのバージョン(Player Settings の Version = Application.version)を常に表示する。
/// Webサイト(docs/app.js の APP_VERSION)と同じ番号にしておくと、会場でどの版が動いているか確認できる。
///
/// シーンに配置する必要はない(起動時に自動で生成し、シーンをまたいで表示し続ける)。
/// </summary>
public sealed class VersionLabel : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        GameObject go = new GameObject("VersionLabel", typeof(Canvas), typeof(CanvasScaler));
        DontDestroyOnLoad(go);

        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000; // どの画面よりも手前に出す

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = OrisamoUI.ReferenceResolution;
        scaler.matchWidthOrHeight = 0.5f;

        TextMeshProUGUI label = OrisamoUI.CreateText("Version", go.transform, "ver " + Application.version, 44f,
            OrisamoUI.WithAlpha(OrisamoUI.Muted, 0.85f), TextAlignmentOptions.BottomRight, FontStyles.Normal);
        OrisamoUI.Place(label.rectTransform, new Vector2(1f, 0f), new Vector2(-36f, 24f), new Vector2(600f, 70f));
        OrisamoUI.ApplyOutline(label, OrisamoUI.Ink, 0.2f, new Color(0f, 0f, 0f, 0f));
    }
}
