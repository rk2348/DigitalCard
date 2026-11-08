using System;
using System.Collections.Generic;
using UnityEngine;

public enum IntroductionShotKind { Hook, Title, Capture, Name, Summon, Choice, Battle, Closing }

[Serializable]
public class IntroductionShot
{
    public IntroductionShotKind kind;
    [Min(1)] public float seconds = 8;
    public string heading;
    [TextArea] public string caption;
    public Vector3 cameraFrom = new Vector3(0, 5, -17);
    public Vector3 cameraTo = new Vector3(1, 4, -14);
    public Vector3 lookAt = new Vector3(0, 2, 0);
    [Range(25, 75)] public float fieldOfView = 45;
}

[CreateAssetMenu(menuName = "オリサモ/動画構成", fileName = "IntroductionSequence")]
public class IntroductionSequence : ScriptableObject
{
    public const int Fps = 30;
    public AudioClip music;
    [Range(0, 1)] public float musicVolume = .65f;
    public List<IntroductionShot> shots = new List<IntroductionShot>();
    public float Duration
    {
        get { float value = 0; foreach (var shot in shots) value += Mathf.Max(1, shot.seconds); return value; }
    }
    public int FrameCount => Mathf.CeilToInt(Duration * Fps);
    public int FindShot(float seconds, out float localTime)
    {
        localTime = Mathf.Clamp(seconds, 0, Mathf.Max(0, Duration - 1f / Fps));
        for (int i = 0; i < shots.Count; i++)
        {
            float duration = Mathf.Max(1, shots[i].seconds);
            if (localTime < duration || i == shots.Count - 1) return i;
            localTime -= duration;
        }
        return -1;
    }
    public void SetDefaults()
    {
        shots.Clear();
        Add(IntroductionShotKind.Hook, 7, "キミの作品が、主役になる。", "想像した相棒が、いま動き出す。", new Vector3(-5,3,-9), new Vector3(-2,3,-8), new Vector3(-3,2,0));
        Add(IntroductionShotKind.Title, 8, "オリジナルサモナー", "つくって、召喚、バトル。", new Vector3(-3,7,-20), new Vector3(0,5,-16), new Vector3(0,2,0));
        Add(IntroductionShotKind.Capture, 10, "作品を、キャラクターに。", "作品の姿を取り込み、背景を切り抜く。", new Vector3(-8,4,-12), new Vector3(-7,4,-11), new Vector3(-5,2,0));
        Add(IntroductionShotKind.Name, 9, "名前をつけて、相棒に。", "自分だけのキャラクターをカードに登録。", new Vector3(-8,4,-12), new Vector3(-7,3,-11), new Vector3(-5,2,0));
        Add(IntroductionShotKind.Summon, 9, "カードから、アリーナへ。", "光の中から、キミの相棒を召喚。", new Vector3(-5,7,-17), new Vector3(0,4,-15), new Vector3(0,2,0));
        Add(IntroductionShotKind.Choice, 11, "その一手が、勝負を変える。", "強・普・弱。相手の選択を読もう。", new Vector3(0,6,-18), new Vector3(2,4,-16), new Vector3(0,2,0));
        Add(IntroductionShotKind.Battle, 12, "最後の一撃まで、目を離すな。", "相棒たちの力が、アリーナでぶつかる。", new Vector3(-2,4,-15), new Vector3(2,3,-12), new Vector3(0,2,0));
        Add(IntroductionShotKind.Closing, 9, "次の主役は、キミの作品。", "オリジナルサモナー  ／  オリサモ", new Vector3(0,4,-14), new Vector3(0,7,-20), new Vector3(0,2,0));
    }
    void Add(IntroductionShotKind kind, float seconds, string heading, string caption, Vector3 from, Vector3 to, Vector3 look)
    {
        shots.Add(new IntroductionShot { kind = kind, seconds = seconds, heading = heading, caption = caption, cameraFrom = from, cameraTo = to, lookAt = look });
    }
}
