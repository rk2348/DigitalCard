# キャラクター作成 → 自動バトル ゲーム セットアップ手順

## 構成ファイル
| ファイル | 役割 |
|---|---|
| `ElementType.cs` | 6属性を定義するenum(名称は仮。デザイン確定後に書き換え可能) |
| `ElementAffinity.cs` | 6属性間の相性(ダメージ倍率)を計算するユーティリティ |
| `CharacterSkill.cs` | 他ステータスを参照するスキルのデータクラス(4種類) |
| `CharacterStats.cs` | キャラクターのステータス・属性・スキル・突然変異フラグを保持するデータクラス |
| `SimpleRotator.cs` | 3Dオブジェクトをゆっくり回転させる汎用の演出スクリプト |
| `CharacterModelUtility.cs` | 3Dオブジェクト生成処理の共通化ユーティリティ(キャラクター作成・QR読み取りの両方で使用) |
| `QRCodeGenerator.cs` | ステータスをQRコード画像として生成する(ZXing.Net使用) |
| `QRCodeScanner.cs` | カメラ映像からQRコードをスキャンする(ZXing.Net使用、未導入時はテスト機能で代替) |
| `QRCharacterStatusDisplay.cs` | QRコードから読み取ったステータスを表示し、名前入力→キャラクターとして登録する |
| `GameManager.cs` | シーンをまたいでキャラクターデータを保持するシングルトン |
| `TitleManager.cs` | タイトルシーン：3つのボタンでキャラクター登録/バトル/QRコード作成(開発者画面)へ遷移 |
| `ReturnToTitleOnEscape.cs` | ESCキーでタイトルシーンに戻る共通コンポーネント(各シーンに配置して使用) |
| `CharacterCreationManager.cs` | キャラクター作成(カード作成)シーン：スペースキーでステータス/属性/スキルを決定しQR化(名前はまだ付けない) |
| `BattleManager.cs` | バトルシーン：敵を自動生成し、属性相性・スキル効果を反映した自動戦闘を実行、勝敗UIを表示 |

## カード作成〜キャラクター登録の流れ（今回追加）
設計書のフロー「イラスト解析→ステータス自動生成→QR付きカード印刷→QR読み取りで対戦」を見据え、**QR作成(名前なし)とキャラクター登録(名前あり)を別工程**に分けています。

```
① CharacterCreationシーン         ② カードを印刷して          ③ QRScanシーンで
   スペースキーでカード作成            カードに貼り付け              QRを読み取り
   （ステータス・属性・スキルを決定       （このQRがそのまま           → 名前を入力
    → QRコード化。名前はまだ空）         カードに載る）                → 「登録」でキャラクター確定
                                                                    （GameManagerに保存）
```

- **①CharacterCreationシーン**：スペースキーを押すたびに新しい1枚分のステータスが決定され、QRコードとして表示される。名前はまだ付けない。何枚でも連続で作成できる（枚数制限なし）
- **②印刷**：表示されたQRコードを画像として書き出し、カードに印刷・貼り付ける（現状はUnity内表示のみ。書き出し方法は後述の「今後の課題」を参照）
- **③QRScanシーン**：完成したカードをカメラで読み取ると、ステータスが表示され、名前入力欄と「登録」ボタンが表示される。名前を入力して登録すると、そのキャラクターが`GameManager`に保存され、Battleシーンでそのまま使用できる

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

## シーン構成（4シーン）
```
        Title ───┬─→ CharacterCreation（QR作成、名前なし・開発者画面）
                 │        ↓（印刷してカードに貼る）
                 ├─→ QRScan（QR読み取り→名前入力→登録）
                 │        ↓
                 └─→ Battle（自動対戦）
```
- **Title** シーン：3つのボタンで各シーンへ直接遷移
- **CharacterCreation** シーン（＝カード作成シーン・開発者画面。名前を付けずにステータス＋QRだけ作る）
- **QRScan** シーン（＝キャラクター登録シーン。QRを読み取って名前を付け、`GameManager`に登録する）
- **Battle** シーン

タイトルからは「キャラクター登録」「バトル」「QRコード作成(開発者用)」の3方向すべてに直接ボタンで遷移できます。
- タイトル → バトル（直行）：キャラクター未登録のため、バトルシーン側でランダムキャラクターを自動生成して対戦
- CharacterCreation / QRScan / Battle のいずれのシーンでも、**ESCキーでタイトルへ戻れます**（`ReturnToTitleOnEscape.cs`）

Build Settings（File > Build Profiles > Scene List）に上記4シーンを登録してください（登録順は自由ですが、インデックス0をTitleにしておくと起動時にタイトルから始まります）。

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
- **3Dモデル表示**
  - シーン内に空のGameObject（例："ModelSpawnPoint"）を作成し、カメラに映る位置に配置。Inspectorの `Model Spawn Point` にドラッグ
  - `Character Model Prefab` は現時点では**未設定のままでOK**。未設定の場合、スペースキーを押すと属性に応じて色分けされた仮のカプセルがその場に自動生成される（ゆっくり回転する演出付き）
  - 後日、実際のキャラクターモデルのプレハブが用意できたら `Character Model Prefab` にドラッグするだけで、そのモデルに差し替わる
- **QRコード表示（カード印刷用）**
  - Canvas上にRawImageを配置し、Inspectorの `Qr Code Image` にドラッグ
  - ZXing.Net未導入の場合はコンソールに警告が出るのみで、他の機能（ステータス表示・3Dモデル表示）には影響しません
  - 生成されるQRコードの中身は、**名前が空の状態のステータス(JSON)**。この文字列が印刷カードに載るQRコードの中身になります
- **注意**：このシーンではキャラクターは`GameManager`にまだ保存されません（名前が決まっていないため）。スペースキーを押すたびに新しいカードが作られ、前のカードの表示は上書きされます（1枚作るごとにQR画像を書き出す運用を想定）
- **タイトルへ戻る（今回追加）**：空のGameObjectを作成し `ReturnToTitleOnEscape.cs` をアタッチ。ESCキーでいつでもTitleシーンに戻れます

### 3. Battle シーン
- 空のGameObjectを作成し `BattleManager.cs` をアタッチ
- Canvas上に以下を用意しInspectorにドラッグ
  - `Battle Log Text`：戦闘経過を表示するテキスト
  - `Win Panel` / `Lose Panel`：勝敗時に表示するUIパネル（**最初は非アクティブにしておく**）
  - `Win Panel Text` / `Lose Panel Text`：各パネル内の詳細テキスト（任意）
- シーン読み込み時に自動で敵キャラクターが生成され、自動戦闘が始まります
- **タイトルへ戻る（今回追加）**：空のGameObjectを作成し `ReturnToTitleOnEscape.cs` をアタッチ。ESCキーでいつでもTitleシーンに戻れます

### 4. QRScan シーン（＝キャラクター登録シーン）
印刷したカードをカメラで読み取り、名前を入力してキャラクターとして登録する専用シーン。
- 空のGameObjectを2つ作成
  - `QRScanner`（`QRCodeScanner.cs` をアタッチ）：カメラ制御・QR解析を担当
  - `QRDisplay`（`QRCharacterStatusDisplay.cs` をアタッチ）：読み取り結果の表示・登録処理を担当
- `QRDisplay` の Inspector にある `Qr Code Scanner` に、`QRScanner` をドラッグして紐付ける
- カメラ映像をプレビュー表示したい場合、Canvas上にRawImageを配置し `QRScanner` の `Preview Image` にドラッグ（任意）
- ステータス表示用の `TextMeshProUGUI` を `QRDisplay` の `Status Text` にドラッグ
- **名前入力・登録UI（今回追加）**
  - Canvas上に `TMP_InputField` を配置し、`QRDisplay` の `Name Input Field` にドラッグ
  - Canvas上に `Button` を配置し、`QRDisplay` の `Register Button` にドラッグ（OnClickの設定は不要、スクリプトが自動で紐付けます）
  - 上記2つを1つの親GameObjectにまとめておくと、`Registration Panel` にドラッグすることで「QR読み取り後だけ表示」という制御ができる（任意）
  - QRを読み取ると、これらのUIが表示され、プレイヤーが名前を入力して「登録」を押すとキャラクターが確定する
- `Register To Game Manager` はデフォルトでオン。オンの場合、登録したキャラクターが`GameManager`のプレイヤーキャラクターとして保存され、その後Battleシーンへ遷移すればそのキャラクターで対戦できます
- **タイトルへ戻る（今回追加）**：空のGameObjectを作成し `ReturnToTitleOnEscape.cs` をアタッチ。ESCキーでいつでもTitleシーンに戻れます（カメラは自動的に停止されます）

## ゲームの流れ

**カード作成〜登録〜バトルの一連の流れ**
1. CharacterCreationシーンでスペースキー →
   - ステータス（攻撃10〜30 / 防御5〜20 / 素早さ5〜20）、6属性、他ステータス参照スキルがランダムに割り振られる（5%の確率で突然変異＝レアカード）
   - 名前は空のままQRコードとして表示される（このQRを印刷してカードに貼り付ける）
   - 続けてスペースキーを押せば、次のカードをどんどん作成できる
2. QRScanシーンでカードをカメラにかざす →
   - ステータスが表示され、名前入力欄と「登録」ボタンが出現する
   - 名前を入力して「登録」を押すと、そのキャラクターが`GameManager`に保存される
3. Battleシーンへ遷移すると、登録したキャラクターで自動対戦が始まる

**タイトルから直接バトルへ行った場合**
1. タイトルでEnterキー（または「バトルへ」ボタン） → Battleシーンへ直行
2. キャラクター未登録のため、Battleシーン側で自動的にランダムキャラクターが生成され、`GameManager`に保存される

**共通：バトル処理**
- ランダムなステータスの敵キャラクターが生成される
- 素早さが高い方が先制し、交互に攻撃（属性相性・スキル効果を反映したダメージ計算）
- HPが0以下になった方が敗北、勝者側のUIパネルが表示される

## 今後の課題（現状の制約）
- QRコードは現状Unity画面上に表示されるのみで、**PNG等のファイルとして書き出す機能はまだ実装していません**。実際の印刷運用に入る際は、Texture2Dをファイル保存する処理（`Texture2D.EncodeToPNG()` 等）を追加する必要があります
- 大量のカードを事前にまとめて作りたい場合（Editorツール化、CSV一括生成など）は別途対応が必要です。必要になったタイミングでお知らせください

## カスタマイズしやすいポイント
- ステータスの乱数レンジ：`CharacterStats.AssignRandomStats()` 内の `Random.Range(...)`
- HP初期値：`CharacterStats` コンストラクタ内の `maxHp = 100`
- ターンごとの演出間隔：`BattleManager` の `turnInterval`
- 名前未入力時のデフォルト名：`QRCharacterStatusDisplay.cs` の `defaultCharacterName`
- 属性名：`ElementType.cs` のenum項目名を変更するだけでOK（例：Fire → 炎、など日本語名にすることも可能）
- 属性相性の強さ：`ElementAffinity.cs` の `AdvantageMultiplier`(1.5倍) / `DisadvantageMultiplier`(0.67倍)
- スキルの種類・効果：`CharacterSkill.cs` の `SkillType` enumと `GenerateSkillName` / `GetDescription`
- 突然変異の発生率・倍率：`CharacterStats.cs` 内の `MutationChance`(5%) / `MutationMultiplier`(1.5倍)
- 3Dモデルの回転速度：`SimpleRotator.cs` の `rotationSpeed`
- 3Dモデルの属性別カラー：`CharacterModelUtility.cs` の `GetElementColor()`
- QRコードの画像サイズ：`CharacterCreationManager.cs` の `qrCodeSize`（デフォルト512px）
- QRスキャンの間隔・連続読み取り防止時間：`QRCodeScanner.cs` の `scanInterval` / `duplicateCooldown`
- QRデータのバージョン管理：`CharacterStats.cs` の `CurrentDataVersion`（ステータス構造を変更した際にインクリメントする想定）
- タイトルへの戻り先シーン名：`ReturnToTitleOnEscape.cs` の `titleSceneName`
- 開発者画面(QRコード作成)ボタンの表示/非表示：`TitleManager.cs` の `showDeveloperMode`
