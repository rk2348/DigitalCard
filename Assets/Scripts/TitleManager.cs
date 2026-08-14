using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// タイトルシーンの制御。
/// タイトルからは以下の3方向にボタンで遷移できる：
///   ・キャラクター登録シーンへ（QRコードを読み取って名前を付け、キャラクターとして登録する）
///   ・バトルシーンへ（登録済みのキャラクターでそのまま対戦、
///     もし未登録ならバトルシーン側でランダムキャラクターを自動生成する）
///   ・QRコード作成シーンへ（開発者画面。カード印刷用のQRコードを作成する）
///
/// 【セットアップ方法】
/// 1. タイトルシーンに空のGameObjectを作成し、このスクリプトをアタッチ
/// 2. GameManagerもタイトルシーンに配置しておく
/// 3. Build Settingsに "CharacterCreation" "QRScan" "Battle" のシーンを追加しておく
///    （インスペクターでシーン名を変更可）
/// 4. UI上に3つボタンを用意し、それぞれ
///      「キャラクター登録」ボタン         → GoToCharacterRegistration()
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
    [SerializeField] private string characterRegistrationSceneName = "QRScan";
    [SerializeField] private string battleSceneName = "Battle";

    [Header("開発者モード設定")]
    [Tooltip("QRコード作成（開発者画面）ボタンを表示するかどうか。本番運用では非表示にすることを推奨")]
    [SerializeField] private bool showDeveloperMode = true;

    [Tooltip("「QRコード作成」ボタンのGameObject（任意。指定するとshowDeveloperModeで表示/非表示を制御できる）")]
    [SerializeField] private GameObject developerModeButton;

    private void Start()
    {
        if (developerModeButton != null)
        {
            developerModeButton.SetActive(showDeveloperMode);
        }
    }

    /// <summary>
    /// QRコード作成シーン（開発者画面）へ遷移する。UIボタンから呼び出す。
    /// </summary>
    public void GoToCharacterCreation()
    {
        SceneManager.LoadScene(characterCreationSceneName);
    }

    /// <summary>
    /// キャラクター登録シーンへ遷移する。UIボタンから呼び出す。
    /// カードのQRコードを読み取り、名前を付けて登録する画面。
    /// </summary>
    public void GoToCharacterRegistration()
    {
        SceneManager.LoadScene(characterRegistrationSceneName);
    }

    /// <summary>
    /// バトルシーンへ直接遷移する。UIボタンから呼び出す。
    /// キャラクターを登録済みならそのキャラクターで、
    /// 未登録の場合はバトルシーン側でランダムキャラクターが自動生成される。
    /// </summary>
    public void GoToBattle()
    {
        SceneManager.LoadScene(battleSceneName);
    }
}
