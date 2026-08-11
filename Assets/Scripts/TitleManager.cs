using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// タイトルシーンの制御。
/// タイトルからは以下の2方向に分岐できる：
///   ・キャラクター作成シーンへ（新しくキャラクターを作る）
///   ・バトルシーンへ（既存のキャラクターでそのまま対戦、
///     もし未作成ならバトルシーン側でランダムキャラクターを自動生成する）
///
/// 【セットアップ方法】
/// 1. タイトルシーンに空のGameObjectを作成し、このスクリプトをアタッチ
/// 2. GameManagerもタイトルシーンに配置しておく
/// 3. Build Settingsに "CharacterCreation" と "Battle" のシーンを追加しておく
///    （インスペクターでシーン名を変更可）
/// 4. UI上に2つボタンを用意し、それぞれ
///      「キャラクターを作る」ボタン → GoToCharacterCreation()
///      「バトルへ」ボタン         → GoToBattle()
///    をOnClickに登録する
///    （ボタンを使わずキー入力だけで済ませたい場合は、
///      下記の characterCreationKey / battleKey で好きなキーに変更可能）
/// </summary>
public class TitleManager : MonoBehaviour
{
    [Header("遷移先シーン名（Build Settingsに登録されているもの）")]
    [SerializeField] private string characterCreationSceneName = "CharacterCreation";
    [SerializeField] private string battleSceneName = "Battle";

    [Header("キー入力設定（ボタンを使わない場合用）")]
    [SerializeField] private KeyCode characterCreationKey = KeyCode.Space;
    [SerializeField] private KeyCode battleKey = KeyCode.Return;

    private void Update()
    {
        if (Input.GetKeyDown(characterCreationKey))
        {
            GoToCharacterCreation();
        }
        else if (Input.GetKeyDown(battleKey))
        {
            GoToBattle();
        }
    }

    /// <summary>
    /// キャラクター作成シーンへ遷移する。UIボタンからも呼び出し可能。
    /// </summary>
    public void GoToCharacterCreation()
    {
        SceneManager.LoadScene(characterCreationSceneName);
    }

    /// <summary>
    /// バトルシーンへ直接遷移する。UIボタンからも呼び出し可能。
    /// キャラクターを作成済みならそのキャラクターで、
    /// 未作成の場合はバトルシーン側でランダムキャラクターが自動生成される。
    /// </summary>
    public void GoToBattle()
    {
        SceneManager.LoadScene(battleSceneName);
    }
}
