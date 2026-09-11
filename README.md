# キャラクター作成 → 自動バトル ゲーム セットアップ手順

## 構成ファイル
| ファイル | 役割 |
|---|---|
| `ElementType.cs` | 6属性を定義するenum(名称は仮。デザイン確定後に書き換え可能) |
| `ElementAffinity.cs` | 6属性間の相性(ダメージ倍率)を計算するユーティリティ |
| `CharacterSkill.cs` | 他ステータスを参照するスキルのデータクラス(4種類) |
| `CharacterStats.cs` | キャラクターのステータス・属性・スキル・突然変異フラグを保持するデータクラス |
| `QRCodeGenerator.cs` | カードID+シード値をQRコード画像として生成する(ZXing.Net使用) |
| `QRCodeScanner.cs` | カメラ映像からQRコードをスキャンする(ZXing.Net使用、未導入時はテスト機能で代替) |
| `FirebaseCardListener.cs` | QRScanシーン：再スキャンされたカードのcardIdをもとにFirebaseから確定済みキャラクターを取得し、所持キャラクターとして登録する |
| `GameManager.cs` | シーンをまたいで所持キャラクターのコレクションと編成(出撃順)を保持するシングルトン |
| `TitleManager.cs` | タイトルシーン：3つのボタンでキャラクター登録/バトル/QRコード作成(開発者画面)へ遷移 |
| `TeamSelectionManager.cs` | チーム編成シーン：所持キャラクターから出撃させる最大3体を選択する |
| `ReturnToTitleOnEscape.cs` | ESCキーでタイトルシーンに戻る共通コンポーネント(各シーンに配置して使用) |
| `CharacterCreationManager.cs` | キャラクター作成(カード作成)シーン：スペースキーでカードID+シード値のみを生成しQR化(中身はまだ何も決めない) |
| `BattleManager.cs` | バトルシーン：選択編成(または不足分をランダム生成)で3vs3の自動戦闘を実行、勝敗UIを表示 |

## カード作成〜キャラクター登録の流れ
設計書のフロー「イラスト解析→ステータス自動生成→QR付きカード印刷→QR読み取りで対戦」を見据え、**QR作成(Unity・名前なし)とキャラクター登録(スマホ・名前あり)を別工程**に分けています。キャラクターの中身を確定させて名前を入力する作業は**スマホ側(`docs/`のWebページ)で完結**し、Unityは対戦直前に同じカードを再スキャンしてFirebaseから結果を呼び出すだけです。

```
① CharacterCreationシーン    ② カードを印刷して     ③ スマホ(docs/)で       ④ 対戦前にUnityの
   （Unity・開発者画面）          カードに貼り付け        QRを読み取り登録         QRScanシーンで
   スペースキーでカードID+                                （名前入力→ステータス     同じカードを再スキャン
   シード値だけを生成しQR化                                 確定→Firebaseに保存）    → GameManagerに追加
```

- **①CharacterCreationシーン**：スペースキーを押すたびに新しいカード(カードID+シード値のみ)が生成され、QRコードとして表示される。ステータス・属性・スキル・名前は一切決めない。何枚でも連続で作成できる（枚数制限なし）
- **②印刷**：表示されたQRコードを画像として書き出し、カードに印刷・貼り付ける（現状はUnity内表示のみ。書き出し方法は後述の「今後の課題」を参照）
- **③スマホでの登録**：プレイヤーが `docs/` のWebページ（GitHub Pages）をスマホで開き、カードのQRを読み取る。その場でJavaScript側がシード値からステータス・属性・スキルを確定させ、名前を入力すると `Firebase Realtime Database` の `/characters/{cardId}` に保存される（Unityの起動は不要）
- **④Unity(QRScanシーン)での呼び出し**：対戦の前に、同じ物理カードをUnity側のカメラ(`QRCodeScanner`)でもう一度スキャンする。`FirebaseCardListener`がcardIdをもとに`/characters/{cardId}`をFirebaseから取得し、`GameManager`の所持キャラクターとして追加する。まだスマホで登録されていないカードをスキャンした場合はその旨が表示される

## 設計書との対応関係
オリサモ企画書(2. ゲームシステム・仕様)の以下の要件に対応しています。

| 設計書の記載 | 実装箇所 |
|---|---|
| 6つの属性(ダメージ増減あり) | `ElementType.cs` + `ElementAffinity.cs`(有利1.5倍/不利0.67倍) |
| イラスト解析→ステータス自動生成 | `CharacterStats.AssignRandomStats()`(現状は乱数で代替。将来AI解析結果に差し替え可能) |
| 他ステータスを参照するスキルをカード下部に自動生成 | `CharacterSkill.cs`(素早さ参照の攻撃/防御アップ、ダメージ参照の回復など4種) |
| 突然変異によるレアカード出現 | `CharacterStats.AssignRandomStats()`内の突然変異判定(発生率5%、該当ステータス1.5倍・名前に★付与) |
| 素早さが高い方から行動、体力が最後まで残った方が勝利 | `BattleManager.RunBattle()` |

**注意:** 属性名(炎・風・雷・水・土・光)とスキル4種の内容は、設計書に具体的な仕様が明記されていなかったため仮に設定したものです。実際のゲームデザインが固まり次第、`ElementType.cs`のenum項目名や`CharacterSkill.cs`のスキル内容を書き換えるだけで反映されます。

## シーン構成（5シーン）
```
        Title ───┬─→ CharacterCreation（QR作成、開発者画面）
                 │        ↓（印刷してカードに貼る→スマホで登録）
                 ├─→ QRScan（同じカードを再スキャンしFirebaseから呼び出す）
                 │        ↓（所持キャラクターが1体以上いる場合）
                 ├─→ TeamSelection（出撃する最大3体を選択）
                 │        ↓
                 └─→ Battle（自動対戦）
```
- **Title** シーン：3つのボタンで各シーンへ直接遷移
- **CharacterCreation** シーン（＝カード作成シーン・開発者画面。カードID+シード値のみのQRを作る）
- **QRScan** シーン（＝キャラクター呼び出しシーン。スマホで登録済みのカードを再スキャンし、Firebaseから確定済みキャラクターを取得して`GameManager`に追加する）
- **TeamSelection** シーン（所持キャラクターから出撃させる最大3体を選ぶ。**現状プロジェクトに未作成 — 手動でのセットアップが必要。下記「4. TeamSelectionシーン」を参照**）
- **Battle** シーン

タイトルからは「キャラクター登録」「バトル」「QRコード作成(開発者用)」の3方向すべてに直接ボタンで遷移できます。
- タイトル → バトル：所持キャラクターが1体以上いればTeamSelectionシーンへ、1体もいなければBattleシーンへ直行（ランダムキャラクターを自動生成して対戦）
- CharacterCreation / QRScan / TeamSelection / Battle のいずれのシーンでも、**ESCキーでタイトルへ戻れます**（`ReturnToTitleOnEscape.cs`）

Build Settings（File > Build Profiles > Scene List）に上記5シーンを登録してください（登録順は自由ですが、インデックス0をTitleにしておくと起動時にタイトルから始まります）。

## ZXing.Netの導入（QRコード生成・読み取りに必要）
QRコードの生成(`QRCodeGenerator.cs`)と読み取り(`QRCodeScanner.cs`)は、どちらもZXing.Netというライブラリを使用します。未導入でもコンパイルエラーにはなりませんが、QR機能自体は動作しません。

1. NuGetForUnity（推奨）または zxing.unity 系の `.unitypackage` を使ってZXing.Netをプロジェクトに導入
2. `Edit > Project Settings > Player > Scripting Define Symbols` に `ORISAMO_ZXING` を追加
3. Android/iOS実機でカメラを使う場合は、`Player Settings` でカメラ使用許可の設定を忘れずに行う（Camera Usage Description 等）

導入前でも、`QRCodeScanner.cs`には**開発用テスト機能**が用意されており、指定キー（デフォルトT）を押すとテスト用のJSONを読み取ったことにしてUIの動作確認ができます。

## 各シーンのセットアップ

### 1. Title シーン
- 空のGameObjectを2つ作成
  - `GameManager`（GameManager.csをアタッチ）※このシーンだけに置けばOK、DontDestroyOnLoadで引き継がれます
  - `TitleManager`（TitleManager.csをアタッチ）
- UI上に3つボタンを配置し、OnClickに以下を登録
  - 「キャラクター登録」ボタン → `TitleManager.GoToCharacterRegistration()`（QRScanシーンへ）
  - 「バトルへ」ボタン → `TitleManager.GoToBattle()`
  - 「QRコード作成（開発者用）」ボタン → `TitleManager.GoToCharacterCreation()`（CharacterCreationシーンへ）
- 本番運用でQRコード作成ボタンを一般利用者に見せたくない場合は、そのボタンのGameObjectを `TitleManager` の `Developer Mode Button` にドラッグし、`Show Developer Mode` をオフにすればタイトル画面から非表示にできます（コード変更不要）

### 2. CharacterCreation シーン（＝カード作成シーン）
- 空のGameObjectを作成し `CharacterCreationManager.cs` をアタッチ
- Canvas上にステータス表示用の `TextMeshProUGUI` を配置し、Inspectorの `Status Text` にドラッグ
- （TextMeshProがプロジェクトに未導入の場合、Window > TextMeshPro > Import TMP Essential Resources を実行してください。または通常のUI.Textを使いたい場合はスクリプト冒頭の `using TMPro;` を `using UnityEngine.UI;` に変更し、型を `Text` に変更してください）
- **QRコード表示（カード印刷用）**
  - Canvas上にRawImageを配置し、Inspectorの `Qr Code Image` にドラッグ
  - ZXing.Net未導入の場合はコンソールに警告が出るのみで、他の機能（ステータス表示）には影響しません
  - 生成されるQRコードの中身は、**カードID+シード値のみのJSON**。ステータス・属性・スキル・名前はこの時点では一切決まっておらず、スマホで登録する際に初めて確定する
- **注意**：このシーンではキャラクターは`GameManager`にまだ保存されません（中身が何も決まっていないため）。スペースキーを押すたびに新しいカードが作られ、前のカードの表示は上書きされます（1枚作るごとにQR画像を書き出す運用を想定）
- **タイトルへ戻る**：空のGameObjectを作成し `ReturnToTitleOnEscape.cs` をアタッチ。ESCキーでいつでもTitleシーンに戻れます

### 3. QRScan シーン（＝キャラクター呼び出しシーン）
スマホ(`docs/`のWebページ)で登録済みのカードをカメラで再スキャンし、Firebaseから確定済みキャラクターを取得して`GameManager`に追加する専用シーン。
- 空のGameObjectを2つ作成
  - `QRScanner`（`QRCodeScanner.cs` をアタッチ）：カメラ制御・QR解析を担当
  - `FirebaseListener`（`FirebaseCardListener.cs` をアタッチ）：Firebaseへの問い合わせ・キャラクター登録を担当
- `FirebaseListener` の Inspector にある `Qr Code Scanner` に、`QRScanner` をドラッグして紐付ける
- `FirebaseListener` の `Database Url` に、FirebaseコンソールのdatabaseURL（例: `https://digitalcard-b825d-default-rtdb.firebaseio.com`）を入力
- カメラ映像をプレビュー表示したい場合、Canvas上にRawImageを配置し `QRScanner` の `Preview Image` にドラッグ（任意）
- 検索結果・エラーメッセージを表示したい場合、Canvas上に `TextMeshProUGUI` を配置し `FirebaseListener` の `Status Text` にドラッグ（任意）
- カードをスキャンすると、`/characters/{cardId}` がFirebaseから取得され、見つかればそのキャラクターが`GameManager`に追加される。まだスマホで登録されていないカードの場合は未登録である旨がステータス表示される
- `Save Scanned Character To Game Manager` はデフォルトでオン。オフにすると検索結果を`GameManager`に反映せず、動作確認だけ行える
- **タイトルへ戻る**：空のGameObjectを作成し `ReturnToTitleOnEscape.cs` をアタッチ。ESCキーでいつでもTitleシーンに戻れます（カメラは自動的に停止されます）

### 4. TeamSelection シーン（＝チーム編成シーン・現状未作成）
所持キャラクターから出撃させる最大3体を選ぶシーン。`TeamSelectionManager.cs` は実装済みだが、**このシーン自体がプロジェクトにまだ存在しない**ため、以下の手順で新規作成する必要がある。これを作らないまま所持キャラクターが1体以上ある状態で「バトルへ」ボタンを押すと、`TitleManager.GoToBattle()`が存在しないシーンをロードしようとしてエラーになる。
- 新しいシーンを作成し `TeamSelection` という名前で保存、Build Settingsに追加
- 空のGameObjectを作成し `TeamSelectionManager.cs` をアタッチ
- **キャラクターボタン用プレハブ**を作成する：ルートに `Button` + `Image`、子に名前・ステータス表示用の `TextMeshProUGUI` を1つ配置し、プレハブ化する → `Character Button Prefab` にドラッグ
- Canvas上に以下を用意しInspectorにドラッグ
  - `List Content`：キャラクターボタンを並べる親（ScrollView内のContentなど。縦に並べたい場合はVertical Layout Groupを付けておく）
  - `Selection Status Text`：「2/3体選択中」のような状態表示テキスト（任意）
  - `Confirm Button`：選択確定してバトルシーンへ進むボタン
- `Confirm Button` のOnClickに `TeamSelectionManager.ConfirmSelection()` を登録
- **タイトルへ戻る**：空のGameObjectを作成し `ReturnToTitleOnEscape.cs` をアタッチ

### 5. Battle シーン
- 空のGameObjectを作成し `BattleManager.cs` をアタッチ
- Canvas上に以下を用意しInspectorにドラッグ
  - `Battle Log Text`：戦闘経過を表示するテキスト
  - `Win Panel` / `Lose Panel`：勝敗時に表示するUIパネル（**最初は非アクティブにしておく**）
  - `Win Panel Text` / `Lose Panel Text`：各パネル内の詳細テキスト（任意）
- シーン読み込み時、選択編成（TeamSelectionで選んでいれば）または不足分をランダム生成したキャラクターで自動的にチームが組まれ、敵チームも自動生成されて自動戦闘が始まります
- **タイトルへ戻る**：空のGameObjectを作成し `ReturnToTitleOnEscape.cs` をアタッチ。ESCキーでいつでもTitleシーンに戻れます

## ゲームの流れ

**カード作成〜登録〜バトルの一連の流れ**
1. CharacterCreationシーンでスペースキー →
   - カードID+シード値のみのQRコードが表示される（ステータス・属性・スキル・名前はまだ何も決まっていない）。このQRを印刷してカードに貼り付ける
   - 続けてスペースキーを押せば、次のカードをどんどん作成できる
2. スマホで `docs/` のWebページを開き、カードのQRを読み取る →
   - ステータス（攻撃10〜30 / 防御5〜20 / 素早さ5〜20）、6属性、他ステータス参照スキルがシード値から確定する（5%の確率で突然変異＝レアカード）
   - 名前を入力すると、確定したステータスが `Firebase Realtime Database` の `/characters/{cardId}` に保存される
3. 対戦の前に、Unityの QRScanシーンで同じカードをもう一度カメラにかざす →
   - `FirebaseCardListener`が`/characters/{cardId}`を取得し、そのキャラクターが`GameManager`の所持キャラクターに追加される
4. TeamSelectionシーンで所持キャラクターから出撃させる最大3体を選ぶ（1体もいない場合はこのシーンへは進まない）
5. Battleシーンへ遷移すると、選んだ編成で自動対戦が始まる

**タイトルから直接バトルへ行った場合**
1. タイトルで「バトルへ」ボタン → 所持キャラクターが1体もいない場合はBattleシーンへ直行
2. キャラクター未登録のため、Battleシーン側で自動的にランダムキャラクターが3体生成され、対戦に使われる（`GameManager`への保存は行われない）

**共通：バトル処理**
- 敵チームは常に3体、ランダムなステータスで生成される
- 各対戦カードで素早さが高い方が先制し、交互に攻撃（属性相性・スキル効果を反映したダメージ計算）
- 一方のチームの3体すべてが倒れた時点で決着、勝者側のUIパネルが表示される

## 今後の課題（現状の制約）
- QRコードは現状Unity画面上に表示されるのみで、**PNG等のファイルとして書き出す機能はまだ実装していません**。実際の印刷運用に入る際は、Texture2Dをファイル保存する処理（`Texture2D.EncodeToPNG()` 等）を追加する必要があります
- 大量のカードを事前にまとめて作りたい場合（Editorツール化、CSV一括生成など）は別途対応が必要です。必要になったタイミングでお知らせください
- Firebase Realtime Databaseの**セキュリティルールファイル(`database.rules.json`)がリポジトリに存在せず、誰でも読み書き可能なテストモードのまま**運用されている。イベント本番運用の前には、最低限 `/characters` 配下だけ読み書きを許可するようルールを絞ることを推奨する
- `/characters`はcardIdをキーに保存されるため、同じ物理カードは何度でも安全に再登録・再スキャンできる（再登録すると同じ場所が上書きされるだけで、seedが同じ以上ステータスも変わらない）。これは意図した仕様であり、TeamSelectionシーンに「他プレイヤーの全キャラクター」が出てくるようなことはない
- TeamSelectionシーンは現状プロジェクトに未作成（上記「各シーンのセットアップ」の該当節を参照して手動作成が必要）

## カスタマイズしやすいポイント
- ステータスの乱数レンジ：`CharacterStats.AssignRandomStats()`（Unity側の予備ロジック）と `docs/app.js` の `generateCharacterStats()`（実際にスマホで使われるロジック）の両方を揃えて変更する必要がある
- HP初期値：`CharacterStats` コンストラクタ内の `maxHp = 100`（および`docs/app.js`の`maxHp`）
- ターンごとの演出間隔：`BattleManager` の `turnInterval`
- チームの人数：`BattleManager` の `teamSize` / `TeamSelectionManager` の `maxTeamSize`
- 属性名：`ElementType.cs` のenum項目名を変更するだけでOK（例：Fire → 炎、など日本語名にすることも可能。`docs/app.js`の`ELEMENT_TYPES`と`ELEMENT_LABELS`も合わせて変更）
- 属性相性の強さ：`ElementAffinity.cs` の `AdvantageMultiplier`(1.5倍) / `DisadvantageMultiplier`(0.67倍)
- スキルの種類・効果：`CharacterSkill.cs` の `SkillType` enumと `GenerateSkillName` / `GetDescription`（`docs/app.js`の`SKILL_TYPES`/`SKILL_NAME_MAP`/`buildSkillDescription`と名称を一致させること）
- 突然変異の発生率・倍率：`CharacterStats.cs` 内の `MutationChance`(5%) / `MutationMultiplier`(1.5倍)（`docs/app.js`側は`0.05`/`1.5`をハードコードしているので合わせて変更）
- QRコードの画像サイズ：`CharacterCreationManager.cs` の `qrCodeSize`（デフォルト512px）
- QRスキャンの間隔・連続読み取り防止時間：`QRCodeScanner.cs` の `scanInterval` / `duplicateCooldown`
- QRデータのバージョン管理：`CharacterStats.cs` / `QRCardData.cs` の `CurrentDataVersion`（データ構造を変更した際にインクリメントする想定）
- タイトルへの戻り先シーン名：`ReturnToTitleOnEscape.cs` の `titleSceneName`
- 開発者画面(QRコード作成)ボタンの表示/非表示：`TitleManager.cs` の `showDeveloperMode`
- FirebaseのdatabaseURL：`FirebaseCardListener.cs` の `databaseUrl`（`docs/app.js`の`firebaseConfig.databaseURL`と同じFirebaseプロジェクトを指すこと）
