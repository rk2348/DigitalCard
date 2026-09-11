using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// チーム編成（出撃メンバー選択）シーンの制御。
/// GameManagerが持つ所持キャラクターコレクションを一覧表示し、
/// プレイヤーがクリックした順番で最大3体を選択する。選んだ順番がそのまま
/// バトルでの出撃順になる（先頭から順に、倒れたら次の1体が出撃）。
/// 選択済みの数が (所持数と3のうち小さい方) に達すると「バトルへ」ボタンが押せるようになる。
///
/// 【セットアップ方法】
/// 1. 新しいシーン（例:"TeamSelection"）を作成し、Build Settingsに追加する
///    （TitleManager.GoToBattle()は所持キャラクターが1体以上いる場合、このシーンへ遷移します）
/// 2. シーン内に空のGameObjectを作成し、このスクリプトをアタッチ
/// 3. Canvas上に以下を用意してインスペクターにドラッグ：
///    - listContent          : キャラクターボタンを並べる親（ScrollView内のContentなど。
///                             縦に並べたい場合はVertical Layout Groupを付けておく）
///    - characterButtonPrefab: Button + Image + 子にTextMeshProUGUI（名前・ステータス表示用）を
///                             持つプレハブ
///    - selectionStatusText  : "2/3体選択中" のような状態表示テキスト（任意）
///    - confirmButton        : 選択確定してバトルシーンへ進むボタン
/// 4. confirmButtonのOnClickにこのスクリプトの ConfirmSelection() を登録する
/// 5. battleSceneName にバトルシーンの名前を設定する（デフォルト:"Battle"）
/// </summary>
public class TeamSelectionManager : MonoBehaviour
{
    [Header("UI参照")]
    [SerializeField] private Transform listContent;
    [SerializeField] private GameObject characterButtonPrefab;
    [SerializeField] private TextMeshProUGUI selectionStatusText;
    [SerializeField] private Button confirmButton;

    [Header("設定")]
    [SerializeField] private string battleSceneName = "Battle";
    [Tooltip("編成に選択できる最大人数")]
    [SerializeField] private int maxTeamSize = 3;

    [Header("選択中のボタンの見た目（任意）")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color selectedColor = new Color(1f, 0.85f, 0.3f);

    private readonly List<CharacterStats> selectedTeam = new List<CharacterStats>();

    /// <summary>
    /// 実際に選択すべき人数。所持数がmaxTeamSizeに満たない場合は所持数に合わせる。
    /// </summary>
    private int RequiredCount => Mathf.Min(maxTeamSize, GameManager.Instance != null ? GameManager.Instance.OwnedCharacters.Count : 0);

    private void Start()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogError("GameManagerが見つかりません。タイトルシーンにGameManagerを配置してください。");
            return;
        }

        BuildCharacterList();
        UpdateStatusText();
        UpdateConfirmButtonInteractable();
    }

    /// <summary>
    /// 所持キャラクター一覧を元に、選択用ボタンを並べる。
    /// </summary>
    private void BuildCharacterList()
    {
        if (listContent == null || characterButtonPrefab == null) return;

        IReadOnlyList<CharacterStats> owned = GameManager.Instance.OwnedCharacters;

        for (int i = 0; i < owned.Count; i++)
        {
            CharacterStats stats = owned[i];

            GameObject buttonObj = Instantiate(characterButtonPrefab, listContent);
            buttonObj.name = "CharacterButton_" + stats.characterName;

            TextMeshProUGUI label = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = $"{stats.characterName}\n属性:{stats.element} ATK:{stats.attack} DEF:{stats.defense} SPD:{stats.speed}";
            }

            Image buttonImage = buttonObj.GetComponent<Image>();
            if (buttonImage != null)
            {
                buttonImage.color = normalColor;
            }

            Button button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(() => OnCharacterClicked(stats, buttonImage));
            }
        }
    }

    /// <summary>
    /// キャラクターボタンがクリックされた時の処理。
    /// 未選択なら選択(末尾に追加＝出撃順の最後尾)、選択済みなら選択解除する。
    /// 既にRequiredCount体選択済みの状態で新規選択しようとした場合は何もしない。
    /// </summary>
    private void OnCharacterClicked(CharacterStats stats, Image buttonImage)
    {
        if (selectedTeam.Contains(stats))
        {
            selectedTeam.Remove(stats);
            if (buttonImage != null) buttonImage.color = normalColor;
        }
        else
        {
            if (selectedTeam.Count >= RequiredCount) return;

            selectedTeam.Add(stats);
            if (buttonImage != null) buttonImage.color = selectedColor;
        }

        UpdateStatusText();
        UpdateConfirmButtonInteractable();
    }

    private void UpdateStatusText()
    {
        if (selectionStatusText == null) return;

        if (RequiredCount <= 0)
        {
            selectionStatusText.text = "所持しているキャラクターがいません（ランダムなチームで開始します）";
            return;
        }

        List<string> names = selectedTeam.ConvertAll(s => s.characterName);
        selectionStatusText.text = $"{selectedTeam.Count}/{RequiredCount}体 選択中\n{string.Join(" → ", names)}";
    }

    private void UpdateConfirmButtonInteractable()
    {
        if (confirmButton == null) return;
        confirmButton.interactable = RequiredCount == 0 || selectedTeam.Count == RequiredCount;
    }

    /// <summary>
    /// 選択を確定し、GameManagerに編成(出撃順)を保存してバトルシーンへ遷移する。
    /// UIボタンのOnClickから呼び出す。
    /// </summary>
    public void ConfirmSelection()
    {
        GameManager.Instance.SetSelectedTeam(selectedTeam);
        SceneManager.LoadScene(battleSceneName);
    }
}
