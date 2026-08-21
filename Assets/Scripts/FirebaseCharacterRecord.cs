using System;

/// <summary>
/// Firebase Realtime Databaseの /characterByCard/{cardId} に保存されている
/// 確定済みキャラクターデータの形式。
///
/// スマホ側(app.js)がQRスキャン→実物の撮影・背景切り抜き→名前入力→登録、の流れの中で
/// 確定させたステータス・スキル・写真(背景切り抜き済みPNGのdata URL)を、
/// カードを登録するたびに cardId をキーとして上書き保存(.set())している。
/// (登録履歴は /characters 配下にも別途push()で残しているが、こちらは
///  「このカードの最新の中身」を1件だけ引くための場所)
///
/// フィールド名はapp.js側でFirebaseに書き込むJSONのキーと完全に一致させること
/// (JsonUtilityはフィールド名でマッピングするため)。
/// </summary>
[Serializable]
public class FirebaseCharacterRecord
{
    public string cardId;
    public int seed;

    public string characterName;

    /// ElementType の名前("Fire" / "Wind" / "Dark" / "Water" / "Earth" / "Light")
    public string element;

    public int attack;
    public int defense;
    public int speed;
    public int hp;
    public int maxHp;
    public bool isMutation;

    /// SkillType の名前("PowerBoost" / "GuardBoost" / "LifeDrain" / "Overdrive")
    public string skillType;
    public float ratio;
    public string skillName;
    public string skillDescription;

    /// 背景切り抜き済みの写真(PNGのdata URL文字列。例: "data:image/png;base64,....")。
    /// 撮影せずに登録した場合は空/nullになる。
    public string photoDataUrl;
}
