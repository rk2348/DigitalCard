using UnityEngine;
using UnityEngine.UI;
using TMPro; // TextMeshProを使用。通常のUI.Textを使う場合は using UnityEngine.UI; に変更してください

/// <summary>
/// QRコード作成シーンの制御（開発者用／カード印刷用）。
///
/// 【重要な変更点】
/// 以前はここでキャラクターの名前・ステータス・属性・スキルをすべて決定し、
/// その内容をそのままQRコードに埋め込んでいた。
/// 今回、「QRコード作成時点ではキャラクターの中身を一切決めず、QRコードを
/// 読み取った瞬間（QRScanシーン側）に初めて決定する」という仕様に変更したため、
/// このシーンでは
///   ・カードを一意に識別するID
///   ・ステータス生成用のシード値
/// の2つだけ（QRCardData）をQRコードに埋め込む。キャラクターの中身は
/// 完全に伏せられたまま、QRコードだけが生成される。
///
/// 【セットアップ方法】
/// 1. このシーンに空のGameObjectを作成し、このスクリプトをアタッチ
/// 2. カード生成の確認メッセージを出したい場合はCanvas上にTextMeshProUGUIを配置し
///    statusText にドラッグ（任意）
/// 3. QRコードを表示したい場合は、Canvas上にRawImageを配置し qrCodeImage にドラッグ
///    （ZXing.Net未導入の場合はログに警告が出るだけで、他の機能は問題なく動作します）
/// 4. スペースキーを押すたびに新しいカード（QRコード）が1枚生成されます。
///    印刷したい枚数分、繰り返しスペースキーを押してください
///    （中身は伏せられたままなので、そのまま何枚でも量産できます）。
/// </summary>
public class CharacterCreationManager : MonoBehaviour
{
    [Tooltip("カード生成結果を表示するUIテキスト（任意）")]
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("QRコード表示（カード印刷用）")]
    [Tooltip("生成したQRコードを表示するRawImage（未設定ならQR生成自体をスキップ）")]
    [SerializeField] private RawImage qrCodeImage;

    [Tooltip("QRコードの画像サイズ（ピクセル）")]
    [SerializeField] private int qrCodeSize = 512;

    [Tooltip("連続でスペースキーを押した際に同じ操作が何度も走らないようにするクールダウン（秒）")]
    [SerializeField] private float inputCooldown = 0.5f;

    private float cooldownTimer = 0f;

    private void Update()
    {
        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
            return;
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            CreateCard();
        }
    }

    /// <summary>
    /// 新しいカード（QRコード）を1枚作成する。
    /// キャラクターの中身（名前・ステータス・属性・スキル）はここでは一切決めない。
    /// 決めるのはあくまで「カードID」と「ステータス生成用のシード値」だけ。
    /// </summary>
    private void CreateCard()
    {
        cooldownTimer = inputCooldown;

        // カードを一意に識別するID（印刷管理用。ゲームロジックには使わない）
        string cardId = System.Guid.NewGuid().ToString("N");

        // ステータス生成用のシード値。QRコードを読み取った瞬間、この値を使って
        // CharacterStats.AssignRandomStats(seed) が呼ばれ、初めて中身が決まる。
        int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

        QRCardData cardData = new QRCardData(cardId, seed);

        if (statusText != null)
        {
            statusText.text = $"カードを生成しました\nID: {cardId}\n\n（中身はQRコードを読み取るまでのお楽しみ！）";
        }

        GenerateAndDisplayQRCode(cardData);
    }

    /// <summary>
    /// カード情報（ID・シード値のみ）をJSON化し、QRコード画像として表示する。
    /// この文字列がそのまま、カードに印刷するQRコードの中身になる。
    /// ZXing.Net未導入の場合はコンソールに警告が出るのみで、他の処理には影響しない。
    /// </summary>
    private void GenerateAndDisplayQRCode(QRCardData cardData)
    {
        if (qrCodeImage == null)
        {
            return; // QR表示を使わない構成
        }

        string json = cardData.ToJson();
        Texture2D qrTexture = QRCodeGenerator.GenerateTexture(json, qrCodeSize);

        if (qrTexture != null)
        {
            qrCodeImage.texture = qrTexture;
            qrCodeImage.gameObject.SetActive(true);
        }
    }
}
