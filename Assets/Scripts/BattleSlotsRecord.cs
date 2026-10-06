using System;

/// <summary>
/// Firebase Realtime Databaseの /battleSlots のJSON形状を表すラッパー。
///
/// battleSlotsは「先着2台分の対戦登録」を置く場所で、キー名を player1 / player2 に
/// 固定している(pushで生成するランダムキーのリストにはしていない)。
/// これにより、キーが可変なJSONオブジェクト(辞書)をUnity側で頑張ってパースする必要がなく、
/// JsonUtilityでそのままデシリアライズできる。
///
/// 空いている枠はJSON上のキー自体が存在しない(＝null)ため、
/// 対応するフィールドはデフォルトのnullのままになる。
/// </summary>
[Serializable]
public class BattleSlotsRecord
{
    public FirebaseCharacterRecord player1;
    public FirebaseCharacterRecord player2;
}

/// <summary>
/// /battleSlots.json?shallow=true の応答。埋まっている枠のキーだけが true で返る。
/// 待機中のポーリングで写真を含む全データを毎回取得しないために使う。
/// </summary>
[Serializable]
public class BattleSlotsPresence
{
    public bool player1;
    public bool player2;
}
