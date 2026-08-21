using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro; // TextMeshProを使用。通常のUI.Textを使う場合は using UnityEngine.UI; に変更してください

/// <summary>
/// バトルシーンの制御。
/// シーン開始時にランダムな敵キャラクターを生成し、
/// プレイヤーキャラクター(GameManagerに保存済み)と自動/手動を組み合わせて戦闘を行う。
///
/// 【今回の変更点：バトルアルゴリズムの刷新】
/// 従来の「毎ターン自動でダメージ計算」から、
/// 「強・普・弱の3ボタンによるじゃんけん式」に変更した。
///
///   1. 攻撃側が「強/普/弱」のいずれかを選ぶ(攻撃ターン)
///   2. 防御側が「強/普/弱」のいずれかを選ぶ(防御ターン)
///   3. 両者が同じレベルを選んでいたら防御成功＝ノーダメージ
///   4. 異なるレベルなら攻撃が命中し、攻撃側が選んだレベルに応じた威力でダメージが入る
///      (強＞普＞弱の順にダメージ倍率が大きい)
///
/// さらに、キャラクターごとに「強/普/弱」のうちどれか1つが必殺技レベルとして
/// 決定論的に割り当てられる(同じキャラは常に同じレベルが必殺技になる)。
/// 必殺技レベルで攻撃が命中すると、通常より大きい追加倍率がかかり、
/// 既存のスキル効果(LifeDrain/Overdrive)もこのタイミングでのみ発動する。
///
/// プレイヤーの番になると画面にボタンが表示され選択を待つ。
/// 敵の番はAIが少し考える間を置いてから、必殺技レベルをやや選びやすい確率でランダムに選ぶ。
///
/// 【セットアップ方法】
/// 1. Canvas上に「PlayerSpawnPoint」「EnemySpawnPoint」という空のRectTransformを2つ用意し、
///    プレイヤー側(画面左寄り)、敵側(画面右寄り)に配置する。
/// 2. バトル管理用の空のGameObjectを作成し、このスクリプトをアタッチ。
/// 3. Canvas上に以下を用意してインスペクターにドラッグ：
///    - battleLogText / winPanel / losePanel / winPanelText / losePanelText
///    - playerNameText / enemyNameText
///    - playerHpSlider / enemyHpSlider（Min=0, Max=1推奨）
///    - playerHpText / enemyHpText
///    - buttonPanel（強/普/弱ボタンをまとめた親GameObject。最初は非アクティブ推奨）
///    - promptText（「攻撃を選べ！」等の案内テキスト）
///    - strongButton / normalButton / weakButton（Buttonコンポーネント3つ）
/// 4. 各ボタンのOnClick()に、このスクリプトの
///    OnStrongButton() / OnNormalButton() / OnWeakButton() をそれぞれ登録する。
/// 5. playerSpawnPoint / enemySpawnPoint に手順1で作成したRectTransformをドラッグ。
/// 6. ElementAffinityクラスに GetElementColor(ElementType) が必要です。
/// </summary>
public class BattleManager : MonoBehaviour
{
    [Header("UI参照 - ログ/結果")]
    [SerializeField] private TextMeshProUGUI battleLogText;
    [SerializeField] private GameObject winPanel;
    [SerializeField] private GameObject losePanel;
    [SerializeField] private TextMeshProUGUI winPanelText;
    [SerializeField] private TextMeshProUGUI losePanelText;

    [Header("UI参照 - キャラクター情報")]
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private TextMeshProUGUI enemyNameText;
    [SerializeField] private Slider playerHpSlider;
    [SerializeField] private Slider enemyHpSlider;
    [SerializeField] private TextMeshProUGUI playerHpText;
    [SerializeField] private TextMeshProUGUI enemyHpText;

    [Header("UI参照 - 強/普/弱ボタン")]
    [Tooltip("強/普/弱ボタンをまとめた親。プレイヤーの選択中だけ表示する")]
    [SerializeField] private GameObject buttonPanel;
    [Tooltip("「攻撃を選べ」「防御を選べ」などの案内テキスト")]
    [SerializeField] private TextMeshProUGUI promptText;
    [SerializeField] private Button strongButton;
    [SerializeField] private Button normalButton;
    [SerializeField] private Button weakButton;

    [Header("2Dバトルステージ")]
    [SerializeField] private RectTransform playerSpawnPoint;
    [SerializeField] private RectTransform enemySpawnPoint;
    [SerializeField] private float iconScale = 1f;
    [SerializeField] private float damageNumberHeight = 110f;
    [Tooltip("ダメージ数値などの演出用UIを生成する親(未設定ならbattleLogTextのCanvasを自動使用)")]
    [SerializeField] private Transform effectParent;
    [Tooltip("被弾を軽く揺らす簡易シェイク対象(バトル全体のPanelなど。未設定なら無効)")]
    [SerializeField] private RectTransform shakeTarget;
    [SerializeField] private float shakeStrength = 6f;

    [Header("演出設定")]
    [Tooltip("1ターン終了後、次のターンに移るまでの間隔（秒）")]
    [SerializeField] private float turnInterval = 0.8f;
    [Tooltip("ログメッセージ1件あたりの表示時間（秒）")]
    [SerializeField] private float messageInterval = 0.9f;
    [Tooltip("HPバーが変化する際のアニメーション速度（大きいほど速い）")]
    [SerializeField] private float hpBarLerpSpeed = 4f;
    [Tooltip("攻撃時に突進する距離（ピクセル）")]
    [SerializeField] private float attackLungeDistance = 80f;
    [Tooltip("攻撃演出の所要時間（秒）")]
    [SerializeField] private float attackLungeDuration = 0.35f;
    [Tooltip("戦闘不能演出の所要時間（秒）")]
    [SerializeField] private float faintDuration = 0.6f;
    [Tooltip("被弾時のフラッシュ色")]
    [SerializeField] private Color hitFlashColor = new Color(1f, 0.4f, 0.4f, 1f);
    [Tooltip("被弾時に弾かれるノックバック距離（ピクセル）")]
    [SerializeField] private float hitKnockbackDistance = 24f;
    [Tooltip("命中時に表示する衝撃波エフェクトの最大サイズ（ピクセル）")]
    [SerializeField] private float impactBurstSize = 130f;
    [Tooltip("防御成功時のパルス演出の所要時間（秒）")]
    [SerializeField] private float guardPulseDuration = 0.3f;
    [Tooltip("敵AIが選択するまでの「考える時間」（秒）")]
    [SerializeField] private float aiThinkDelay = 0.6f;

    [Header("強さレベルごとのダメージ倍率")]
    [SerializeField] private float weakMultiplier = 0.7f;
    [SerializeField] private float normalMultiplier = 1.0f;
    [SerializeField] private float strongMultiplier = 1.4f;
    [Tooltip("必殺技レベルが命中した際の追加倍率")]
    [SerializeField] private float specialBonusMultiplier = 1.5f;
    [Tooltip("敵AIが必殺技レベルを選ぶ確率(0〜1)。残りは3択の均等ランダム")]
    [Range(0f, 1f)]
    [SerializeField] private float aiSpecialBias = 0.4f;

    [Header("参戦カードのQRスキャン(任意)")]
    [Tooltip("設定すると、バトル開始前に「自分のカードをQRスキャンして参戦する」フェーズが入り、" +
             "スキャンしたカードのステータス・スキル・実物写真がそのままプレイヤーキャラクターになる。" +
             "未設定の場合は従来通りGameManagerに保存済みのキャラクター(無ければ自動生成)を使う。")]
    [SerializeField] private BattleCardIntake playerCardIntake;

    private CharacterStats player;
    private CharacterStats enemy;

    private GameObject playerIcon;
    private GameObject enemyIcon;
    private MonsterVisual2D playerVisual;
    private MonsterVisual2D enemyVisual;

    private AttackLevel playerSelectedLevel;
    private bool playerHasSelected;

    private void Start()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogError("GameManagerが見つかりません。タイトルシーンにGameManagerを配置してください。");
            return;
        }

        if (playerCardIntake != null)
        {
            // プレイヤーが自分のカードをQRスキャンするまで待機する。
            // 読み込みが完了したらHandlePlayerCardReady経由でBeginBattle()が呼ばれる。
            playerCardIntake.OnCharacterReady += HandlePlayerCardReady;
            return;
        }

        player = ResolveFallbackPlayer();
        BeginBattle();
    }

    private void OnDestroy()
    {
        if (playerCardIntake != null)
        {
            playerCardIntake.OnCharacterReady -= HandlePlayerCardReady;
        }
    }

    /// <summary>
    /// QRスキャンによる参戦を使わない場合のプレイヤーキャラクター解決。
    /// GameManagerに保存済みのキャラクターがあればそれを、無ければ従来通りランダム生成する。
    /// </summary>
    private CharacterStats ResolveFallbackPlayer()
    {
        if (GameManager.Instance.HasPlayerCharacter())
        {
            return GameManager.Instance.PlayerCharacter;
        }

        CharacterStats fallback = new CharacterStats("プレイヤー");
        fallback.AssignRandomStats();
        GameManager.Instance.SavePlayerCharacter(fallback);
        Debug.Log("キャラクター未作成だったため、ランダムキャラクターを自動生成しました。");
        return fallback;
    }

    /// <summary>
    /// BattleCardIntakeがQRスキャン経由でプレイヤーキャラクター(写真つき)を読み込み終えた時に呼ばれる。
    /// </summary>
    private void HandlePlayerCardReady(CharacterStats scannedPlayer)
    {
        playerCardIntake.OnCharacterReady -= HandlePlayerCardReady;

        player = scannedPlayer;
        GameManager.Instance.SavePlayerCharacter(player);

        BeginBattle();
    }

    /// <summary>
    /// プレイヤーキャラクターが確定した後の共通初期化処理(敵生成〜バトル開始)。
    /// </summary>
    private void BeginBattle()
    {
        enemy = new CharacterStats("敵キャラクター");
        enemy.AssignRandomStats();

        SpawnIcons();
        SetupCharacterDisplay();
        SetupButtons();

        if (buttonPanel != null) buttonPanel.SetActive(false);

        StartCoroutine(RunBattle());
    }

    private void SpawnIcons()
    {
        if (playerSpawnPoint == null || enemySpawnPoint == null)
        {
            Debug.LogError("playerSpawnPoint / enemySpawnPoint が設定されていません。");
            return;
        }

        playerIcon = MonsterSpriteBuilder.Build(player, playerSpawnPoint, iconScale);
        enemyIcon = MonsterSpriteBuilder.Build(enemy, enemySpawnPoint, iconScale);

        playerVisual = playerIcon.GetComponent<MonsterVisual2D>();
        enemyVisual = enemyIcon.GetComponent<MonsterVisual2D>();

        playerVisual.StartIdle();
        enemyVisual.StartIdle();
    }

    private void SetupCharacterDisplay()
    {
        if (playerNameText != null) playerNameText.text = player.characterName;
        if (enemyNameText != null) enemyNameText.text = enemy.characterName;

        SetupHpSlider(playerHpSlider, player.element);
        SetupHpSlider(enemyHpSlider, enemy.element);

        UpdateHpText(playerHpText, player);
        UpdateHpText(enemyHpText, enemy);
    }

    private void SetupHpSlider(Slider slider, ElementType element)
    {
        if (slider == null) return;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;

        if (slider.fillRect != null)
        {
            Image fillImage = slider.fillRect.GetComponent<Image>();
            if (fillImage != null) fillImage.color = ElementAffinity.GetElementColor(element);
        }
    }

    private void SetupButtons()
    {
        if (strongButton != null) strongButton.onClick.AddListener(OnStrongButton);
        if (normalButton != null) normalButton.onClick.AddListener(OnNormalButton);
        if (weakButton != null) weakButton.onClick.AddListener(OnWeakButton);
    }

    // ボタンのOnClick()から呼び出す想定の公開メソッド
    public void OnStrongButton() => SelectLevel(AttackLevel.Strong);
    public void OnNormalButton() => SelectLevel(AttackLevel.Normal);
    public void OnWeakButton() => SelectLevel(AttackLevel.Weak);

    private void SelectLevel(AttackLevel level)
    {
        playerSelectedLevel = level;
        playerHasSelected = true;
    }

    /// <summary>
    /// 自動戦闘のメインループ。
    /// 素早さが高い方が先制攻撃し、以降交互に「攻撃側⇔防御側」を入れ替えながら戦闘を行う。
    /// HPが0以下になった方が敗北。
    /// </summary>
    private IEnumerator RunBattle()
    {
        yield return StartCoroutine(ShowMessage("戦闘開始！"));

        CharacterStats first = player.speed >= enemy.speed ? player : enemy;
        CharacterStats second = player.speed >= enemy.speed ? enemy : player;

        while (player.hp > 0 && enemy.hp > 0)
        {
            yield return StartCoroutine(ExecuteAttack(first, second));
            if (second.hp <= 0) break;

            yield return StartCoroutine(ExecuteAttack(second, first));
        }

        ShowResult();
    }

    /// <summary>
    /// 1回分の攻防処理。
    /// 攻撃側の選択→防御側の選択→一致判定→(命中時のみ)ダメージ演出、の順で行う。
    /// </summary>
    private IEnumerator ExecuteAttack(CharacterStats attacker, CharacterStats defender)
    {
        bool attackerIsPlayer = attacker == player;
        bool defenderIsPlayer = defender == player;

        MonsterVisual2D attackerVisual = attackerIsPlayer ? playerVisual : enemyVisual;
        MonsterVisual2D defenderVisual = attackerIsPlayer ? enemyVisual : playerVisual;

        // ① 攻撃側の選択(内容はまだ公開しない)
        AttackLevel attackLevel;
        if (attackerIsPlayer)
        {
            yield return StartCoroutine(WaitForPlayerChoice($"{attacker.characterName}の番！攻撃の強さを選んでください"));
            attackLevel = playerSelectedLevel;
        }
        else
        {
            yield return new WaitForSeconds(aiThinkDelay);
            attackLevel = ChooseAiLevel(attacker, aiSpecialBias);
        }
        yield return StartCoroutine(ShowMessage($"{attacker.characterName}が攻撃を仕掛けてきた！"));

        // ② 防御側の選択(攻撃側が何を選んだかは分からないまま選ぶ)
        AttackLevel defenseLevel;
        if (defenderIsPlayer)
        {
            yield return StartCoroutine(WaitForPlayerChoice($"{defender.characterName}の番！防御の強さを選んでください"));
            defenseLevel = playerSelectedLevel;
        }
        else
        {
            yield return new WaitForSeconds(aiThinkDelay);
            defenseLevel = ChooseAiLevel(defender, 0f); // 防御は必殺技バイアスなしの均等ランダム
        }

        // ③ 両者の選択を同時に公開
        yield return StartCoroutine(ShowMessage(
            $"{attacker.characterName}は「{LevelLabel(attackLevel)}」で攻撃！ {defender.characterName}は「{LevelLabel(defenseLevel)}」で防御！"));

        // ③ 一致判定
        if (attackLevel == defenseLevel)
        {
            if (defenderVisual != null)
            {
                StartCoroutine(defenderVisual.PlayGuardPulse(guardPulseDuration));
            }
            yield return StartCoroutine(ShowMessage($"{defender.characterName}は攻撃を防いだ！"));
            yield return new WaitForSeconds(turnInterval);
            yield break;
        }

        // ④ 命中：ダメージ計算
        int damage = CalculateDamage(attacker, defender, attackLevel, out float elementMultiplier, out bool isSpecial);
        defender.hp -= damage;

        if (isSpecial)
        {
            string skillName = attacker.skill != null ? attacker.skill.skillName : "必殺技";
            yield return StartCoroutine(ShowMessage($"{attacker.characterName}の必殺「{skillName}」が炸裂！"));
        }

        // 突進演出
        if (attackerVisual != null)
        {
            yield return StartCoroutine(attackerVisual.PlayAttackLunge(attackerIsPlayer, attackLungeDistance, attackLungeDuration));
        }

        // 被弾フラッシュ・ノックバック・衝撃波・ダメージ数値・シェイク・HPバー
        if (defenderVisual != null)
        {
            bool knockAwayRight = !defenderIsPlayer; // 敵(画面右側)は右へ、プレイヤー(画面左側)は左へ弾かれる
            StartCoroutine(defenderVisual.PlayHitFlash(hitFlashColor, 0.25f, hitKnockbackDistance, knockAwayRight));
            defenderVisual.SpawnImpactBurst(DamageColorFor(elementMultiplier), ResolveEffectParent(), impactBurstSize);
            defenderVisual.SpawnDamageNumber(damage, DamageColorFor(elementMultiplier), ResolveEffectParent(), damageNumberHeight);
        }
        if (shakeTarget != null)
        {
            StartCoroutine(ShakeUI(0.15f));
        }
        StartCoroutine(AnimateHpBar(defenderIsPlayer ? playerHpSlider : enemyHpSlider, defender));

        yield return StartCoroutine(ShowMessage(
            $"{defender.characterName}に{damage}ダメージ！（残りHP:{Mathf.Max(defender.hp, 0)}）"));

        string effectLabel = ElementAffinity.GetMultiplierLabel(elementMultiplier);
        if (!string.IsNullOrEmpty(effectLabel))
        {
            yield return StartCoroutine(ShowMessage(effectLabel));
        }

        // ⑤ 生命吸収スキル：必殺技命中時のみ発動
        if (isSpecial && attacker.skill != null && attacker.skill.skillType == SkillType.LifeDrain)
        {
            int healAmount = Mathf.RoundToInt(damage * attacker.skill.ratio);
            attacker.hp = Mathf.Min(attacker.maxHp, attacker.hp + healAmount);

            StartCoroutine(AnimateHpBar(attackerIsPlayer ? playerHpSlider : enemyHpSlider, attacker));

            yield return StartCoroutine(ShowMessage(
                $"{attacker.characterName}は{healAmount}回復した！（現在HP:{attacker.hp}）"));
        }

        // ⑥ 戦闘不能
        if (defender.hp <= 0 && defenderVisual != null)
        {
            yield return StartCoroutine(defenderVisual.PlayFaint(faintDuration));
            yield return StartCoroutine(ShowMessage($"{defender.characterName}は倒れた！"));
        }

        yield return new WaitForSeconds(turnInterval);
    }

    /// <summary>
    /// プレイヤーがボタンを押すまで待機する。押されたらボタンパネルを隠す。
    /// </summary>
    private IEnumerator WaitForPlayerChoice(string prompt)
    {
        playerHasSelected = false;
        if (promptText != null) promptText.text = prompt;
        if (buttonPanel != null) buttonPanel.SetActive(true);

        yield return new WaitUntil(() => playerHasSelected);

        if (buttonPanel != null) buttonPanel.SetActive(false);
    }

    /// <summary>
    /// 敵AIがレベルを選択する。specialBiasの確率で必殺技レベルを狙い、
    /// それ以外は3択の均等ランダムで選ぶ。
    /// </summary>
    private AttackLevel ChooseAiLevel(CharacterStats character, float specialBias)
    {
        if (specialBias > 0f && Random.value < specialBias)
        {
            return GetSpecialLevel(character);
        }

        int roll = Random.Range(0, 3);
        return (AttackLevel)roll;
    }

    /// <summary>
    /// キャラクターの必殺技レベルを決定論的に算出する(名前+属性のハッシュから固定)。
    /// 同じキャラクターは常に同じレベルが必殺技になる。
    /// </summary>
    private AttackLevel GetSpecialLevel(CharacterStats character)
    {
        int hash = (character.characterName + character.element).GetHashCode();
        int mod = ((hash % 3) + 3) % 3; // 負の剰余を避ける
        return (AttackLevel)mod;
    }

    /// <summary>
    /// ダメージ計算：
    /// 1. スキルによる実質攻撃力・防御力の差分をベースダメージとする（最低1保証）
    /// 2. 属性相性による倍率を乗算
    /// 3. 選択した強さレベル(強/普/弱)による倍率を乗算
    /// 4. そのレベルが攻撃側の必殺技レベルと一致するなら、さらに追加倍率＋スキル加算ダメージ(Overdrive)を適用
    /// </summary>
    private int CalculateDamage(CharacterStats attacker, CharacterStats defender, AttackLevel level,
        out float elementMultiplier, out bool isSpecial)
    {
        int baseDamage = attacker.GetEffectiveAttack() - defender.GetEffectiveDefense();
        baseDamage = Mathf.Max(baseDamage, 1);

        elementMultiplier = ElementAffinity.GetMultiplier(attacker.element, defender.element);
        float levelMultiplier = GetLevelMultiplier(level);

        isSpecial = level == GetSpecialLevel(attacker);
        float specialMultiplier = isSpecial ? specialBonusMultiplier : 1f;

        float finalDamage = baseDamage * elementMultiplier * levelMultiplier * specialMultiplier;

        if (isSpecial && attacker.skill != null && attacker.skill.skillType == SkillType.Overdrive)
        {
            finalDamage += attacker.attack * attacker.skill.ratio;
        }

        return Mathf.Max(Mathf.RoundToInt(finalDamage), 1);
    }

    private float GetLevelMultiplier(AttackLevel level)
    {
        switch (level)
        {
            case AttackLevel.Weak: return weakMultiplier;
            case AttackLevel.Normal: return normalMultiplier;
            case AttackLevel.Strong: return strongMultiplier;
            default: return 1f;
        }
    }

    private string LevelLabel(AttackLevel level)
    {
        switch (level)
        {
            case AttackLevel.Weak: return "弱";
            case AttackLevel.Normal: return "普";
            case AttackLevel.Strong: return "強";
            default: return "";
        }
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

    // ==================== 表示演出まわり ====================

    private IEnumerator ShowMessage(string text)
    {
        SetLog(text);
        yield return new WaitForSeconds(messageInterval);
    }

    private IEnumerator AnimateHpBar(Slider slider, CharacterStats target)
    {
        if (slider == null) yield break;

        float targetValue = target.maxHp > 0
            ? Mathf.Clamp01((float)Mathf.Max(target.hp, 0) / target.maxHp)
            : 0f;

        while (Mathf.Abs(slider.value - targetValue) > 0.001f)
        {
            slider.value = Mathf.Lerp(slider.value, targetValue, Time.deltaTime * hpBarLerpSpeed);
            yield return null;
        }
        slider.value = targetValue;

        UpdateHpText(target == player ? playerHpText : enemyHpText, target);
    }

    private void UpdateHpText(TextMeshProUGUI text, CharacterStats stats)
    {
        if (text == null) return;
        text.text = $"{Mathf.Max(stats.hp, 0)} / {stats.maxHp}";
    }

    private Color DamageColorFor(float elementMultiplier)
    {
        if (elementMultiplier > 1f) return new Color(1f, 0.3f, 0.2f);
        if (elementMultiplier < 1f) return new Color(0.6f, 0.6f, 0.6f);
        return Color.white;
    }

    private Transform ResolveEffectParent()
    {
        if (effectParent != null) return effectParent;
        if (battleLogText != null && battleLogText.canvas != null) return battleLogText.canvas.transform;
        return null;
    }

    private IEnumerator ShakeUI(float duration)
    {
        Vector2 originalPos = shakeTarget.anchoredPosition;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            Vector2 offset = Random.insideUnitCircle * shakeStrength * (1f - t / duration);
            shakeTarget.anchoredPosition = originalPos + offset;
            yield return null;
        }
        shakeTarget.anchoredPosition = originalPos;
    }

    private void SetLog(string text)
    {
        if (battleLogText != null) battleLogText.text = text;
    }
}