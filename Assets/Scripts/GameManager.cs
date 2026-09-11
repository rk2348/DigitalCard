using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// シーンをまたいでキャラクターデータを保持するシングルトン。
/// タイトル → (キャラクター登録/QR読取) → チーム編成 → バトル、の一連の流れで
/// プレイヤーが所持するキャラクターのコレクションと、バトルへ出撃させる編成(選択順)を保持する。
///
/// 【セットアップ方法】
/// 1. 空のGameObjectを作成し、名前を "GameManager" にする
/// 2. このスクリプトをアタッチする
/// 3. タイトルシーンにだけ配置すればOK（DontDestroyOnLoadで自動的に引き継がれる）
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    /// <summary>
    /// プレイヤーが所持している全キャラクター（QRコードを読み取るたびにここへ追加される）。
    /// </summary>
    private readonly List<CharacterStats> ownedCharacters = new List<CharacterStats>();
    public IReadOnlyList<CharacterStats> OwnedCharacters => ownedCharacters;

    /// <summary>
    /// バトルに出撃させる編成（チーム選択シーンで選んだ、最大3体の並び順）。
    /// バトル開始時にこの並び順で1体ずつ出撃する。
    /// </summary>
    public List<CharacterStats> SelectedTeam { get; private set; } = new List<CharacterStats>();

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
    /// QRコード等から読み取ったキャラクターをコレクションに追加する。
    /// キャラクター登録シーンやFirebaseCardListenerから呼び出す想定。
    /// </summary>
    public void AddOwnedCharacter(CharacterStats stats)
    {
        if (stats == null) return;

        ownedCharacters.Add(stats);
        Debug.Log($"キャラクターをコレクションに追加しました（所持数:{ownedCharacters.Count}）: {stats}");
    }

    /// <summary>
    /// 所持キャラクターが1体以上いるかどうか。
    /// </summary>
    public bool HasOwnedCharacters()
    {
        return ownedCharacters.Count > 0;
    }

    /// <summary>
    /// チーム選択シーンから呼び出し、バトルへ出撃させる編成（出撃順）を保存する。
    /// </summary>
    public void SetSelectedTeam(List<CharacterStats> team)
    {
        SelectedTeam = team != null ? new List<CharacterStats>(team) : new List<CharacterStats>();
    }

    /// <summary>
    /// 編成が選択済みかどうか。
    /// </summary>
    public bool HasSelectedTeam()
    {
        return SelectedTeam != null && SelectedTeam.Count > 0;
    }
}
