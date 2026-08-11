using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ESCキーが押されたらタイトルシーンに戻る共通コンポーネント。
/// タイトル以外の各シーン（CharacterCreation / QRScan / Battle）に
/// 空のGameObjectを作ってこのスクリプトをアタッチするだけで使える。
/// </summary>
public class ReturnToTitleOnEscape : MonoBehaviour
{
    [Tooltip("戻り先シーン名（Build Settingsに登録されているもの）")]
    [SerializeField] private string titleSceneName = "Title";

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SceneManager.LoadScene(titleSceneName);
        }
    }
}
