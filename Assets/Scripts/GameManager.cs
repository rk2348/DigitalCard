using UnityEngine;

/// <summary>
/// シーンをまたいでキャラクターデータを保持するシングルトン。
/// タイトル → キャラクター作成 → バトル、の一連の流れで
/// プレイヤーキャラクターの情報を保持し続ける役割を持つ。
///
/// 【セットアップ方法】
/// 1. 空のGameObjectを作成し、名前を "GameManager" にする
/// 2. このスクリプトをアタッチする
/// 3. タイトルシーンにだけ配置すればOK（DontDestroyOnLoadで自動的に引き継がれる）
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // プレイヤーが作成したキャラクター
    public CharacterStats PlayerCharacter { get; private set; }

    private void Awake()
    {
        // シングルトン化：既に存在する場合は自分を破棄する
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// キャラクター作成シーンから呼び出し、キャラクターを保存する。
    /// </summary>
    public void SavePlayerCharacter(CharacterStats stats)
    {
        PlayerCharacter = stats;
        Debug.Log("キャラクターを保存しました: " + stats);
    }

    /// <summary>
    /// バトルシーンなどから、保存済みキャラクターがあるか確認するために使う。
    /// </summary>
    public bool HasPlayerCharacter()
    {
        return PlayerCharacter != null;
    }
}
