Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$ErrorActionPreference = 'Stop'
$out = Join-Path (Get-Location) 'deliverables'
New-Item -ItemType Directory -Force $out | Out-Null

function Esc([string]$s) { [System.Security.SecurityElement]::Escape($s) }
function P([string]$text, [string]$style='Body', [string]$align='left', [bool]$bold=$false) {
  $a = if($align -eq 'center') {'<w:jc w:val="center"/>'} elseif($align -eq 'right') {'<w:jc w:val="right"/>'} else {''}
  $b = if($bold) {'<w:b/>'} else {''}
  return "<w:p><w:pPr><w:pStyle w:val=""$style""/>$a</w:pPr><w:r><w:rPr>$b</w:rPr><w:t xml:space=""preserve"">$(Esc $text)</w:t></w:r></w:p>"
}
function Bullet([string]$text) { "<w:p><w:pPr><w:pStyle w:val=""Body""/><w:ind w:left=""420"" w:hanging=""210""/></w:pPr><w:r><w:t>• $(Esc $text)</w:t></w:r></w:p>" }
function PageBreak { '<w:p><w:r><w:br w:type="page"/></w:r></w:p>' }
function Table([object[]]$rows, [int[]]$widths) {
  $grid = ($widths | ForEach-Object {"<w:gridCol w:w=""$_""/>"}) -join ''
  $xml = "<w:tbl><w:tblPr><w:tblW w:w=""0"" w:type=""auto""/><w:tblLayout w:type=""fixed""/><w:tblBorders><w:top w:val=""single"" w:sz=""4"" w:color=""D9D9D9""/><w:left w:val=""single"" w:sz=""4"" w:color=""D9D9D9""/><w:bottom w:val=""single"" w:sz=""4"" w:color=""D9D9D9""/><w:right w:val=""single"" w:sz=""4"" w:color=""D9D9D9""/><w:insideH w:val=""single"" w:sz=""4"" w:color=""D9D9D9""/><w:insideV w:val=""single"" w:sz=""4"" w:color=""D9D9D9""/></w:tblBorders><w:tblCellMar><w:top w:w=""90"" w:type=""dxa""/><w:left w:w=""110"" w:type=""dxa""/><w:bottom w:w=""90"" w:type=""dxa""/><w:right w:w=""110"" w:type=""dxa""/></w:tblCellMar></w:tblPr><w:tblGrid>$grid</w:tblGrid>"
  for($r=0;$r -lt $rows.Count;$r++) { $shade=if($r -eq 0){'1F4E79'}elseif($r%2 -eq 0){'F5F9FC'}else{'FFFFFF'}; $xml+='<w:tr><w:trPr><w:cantSplit/></w:trPr>'
    for($c=0;$c -lt $rows[$r].Count;$c++){ $white=if($r -eq 0){'<w:color w:val="FFFFFF"/><w:b/>'}else{''}; $xml+="<w:tc><w:tcPr><w:tcW w:w=""$($widths[$c])"" w:type=""dxa""/><w:shd w:val=""clear"" w:fill=""$shade""/><w:vAlign w:val=""center""/></w:tcPr><w:p><w:pPr><w:pStyle w:val=""TableText""/></w:pPr><w:r><w:rPr>$white</w:rPr><w:t xml:space=""preserve"">$(Esc ([string]$rows[$r][$c]))</w:t></w:r></w:p></w:tc>" }
    $xml+='</w:tr>' }
  return $xml+'</w:tbl><w:p><w:pPr><w:spacing w:after="100"/></w:pPr></w:p>'
}
function Save-Docx([string]$name,[string]$header,[string]$body) {
  $path=Join-Path $out $name; if(Test-Path $path){Remove-Item -Force $path}
  $styles=@'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Meiryo" w:hAnsi="Meiryo" w:eastAsia="Meiryo"/><w:sz w:val="20"/><w:lang w:val="ja-JP" w:eastAsia="ja-JP"/></w:rPr></w:rPrDefault></w:docDefaults><w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/><w:pPr><w:spacing w:after="120" w:line="300" w:lineRule="auto"/></w:pPr></w:style><w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:qFormat/><w:rPr><w:color w:val="000000"/><w:sz w:val="40"/><w:b/></w:rPr><w:pPr><w:spacing w:after="220"/></w:pPr></w:style><w:style w:type="paragraph" w:styleId="Subtitle"><w:name w:val="Subtitle"/><w:basedOn w:val="Normal"/><w:rPr><w:color w:val="000000"/><w:sz w:val="24"/></w:rPr><w:pPr><w:spacing w:after="180"/></w:pPr></w:style><w:style w:type="paragraph" w:styleId="H1"><w:name w:val="Heading 1"/><w:basedOn w:val="Normal"/><w:qFormat/><w:rPr><w:color w:val="000000"/><w:sz w:val="28"/><w:b/></w:rPr><w:pPr><w:keepNext/><w:spacing w:before="260" w:after="120"/></w:pPr></w:style><w:style w:type="paragraph" w:styleId="H2"><w:name w:val="Heading 2"/><w:basedOn w:val="Normal"/><w:qFormat/><w:rPr><w:color w:val="000000"/><w:sz w:val="23"/><w:b/></w:rPr><w:pPr><w:keepNext/><w:spacing w:before="180" w:after="80"/></w:pPr></w:style><w:style w:type="paragraph" w:styleId="Body"><w:name w:val="Body"/><w:basedOn w:val="Normal"/></w:style><w:style w:type="paragraph" w:styleId="TableText"><w:name w:val="Table Text"/><w:basedOn w:val="Normal"/><w:rPr><w:sz w:val="18"/></w:rPr><w:pPr><w:spacing w:after="40" w:line="250"/></w:pPr></w:style></w:styles>
'@
  $document="<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><w:document xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships""><w:body>$body<w:sectPr><w:headerReference w:type=""default"" r:id=""rId1""/><w:footerReference w:type=""default"" r:id=""rId2""/><w:pgSz w:w=""11906"" w:h=""16838""/><w:pgMar w:top=""1300"" w:right=""1300"" w:bottom=""1200"" w:left=""1300"" w:header=""600"" w:footer=""600""/></w:sectPr></w:body></w:document>"
  $files=@{
   '[Content_Types].xml'='<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/><Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/><Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/><Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/><Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/></Types>'
   '_rels/.rels'='<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/></Relationships>'
   'word/_rels/document.xml.rels'='<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/></Relationships>'
   'word/styles.xml'=$styles; 'word/document.xml'=$document
   'word/header1.xml'="<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><w:hdr xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main""><w:p><w:pPr><w:jc w:val=""right""/></w:pPr><w:r><w:rPr><w:color w:val=""000000""/><w:sz w:val=""16""/></w:rPr><w:t>ORISAMO  |  $header</w:t></w:r></w:p></w:hdr>"
   'word/footer1.xml'='<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:pPr><w:jc w:val="center"/></w:pPr><w:r><w:rPr><w:color w:val="666666"/><w:sz w:val="16"/></w:rPr><w:t>DigitalCard Project  |  2026年9月29日  |  </w:t></w:r><w:fldSimple w:instr="PAGE"><w:r><w:t>1</w:t></w:r></w:fldSimple></w:p></w:ftr>'
   'docProps/core.xml'='<?xml version="1.0" encoding="UTF-8" standalone="yes"?><cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><dc:title>ORISAMO</dc:title><dc:creator>DigitalCard Project</dc:creator><dcterms:created xsi:type="dcterms:W3CDTF">2026-09-29T00:00:00Z</dcterms:created></cp:coreProperties>'
   'docProps/app.xml'='<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"><Application>DigitalCard Project</Application></Properties>' }
  $fs=[IO.File]::Open($path,[IO.FileMode]::Create); $zip=[IO.Compression.ZipArchive]::new($fs,[IO.Compression.ZipArchiveMode]::Create)
  foreach($k in $files.Keys){$e=$zip.CreateEntry($k);$sw=[IO.StreamWriter]::new($e.Open(),[Text.UTF8Encoding]::new($false));$sw.Write($files[$k]);$sw.Dispose()};$zip.Dispose();$fs.Dispose()
}

$proposal = ''
$proposal += P 'ORISAMO 事業企画書' 'Title' 'center'
$proposal += P 'オリジナルカードを入口にした対面型デジタル対戦体験' 'Subtitle' 'center'
$proposal += P '提出用  Version 1.0  2026年9月29日' 'Body' 'center'
$proposal += P '本書は、カードゲーム原案と現行のDigitalCard実装を統合し、導入・協業検討に必要な価値、提供範囲、実装状況および判断事項を整理した企画書です。' 'Body'
$proposal += Table @(@('文書区分','事業企画書'),@('対象','教育機関、イベント運営者、商業施設、協業候補企業'),@('企画名称','ORISAMO オリサモ'),@('版数','Version 1.0')) @(1800,6500)
$proposal += PageBreak
$proposal += P '目次' 'H1'; $proposal += P '1. 提案概要　2. 体験設計　3. ゲームルール　4. 現行実装　5. 事業価値　6. 導入計画　7. 判断事項' 'Body'
$proposal += P '1. 提案概要' 'H1'
$proposal += P 'ORISAMOは、参加者自身が描いた又は選んだカードをスマートフォンで読み取り、キャラクターとして登録し、会場の大画面で対戦を楽しむ体験型カードゲームです。物理カードの収集性と、デジタル演出・対戦進行を組み合わせることで、短時間でも参加者が自分の作品に愛着を持てる設計です。' 'Body'
$proposal += Table @(@('項目','内容'),@('提供価値','手描きカードをその場でデジタルキャラクター化し、対戦の主役にする'),@('利用場面','学校行事、ワークショップ、商業施設イベント、展示、地域交流企画'),@('参加単位','スマートフォンを持つ参加者2名を基本とする対戦'),@('表示装置','会場PC上のUnityアプリケーション。観戦者にも進行と結果を見せる')) @(1800,6500)
$proposal += P '2. 体験設計' 'H1'
$proposal += P '参加者は、スマートフォンのWeb画面でカードのQRコードを読み取ります。キャラクター作成時には名前入力、実物写真の撮影、背景除去を行い、生成されたステータスとスキルを確認します。対戦時には登録済みカードを再度読み取り、空いている対戦枠に参加します。2名が揃うと会場PCが試合を開始し、各参加者は自分のスマートフォンで攻撃又は防御の選択を行います。' 'Body'
$proposal += P '3. ゲームルール' 'H1'
$proposal += P '原案ルール' 'H2'; $proposal += Bullet '各プレイヤーはカードを3枚選び、場に裏向きで置いて開始する。'; $proposal += Bullet '両者は場のカードを1枚ずつ表にし、HPが0になるまで対戦する。'; $proposal += Bullet 'SPDの高いカードから攻撃する。同値時はランダムに先攻を決定する。'; $proposal += Bullet '攻撃を受けたカードは、攻撃側のATK相当のHPを失う。HPが0になったカードは敗北する。'; $proposal += Bullet '敗北したカードの代わりに、残りの裏向きカードを1枚表にする。最後にカードが残ったプレイヤーが勝利する。'
$proposal += P 'カードの構成' 'H2'; $proposal += P 'カードにはHP、ATK、SPD、属性、スキルを持たせます。原案では属性とスキルが一部の乱数出力に影響し、スキルは記載条件を満たした時に発動します。' 'Body'
$proposal += P '4. 現行デジタル実装と原案との差分' 'H1'
$proposal += P '現行ビルドは、原案を会場型デジタル対戦に適用した1対1版です。商用・正式導入前には、原案の3枚制を復帰させるか、現行の1対1を正式ルールとするかを決定し、表示・データ・テストを統一する必要があります。' 'Body'
$proposal += PageBreak
$proposal += Table @(@('観点','原案','現行実装','提案'),@('編成','3枚','1キャラクター対1キャラクター','イベント版は1対1を正式仕様として先行導入'),@('手番','SPD順でATKダメージ','SPD順で攻撃と防御を交互に実施','選択型の駆け引きを継続'),@('選択','明示なし','強 普 弱。同じ選択なら防御成功','ルール説明を会場表示に追加'),@('勝敗','3枚を全滅させた側が敗北','HP 0で勝敗決定','3枚制は拡張版として別途設計')) @(1250,1900,2050,2050)
$proposal += P '5. 現行実装の価値' 'H1'
$proposal += Table @(@('機能','実装状況','利用者価値'),@('スマホQR読取','実装済み','参加者端末でカード認識が完結する'),@('撮影と背景除去','実装済み','本人又は作品を対戦演出に反映できる'),@('ステータスとスキル生成','実装済み','カードごとの個性と再プレイ性を提供する'),@('2人対戦キュー','実装済み','受付を介さず対戦参加を成立させる'),@('スマホ選択と大画面演出','実装済み','操作は手元、観戦は会場画面に分離する'),@('製品版UI素材','実装済み','HPゲージ、枠、背景、数値プレートを専用素材へ置換済み')) @(2400,1500,3350)
$proposal += PageBreak
$proposal += P '6. 導入計画' 'H1'; $proposal += P '初回導入では、カード配布、QRを掲載した案内、スマートフォン2台、Unity実行PC1台、大型ディスプレイ1台を基本構成とします。スタッフは、QR読み取りとカメラ権限の案内、対戦待機時の誘導、終了後のリセット確認を担当します。会場ネットワークは安定したHTTPS接続を前提とします。' 'Body'
$proposal += P '7. 正式導入前の判断事項' 'H1'; $proposal += Bullet '原案3枚制と現行1対1制の正式採用範囲を決定する。'; $proposal += Bullet 'Firebaseの認証、データ保持期間、利用者写真の同意取得と削除手順を確定する。'; $proposal += Bullet '会場ごとの同時参加数、通信断時の復旧、スタッフ運用を受入試験で確認する。'
Save-Docx 'ORISAMO_企画書.docx' '事業企画書' $proposal

$design = ''
$design += P 'ORISAMO システム設計書' 'Title' 'center'; $design += P '現行DigitalCard実装を対象とした機能及び連携設計' 'Subtitle' 'center'; $design += P '提出用  Version 1.0  2026年9月29日' 'Body' 'center'
$design += P '本書は、現行のスマホWeb、Firebase Realtime Database、Unity会場表示アプリケーションで構成されるORISAMOの設計を記述します。実装済み仕様と、製品導入前に解消すべき制約を区別しています。' 'Body'
$design += Table @(@('対象システム','DigitalCard'),@('Unity版','6000.3.12f1'),@('主要構成','スマホWeb、Firebase Realtime Database、Unity、TextMeshPro、UGUI'),@('対象対戦','2名による1対1のリアルタイム対戦')) @(2200,6100)
$design += PageBreak; $design += P '目次' 'H1'; $design += P '1. システム概要　2. アーキテクチャ　3. 画面と機能　4. データ設計　5. バトル設計　6. 非機能要件　7. 運用と品質保証' 'Body'
$design += P '1. システム概要' 'H1'; $design += P '参加者のスマートフォンは、QR読み取り、写真撮影、キャラクター登録、対戦選択を担当します。Unityアプリケーションは、対戦待機、ステータス表示、ダメージ計算、キャラクター演出、勝敗表示を担当します。Firebase Realtime Databaseは、登録済みキャラクター、対戦枠、ターン選択を共有します。' 'Body'
$design += P '2. アーキテクチャ' 'H1'; $design += Table @(@('層','責務','主要要素'),@('スマホWeb','QR読取、撮影、背景除去、登録、強 普 弱の選択','docs/index.html、docs/app.js、html5-qrcode、Firebase SDK'),@('共有データ','キャラクター、対戦参加、ターン状態の保存と購読','Firebase Realtime Database'),@('会場Unity','試合成立、ダメージ計算、演出、UI、結果','BattleQueueIntake、BattleTurnSync、BattleManager'),@('表示素材','背景、HPゲージ、枠、数値プレート','Resources/BattleUI と BattleArenaArtTheme')) @(1300,3600,3400)
$design += P '対戦シーケンス' 'H2'; $design += P '1. スマホで登録済みカードを読み取る。2. 空いているbattleSlots/player1又はplayer2へトランザクションで参加する。3. Unityが両枠を検知し、キャラクターを生成後に枠をクリアする。4. UnityがactiveBattleへ手番を登録する。5. 該当スマホが選択値を書き込み、Unityが解決と演出を行う。6. HPが0になるまで反復し、Unityが勝者をactiveBattleへ記録する。' 'Body'
$design += P '3. 画面と機能' 'H1'; $design += Table @(@('画面','利用者','主な機能'),@('スマホホーム','参加者','キャラクター作成又は対戦開始を選択'),@('QR読取','参加者','カメラでカードQRを解析。HTTPS又はlocalhostが必須'),@('写真と名前入力','参加者','実物写真の撮影、背景除去、名前入力、登録'),@('対戦待機と選択','参加者','参加枠、手番、強 普 弱の選択結果を表示'),@('Unityタイトル','運営者','バトル開始。QR作成は開発者モード時のみ表示'),@('Unityバトル','観戦者と参加者','待機、HP、キャラクター、攻防演出、勝敗を大画面表示')) @(1900,1600,4800)
$design += P '4. データ設計' 'H1'; $design += Table @(@('パス','用途','主な属性'),@('/characters','登録履歴','キャラクター記録をpush保存'),@('/characterByCard/{cardId}','カードの最新登録情報','cardId、名前、属性、ATK、DEF、SPD、HP、スキル、写真'),@('/battleSlots/player1 player2','対戦参加枠','登録済みキャラクターのスナップショット'),@('/activeBattle','対戦進行','turn、attackerSlot、defenderSlot、attackerChoice、defenderChoice、status、winnerSlot')) @(2400,2100,3800)
$design += P 'キャラクターレコード' 'H2'; $design += P 'FirebaseCharacterRecordはcardId、seed、characterName、element、attack、defense、speed、hp、maxHp、isMutation、skillType、ratio、skillName、skillDescription、photoDataUrlを保持します。写真は最大辺約500pxに縮小したPNG data URLとして保存され、UnityはSpriteへ変換して表示します。' 'Body'
$design += P '5. バトル設計' 'H1'; $design += P '初期HPは100です。SPDが高い側を先攻とし、同値時は現行実装上プレイヤー側を先攻とします。各攻撃では、攻撃側と防御側が強、普、弱を選択します。同一レベルなら防御成功としてダメージは発生しません。異なる場合は命中し、HPが0以下になった側が敗北します。' 'Body'
$design += Table @(@('計算要素','現行仕様'),@('基礎ダメージ','max 1 有効ATK - 有効DEF'),@('属性相性','有利 1.5、不利 0.67、中立 1.0'),@('選択倍率','弱 0.7、普 1.0、強 1.4'),@('必殺技','キャラクター固有レベルと一致した命中時に1.5倍'),@('LifeDrain','必殺技命中時、与ダメージのratioを回復'),@('Overdrive','必殺技命中時、ATK x ratioを追加')) @(2300,6100)
$design += P '原案との整合性' 'H2'; $design += P '原案のHP、ATK、SPD、属性、スキルというカード性は維持しています。一方、現行は1キャラクター対戦と強普弱選択を採用しています。3枚制を採用する場合は、チームデータ、出撃順、敗北時の交代、勝敗条件、画面表示、Firebaseスキーマを拡張する必要があります。' 'Body'
$design += P '6. 非機能要件とセキュリティ' 'H1'; $design += Table @(@('項目','現行設計','製品導入時の要件'),@('通信','Unityは1.5秒及び1秒間隔でポーリング','通信断、タイムアウト、再参加、重複参加の復旧仕様を定義'),@('認証','Firebase設定をクライアントに配置','匿名認証又はイベント用トークン、Database Rulesを適用'),@('写真','Base64 PNGをRealtime Databaseに保存','本人同意、保存期間、削除、容量上限を規程化'),@('可用性','単一のbattleSlotsとactiveBattle','会場又は卓単位のmatchIdで対戦を分離'),@('監査','Unityログ中心','エラー分類、運営ログ、障害対応手順を整備')) @(1300,3000,4100)
$design += P '7. 運用と品質保証' 'H1'; $design += P '受入試験では、QR読取、カメラ権限拒否、写真あり・なしの登録、同時参加、片方のみ参加、通信断、同一選択、防御成功、各選択倍率、属性相性、スキル発動、HP0、結果表示、次試合へのリセットを確認します。UIはCanvasGroup未設定の参照を前提にせず、対象コンポーネントの存在確認を行う実装方針とします。' 'Body'
$design += P '既知の設計上の留意点' 'H2'; $design += Bullet '旧来のUnity QRScan及びFirebaseCardListenerは、スマホ完結型の現行フローでは主経路ではない。保守対象を明確にし、不要な経路は段階的に廃止する。'; $design += Bullet 'DB URLはInspector設定値に依存する。環境別設定と起動時検証を導入する。'; $design += Bullet '対戦枠がグローバルであるため、複数台運用時はmatchId単位の分離が必要である。'
Save-Docx 'ORISAMO_システム設計書.docx' 'システム設計書' $design
