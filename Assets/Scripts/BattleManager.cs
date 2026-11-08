using System.Collections;
using System.Collections.Generic;
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
/// 【登録カードでのCPU対戦】(会場PCの操作)
///   受付画面で 1 を押すと「1番目に登録されたカード」をPLAYER 1に、2 を押すと「2番目に登録されたカード」を
///   PLAYER 2に呼び出して表示する(登録の古い順。RegisteredCards)。両方そろうと、CPU同士で対戦する。
///   呼び出した時点でスマホからの受付は止める(待っていた人は次の試合の受付で拾われる)。
///
/// 【トーナメント】(Tournament.Current がある時)
///   受付の代わりに組み合わせ表を出し、次の試合をCPU同士で行う。試合が終わるとシーンを読み込み直して、
///   勝ち上がりを表に反映して次の試合へ進む。最後の試合が終わったら優勝者を表彰し、優勝回数を記録してタイトルへ戻る。
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

    /// <summary>次にバトルシーンを開いた時に、CPU同士で戦わせる登録カード2枚(タイトルの登録カード一覧で選ぶ)。</summary>
    public static FirebaseCharacterRecord[] PendingCardMatch;

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

    [Tooltip("トーナメントが終わった後に戻るシーン名")]
    [SerializeField] private string titleSceneName = "Title";
    [Tooltip("トーナメントで、試合が終わってから組み合わせ表に戻るまでの秒数")]
    [SerializeField] private float tournamentNextMatchDelaySeconds = 6f;
    [Tooltip("トーナメントの優勝者を表彰してからタイトルへ戻るまでの秒数")]
    [SerializeField] private float championCeremonySeconds = 15f;

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

    [Header("クリティカル")]
    [Range(0f, 1f)] [SerializeField] private float criticalChance = 0.1f;
    [Min(1f)] [SerializeField] private float criticalDamageMultiplier = 1.5f;
    public static float PlaybackSpeed { get; private set; } = 1f;
    private int consecutiveTies;

    private void TogglePlaybackSpeed()
    {
        float previous = PlaybackSpeed;
        PlaybackSpeed = previous < 1.5f ? 2f : 1f;
        Time.timeScale *= PlaybackSpeed / previous;
        hud.SetPlaybackSpeed(PlaybackSpeed);
    }

    private DemoMode demoMode = DemoMode.None;
    private bool matchStarted;
    private bool isReloading;
    private int player1ConsecutiveTimeouts;
    private int player2ConsecutiveTimeouts;
    private string forfeitSlot; // 不戦敗になった側("player1" / "player2")。なければnull

    // トーナメント(この試合が何回戦の何試合目か。トーナメントでなければ -1)
    private TournamentBracketView bracket;
    private int tournamentRound = -1;
    private int tournamentMatch = -1;

    // 登録カードでのCPU対戦(受付画面で 1 / 2)
    private CharacterStats registeredCard1;
    private CharacterStats registeredCard2;
    private List<string> registeredCardIds; // 登録の古い順
    private bool loadingRegisteredCard;

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

        Canvas canvas = PrepareCanvas();
        hud = BattleHud.Create(canvas);
        PlaybackSpeed = 1f;
        Time.timeScale = 1f;
        hud.ConfigurePlaybackSpeed(TogglePlaybackSpeed);
        bracket = TournamentBracketView.Create(canvas);
        stadium.SetScreens("ORISAMO", "対戦受付中");
        hud.ShowWaiting(FirebaseRest.TableId,
            DeveloperFeaturesAllowed
                ? "1 / 2 = 1番目・2番目に登録されたカードでCPU対戦　／　開発者用:  F5 = CPU同士のデモ対戦　／　F6 = キーボードで対戦（1=強 2=普 3=弱）"
                : "1 / 2 = 1番目・2番目に登録されたカードでCPU対戦");

        OrisamoAudio.PlayBgm(OrisamoAudio.Bgm.Waiting);

        if (Tournament.Current != null)
        {
            StartCoroutine(RunTournamentStep(Tournament.Current));
            return;
        }

        if (PendingCardMatch != null && PendingCardMatch.Length == 2)
        {
            FirebaseCharacterRecord[] records = PendingCardMatch;
            PendingCardMatch = null;
            StartCardMatch(records[0], records[1]);
            return;
        }

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
        battleQueueIntake.OnConnectionProblem += HandleConnectionProblem;
    }

    /// <summary>Firebaseに接続できない理由を受付画面に出す(null で消す)。</summary>
    private void HandleConnectionProblem(string reason)
    {
        if (hud != null) hud.SetWaitingNotice(reason ?? "");
    }

    private void OnDestroy()
    {
        PlaybackSpeed = 1f;
        Time.timeScale = 1f;
        if (battleQueueIntake != null)
        {
            battleQueueIntake.OnSlotsChanged -= HandleSlotsChanged;
            battleQueueIntake.OnMatchReady -= HandleMatchReady;
            battleQueueIntake.OnConnectionProblem -= HandleConnectionProblem;
        }
    }

    /// <summary>
    /// HUDを置くCanvasを用意する。シーンに置かれていた旧HUD(Canvas配下の要素)はすべて非表示にする。
    /// </summary>
    private static Canvas PrepareCanvas()
    {
        Canvas candidate = OrisamoUI.FindSceneCanvas();
        if (candidate != null)
        {
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

    // ==================== トーナメント ====================

    /// <summary>
    /// 組み合わせ表を見せてから、次の試合を始める。すべて終わっていれば表彰に進む。
    /// </summary>
    private IEnumerator RunTournamentStep(Tournament tournament)
    {
        matchStarted = true; // 受付画面のキー操作(1/2, F5など)を受け付けない
        if (battleQueueIntake != null)
        {
            battleQueueIntake.OnSlotsChanged -= HandleSlotsChanged;
            battleQueueIntake.OnMatchReady -= HandleMatchReady;
            battleQueueIntake.StopWaiting();
        }
        hud.HideWaiting();
        stadiumCamera.Orbit(Vector3.up * 2f, 30f, 10f, 5f, 44f, -90f, true);

        if (tournament.IsFinished)
        {
            yield return TournamentCeremony(tournament);
            yield break;
        }

        tournament.TryGetNextMatch(out int round, out int match);
        FirebaseCharacterRecord first = tournament.EntrantAt(round, match * 2);
        FirebaseCharacterRecord second = tournament.EntrantAt(round, match * 2 + 1);
        string roundName = tournament.RoundName(round);
        string caption = roundName == "決勝" ? "決勝戦" : $"{roundName}　第{tournament.MatchNumber(round, match)}試合";
        stadium.SetScreens("TOURNAMENT", caption);
        bracket.Show(tournament, round, match, caption, $"{first.characterName}　VS　{second.characterName}");
        if (roundName == "決勝")
        {
            stadium.FlashLed(OrisamoUI.Gold, 3f);
            stadium.Cheer(1f);
        }
        yield return new WaitForSeconds(tournament.LastRound >= 0 ? 6f : 5f);
        bracket.Hide();

        CharacterStats a = FirebaseCharacterMapper.ToCharacterStats(first, nameof(BattleManager));
        CharacterStats b = FirebaseCharacterMapper.ToCharacterStats(second, nameof(BattleManager));
        tournamentRound = round;
        tournamentMatch = match;
        demoMode = DemoMode.CpuVsCpu;
        BeginMatch(a, b);
    }

    /// <summary>優勝者の表彰: 組み合わせ表 → 優勝者がスタジアムの中央に登場 → 表彰パネル。優勝回数を記録してタイトルへ戻る。</summary>
    private IEnumerator TournamentCeremony(Tournament tournament)
    {
        FirebaseCharacterRecord championRecord = tournament.Champion;
        stadium.SetScreens($"CHAMPION  {championRecord.characterName}", $"{tournament.Size}人トーナメント 優勝");
        bracket.Show(tournament, -1, -1, "トーナメント決着！", $"優勝は {championRecord.characterName}！");
        stadium.FlashLed(OrisamoUI.Gold, 4f);
        stadium.Cheer(1f);
        yield return new WaitForSeconds(4f);
        bracket.Hide();

        OrisamoAudio.PlayBgm(OrisamoAudio.Bgm.Result);
        CharacterStats champion = FirebaseCharacterMapper.ToCharacterStats(championRecord, nameof(BattleManager));
        Vector3 center = (stadium.Player1Spot + stadium.Player2Spot) * 0.5f;
        Fighter3D fighter = Fighter3D.Create(champion, center, true, stadium.transform);
        stadiumCamera.CloseUp(center, fighter.Height, 0.6f, true);
        yield return new WaitForSeconds(0.4f);
        yield return fighter.PlayEntrance();

        stadiumCamera.Orbit(center + Vector3.up * (fighter.Height * 0.5f), 11f, 3f, 10f, 40f, -90f);
        StadiumKit.Confetti(center + Vector3.up * 12f, 12f);
        stadium.FlashLed(OrisamoUI.Gold, championCeremonySeconds);
        stadium.Cheer(1f);
        StartCoroutine(fighter.PlayVictory());
        bracket.ShowChampion(champion, $"{tournament.Size}人トーナメント 優勝");
        StartCoroutine(CardRecords.RecordChampion(championRecord));

        for (int seconds = Mathf.CeilToInt(championCeremonySeconds); seconds > 0; seconds--)
        {
            bracket.SetFooter($"{seconds} 秒後にタイトルへ戻ります");
            if (seconds % 5 == 0) StadiumKit.Confetti(center + Vector3.up * 12f, 12f);
            yield return new WaitForSeconds(1f);
        }
        Tournament.Current = null;
        Time.timeScale = 1f;
        SceneManager.LoadScene(titleSceneName);
    }

    /// <summary>登録カード一覧で選んだ2枚を、CPU同士で戦わせる。</summary>
    private void StartCardMatch(FirebaseCharacterRecord first, FirebaseCharacterRecord second)
    {
        CharacterStats a = FirebaseCharacterMapper.ToCharacterStats(first, nameof(BattleManager));
        CharacterStats b = FirebaseCharacterMapper.ToCharacterStats(second, nameof(BattleManager));
        if (a == null || b == null) return;
        if (battleQueueIntake != null)
        {
            battleQueueIntake.OnSlotsChanged -= HandleSlotsChanged;
            battleQueueIntake.OnMatchReady -= HandleMatchReady;
            battleQueueIntake.StopWaiting();
        }
        hud.ShowRegisteredCard(true, a, "<color=#F8CF70>登録カード</color>・CPU");
        hud.ShowRegisteredCard(false, b, "<color=#F8CF70>登録カード</color>・CPU");
        hud.SetWaitingNotice("登録カード一覧で選んだ2枚でCPU対戦を始めます");
        demoMode = DemoMode.CpuVsCpu;
        BeginMatch(a, b);
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
        if (Input.GetKeyDown(KeyCode.F8)) TogglePlaybackSpeed();
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

        if (!matchStarted && !loadingRegisteredCard)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) StartCoroutine(CallRegisteredCard(0));
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) StartCoroutine(CallRegisteredCard(1));
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

    /// <summary>
    /// 受付画面で、index番目(0始まり)に登録されたカードを呼び出して枠に表示する。
    /// 0番目はPLAYER 1、1番目はPLAYER 2。両方そろったらCPU同士で対戦を始める。
    /// </summary>
    private IEnumerator CallRegisteredCard(int index)
    {
        loadingRegisteredCard = true;
        bool slot1 = index == 0;

        // スマホの受付は止める(スマホの参加状況で枠の表示が上書きされないように)
        if (battleQueueIntake != null)
        {
            battleQueueIntake.OnSlotsChanged -= HandleSlotsChanged;
            battleQueueIntake.OnMatchReady -= HandleMatchReady;
            battleQueueIntake.StopWaiting();
            if (registeredCard1 == null && registeredCard2 == null) hud.SetSlots(false, null, false, null);
        }

        hud.SetWaitingNotice($"{index + 1}番目に登録されたカードを読み込んでいます…");
        if (registeredCardIds == null)
        {
            string error = null;
            yield return RegisteredCards.LoadOrderedIds(this, (ids, e) => { registeredCardIds = ids; error = e; });
            if (registeredCardIds == null)
            {
                Debug.LogWarning("BattleManager: 登録カードの一覧を取得できませんでした: " + error);
                hud.SetWaitingNotice("登録カードを読み込めませんでした（ネットワークと、この会場PCが /admins に登録されているかを確認してください）");
                loadingRegisteredCard = false;
                yield break;
            }
        }

        if (index >= registeredCardIds.Count)
        {
            hud.SetWaitingNotice($"登録されたカードが{index + 1}枚目までありません（現在 {registeredCardIds.Count}枚）");
            loadingRegisteredCard = false;
            yield break;
        }

        FirebaseCharacterRecord record = null;
        yield return RegisteredCards.LoadRecord(registeredCardIds[index], r => record = r);
        CharacterStats stats = record != null ? FirebaseCharacterMapper.ToCharacterStats(record, nameof(BattleManager)) : null;
        loadingRegisteredCard = false;
        if (stats == null || matchStarted)
        {
            if (stats == null) hud.SetWaitingNotice($"{index + 1}番目のカードを読み込めませんでした");
            yield break;
        }

        if (slot1) registeredCard1 = stats; else registeredCard2 = stats;
        hud.ShowRegisteredCard(slot1, stats, $"<color=#F8CF70>{index + 1}番目の登録カード</color>・CPU");

        if (registeredCard1 != null && registeredCard2 != null)
        {
            hud.SetWaitingNotice("登録カードどうしのCPU対戦を始めます");
            demoMode = DemoMode.CpuVsCpu;
            BeginMatch(registeredCard1, registeredCard2);
        }
        else
        {
            hud.SetWaitingNotice(slot1 ? "2 を押すと、2番目に登録されたカードを呼び出して対戦します" : "1 を押すと、1番目に登録されたカードを呼び出して対戦します");
        }
    }

    /// <summary>シーンを読み込み直して次の試合を待ち受ける。進行中の試合はスマホ側に中断として伝わる。</summary>
    private void ReloadForNextMatch()
    {
        if (isReloading) return;
        isReloading = true;
        Time.timeScale = 1f; // スローモーションの途中で中断しても、次の試合に持ち越さない
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
        consecutiveTies = 0;
        hud.SetTieBoost(0);
        yield return new WaitForSeconds(0.8f); // 受付画面で「参加完了」を見せる間
        OrisamoAudio.PlayBgm(OrisamoAudio.Bgm.Battle);
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
        hud.SetAffinity(ElementAffinity.GetMultiplier(player1.element, player2.element),
            ElementAffinity.GetMultiplier(player2.element, player1.element));
        yield return hud.PlayIntro(player1, player2);

        // 属性の相性を先に知らせておく(強・普・弱の読み合いに作戦の要素を足す)
        string matchup = ElementAffinity.DescribeMatchup(player1.element, player2.element);
        if (matchup != null) yield return ShowMessage(matchup);
        yield return RunBattle();
    }

    private IEnumerator Entrance(Fighter3D fighter, float side)
    {
        stadiumCamera.CloseUp(fighter.Home, fighter.Height, 0.8f, false, side);
        yield return new WaitForSeconds(0.55f);
        yield return fighter.PlayEntrance();
        stadium.FlashLed(ElementAffinity.GetElementColor(fighter.Stats.element), 1.6f);
        stadium.Cheer(0.8f);
        if (fighter.Stats.isMutation)
        {
            stadium.FlashLed(OrisamoUI.Gold, 2.5f);
            stadium.Cheer(1f);
            yield return hud.PlayBanner("RARE CARD!", $"{fighter.Stats.characterName} は突然変異のレアカード！", OrisamoUI.Gold, 0.7f);
        }
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

        // トーナメントでは引き分けにできないので、抽選で勝ち上がりを決める
        if (winnerSlot == "draw" && tournamentRound >= 0)
        {
            winnerSlot = Random.value < 0.5f ? "player1" : "player2";
            CharacterStats lucky = winnerSlot == "player1" ? player1 : player2;
            yield return ShowMessage($"残りHPが同じため抽選！　{lucky.characterName} が勝ち上がります");
        }

        if (IsPhoneMatch)
        {
            // 戦績を先に書いてから終了を伝える(スマホは終了を見てから通算成績を読みに来るため)
            yield return CardRecords.RecordResult(player1, player2, winnerSlot);
            yield return battleTurnSync.WriteFinished(winnerSlot, endReason);
        }
        yield return ShowResult(winnerSlot, endReason);
    }

    /// <summary>1回分の攻防: 両者の選択 → 同時公開 → 防御成功 or 命中の演出。</summary>
    private IEnumerator ExecuteAttack(CharacterStats attacker, CharacterStats defender)
    {
        bool attackerIsPlayer1 = attacker == player1;
        Fighter3D attackerFighter = attackerIsPlayer1 ? fighter1 : fighter2;
        Fighter3D defenderFighter = attackerIsPlayer1 ? fighter2 : fighter1;

        // 選択を待つ間は、いくつかのカメラアングルを切り替えながら見せる(最初は攻撃側を手前に置いたバトルビュー)
        Coroutine choosingCamera = StartCoroutine(ChoosingCameraLoop(attackerFighter, defenderFighter));
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
            StopCoroutine(choosingCamera); // 選び終えたらカメラの切り替えを止める(この後の早期終了でも動き続けないように)
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

        StopCoroutine(choosingCamera);

        // 両者の選択を同時に公開
        bool guarded = attackLevel == defenseLevel;
        yield return hud.PlayReveal(attackLevel, defenseLevel, attackerIsPlayer1, guarded);
        ClearRoles();

        float power = AttackPower(attackLevel);
        if (guarded)
        {
            consecutiveTies = Mathf.Min(10, consecutiveTies + 1);
            hud.SetTieBoost(consecutiveTies);
            stadiumCamera.OverShoulder(attackerFighter.Home, defenderFighter.Home);
            ElementalEffects.Charge(attacker.element, attackerFighter.Home, attackerFighter.Height, power);
            yield return new WaitForSeconds(0.55f);
            yield return attackerFighter.PlayLunge(defenderFighter.Home, () =>
            {
                StartCoroutine(defenderFighter.PlayGuard());
                OrisamoAudio.Play(OrisamoAudio.Sfx.Guard);
                StadiumKit.Shockwave(defenderFighter.Home, OrisamoUI.DefenseColor, 5f);
                stadiumCamera.Shake(0.25f);
                stadium.Cheer(0.5f);
            });
            stadiumCamera.Broadcast(0.8f);
            yield return ShowMessage($"あいこ！　{defender.characterName} は防いだ！　次の攻撃は威力 ×{BattleRules.TieDamageMultiplier(consecutiveTies):0.0}");
            ResetRoleColors();
            yield return new WaitForSeconds(turnInterval);
            yield break;
        }

        // 命中: ダメージ計算
        int damage = CalculateDamage(attacker, defender, attackLevel, out float elementMultiplier, out bool isSpecial);
        damage = Mathf.Max(1, Mathf.RoundToInt(damage * BattleRules.TieDamageMultiplier(consecutiveTies)));
        consecutiveTies = 0;
        hud.SetTieBoost(0);
        bool critical = BattleRules.IsCritical(Random.value, criticalChance);
        damage = BattleRules.ApplyCritical(damage, critical, criticalDamageMultiplier);
        Color attackColor = ElementAffinity.GetElementColor(attacker.element);
        bool heavy = critical || isSpecial || elementMultiplier > 1f;
        // この一撃で相手のHPが尽きる(=決着がつく)なら、確定演出を入れる
        bool finishing = damage >= defender.hp;

        if (finishing)
        {
            stadium.FlashLed(OrisamoUI.Gold, 3f);
            stadium.Cheer(1f);
            stadiumCamera.CloseUp(attackerFighter.Home, attackerFighter.Height, 0.25f, true, attackerIsPlayer1 ? -0.6f : 0.6f);
            yield return hud.PlayFinisherCue(attacker.characterName, attackColor);
        }

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

        if (heavy || finishing) power *= 1.4f;
        stadiumCamera.OverShoulder(attackerFighter.Home, defenderFighter.Home);
        ElementalEffects.Charge(attacker.element, attackerFighter.Home, attackerFighter.Height, power);
        yield return new WaitForSeconds(0.55f);

        if (finishing)
        {
            bool knockedDown = false;
            yield return attackerFighter.PlayLunge(defenderFighter.Home, () =>
            {
                defender.hp -= damage;
                if (critical) hud.Callout("CRITICAL!", OrisamoUI.Gold);
                hud.SetHp(!attackerIsPlayer1, defender.hp, defender.maxHp);
                hud.FlashScreen(Color.white, 0.5f);
                ElementalEffects.Impact(attacker.element, defenderFighter.ChestPoint, defenderFighter.Home, power * 1.3f);
                StadiumKit.Shockwave(defenderFighter.Home, attackColor, 14f, 0.9f);
                StadiumKit.Burst(defenderFighter.ChestPoint, OrisamoUI.Gold, 120, 14f, 0.6f);
                stadiumCamera.Shake(2.4f);
                hud.SpawnDamageNumber(defenderFighter.HeadPoint, damage, DamageColorFor(elementMultiplier), true);
                stadium.FlashLed(Color.white, 2f);
                stadium.Cheer(1f);
                OrisamoAudio.Play(OrisamoAudio.Sfx.FinisherImpact);
                if (IsPhoneMatch) battleTurnSync.WriteHit(attackerIsPlayer1 ? "player2" : "player1", damage, true);
                StartCoroutine(FinisherSlowMotion());
                StartCoroutine(KnockDown(defenderFighter, attackerFighter.Home, () => knockedDown = true));
            });
            while (!knockedDown) yield return null;

            hud.EndFinisherCue();
            yield return hud.PlayKnockoutStamp();
            yield return ShowMessage($"{defender.characterName} に {damage} ダメージ！　{defender.characterName} は倒れた！");
            ResetRoleColors();
            yield return new WaitForSeconds(turnInterval);
            yield break;
        }

        yield return attackerFighter.PlayLunge(defenderFighter.Home, () =>
        {
            defender.hp -= damage;
            if (critical)
            {
                hud.Callout("CRITICAL!", OrisamoUI.Gold);
                hud.FlashScreen(new Color(1f, 0.8f, 0.35f, 0.3f), 0.18f);
            }
            hud.SetHp(!attackerIsPlayer1, defender.hp, defender.maxHp);
            StartCoroutine(defenderFighter.PlayHit(attackerFighter.Home, heavy));
            ElementalEffects.Impact(attacker.element, defenderFighter.ChestPoint, defenderFighter.Home, power);
            if (heavy || attackLevel == AttackLevel.Strong) ElementalEffects.HitStop(heavy ? 0.12f : 0.07f);
            stadiumCamera.Shake((heavy ? 1.1f : 0.55f) * Mathf.Sqrt(power));
            hud.SpawnDamageNumber(defenderFighter.HeadPoint, damage, DamageColorFor(elementMultiplier), heavy);
            stadium.Cheer(heavy ? 1f : 0.6f);
            OrisamoAudio.Play(heavy || attackLevel == AttackLevel.Strong ? OrisamoAudio.Sfx.HitStrong
                : attackLevel == AttackLevel.Normal ? OrisamoAudio.Sfx.HitNormal : OrisamoAudio.Sfx.HitWeak);
            if (IsPhoneMatch) battleTurnSync.WriteHit(attackerIsPlayer1 ? "player2" : "player1", damage, false);
        });
        yield return new WaitForSeconds(0.25f);
        stadiumCamera.Broadcast(0.9f);

        string effectLabel = ElementAffinity.GetMultiplierLabel(elementMultiplier);
        if (!string.IsNullOrEmpty(effectLabel))
        {
            hud.Callout(effectLabel, elementMultiplier > 1f ? new Color(1f, 0.6f, 0.3f) : OrisamoUI.Muted);
        }
        yield return ShowMessage($"{(critical ? "クリティカル！　" : "")}{defender.characterName} に {damage} ダメージ！");

        // 生命吸収スキル: 必殺技命中時のみ発動
        if (isSpecial && attacker.skill != null && attacker.skill.skillType == SkillType.LifeDrain)
        {
            int healAmount = Mathf.RoundToInt(damage * attacker.skill.ratio);
            attacker.hp = Mathf.Min(attacker.maxHp, attacker.hp + healAmount);
            hud.SetHp(attackerIsPlayer1, attacker.hp, attacker.maxHp);
            StadiumKit.Burst(attackerFighter.ChestPoint, new Color(0.4f, 1f, 0.5f), 40, 5f);
            yield return ShowMessage($"{attacker.characterName} は {healAmount} 回復した！");
        }

        ResetRoleColors();
        yield return new WaitForSeconds(turnInterval);
    }

    /// <summary>
    /// とどめの一撃が当たった瞬間の時間の流れ: ほぼ止める(ヒットストップ) → 0.3倍のスローから徐々に通常速度へ戻す。
    /// 実時間で進める(Time.timeScale自体を変えるため)。
    /// </summary>
    private IEnumerator FinisherSlowMotion()
    {
        Time.timeScale = 0.03f * PlaybackSpeed;
        yield return new WaitForSecondsRealtime(0.22f);
        const float duration = 1.6f;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float p = t / duration;
            Time.timeScale = Mathf.Lerp(0.3f, 1f, p * p) * PlaybackSpeed;
            yield return null;
        }
        Time.timeScale = PlaybackSpeed;
    }

    /// <summary>倒れた側が吹き飛ぶのをカメラで追い、地面に落ちたら寄って見せる。</summary>
    private IEnumerator KnockDown(Fighter3D fighter, Vector3 attackerHome, System.Action onDone)
    {
        Vector3 away = fighter.Home - attackerHome;
        away.y = 0f;
        away = away.sqrMagnitude > 0.01f ? away.normalized : (fighter.IsPlayer1 ? Vector3.left : Vector3.right);
        Vector3 landing = fighter.Home + away * 5.6f;
        Vector3 middle = Vector3.Lerp(fighter.Home, landing, 0.5f) + Vector3.up * 1.6f;
        stadiumCamera.Shot(middle + new Vector3(-away.x * 3f, 3.2f, -15f), middle, 42f, 0.35f);

        yield return fighter.PlayFaint(attackerHome, () =>
        {
            stadiumCamera.Shake(1.3f);
            stadium.Cheer(1f);
            Vector3 body = landing + away * (fighter.Height * 0.4f) + Vector3.up * 0.8f;
            stadiumCamera.Shot(body + new Vector3(-away.x * 2f, 2.6f, -9.5f), body, 36f, 0.9f);
        });
        onDone();
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
        OrisamoAudio.PlayBgm(OrisamoAudio.Bgm.Result);
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

        bool tournamentMatchFinished = tournamentRound >= 0 && Tournament.Current != null && !draw;
        if (tournamentMatchFinished)
        {
            Tournament tournament = Tournament.Current;
            bool final = tournamentRound == tournament.RoundCount - 1;
            tournament.ReportWinner(tournamentRound, tournamentMatch, winnerIsPlayer1);
            reasonText = final ? $"{reasonText}　／　トーナメント優勝！" : $"{reasonText}　／　{tournament.RoundName(tournamentRound + 1)}へ進出";
        }

        yield return hud.PlayBanner(draw ? "DRAW" : "決着！", reasonText, OrisamoUI.Gold, 0.7f);
        hud.ShowResult(winner, winnerIsPlayer1, reasonText);

        float delay = tournamentMatchFinished ? tournamentNextMatchDelaySeconds : nextMatchDelaySeconds;
        for (int seconds = Mathf.CeilToInt(delay); seconds > 0; seconds--)
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
        float deadline = Time.unscaledTime + keyboardChoiceSeconds;
        while (keyboardChoice == null && Time.unscaledTime < deadline)
        {
            hud.SetCountdown(deadline - Time.unscaledTime, keyboardChoiceSeconds);
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
    // ==================== 選択待ちのカメラワーク ====================

    /// <summary>
    /// 両者が選んでいる間、テレビ中継のようにカメラアングルを切り替え続ける。
    /// 同じアングルが続かないようにランダムに選び、切り替えは「カット」と「なめらかな移動」を混ぜる。
    /// </summary>
    private IEnumerator ChoosingCameraLoop(Fighter3D attackerFighter, Fighter3D defenderFighter)
    {
        Vector3 attackerPos = attackerFighter.Home;
        Vector3 defenderPos = defenderFighter.Home;
        Vector3 center = (attackerPos + defenderPos) * 0.5f;
        float attackerSide = Mathf.Sign(attackerPos.x - center.x);
        if (attackerSide == 0f) attackerSide = -1f;

        System.Action<bool>[] shots =
        {
            // 0. バトルビュー(攻撃側が手前)
            cut => stadiumCamera.BattleView(attackerPos, defenderPos, cut ? 0.5f : 1.1f, cut),
            // 1. 逆側からのバトルビュー(防御側が手前)
            cut => stadiumCamera.BattleViewReverse(defenderPos, attackerPos, cut ? 0.5f : 1.1f, cut),
            // 2. 横から2人を追いかけるドリー
            cut => stadiumCamera.Track(
                center + new Vector3(-attackerSide * 13f, 3.6f, -15f), center + new Vector3(attackerSide * 13f, 3.6f, -15f),
                center + new Vector3(-attackerSide * 3f, 2.4f, 0f), center + new Vector3(attackerSide * 3f, 2.4f, 0f), 40f, 5f, cut),
            // 3. 攻撃側を下からあおるヒーローショット(ゆっくり寄る)
            cut => stadiumCamera.Track(
                attackerPos + new Vector3(attackerSide * -3.2f, 0.7f, -8.5f), attackerPos + new Vector3(attackerSide * -2.2f, 0.9f, -6f),
                attackerPos + Vector3.up * (attackerFighter.Height * 0.85f), attackerPos + Vector3.up * (attackerFighter.Height * 0.8f), 40f, 4.5f, cut),
            // 4. 防御側の緊張した表情に寄る
            cut => stadiumCamera.Track(
                defenderPos + new Vector3(-attackerSide * 3.5f, defenderFighter.Height * 0.6f, -9f), defenderPos + new Vector3(-attackerSide * 2.4f, defenderFighter.Height * 0.62f, -6.8f),
                defenderPos + Vector3.up * (defenderFighter.Height * 0.6f), defenderPos + Vector3.up * (defenderFighter.Height * 0.62f), 34f, 4.5f, cut),
            // 5. 真上近くから降りてくるクレーン
            cut => stadiumCamera.Track(
                center + new Vector3(0f, 24f, -16f), center + new Vector3(0f, 11f, -19f),
                center + Vector3.up * 0.5f, center + Vector3.up * 1.8f, 44f, 5f, cut),
            // 6. フィールドをぐるりと回る
            cut => stadiumCamera.Orbit(center + Vector3.up * 2f, 21f, 6.5f, attackerSide * 9f, 42f, attackerSide > 0f ? -60f : -120f, cut),
            // 7. 正面の低い位置から2人のにらみ合い(ゆっくり寄る)
            cut => stadiumCamera.Track(
                center + new Vector3(0f, 1.3f, -16f), center + new Vector3(0f, 1.5f, -12.5f),
                center + Vector3.up * 2.8f, center + Vector3.up * 2.6f, 52f, 5f, cut),
            // 8. 防御側の肩越しに攻撃側を見る
            cut => stadiumCamera.OverShoulder(defenderPos, attackerPos, cut ? 0.5f : 1.1f, cut),
        };

        int last = 0;
        shots[0](false);
        yield return new WaitForSeconds(Random.Range(3.2f, 4.2f));
        while (true)
        {
            int next;
            do { next = Random.Range(0, shots.Length); } while (next == last);
            last = next;
            shots[next](Random.value < 0.5f);
            yield return new WaitForSeconds(Random.Range(3f, 4.5f));
        }
    }

    /// <summary>攻撃エフェクトの強さ(強いほど派手にする)。</summary>
    private static float AttackPower(AttackLevel level)
    {
        switch (level)
        {
            case AttackLevel.Strong: return 1.35f;
            case AttackLevel.Weak: return 0.8f;
            default: return 1f;
        }
    }

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
