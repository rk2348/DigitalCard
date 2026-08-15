using System;
using UnityEngine;

/// <summary>
/// QRコードに埋め込む「カード」の情報。
///
/// 【この仕様変更の狙い】
/// 以前はQRコード作成時点(CharacterCreationManager)でキャラクターの
/// 名前・ステータス・属性・スキルをすべて決定し、その結果(CharacterStats)を
/// そのままJSONにしてQRコードへ埋め込んでいた。
/// 今回、「QRコード作成時点ではキャラクターの中身を一切決めず、QRコードを
/// 読み取った瞬間に初めて決める」という仕様に変更したため、QRコードには
/// 最小限の情報だけを埋め込むようにした：
///   ・cardId : カードを一意に識別するID(印刷管理用。ゲームロジックには未使用)
///   ・seed   : ステータス生成用のシード値
///
/// 読み取り側(QRCharacterStatusDisplay)は、このseedを
/// CharacterStats.AssignRandomStats(int seed) に渡すことで、初めて
/// 属性・ステータス・スキルを確定させる。同じカード(同じseed)を何度読み取っても
/// 同じ中身が再現される。名前だけは読み取った人がその場で自由に付けられる。
/// </summary>
[Serializable]
public class QRCardData
{
    /// カードを一意に識別するID(印刷管理用。ゲームロジックには未使用)
    public string cardId;

    /// ステータス・属性・スキルを決定するためのシード値。
    /// 同じseedからは常に同じCharacterStatsが生成される。
    public int seed;

    /// 今後QRコードの中身の形式を変更した際の互換性チェック用。
    public const int CurrentDataVersion = 1;
    public int dataVersion = CurrentDataVersion;

    /// <summary>
    /// JsonUtilityでの復元(QRコード読み取り時)に必要なパラメータ無しコンストラクタ。
    /// </summary>
    public QRCardData() { }

    public QRCardData(string cardId, int seed)
    {
        this.cardId = cardId;
        this.seed = seed;
    }

    /// <summary>
    /// QRコードに埋め込むJSON文字列に変換する。
    /// </summary>
    public string ToJson()
    {
        return JsonUtility.ToJson(this);
    }

    /// <summary>
    /// QRコードから読み取ったJSON文字列をQRCardDataに変換する。
    /// 形式が不正な場合はnullを返す(呼び出し側でnullチェックを行うこと)。
    /// </summary>
    public static QRCardData FromJson(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            QRCardData data = JsonUtility.FromJson<QRCardData>(json);

            if (data == null || string.IsNullOrEmpty(data.cardId))
            {
                return null;
            }

            if (data.dataVersion > CurrentDataVersion)
            {
                Debug.LogWarning($"未知のデータバージョンです(dataVersion:{data.dataVersion})。読み取り結果が正しく表示されない可能性があります。");
            }

            return data;
        }
        catch (Exception e)
        {
            Debug.LogError("QRコードのカードデータ解析に失敗しました: " + e.Message);
            return null;
        }
    }
}
