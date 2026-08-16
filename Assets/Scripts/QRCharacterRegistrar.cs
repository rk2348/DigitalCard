using System;
using UnityEngine;

/// <summary>
/// QRWebServerが受信したカード情報(cardId＋seed)とスマホで入力されたcharacterNameから
/// CharacterStats.AssignRandomStats(seed) を呼び出してキャラクターを確定させ、
/// その結果をJSONにしてスマホへ返す(=スマホ側でステータスを表示するため)。
///
/// ステータス表示・キャラクター生成のUIはすべてスマホ側(index.html / app.js)で完結させる
/// 方針のため、Unity側にはUI表示処理を持たない（QRCharacterStatusDisplay.csは不要）。
///
/// 【セットアップ方法】
/// 1. QRScanシーン内の、QRWebServerと同じ（または別の）GameObjectにこのスクリプトをアタッチ
/// 2. Inspectorの qrWebServer フィールドに、QRWebServerコンポーネントをドラッグする
/// </summary>
public class QRCharacterRegistrar : MonoBehaviour
{
    [Tooltip("QRWebServerコンポーネントをドラッグしてください")]
    [SerializeField] private QRWebServer qrWebServer;

    [Tooltip("オンにすると、確定したキャラクターをGameManagerにプレイヤーキャラクターとして保存する")]
    [SerializeField] private bool saveScannedCharacterToGameManager = false;

    private void OnEnable()
    {
        if (qrWebServer == null)
        {
            Debug.LogWarning("QRCharacterRegistrar: qrWebServerが設定されていません。Inspectorで設定してください。");
            return;
        }

        qrWebServer.ProcessCardJson = HandleCardJson;
    }

    private void OnDisable()
    {
        if (qrWebServer != null)
        {
            qrWebServer.ProcessCardJson = null;
        }
    }

    /// <summary>
    /// QRWebServerからメインスレッドで呼ばれる。スマホから送られてきたJSON
    /// （例: {"cardId":"C001","seed":12345,"characterName":"りょうま"}）を受け取り、
    /// キャラクターを確定させて、スマホへ返すレスポンスJSONを組み立てて返す。
    /// </summary>
    private string HandleCardJson(string requestJson)
    {
        CardRegistrationRequest req;
        try
        {
            req = JsonUtility.FromJson<CardRegistrationRequest>(requestJson);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[QRCharacterRegistrar] JSONのパースに失敗しました: " + e.Message);
            return BuildErrorResponse("カードの形式が正しくありません");
        }

        if (req == null || string.IsNullOrEmpty(req.cardId) || string.IsNullOrEmpty(req.characterName))
        {
            Debug.LogWarning("[QRCharacterRegistrar] cardIdまたはcharacterNameが空のデータを受信しました: " + requestJson);
            return BuildErrorResponse("カードIDまたは名前が空です");
        }

        CharacterStats stats = new CharacterStats(req.characterName);
        stats.AssignRandomStats(req.seed);

        Debug.Log($"[QRCharacterRegistrar] キャラクター登録: cardId={req.cardId}\n{stats}");

        if (saveScannedCharacterToGameManager && GameManager.Instance != null)
        {
            GameManager.Instance.SavePlayerCharacter(stats);
        }

        return BuildStatsResponse(stats);
    }

    /// <summary>確定したキャラクターのステータスを、スマホ表示用のJSONに変換する。</summary>
    private string BuildStatsResponse(CharacterStats stats)
    {
        var res = new CharacterStatsResponse
        {
            status = "ok",
            characterName = stats.characterName,
            element = stats.element.ToString(),
            attack = stats.attack,
            defense = stats.defense,
            speed = stats.speed,
            hp = stats.hp,
            maxHp = stats.maxHp,
            isMutation = stats.isMutation,
            skillName = stats.skill != null ? stats.skill.skillName : "",
            skillDescription = stats.skill != null ? stats.skill.GetDescription() : ""
        };

        return JsonUtility.ToJson(res);
    }

    private string BuildErrorResponse(string message)
    {
        var res = new ErrorResponse { status = "error", message = message };
        return JsonUtility.ToJson(res);
    }

    /// <summary>スマホ側(app.js)から送られてくるリクエストJSONの形式。</summary>
    [Serializable]
    private class CardRegistrationRequest
    {
        public string cardId;
        public int seed;
        public string characterName;
    }

    /// <summary>スマホ側へ返す、確定したキャラクターステータスのJSON形式。</summary>
    [Serializable]
    private class CharacterStatsResponse
    {
        public string status;
        public string characterName;
        public string element;
        public int attack;
        public int defense;
        public int speed;
        public int hp;
        public int maxHp;
        public bool isMutation;
        public string skillName;
        public string skillDescription;
    }

    [Serializable]
    private class ErrorResponse
    {
        public string status;
        public string message;
    }
}
