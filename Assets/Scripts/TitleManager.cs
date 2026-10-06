using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// タイトルシーンの制御。
/// タイトルからは以下の方向にボタンで遷移できる：
///   ・バトルシーンへ（スマホ2台のエントリーを待ち受けて対戦する）
///   ・QRコード作成シーンへ（開発者画面。カード印刷用のQRコードを作成する）
///   ・デモ対戦（開発者モードのみ。QRコードもスマホも使わずにPCだけで対戦する）
/// キャラクターの登録はスマホWeb(docs/)で行うため、会場PC側には登録画面はない。
///
/// 【セットアップ方法】
/// 1. タイトルシーンに空のGameObjectを作成し、このスクリプトをアタッチ
/// 2. GameManagerもタイトルシーンに配置しておく
/// 3. Build Settingsに "QRCreation" "Battle" のシーンを追加しておく（インスペクターでシーン名を変更可）
/// 4. UI上にボタンを用意し、それぞれ
///      「バトルへ」ボタン                → GoToBattle()
///      「QRコード作成（開発者用）」ボタン  → GoToCharacterCreation()
///    をOnClickに登録する
/// 5. 本番運用でQRコード作成（開発者画面）ボタンを一般利用者に見せたくない場合は、
///    そのボタンのGameObjectを developerModeButton にドラッグし、
///    showDeveloperMode をオフにすればタイトル画面から非表示にできる
/// </summary>
public class TitleManager : MonoBehaviour
{
    [Header("遷移先シーン名（Build Settingsに登録されているもの）")]
    [SerializeField] private string characterCreationSceneName = "CharacterCreation";
    [SerializeField] private string battleSceneName = "Battle";

    [Header("開発者モード設定")]
    [Tooltip("QRコード作成（開発者画面）ボタンを表示するかどうか。本番運用では非表示にすることを推奨")]
    [SerializeField] private bool showDeveloperMode = false;

    [Tooltip("「QRコード作成」ボタンのGameObject（任意。指定するとshowDeveloperModeで表示/非表示を制御できる）")]
    [SerializeField] private GameObject developerModeButton;

    [Header("開発者モードの呼び出し")]
    [Tooltip("開発ビルドでのみ使うショートカット。Ctrl + Shift + D でQRコード作成を表示します。")]
    [SerializeField] private bool enableDeveloperModeShortcut = true;

    private bool isDeveloperMode;

    /// <summary>開発者モードの表示状態。</summary>
    public bool IsDeveloperMode => isDeveloperMode;

    /// <summary>開発者モードが切り替わった時に発火(TitleScreenViewがデモ対戦ボタンの表示に使う)。</summary>
    public event System.Action<bool> DeveloperModeChanged;

    private void Start()
    {
        // 画面の見た目(3Dスタジアムの背景・ロゴ・ボタンの配置)はTitleScreenViewが整える
        if (GetComponent<TitleScreenView>() == null) gameObject.AddComponent<TitleScreenView>();
        SetDeveloperMode(showDeveloperMode);
    }

    private void Update()
    {
        if (!enableDeveloperModeShortcut || !Input.GetKeyDown(KeyCode.D)) return;

        bool controlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (controlPressed && shiftPressed) SetDeveloperMode(!isDeveloperMode);
    }

    /// <summary>Shows or hides developer-only entry points without exposing them in the consumer UI.</summary>
    public void SetDeveloperMode(bool enabled)
    {
        isDeveloperMode = enabled;
        if (developerModeButton != null) developerModeButton.SetActive(enabled);
        DeveloperModeChanged?.Invoke(enabled);
    }

    /// <summary>
    /// 開発者用: QRコードもスマホも使わずに、PCだけのデモ対戦を始める。
    /// </summary>
    public void StartDemoBattle(BattleManager.DemoMode mode)
    {
        BattleManager.PendingDemo = mode;
        SceneManager.LoadScene(battleSceneName);
    }

    /// <summary>
    /// QRコード作成シーン（開発者画面）へ遷移する。UIボタンから呼び出す。
    /// </summary>
    public void GoToCharacterCreation()
    {
        SceneManager.LoadScene(characterCreationSceneName);
    }

    /// <summary>
    /// バトルシーンへ遷移する。UIボタンから呼び出す。
    /// </summary>
    public void GoToBattle()
    {
        SceneManager.LoadScene(battleSceneName);
    }
}
