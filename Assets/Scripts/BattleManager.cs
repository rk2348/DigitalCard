using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using TMPro; // TextMeshProを使用。通常のUI.Textを使う場合は using UnityEngine.UI; に変更してください

/// <summary>
/// バトルシーンの制御。
/// プレイヤー・敵、それぞれ3体ずつのチームを1体ずつ順番に出撃させて自動で戦闘を行う
/// （チーム選択シーンでプレイヤーが編成を決めた後は完全オート進行）。
/// 1体ずつの対戦は、属性相性(ElementAffinity)とスキル効果(CharacterSkill)を反映した
/// ダメージ計算を行い、どちらかが倒れたら同じチームの次の1体が自動で出撃する。
/// 一方のチームの3体すべてが倒れた時点で決着とし、勝敗に応じたUIパネルを表示する。
///
/// 【セットアップ方法】
/// 1. バトルシーンに空のGameObjectを作成し、このスクリプトをアタッチ
/// 2. Canvas上に以下を用意してインスペクターにドラッグ：
///    - battleLogText   : 戦闘の経過ログを表示するテキスト
///    - winPanel        : プレイヤーが勝った時に表示するUIパネル
///    - losePanel       : プレイヤーが負けた時に表示するUIパネル
///    - winPanelText / losePanelText（任意）：それぞれの結果詳細を表示するテキスト
/// 3. winPanel / losePanel は最初は非アクティブにしておく
///
/// 【編成について】
/// ・チーム選択シーン(TeamSelectionManager)で選んだ編成があれば、GameManager.SelectedTeam を使用する
/// ・未選択、または3体に満たない場合は、不足分をランダム生成したキャラクターで補って3体にする
/// ・敵チームは常に3体、ランダム生成する
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

    [Header("チーム設定")]
    [Tooltip("1チームの編成人数")]
    [SerializeField] private int teamSize = 3;

    private List<CharacterStats> playerTeam;
    private List<CharacterStats> enemyTeam;

    private void Start()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogError("GameManagerが見つかりません。タイトルシーンにGameManagerを配置してください。");
            return;
        }

        playerTeam = BuildPlayerTeam();
        enemyTeam = BuildEnemyTeam();

        StartCoroutine(RunBattle());
    }

    /// <summary>
    /// プレイヤーチームを構築する。
    /// チーム選択シーンで選んだ編成をコピー(Clone)して使用し、
    /// (Cloneすることで元のコレクションのHPなどは変化しない)
    /// teamSize体に満たない分はランダム生成したキャラクターで補う。
    /// </summary>
    private List<CharacterStats> BuildPlayerTeam()
    {
        var team = new List<CharacterStats>();

        if (GameManager.Instance.HasSelectedTeam())
        {
            foreach (CharacterStats stats in GameManager.Instance.SelectedTeam)
            {
                if (stats == null) continue;
                team.Add(stats.Clone());
                if (team.Count >= teamSize) break;
            }
        }

        int missing = teamSize - team.Count;
        if (missing > 0)
        {
            if (team.Count == 0)
            {
                Debug.Log("編成が選択されていないため、プレイヤーチームをすべてランダム生成します。");
            }
            else
            {
                Debug.Log($"編成が{teamSize}体に満たないため、不足分({missing}体)をランダム生成で補います。");
            }

            for (int i = 0; i < missing; i++)
            {
                CharacterStats filler = new CharacterStats($"プレイヤー{team.Count + 1}");
                filler.AssignRandomStats();
                team.Add(filler);
            }
        }

        return team;
    }

    /// <summary>
    /// 敵チームを構築する(常にランダム生成 x teamSize体)。
    /// </summary>
    private List<CharacterStats> BuildEnemyTeam()
    {
        var team = new List<CharacterStats>();
        for (int i = 0; i < teamSize; i++)
        {
            CharacterStats enemy = new CharacterStats($"敵キャラクター{i + 1}");
            enemy.AssignRandomStats();
            team.Add(enemy);
        }
        return team;
    }

    /// <summary>
    /// 自動戦闘のメインループ。
    /// 各チームの「まだ倒れていない先頭の1体」同士を対戦させ、
    /// どちらかが倒れたら同じチームの次の1体に自動で交代する。
    /// どちらかのチーム全員が倒れた時点で終了。
    /// </summary>
    private IEnumerator RunBattle()
    {
        SetLog($"{FormatTeam("プレイヤーチーム", playerTeam)}\n\nVS\n\n{FormatTeam("敵チーム", enemyTeam)}\n\n戦闘開始！");
        yield return new WaitForSeconds(turnInterval);

        while (HasAliveMember(playerTeam) && HasAliveMember(enemyTeam))
        {
            CharacterStats player = GetActiveMember(playerTeam);
            CharacterStats enemy = GetActiveMember(enemyTeam);

            SetLog($"{player.characterName} 対 {enemy.characterName}！");
            yield return new WaitForSeconds(turnInterval);

            // 素早さで先攻・後攻を決定
            CharacterStats first = player.speed >= enemy.speed ? player : enemy;
            CharacterStats second = player.speed >= enemy.speed ? enemy : player;

            int turnCount = 1;

            while (player.hp > 0 && enemy.hp > 0)
            {
                ExecuteAttack(first, second, turnCount);
                yield return new WaitForSeconds(turnInterval);
                if (second.hp <= 0) break;

                ExecuteAttack(second, first, turnCount);
                yield return new WaitForSeconds(turnInterval);

                turnCount++;
            }

            if (player.hp <= 0)
            {
                SetLog($"{player.characterName} は倒れた…");
                yield return new WaitForSeconds(turnInterval);
            }

            if (enemy.hp <= 0)
            {
                SetLog($"{enemy.characterName} を倒した！");
                yield return new WaitForSeconds(turnInterval);
            }
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
        string skillLabel = attacker.skill != null ? attacker.skill.skillName : "通常攻撃";

        string logText = $"ターン{turnCount}: {attacker.characterName}の「{skillLabel}」！ " +
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

    /// <summary>
    /// チーム内でまだ倒れていない先頭の1体を返す（出撃順＝編成時の並び順）。
    /// 全滅している場合はnullを返す。
    /// </summary>
    private CharacterStats GetActiveMember(List<CharacterStats> team)
    {
        foreach (CharacterStats member in team)
        {
            if (member.hp > 0) return member;
        }
        return null;
    }

    private bool HasAliveMember(List<CharacterStats> team)
    {
        return GetActiveMember(team) != null;
    }

    private string FormatTeam(string teamLabel, List<CharacterStats> team)
    {
        var sb = new StringBuilder();
        sb.Append(teamLabel).Append("\n");
        for (int i = 0; i < team.Count; i++)
        {
            sb.Append($"{i + 1}. {team[i].characterName}（{team[i].element}）\n");
        }
        return sb.ToString().TrimEnd();
    }

    private void ShowResult()
    {
        bool playerWon = HasAliveMember(playerTeam);

        if (playerWon)
        {
            if (winPanel != null) winPanel.SetActive(true);
            if (winPanelText != null)
            {
                winPanelText.text = $"プレイヤーチームの勝利！\n{FormatSurvivors(playerTeam)}";
            }
            SetLog("プレイヤーチームの勝利！");
        }
        else
        {
            if (losePanel != null) losePanel.SetActive(true);
            if (losePanelText != null)
            {
                losePanelText.text = $"敵チームの勝利…\n{FormatSurvivors(enemyTeam)}";
            }
            SetLog("敵チームの勝利…");
        }
    }

    /// <summary>
    /// 結果画面用に、生き残ったメンバーの一覧テキストを作る。
    /// </summary>
    private string FormatSurvivors(List<CharacterStats> team)
    {
        var survivors = team.Where(m => m.hp > 0).Select(m => $"{m.characterName}（残りHP:{m.hp}）");
        return string.Join("\n", survivors);
    }

    /// <summary>
    /// バトルログを最新の1件だけに更新する（過去ログは残さず上書き）。
    /// </summary>
    private void SetLog(string text)
    {
        if (battleLogText != null) battleLogText.text = text;
    }
}
