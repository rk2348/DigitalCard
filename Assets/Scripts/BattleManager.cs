using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// バトルシーンの進行役。
///
/// 【画面】
/// 会場は3Dのスタジアム(Stadium)で、キャラクター(Fighter3D)がフィールドに立って戦う。
/// カメラ(StadiumCamera)は中継のように、引き・正面・肩越し・アップを切り替える。
/// 名前やHPなどの文字情報は、その上に重ねた2DのHUD(BattleHud)に表示する。
/// これらはすべて実行時にコードで生成するため、シーンに置かれていた旧HUD(Canvas配下)は開始時に非表示にする。
///
/// 【対戦ルール】(数値の計算は BattleRules に集約)
///   1. SPDが高い方が先攻。以降、攻撃側と防御側を交互に入れ替える
///   2. 攻撃側・防御側がそれぞれ「強/普/弱」を選ぶ(本番は両者のスマホで同時に選ぶ)
///   3. 同じレベルなら防御成功(ノーダメージ)。違えば命中し、攻撃側のレベルに応じたダメージが入る
///   4. カードごとの必殺技レベルで命中すると追加倍率がかかり、スキル(LifeDrain/Overdrive)も発動する
///   5. HPが0になるか、BattleRules.MaxExchanges 回の攻防で決着しなければ残りHPの割合で判定
///   スマホ対戦では、同じプレイヤーの時間切れが maxConsecutiveTimeouts 回続くと不戦敗になる。
///   試合が終わると nextMatchDelaySeconds 秒後にシーンを読み込み直し、次の試合を待ち受ける。
///
/// 【開発者用のデモ対戦】(QRコードもスマホも使わずに、PCだけで遊べる)
///   エディタまたは開発ビルドの受付画面で F5 = CPU同士、F6 = キーボード(1=強 2=普 3=弱)対CPU。
///   タイトル画面の開発者モードからも開始できる(PendingDemoに指定してからシーンを読み込む)。
///   battleQueueIntake が未設定の場合も、自動的にCPU同士のデモ対戦になる。
///
/// 【セットアップ】
///   バトル管理用のGameObjectにこのスクリプトをアタッチし、battleQueueIntake / battleTurnSync を設定する。
///   シーンには Main Camera と、オーバーレイのCanvasが1つあればよい(無ければCanvasは自動で作る)。
/// </summary>
public class BattleManager : MonoBehaviour
{
    public enum DemoMode { None, CpuVsCpu, PlayerVsCpu }

    /// <summary>次にバトルシーンを開いた時に始めるデモ対戦(タイトルの開発者モードから指定する)。</summary>
    public static DemoMode PendingDemo = DemoMode.None;

    [Header("演出設定")]
    [Tooltip("1回の攻防が終わってから次に移るまでの間隔（秒）")]
    [SerializeField] private float turnInterval = 0.6f;
    [Tooltip("メッセージ1件あたりの表示時間（秒）")]
    [SerializeField] private float messageInterval = 1.1f;
    [Tooltip("デモ対戦でCPUが選ぶまでの「考える時間」（秒）")]
    [SerializeField] private float aiThinkDelay = 0.8f;

    [Header("強さレベルごとのダメージ倍率")]
    [SerializeField] private float weakMultiplier = 0.7f;
    [SerializeField] private float normalMultiplier = 1.0f;
    [SerializeField] private float strongMultiplier = 1.4f;
    [Tooltip("必殺技レベルが命中した際の追加倍率")]
    [SerializeField] private float specialBonusMultiplier = 1.5f;
    [Tooltip("CPUが必殺技レベルを選ぶ確率(0〜1)。残りは3択の均等ランダム")]
    [Range(0f, 1f)]
    [SerializeField] private float aiSpecialBias = 0.4f;

    [Header("試合の運営")]
    [Tooltip("試合終了から次の試合の受付に戻るまでの秒数")]
    [SerializeField] private float nextMatchDelaySeconds = 12f;
    [Tooltip("同じプレイヤーの時間切れがこの回数続いたら不戦敗にする")]
    [SerializeField] private int maxConsecutiveTimeouts = 2;
    [Tooltip("試合を中断して次の試合の受付に戻すキー(運営者用)")]
    [SerializeField] private KeyCode abortKey = KeyCode.R;
    [Tooltip("試合前の受付中に、待機している枠をすべて空けるキー(運営者用。立ち去った人の枠が残った時に使う)")]
    [SerializeField] private KeyCode resetQueueKey = KeyCode.Delete;
    [Tooltip("キーボード対CPUのデモ対戦で、プレイヤーが選ぶ制限時間（秒）")]
    [SerializeField] private float keyboardChoiceSeconds = 20f;

    [Header("対戦キュー(スマホ側の登録待ち)")]
    [Tooltip("スマホ側で2台分の対戦登録(battleSlots)が揃うのを待つ。未設定ならCPU同士のデモ対戦になる。")]
    [SerializeField] private BattleQueueIntake battleQueueIntake;
    [Tooltip("各ターンの「強/普/弱」をスマホからFirebase経由で受け取る(本番の2台対戦モード)。")]
    [SerializeField] private BattleTurnSync battleTurnSync;

    private static readonly string[] DemoNames = { "ホムラドラゴン", "ミナモスライム", "カゼキリ", "ツチノコ丸", "ヒカリウサギ", "ヤミネコ" };

    private CharacterStats player1;
    private CharacterStats player2;
    private Fighter3D fighter1;
    private Fighter3D fighter2;
    private Stadium stadium;
    private StadiumCamera stadiumCamera;
    private BattleHud hud;

    private DemoMode demoMode = DemoMode.None;
    private bool matchStarted;
    private bool isReloading;
    private int player1ConsecutiveTimeouts;
    private int player2ConsecutiveTimeouts;
    private string forfeitSlot; // 不戦敗になった側("player1" / "player2")。なければnull

    // キーボード入力(デモ対戦)
    private bool awaitingKeyboard;
    private AttackLevel? keyboardChoice;

    private static bool DeveloperFeaturesAllowed => Application.isEditor || Debug.isDebugBuild;
    private bool IsPhoneMatch => demoMode == DemoMode.None && battleTurnSync != null;

    // ==================== 起動と受付 ====================

    private void Start()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            mainCamera = new GameObject("Main Camera").AddComponent<Camera>();
            mainCamera.tag = "MainCamera";
        }
        stadium = Stadium.Build(mainCamera);
        stadiumCamera = mainCamera.GetComponent<StadiumCamera>();
        if (stadiumCamera == null) stadiumCamera = mainCamera.gameObject.AddComponent<StadiumCamera>();
        stadiumCamera.Orbit(Vector3.up * 2f, 34f, 12f, 4f, 42f, -90f, true);

        hud = BattleHud.Create(PrepareCanvas());
        stadium.SetScreens("ORISAMO", "対戦受付中");
        hud.ShowWaiting(FirebaseRest.TableId,
            DeveloperFeaturesAllowed ? "開発者用:  F5 = CPU同士のデモ対戦　／　F6 = キーボードで対戦（1=強 2=普 3=弱）" : null);

        if (PendingDemo != DemoMode.None)
        {
            DemoMode requested = PendingDemo;
            PendingDemo = DemoMode.None;
            StartDemo(requested);
            return;
        }

        if (battleQueueIntake == null)
        {
            StartDemo(DemoMode.CpuVsCpu);
            return;
        }

        battleQueueIntake.OnSlotsChanged += HandleSlotsChanged;
        battleQueueIntake.OnMatchReady += HandleMatchReady;
    }

    private void OnDestroy()
    {
        if (battleQueueIntake != null)
        {
            battleQueueIntake.OnSlotsChanged -= HandleSlotsChanged;
            battleQueueIntake.OnMatchReady -= HandleMatchReady;
        }
    }

    /// <summary>
    /// HUDを置くCanvasを用意する。シーンに置かれていた旧HUD(Canvas配下の要素)はすべて非表示にする。
    /// </summary>
    private static Canvas PrepareCanvas()
    {
        foreach (Canvas candidate in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!candidate.isRootCanvas || candidate.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            foreach (Transform child in candidate.transform) child.gameObject.SetActive(false);
            return candidate;
        }

        GameObject go = new GameObject("Canvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        return canvas;
    }

    private void HandleSlotsChanged(bool player1Joined, string player1Name, bool player2Joined, string player2Name)
    {
        if (!matchStarted) hud.SetSlots(player1Joined, player1Name, player2Joined, player2Name);
    }

    /// <summary>スマホ側で2人の登録が揃った(BattleQueueIntakeがactiveBattleを初期化済み)。</summary>
    private void HandleMatchReady(BattleQueueIntake.MatchInfo match)
    {
        if (matchStarted) return;
        battleQueueIntake.OnMatchReady -= HandleMatchReady;
        hud.SetSlots(true, match.player1.characterName, true, match.player2.characterName);
        if (battleTurnSync != null) battleTurnSync.BeginMatch();
        BeginMatch(match.player1, match.player2);
    }

    /// <summary>開発者用: QRコードもスマホも使わずに、PCだけでデモ対戦を始める。</summary>
    private void StartDemo(DemoMode mode)
    {
        if (matchStarted) return;
        if (battleQueueIntake != null) battleQueueIntake.StopWaiting();
        demoMode = mode;

        int first = Random.Range(0, DemoNames.Length);
        int second = (first + Random.Range(1, DemoNames.Length)) % DemoNames.Length;
        CharacterStats a = new CharacterStats(mode == DemoMode.PlayerVsCpu ? "あなた・" + DemoNames[first] : DemoNames[first]);
        a.AssignRandomStats();
        CharacterStats b = new CharacterStats("CPU・" + DemoNames[second]);
        b.AssignRandomStats();
        hud.SetSlots(true, a.characterName, true, b.characterName);
        BeginMatch(a, b);
    }

    private void Update()
    {
        if (Input.GetKeyDown(abortKey) && matchStarted)
        {
            Debug.Log("BattleManager: 運営者の操作で試合を中断し、受付に戻ります。");
            ReloadForNextMatch();
        }

        if (Input.GetKeyDown(resetQueueKey) && !matchStarted && battleQueueIntake != null)
        {
            battleQueueIntake.ResetQueue();
            hud.SetSlots(false, null, false, null);
        }

        if (!matchStarted && DeveloperFeaturesAllowed)
        {
            if (Input.GetKeyDown(KeyCode.F5)) StartDemo(DemoMode.CpuVsCpu);
            else if (Input.GetKeyDown(KeyCode.F6)) StartDemo(DemoMode.PlayerVsCpu);
        }

        if (awaitingKeyboard)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) keyboardChoice = AttackLevel.Strong;
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) keyboardChoice = AttackLevel.Normal;
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) keyboardChoice = AttackLevel.Weak;
        }
    }

    /// <summary>シーンを読み込み直して次の試合を待ち受ける。進行中の試合はスマホ側に中断として伝わる。</summary>
    private void ReloadForNextMatch()
    {
        if (isReloading) return;
        isReloading = true;
        if (battleTurnSync != null) battleTurnSync.AbortIfInProgress();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ==================== 試合の進行 ====================

    private void BeginMatch(CharacterStats first, CharacterStats second)
    {
        matchStarted = true;
        player1 = first;
        player2 = second;
        StartCoroutine(RunMatch());
    }

    private IEnumerator RunMatch()
    {
        yield return new WaitForSeconds(0.8f); // 受付画面で「参加完了」を見せる間
        hud.HideWaiting();
        stadium.SetScreens($"{player1.characterName}  VS  {player2.characterName}", "BATTLE");

        fighter1 = Fighter3D.Create(player1, stadium.Player1Spot, true, stadium.transform);
        fighter2 = Fighter3D.Create(player2, stadium.Player2Spot, false, stadium.transform);

        // 入場: 引きの画 → それぞれのアップで光の柱とともに登場 → 中継アングル
        stadiumCamera.Wide(1.2f);
        stadium.Cheer(0.6f);
        yield return new WaitForSeconds(1f);
        yield return Entrance(fighter1, -0.7f);
        yield return Entrance(fighter2, 0.7f);

        stadiumCamera.Broadcast(1.1f);
        hud.ShowBattle(player1, player2);
        yield return hud.PlayIntro(player1, player2);
        yield return RunBattle();
    }

    private IEnumerator Entrance(Fighter3D fighter, float side)
    {
        stadiumCamera.CloseUp(fighter.Home, fighter.Height, 0.8f, false, side);
        yield return new WaitForSeconds(0.55f);
        yield return fighter.PlayEntrance();
        stadium.FlashLed(ElementAffinity.GetElementColor(fighter.Stats.element), 1.6f);
        stadium.Cheer(0.8f);
        yield return new WaitForSeconds(0.6f);
    }

    private IEnumerator RunBattle()
    {
        bool player1First = BattleRules.Player1AttacksFirst(player1.speed, player2.speed, Random.value);
        CharacterStats first = player1First ? player1 : player2;
        CharacterStats second = player1First ? player2 : player1;
        yield return ShowMessage($"素早さで勝る {first.characterName} の先攻！");

        int exchanges = 0;
        while (player1.hp > 0 && player2.hp > 0 && forfeitSlot == null && exchanges < BattleRules.MaxExchanges)
        {
            CharacterStats attacker = exchanges % 2 == 0 ? first : second;
            CharacterStats defender = attacker == first ? second : first;
            hud.SetTurn(exchanges + 1);
            yield return ExecuteAttack(attacker, defender);
            if (isReloading) yield break;
            exchanges++;
        }

        // 勝敗と決着の理由
        string winnerSlot;
        string endReason;
        if (forfeitSlot != null)
        {
            winnerSlot = forfeitSlot == "player1" ? "player2" : "player1";
            endReason = "forfeit";
        }
        else if (player1.hp <= 0 || player2.hp <= 0)
        {
            winnerSlot = player1.hp > 0 ? "player1" : "player2";
            endReason = "knockout";
        }
        else
        {
            int judged = BattleRules.JudgeByHpRatio(player1.hp, player1.maxHp, player2.hp, player2.maxHp);
            winnerSlot = judged == 1 ? "player1" : judged == 2 ? "player2" : "draw";
            endReason = "judgement";
            yield return ShowMessage($"{BattleRules.MaxExchanges}回の攻防で決着がつかなかった！残りHPで判定します");
        }

        if (IsPhoneMatch) yield return battleTurnSync.WriteFinished(winnerSlot, endReason);
        yield return ShowResult(winnerSlot, endReason);
    }

    /// <summary>1回分の攻防: 両者の選択 → 同時公開 → 防御成功 or 命中の演出。</summary>
    private IEnumerator ExecuteAttack(CharacterStats attacker, CharacterStats defender)
    {
        bool attackerIsPlayer1 = attacker == player1;
        Fighter3D attackerFighter = attackerIsPlayer1 ? fighter1 : fighter2;
        Fighter3D defenderFighter = attackerIsPlayer1 ? fighter2 : fighter1;

        stadiumCamera.Broadcast(0.9f);
        attackerFighter.SetRoleColor(OrisamoUI.AttackColor);
        defenderFighter.SetRoleColor(OrisamoUI.DefenseColor);
        SetRoles(attackerIsPlayer1, false, false);

        AttackLevel attackLevel = AttackLevel.Normal;
        AttackLevel defenseLevel = AttackLevel.Normal;

        if (IsPhoneMatch)
        {
            hud.SetMessage($"{attacker.characterName} の攻撃！　スマホで「強・普・弱」を選んでください");
            string attackerSlot = attackerIsPlayer1 ? "player1" : "player2";
            string defenderSlot = attackerIsPlayer1 ? "player2" : "player1";
            BattleTurnSync.TurnChoices choices = default;
            bool resolved = false;
            StartCoroutine(battleTurnSync.WaitForChoices(attackerSlot, defenderSlot, c => { choices = c; resolved = true; }));
            string offlineNotice = null;
            while (!resolved)
            {
                if (battleTurnSync.IsWaitingForChoices)
                {
                    hud.SetCountdown(battleTurnSync.SecondsRemaining, battleTurnSync.ChoiceTimeoutSeconds);
                    SetRoles(attackerIsPlayer1, battleTurnSync.AttackerChosen, battleTurnSync.DefenderChosen);

                    // 通信が切れている側がいれば知らせる(戻ってくれば元の案内に戻す)
                    string notice = OfflineNotice(battleTurnSync.IsDisconnected(attackerSlot) ? attacker : null,
                        battleTurnSync.IsDisconnected(defenderSlot) ? defender : null);
                    if (notice != offlineNotice)
                    {
                        offlineNotice = notice;
                        hud.SetMessage(notice ?? $"{attacker.characterName} の攻撃！　スマホで「強・普・弱」を選んでください");
                    }
                }
                yield return null;
            }
            hud.SetCountdown(-1f, 0f);
            attackLevel = choices.attackerDisconnected ? ChooseAiLevel(attacker, aiSpecialBias) : choices.attacker;
            defenseLevel = choices.defenderDisconnected ? ChooseAiLevel(defender, 0f) : choices.defender;
            if (choices.attackerDisconnected || choices.defenderDisconnected)
            {
                SetRoles(attackerIsPlayer1, true, true);
                hud.SetMessage(OfflineNotice(choices.attackerDisconnected ? attacker : null, choices.defenderDisconnected ? defender : null));
                yield return new WaitForSeconds(0.8f);
            }

            // 時間切れの記録と不戦敗の判定
            bool player1TimedOut = attackerIsPlayer1 ? choices.attackerTimedOut : choices.defenderTimedOut;
            bool player2TimedOut = attackerIsPlayer1 ? choices.defenderTimedOut : choices.attackerTimedOut;
            player1ConsecutiveTimeouts = player1TimedOut ? player1ConsecutiveTimeouts + 1 : 0;
            player2ConsecutiveTimeouts = player2TimedOut ? player2ConsecutiveTimeouts + 1 : 0;
            bool player1Out = player1ConsecutiveTimeouts >= maxConsecutiveTimeouts;
            bool player2Out = player2ConsecutiveTimeouts >= maxConsecutiveTimeouts;

            if (player1Out && player2Out)
            {
                yield return ShowMessage("両プレイヤーの応答がないため、試合を中断します");
                ReloadForNextMatch();
                yield break;
            }
            if (player1Out || player2Out)
            {
                forfeitSlot = player1Out ? "player1" : "player2";
                CharacterStats absent = player1Out ? player1 : player2;
                ClearRoles();
                yield return ShowMessage($"{absent.characterName} の応答がないため、不戦敗とします");
                yield break;
            }
            if (choices.attackerTimedOut || choices.defenderTimedOut)
            {
                yield return ShowMessage("時間切れ！選ばなかった側は「普」になります");
            }
        }
        else
        {
            hud.SetMessage($"{attacker.characterName} の攻撃！");
            bool attackerIsHuman = demoMode == DemoMode.PlayerVsCpu && attackerIsPlayer1;
            bool defenderIsHuman = demoMode == DemoMode.PlayerVsCpu && !attackerIsPlayer1;
            AttackLevel chosen = AttackLevel.Normal;

            if (attackerIsHuman) yield return WaitForKeyboard("攻撃", attackerIsPlayer1, level => chosen = level);
            else yield return new WaitForSeconds(aiThinkDelay * Random.Range(0.7f, 1.6f));
            attackLevel = attackerIsHuman ? chosen : ChooseAiLevel(attacker, aiSpecialBias);
            SetRoles(attackerIsPlayer1, true, false);

            if (defenderIsHuman) yield return WaitForKeyboard("防御", !attackerIsPlayer1, level => chosen = level);
            else yield return new WaitForSeconds(aiThinkDelay * Random.Range(0.7f, 1.6f));
            defenseLevel = defenderIsHuman ? chosen : ChooseAiLevel(defender, 0f);
            SetRoles(attackerIsPlayer1, true, true);
            yield return new WaitForSeconds(0.3f);
        }

        // 両者の選択を同時に公開
        bool guarded = attackLevel == defenseLevel;
        yield return hud.PlayReveal(attackLevel, defenseLevel, attackerIsPlayer1, guarded);
        ClearRoles();

        if (guarded)
        {
            stadiumCamera.OverShoulder(attackerFighter.Home, defenderFighter.Home);
            yield return new WaitForSeconds(0.35f);
            yield return attackerFighter.PlayLunge(defenderFighter.Home, () =>
            {
                StartCoroutine(defenderFighter.PlayGuard());
                StadiumKit.Shockwave(defenderFighter.Home, OrisamoUI.DefenseColor, 5f);
                stadiumCamera.Shake(0.25f);
                stadium.Cheer(0.5f);
            });
            stadiumCamera.Broadcast(0.8f);
            yield return ShowMessage($"{defender.characterName} は攻撃を見切って防いだ！");
            ResetRoleColors();
            yield return new WaitForSeconds(turnInterval);
            yield break;
        }

        // 命中: ダメージ計算
        int damage = CalculateDamage(attacker, defender, attackLevel, out float elementMultiplier, out bool isSpecial);
        Color attackColor = ElementAffinity.GetElementColor(attacker.element);
        bool heavy = isSpecial || elementMultiplier > 1f;

        if (isSpecial)
        {
            string skillName = attacker.skill != null ? attacker.skill.skillName : "必殺技";
            stadiumCamera.CloseUp(attackerFighter.Home, attackerFighter.Height, 0.5f, false, attackerIsPlayer1 ? -0.6f : 0.6f);
            stadium.FlashLed(attackColor, 2.5f);
            stadium.Cheer(0.9f);
            StartCoroutine(hud.PlayBanner($"必殺「{skillName}」", $"{attacker.characterName} の「{OrisamoUI.LevelKanji(attackLevel)}」が炸裂！", attackColor));
            yield return attackerFighter.PlaySpecialCharge();
            yield return new WaitForSeconds(0.5f);
        }

        stadiumCamera.OverShoulder(attackerFighter.Home, defenderFighter.Home);
        yield return new WaitForSeconds(0.35f);
        yield return attackerFighter.PlayLunge(defenderFighter.Home, () =>
        {
            defender.hp -= damage;
            hud.SetHp(!attackerIsPlayer1, defender.hp, defender.maxHp);
            StartCoroutine(defenderFighter.PlayHit(attackerFighter.Home, heavy));
            StadiumKit.Burst(defenderFighter.ChestPoint, attackColor, heavy ? 90 : 45, heavy ? 13f : 9f);
            StadiumKit.Shockwave(defenderFighter.Home, attackColor, heavy ? 10f : 6f);
            stadiumCamera.Shake(heavy ? 1.1f : 0.55f);
            hud.SpawnDamageNumber(defenderFighter.HeadPoint, damage, DamageColorFor(elementMultiplier), heavy);
            stadium.Cheer(heavy ? 1f : 0.6f);
        });
        yield return new WaitForSeconds(0.25f);
        stadiumCamera.Broadcast(0.9f);

        string effectLabel = ElementAffinity.GetMultiplierLabel(elementMultiplier);
        if (!string.IsNullOrEmpty(effectLabel))
        {
            hud.Callout(effectLabel, elementMultiplier > 1f ? new Color(1f, 0.6f, 0.3f) : OrisamoUI.Muted);
        }
        yield return ShowMessage($"{defender.characterName} に {damage} ダメージ！");

        // 生命吸収スキル: 必殺技命中時のみ発動
        if (isSpecial && attacker.skill != null && attacker.skill.skillType == SkillType.LifeDrain)
        {
            int healAmount = Mathf.RoundToInt(damage * attacker.skill.ratio);
            attacker.hp = Mathf.Min(attacker.maxHp, attacker.hp + healAmount);
            hud.SetHp(attackerIsPlayer1, attacker.hp, attacker.maxHp);
            StadiumKit.Burst(attackerFighter.ChestPoint, new Color(0.4f, 1f, 0.5f), 40, 5f);
            yield return ShowMessage($"{attacker.characterName} は {healAmount} 回復した！");
        }

        // 戦闘不能
        if (defender.hp <= 0)
        {
            stadiumCamera.CloseUp(defenderFighter.Home, defenderFighter.Height, 0.5f);
            yield return new WaitForSeconds(0.3f);
            stadium.Cheer(1f);
            yield return defenderFighter.PlayFaint();
            yield return ShowMessage($"{defender.characterName} は倒れた！");
        }

        ResetRoleColors();
        yield return new WaitForSeconds(turnInterval);
    }

    private IEnumerator ShowResult(string winnerSlot, string endReason)
    {
        bool draw = winnerSlot == "draw";
        bool winnerIsPlayer1 = winnerSlot == "player1";
        CharacterStats winner = draw ? null : winnerIsPlayer1 ? player1 : player2;
        Fighter3D winnerFighter = draw ? null : winnerIsPlayer1 ? fighter1 : fighter2;
        string reasonText = endReason == "judgement" ? "残りHPによる判定"
            : endReason == "forfeit" ? "相手の応答なしによる不戦勝"
            : "ノックアウト";

        ResetRoleColors();
        stadium.SetScreens(draw ? "DRAW" : $"WINNER  {winner.characterName}", reasonText);
        stadium.FlashLed(OrisamoUI.Gold, 8f);
        stadium.Cheer(1f);

        if (winnerFighter != null)
        {
            stadiumCamera.Orbit(winnerFighter.Home + Vector3.up * (winnerFighter.Height * 0.5f), 9f, 2.4f, 12f, 36f, -90f);
            StartCoroutine(winnerFighter.PlayVictory());
            StadiumKit.Confetti(winnerFighter.Home + Vector3.up * 12f, 9f);
        }
        else
        {
            stadiumCamera.Wide();
        }

        yield return hud.PlayBanner(draw ? "DRAW" : "決着！", reasonText, OrisamoUI.Gold, 0.7f);
        hud.ShowResult(winner, winnerIsPlayer1, reasonText);

        for (int seconds = Mathf.CeilToInt(nextMatchDelaySeconds); seconds > 0; seconds--)
        {
            hud.SetResultCountdown(seconds);
            yield return new WaitForSeconds(1f);
        }
        ReloadForNextMatch();
    }

    // ==================== 選択まわり ====================

    /// <summary>キーボードで「強/普/弱」を選ぶ(デモ対戦のプレイヤー側)。制限時間を過ぎたら「普」。</summary>
    private IEnumerator WaitForKeyboard(string role, bool isPlayer1, System.Action<AttackLevel> onChosen)
    {
        hud.SetMessage($"{role}の強さをキーボードで選んでください　　1 = 強　　2 = 普　　3 = 弱");
        keyboardChoice = null;
        awaitingKeyboard = true;
        float deadline = Time.time + keyboardChoiceSeconds;
        while (keyboardChoice == null && Time.time < deadline)
        {
            hud.SetCountdown(deadline - Time.time, keyboardChoiceSeconds);
            yield return null;
        }
        awaitingKeyboard = false;
        hud.SetCountdown(-1f, 0f);
        onChosen(keyboardChoice ?? AttackLevel.Normal);
    }

    private void SetRoles(bool attackerIsPlayer1, bool attackerChosen, bool defenderChosen)
    {
        hud.SetRole(attackerIsPlayer1, "攻撃", OrisamoUI.AttackColor, attackerChosen ? "<color=#F8CF70>決定！</color>" : "選択中…");
        hud.SetRole(!attackerIsPlayer1, "防御", OrisamoUI.DefenseColor, defenderChosen ? "<color=#F8CF70>決定！</color>" : "選択中…");
    }

    private void ClearRoles()
    {
        hud.SetRole(true, null, Color.clear, null);
        hud.SetRole(false, null, Color.clear, null);
    }

    private void ResetRoleColors()
    {
        if (fighter1 != null) fighter1.SetRoleColor(null);
        if (fighter2 != null) fighter2.SetRoleColor(null);
    }

    /// <summary>CPUの選択。specialBiasの確率で必殺技レベルを狙い、それ以外は3択の均等ランダム。</summary>
    /// <summary>通信が切れて自動操作になっている側の案内文。どちらも接続中ならnull。</summary>
    private static string OfflineNotice(CharacterStats offlineA, CharacterStats offlineB)
    {
        if (offlineA != null && offlineB != null) return "両プレイヤーの通信が切れたため、自動で戦います";
        CharacterStats offline = offlineA ?? offlineB;
        return offline == null ? null : $"{offline.characterName} の通信が切れたため、自動で戦います";
    }

    private static AttackLevel ChooseAiLevel(CharacterStats character, float specialBias)
    {
        if (specialBias > 0f && Random.value < specialBias) return character.specialLevel;
        return (AttackLevel)Random.Range(0, 3);
    }

    // ==================== 計算 ====================

    /// <summary>
    /// ダメージ計算(式の本体は BattleRules.CalculateDamage):
    /// 基礎ダメージ(スキル込みの実効ATK/DEF) → 属性倍率 → 強さ倍率 → 必殺技なら追加倍率＋Overdrive加算
    /// </summary>
    private int CalculateDamage(CharacterStats attacker, CharacterStats defender, AttackLevel level,
        out float elementMultiplier, out bool isSpecial)
    {
        elementMultiplier = ElementAffinity.GetMultiplier(attacker.element, defender.element);
        isSpecial = level == attacker.specialLevel;

        float overdriveBonus = attacker.skill != null && attacker.skill.skillType == SkillType.Overdrive
            ? attacker.attack * attacker.skill.ratio
            : 0f;

        return BattleRules.CalculateDamage(attacker.GetEffectiveAttack(), defender.GetEffectiveDefense(),
            elementMultiplier, GetLevelMultiplier(level), isSpecial, specialBonusMultiplier, overdriveBonus);
    }

    private float GetLevelMultiplier(AttackLevel level)
    {
        switch (level)
        {
            case AttackLevel.Weak: return weakMultiplier;
            case AttackLevel.Strong: return strongMultiplier;
            default: return normalMultiplier;
        }
    }

    private static Color DamageColorFor(float elementMultiplier)
    {
        if (elementMultiplier > 1f) return new Color(1f, 0.55f, 0.25f);
        if (elementMultiplier < 1f) return new Color(0.78f, 0.78f, 0.82f);
        return new Color(1f, 0.97f, 0.9f);
    }

    private IEnumerator ShowMessage(string text)
    {
        hud.SetMessage(text);
        yield return new WaitForSeconds(messageInterval);
    }
}
