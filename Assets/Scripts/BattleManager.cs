using System.Collections;
using UnityEngine;
using TMPro; // TextMeshProを使用。通常のUI.Textを使う場合は using UnityEngine.UI; に変更してください

/// <summary>
/// バトルシーンの制御。
/// シーン開始時にランダムな敵キャラクターを生成し、
/// プレイヤーキャラクター(GameManagerに保存済み)と自動で戦闘を行う。
/// 属性相性(ElementAffinity)とスキル効果(CharacterSkill)を反映したダメージ計算を行い、
/// 戦闘終了後、勝者に応じたUIパネルを表示する。
///
/// 【セットアップ方法】
/// 1. バトルシーンに空のGameObjectを作成し、このスクリプトをアタッチ
/// 2. Canvas上に以下を用意してインスペクターにドラッグ：
///    - battleLogText   : 戦闘の経過ログを表示するテキスト
///    - winPanel        : プレイヤーが勝った時に表示するUIパネル
///    - losePanel       : プレイヤーが負けた時に表示するUIパネル
///    - winPanelText / losePanelText（任意）：それぞれの結果詳細を表示するテキスト
/// 3. winPanel / losePanel は最初は非アクティブにしておく
/// </summary>
public class BattleManager : MonoBehaviour
{
    [Header("UI参照")]
    [SerializeField] private TextMeshProUGUI battleLogText;
    [SerializeField] private GameObject winPanel;
    [SerializeField] private GameObject losePanel;
    [SerializeField] private TextMeshProUGUI winPanelText;
    [SerializeField] private TextMeshProUGUI losePanelText;

    [Header("演出設定")]
    [Tooltip("1ターンごとのログ表示間隔（秒）")]
    [SerializeField] private float turnInterval = 0.8f;

    private CharacterStats player;
    private CharacterStats enemy;

    private void Start()
    {
        // 1. プレイヤーキャラクターをGameManagerから取得
        //    タイトルからキャラクター作成を経由せず直接バトルシーンに来た場合は、
        //    ここでランダムなキャラクターを自動生成して代用する。
        if (GameManager.Instance == null)
        {
            Debug.LogError("GameManagerが見つかりません。タイトルシーンにGameManagerを配置してください。");
            return;
        }

        if (GameManager.Instance.HasPlayerCharacter())
        {
            player = GameManager.Instance.PlayerCharacter;
        }
        else
        {
            player = new CharacterStats("プレイヤー");
            player.AssignRandomStats();
            GameManager.Instance.SavePlayerCharacter(player);
            Debug.Log("キャラクター未作成だったため、ランダムキャラクターを自動生成しました。");
        }

        // 2. 敵キャラクターをランダム生成(属性・スキル・突然変異もすべて自動決定)
        enemy = new CharacterStats("敵キャラクター");
        enemy.AssignRandomStats();

        // 3. 自動戦闘を開始
        StartCoroutine(RunBattle());
    }

    /// <summary>
    /// 自動戦闘のメインループ。
    /// 素早さが高い方が先制攻撃し、交互にダメージを与え合う。
    /// HPが0以下になった方が敗北。
    /// </summary>
    private IEnumerator RunBattle()
    {
        SetLog($"{player} \n\nVS\n\n{enemy}\n\n戦闘開始！");
        yield return new WaitForSeconds(turnInterval);

        // 素早さで先攻・後攻を決定
        CharacterStats first = player.speed >= enemy.speed ? player : enemy;
        CharacterStats second = player.speed >= enemy.speed ? enemy : player;

        int turnCount = 1;

        while (player.hp > 0 && enemy.hp > 0)
        {
            // 先攻の攻撃
            ExecuteAttack(first, second, turnCount);
            yield return new WaitForSeconds(turnInterval);
            if (second.hp <= 0) break;

            // 後攻の攻撃
            ExecuteAttack(second, first, turnCount);
            yield return new WaitForSeconds(turnInterval);

            turnCount++;
        }

        ShowResult();
    }

    /// <summary>
    /// 1回分の攻撃処理。ダメージ計算・スキル効果(生命吸収)・ログ表示をまとめて行う。
    /// </summary>
    private void ExecuteAttack(CharacterStats attacker, CharacterStats defender, int turnCount)
    {
        int damage = CalculateDamage(attacker, defender, out float elementMultiplier);
        defender.hp -= damage;

        string effectLabel = ElementAffinity.GetMultiplierLabel(elementMultiplier);
        string effectText = string.IsNullOrEmpty(effectLabel) ? "" : $"（{effectLabel}）";

        string logText = $"ターン{turnCount}: {attacker.characterName}の「{attacker.skill.skillName}」！ " +
                          $"{defender.characterName}に{damage}ダメージ{effectText}（残りHP:{Mathf.Max(defender.hp, 0)}）";

        // 生命吸収スキル：与えたダメージの一部を攻撃側が回復
        if (attacker.skill != null && attacker.skill.skillType == SkillType.LifeDrain)
        {
            int healAmount = Mathf.RoundToInt(damage * attacker.skill.ratio);
            attacker.hp = Mathf.Min(attacker.maxHp, attacker.hp + healAmount);
            logText += $"\n　→ {attacker.characterName}は{healAmount}回復した！（現在HP:{attacker.hp}）";
        }

        // 最新ログのみ表示（過去ログは残さず上書き）
        SetLog(logText);
    }

    /// <summary>
    /// ダメージ計算：
    /// 1. スキルによる実質攻撃力・防御力の差分をベースダメージとする（最低1保証）
    /// 2. 属性相性による倍率を乗算
    /// 3. Overdriveスキルなら攻撃力に応じた追加ダメージを加算
    /// </summary>
    private int CalculateDamage(CharacterStats attacker, CharacterStats defender, out float elementMultiplier)
    {
        int baseDamage = attacker.GetEffectiveAttack() - defender.GetEffectiveDefense();
        baseDamage = Mathf.Max(baseDamage, 1);

        elementMultiplier = ElementAffinity.GetMultiplier(attacker.element, defender.element);
        float finalDamage = baseDamage * elementMultiplier;

        if (attacker.skill != null && attacker.skill.skillType == SkillType.Overdrive)
        {
            finalDamage += attacker.attack * attacker.skill.ratio;
        }

        return Mathf.Max(Mathf.RoundToInt(finalDamage), 1);
    }

    private void ShowResult()
    {
        bool playerWon = player.hp > 0;

        if (playerWon)
        {
            if (winPanel != null) winPanel.SetActive(true);
            if (winPanelText != null)
            {
                winPanelText.text = $"{player.characterName} の勝利！\n{player}";
            }
            SetLog($"{player.characterName} の勝利！");
        }
        else
        {
            if (losePanel != null) losePanel.SetActive(true);
            if (losePanelText != null)
            {
                losePanelText.text = $"{enemy.characterName} の勝利…\n{enemy}";
            }
            SetLog($"{enemy.characterName} の勝利…");
        }
    }

    /// <summary>
    /// バトルログを最新の1件だけに更新する（過去ログは残さず上書き）。
    /// </summary>
    private void SetLog(string text)
    {
        if (battleLogText != null) battleLogText.text = text;
    }
}
