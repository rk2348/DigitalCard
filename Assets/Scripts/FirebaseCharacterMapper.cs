using System;
using UnityEngine;

/// <summary>
/// Firebase の保存レコードをゲーム内のキャラクターデータへ変換する共通入口。
/// カード受付と対戦キューで、同じ変換規則を使う。
/// </summary>
public static class FirebaseCharacterMapper
{
    public static CharacterStats ToCharacterStats(FirebaseCharacterRecord record, string logContext)
    {
        if (record == null)
        {
            Debug.LogWarning($"{logContext}: Character record is null.");
            return null;
        }

        CharacterStats stats = new CharacterStats(record.characterName)
        {
            attack = record.attack,
            defense = record.defense,
            speed = record.speed,
            hp = record.hp,
            maxHp = record.maxHp,
            isMutation = record.isMutation,
        };

        if (Enum.TryParse(record.element, out ElementType element)) stats.element = element;
        else Debug.LogWarning($"{logContext}: Invalid element '{record.element}'.");

        if (Enum.TryParse(record.skillType, out SkillType skillType))
            stats.skill = new CharacterSkill(skillType, record.ratio);
        else
            Debug.LogWarning($"{logContext}: Invalid skill type '{record.skillType}'.");

        stats.specialLevel = Enum.TryParse(record.specialLevel, out AttackLevel specialLevel)
            ? specialLevel
            : BattleRules.ComputeSpecialLevel(record.seed); // specialLevel導入前の登録データ

        stats.photoSprite = CreateSprite(record.photoDataUrl, logContext);
        return stats;
    }

    private static Sprite CreateSprite(string dataUrl, string logContext)
    {
        if (string.IsNullOrEmpty(dataUrl)) return null;

        string base64Data = dataUrl.Substring(dataUrl.IndexOf(',') + 1);
        try
        {
            byte[] imageBytes = Convert.FromBase64String(base64Data);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(imageBytes))
            {
                Debug.LogWarning($"{logContext}: Could not create texture from photo data.");
                UnityEngine.Object.Destroy(texture);
                return null;
            }

            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * 0.5f, 100f);
        }
        catch (FormatException exception)
        {
            Debug.LogWarning($"{logContext}: Invalid photo data: {exception.Message}");
            return null;
        }
    }
}
