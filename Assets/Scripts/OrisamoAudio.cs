using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ORISAMOの効果音・BGM・観客の歓声。
///
/// 音の素材がなくても鳴るよう、すべての音を実行時にコードで合成する(初めて使う時に1回だけ作ってキャッシュする)。
/// 本物の素材を使いたい場合は Resources/Audio/{名前} に置けば、合成した音の代わりにそちらを使う。
///   効果音の名前: Sfx の項目名(例: HitStrong, KnockOut)
///   BGMの名前   : Bgm_ + Bgm の項目名(例: Bgm_Battle)
///   観客のざわめき: CrowdLoop
///
/// シーンに置く必要はない(最初に呼ばれた時に、シーンをまたいで残る再生用のGameObjectを作る)。
/// 効果音とBGMは Time.timeScale の影響を受けない(スローモーション中も同じ速さで鳴る)。
/// </summary>
public static class OrisamoAudio
{
    public enum Sfx
    {
        Click, Join, Reveal, Guard, HitWeak, HitNormal, HitStrong, Charge, Special,
        FinisherCue, FinisherImpact, Land, KnockOut, Victory, Tick, Rare, Cheer, CardCreated,
        ElementFire, ElementWind, ElementDark, ElementWater, ElementEarth, ElementLight,
    }

    public enum Bgm { None, Title, Waiting, Battle, Result }

    public static float SfxVolume = 0.8f;
    public static float BgmVolume = 0.4f;
    public static float CrowdVolume = 0.55f;

    private const int Rate = 22050;
    private static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
    private static readonly Dictionary<Sfx, float> lastPlayed = new Dictionary<Sfx, float>();
    private static OrisamoAudioHost host;

    private static OrisamoAudioHost Host
    {
        get
        {
            if (host != null) return host;
            GameObject go = new GameObject("OrisamoAudio");
            UnityEngine.Object.DontDestroyOnLoad(go);
            host = go.AddComponent<OrisamoAudioHost>();
            return host;
        }
    }

    /// <summary>効果音を鳴らす。同じ音が短い間に重なりすぎないよう、ごく短い間隔の連打は間引く。</summary>
    public static void Play(Sfx sfx, float volume = 1f, float pitch = 1f)
    {
        float minInterval = sfx == Sfx.Cheer ? 1.4f : sfx == Sfx.Tick ? 0.2f : 0.03f;
        float now = Time.unscaledTime;
        if (lastPlayed.TryGetValue(sfx, out float last) && now - last < minInterval) return;
        lastPlayed[sfx] = now;

        AudioClip clip = GetClip(sfx.ToString(), () => SynthesizeSfx(sfx));
        if (clip != null) Host.PlayOneShot(clip, Mathf.Clamp01(volume) * SfxVolume, pitch);
    }

    /// <summary>属性ごとの攻撃音。</summary>
    public static void PlayElement(ElementType element, float volume = 1f)
    {
        switch (element)
        {
            case ElementType.Fire: Play(Sfx.ElementFire, volume); break;
            case ElementType.Wind: Play(Sfx.ElementWind, volume); break;
            case ElementType.Dark: Play(Sfx.ElementDark, volume); break;
            case ElementType.Water: Play(Sfx.ElementWater, volume); break;
            case ElementType.Earth: Play(Sfx.ElementEarth, volume); break;
            case ElementType.Light: Play(Sfx.ElementLight, volume); break;
        }
    }

    /// <summary>BGMを切り替える(1秒ほどでクロスフェード)。同じBGMなら何もしない。</summary>
    public static void PlayBgm(Bgm bgm)
    {
        AudioClip clip = bgm == Bgm.None ? null : GetClip("Bgm_" + bgm, () => SynthesizeBgm(bgm));
        Host.SwitchBgm(bgm, clip);
    }

    /// <summary>観客のざわめきの大きさ(0〜1)。スタジアムが毎フレーム渡す。呼ばれなくなると自然に消える。</summary>
    public static void SetCrowd(float excitement)
    {
        AudioClip clip = GetClip("CrowdLoop", SynthesizeCrowdLoop);
        Host.SetCrowd(clip, Mathf.Clamp01(excitement));
    }

    private static AudioClip GetClip(string name, Func<float[]> synthesize)
    {
        if (cache.TryGetValue(name, out AudioClip clip) && clip != null) return clip;
        clip = Resources.Load<AudioClip>("Audio/" + name);
        if (clip == null)
        {
            float[] data = synthesize();
            if (data == null || data.Length == 0) return null;
            Normalize(data, 0.9f);
            clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
        }
        cache[name] = clip;
        return clip;
    }

    // ==================== 効果音の合成 ====================

    private static float[] SynthesizeSfx(Sfx sfx)
    {
        System.Random random = new System.Random((int)sfx * 7919 + 17);
        switch (sfx)
        {
            case Sfx.Click:
            {
                float[] b = Buffer(0.06f);
                Sweep(b, 0f, 0.06f, 1900f, 1200f, 0.6f, 60f, Wave.Sine);
                return b;
            }
            case Sfx.Join:
            {
                float[] b = Buffer(0.55f);
                Tone(b, 0f, 0.25f, 660f, 0.5f, 14f, Wave.Triangle);
                Tone(b, 0.12f, 0.4f, 990f, 0.5f, 9f, Wave.Triangle);
                Tone(b, 0.12f, 0.4f, 1320f, 0.15f, 12f, Wave.Sine);
                return b;
            }
            case Sfx.Reveal:
            {
                float[] b = Buffer(0.9f);
                Bell(b, 0f, 0.9f, 880f, 0.6f);
                Bell(b, 0.05f, 0.85f, 1320f, 0.4f);
                Noise(b, random, 0f, 0.05f, 0.3f, 6000f, 80f);
                return b;
            }
            case Sfx.Guard:
            {
                float[] b = Buffer(0.8f);
                foreach (float f in new[] { 520f, 1235f, 2110f, 3290f })
                    Tone(b, 0f, 0.8f, f, 0.3f, 6f + f / 900f, Wave.Sine);
                Noise(b, random, 0f, 0.08f, 0.7f, 5000f, 50f);
                return b;
            }
            case Sfx.HitWeak: return Hit(random, 0.35f, 0.5f, 160f, 4000f);
            case Sfx.HitNormal: return Hit(random, 0.5f, 0.75f, 130f, 3000f);
            case Sfx.HitStrong:
            {
                float[] b = Hit(random, 0.8f, 1f, 110f, 2400f);
                Drive(b, 2.2f);
                return b;
            }
            case Sfx.Charge:
            {
                float[] b = Buffer(0.7f);
                NoiseSweep(b, random, 0f, 0.7f, 0.5f, 400f, 4000f);
                Sweep(b, 0f, 0.7f, 220f, 880f, 0.25f, 0f, Wave.Saw, 1.5f);
                return b;
            }
            case Sfx.Special:
            {
                float[] b = Buffer(1.3f);
                foreach (float f in new[] { 220f, 277f, 330f })
                    Sweep(b, 0f, 1.2f, f, f * 2f, 0.22f, 0f, Wave.Saw, 2f);
                NoiseSweep(b, random, 0.1f, 1.2f, 0.35f, 600f, 7000f);
                return b;
            }
            case Sfx.FinisherCue:
            {
                float[] b = Buffer(1.6f);
                Sweep(b, 0f, 1.2f, 70f, 40f, 0.9f, 3f, Wave.Sine);
                Sweep(b, 0.1f, 1.4f, 300f, 2400f, 0.25f, 0f, Wave.Square, 1.2f);
                Noise(b, random, 0f, 0.4f, 0.6f, 2500f, 6f);
                Bell(b, 0.05f, 1.5f, 1760f, 0.25f);
                return b;
            }
            case Sfx.FinisherImpact:
            {
                float[] b = Buffer(2.2f);
                Sweep(b, 0f, 2f, 95f, 28f, 1f, 1.8f, Wave.Sine);
                Noise(b, random, 0f, 2.2f, 0.9f, 3500f, 2.2f);
                Noise(b, random, 0f, 0.12f, 0.8f, 9000f, 30f);
                Drive(b, 2.6f);
                return b;
            }
            case Sfx.Land:
            {
                float[] b = Buffer(0.6f);
                Sweep(b, 0f, 0.5f, 120f, 45f, 0.9f, 7f, Wave.Sine);
                Noise(b, random, 0f, 0.6f, 0.45f, 900f, 6f);
                return b;
            }
            case Sfx.KnockOut:
            {
                float[] b = Buffer(2.2f);
                foreach (float f in new[] { 110f, 164.8f, 220f, 329.6f })
                    Tone(b, 0f, 2.2f, f, 0.3f, 1.4f, Wave.Saw, 1800f);
                Sweep(b, 0f, 0.8f, 80f, 40f, 0.8f, 4f, Wave.Sine);
                Noise(b, random, 0f, 0.1f, 0.6f, 6000f, 30f);
                return b;
            }
            case Sfx.Victory:
            {
                float[] b = Buffer(2.4f);
                float[] notes = { 523.3f, 659.3f, 784f, 1046.5f };
                for (int i = 0; i < notes.Length; i++) Tone(b, i * 0.12f, 0.3f, notes[i], 0.35f, 8f, Wave.Square, 3500f);
                foreach (float f in new[] { 523.3f, 659.3f, 784f, 1046.5f })
                    Tone(b, 0.5f, 1.9f, f, 0.22f, 1.6f, Wave.Square, 3000f);
                return b;
            }
            case Sfx.Tick:
            {
                float[] b = Buffer(0.08f);
                Tone(b, 0f, 0.08f, 1050f, 0.6f, 60f, Wave.Square, 4000f);
                return b;
            }
            case Sfx.Rare:
            {
                float[] b = Buffer(1.8f);
                for (int i = 0; i < 26; i++)
                {
                    float start = (float)random.NextDouble() * 1.3f;
                    float f = 1500f + (float)random.NextDouble() * 3000f;
                    Tone(b, start, 0.45f, f, 0.18f, 9f, Wave.Sine);
                }
                foreach (float f in new[] { 784f, 987.8f, 1174.7f, 1568f })
                    Bell(b, 0f, 1.8f, f, 0.25f);
                return b;
            }
            case Sfx.Cheer: return Crowd(random, 2.6f, true);
            case Sfx.CardCreated:
            {
                float[] b = Buffer(0.35f);
                Tone(b, 0f, 0.12f, 1046.5f, 0.4f, 20f, Wave.Triangle);
                Tone(b, 0.08f, 0.25f, 1568f, 0.4f, 14f, Wave.Triangle);
                return b;
            }
            case Sfx.ElementFire:
            {
                float[] b = Buffer(1f);
                NoiseSweep(b, random, 0f, 1f, 0.8f, 2500f, 500f);
                for (int i = 0; i < 30; i++) Noise(b, random, (float)random.NextDouble() * 0.9f, 0.02f, 0.5f, 8000f, 120f);
                Sweep(b, 0f, 0.6f, 90f, 50f, 0.5f, 4f, Wave.Sine);
                return b;
            }
            case Sfx.ElementWind:
            {
                float[] b = Buffer(1f);
                NoiseSweep(b, random, 0f, 0.5f, 0.7f, 600f, 6000f);
                NoiseSweep(b, random, 0.4f, 0.6f, 0.5f, 6000f, 900f);
                return b;
            }
            case Sfx.ElementDark:
            {
                float[] b = Buffer(1.2f);
                foreach (float f in new[] { 55f, 58.3f, 82.4f })
                    Tone(b, 0f, 1.2f, f, 0.35f, 2.5f, Wave.Saw, 600f);
                Sweep(b, 0f, 1f, 400f, 120f, 0.25f, 2f, Wave.Square, 1f);
                return b;
            }
            case Sfx.ElementWater:
            {
                float[] b = Buffer(1f);
                Noise(b, random, 0f, 0.6f, 0.6f, 3000f, 6f);
                for (int i = 0; i < 14; i++)
                {
                    float start = (float)random.NextDouble() * 0.7f;
                    float f = 500f + (float)random.NextDouble() * 900f;
                    Sweep(b, start, 0.08f, f, f * 2.2f, 0.35f, 30f, Wave.Sine);
                }
                return b;
            }
            case Sfx.ElementEarth:
            {
                float[] b = Buffer(1.2f);
                Noise(b, random, 0f, 1.2f, 0.9f, 350f, 2.5f);
                for (int i = 0; i < 4; i++) Sweep(b, i * 0.16f, 0.35f, 110f, 40f, 0.6f, 9f, Wave.Sine);
                return b;
            }
            case Sfx.ElementLight:
            {
                float[] b = Buffer(1.2f);
                foreach (float f in new[] { 1046.5f, 1318.5f, 1568f, 2093f })
                    Bell(b, 0f, 1.2f, f, 0.3f);
                Sweep(b, 0f, 0.5f, 800f, 3200f, 0.2f, 0f, Wave.Sine, 2f);
                return b;
            }
            default: return Buffer(0.05f);
        }
    }

    private static float[] Hit(System.Random random, float length, float amp, float bodyFreq, float noiseCutoff)
    {
        float[] b = Buffer(length);
        Sweep(b, 0f, length, bodyFreq * 1.6f, bodyFreq * 0.4f, amp, 9f / length, Wave.Sine);
        Noise(b, random, 0f, length, amp * 0.8f, noiseCutoff, 7f / length);
        Noise(b, random, 0f, 0.03f, amp * 0.6f, 9000f, 80f);
        return b;
    }

    private static float[] Crowd(System.Random random, float length, bool swell)
    {
        float[] b = Buffer(length);
        float lowA = 0f, lowB = 0f, mod = 0.5f;
        float aA = Coefficient(2200f), aB = Coefficient(350f);
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float x = (float)(random.NextDouble() * 2.0 - 1.0);
            lowA += aA * (x - lowA);
            lowB += aB * (x - lowB);
            if (i % 600 == 0) mod = 0.55f + (float)random.NextDouble() * 0.45f;
            float env = swell ? Mathf.Sin(Mathf.Clamp01(t / length) * Mathf.PI) * Mathf.Min(1f, t * 6f) : 1f;
            b[i] += (lowA - lowB) * mod * env;
        }
        return b;
    }

    private static float[] SynthesizeCrowdLoop()
    {
        return MakeLoopable(Crowd(new System.Random(4242), 6f, false), 0.5f);
    }

    // ==================== BGMの合成 ====================

    private static float[] SynthesizeBgm(Bgm bgm)
    {
        switch (bgm)
        {
            case Bgm.Title:
                return Song(96f, new[] { "Am", "F", "C", "G", "Am", "F", "G", "E" }, drums: false, bassEighths: false, arpSixteenths: false, padLevel: 0.3f);
            case Bgm.Waiting:
                return Song(112f, new[] { "C", "G", "Am", "F", "C", "G", "F", "G" }, drums: true, bassEighths: false, arpSixteenths: false, padLevel: 0.25f, softDrums: true);
            case Bgm.Battle:
                return Song(150f, new[] { "Dm", "Dm", "Bb", "C", "Dm", "Dm", "Bb", "A" }, drums: true, bassEighths: true, arpSixteenths: true, padLevel: 0.18f);
            case Bgm.Result:
                return Song(88f, new[] { "F", "C", "Dm", "Bb", "F", "C", "Bb", "C" }, drums: false, bassEighths: false, arpSixteenths: false, padLevel: 0.3f);
            default:
                return null;
        }
    }

    private static readonly Dictionary<string, int[]> Chords = new Dictionary<string, int[]>
    {
        // ルートと構成音(MIDIノート番号。ルートはオクターブ3付近)
        { "C", new[] { 48, 52, 55 } }, { "Dm", new[] { 50, 53, 57 } }, { "E", new[] { 52, 56, 59 } },
        { "F", new[] { 53, 57, 60 } }, { "G", new[] { 55, 59, 62 } }, { "Am", new[] { 57, 60, 64 } },
        { "A", new[] { 57, 61, 64 } }, { "Bb", new[] { 58, 62, 65 } },
    };

    private static float[] Song(float bpm, string[] progression, bool drums, bool bassEighths, bool arpSixteenths, float padLevel, bool softDrums = false)
    {
        float beat = 60f / bpm;
        float bar = beat * 4f;
        float[] b = new float[Mathf.RoundToInt(bar * progression.Length * Rate)];
        System.Random random = new System.Random(Mathf.RoundToInt(bpm * 31));

        for (int barIndex = 0; barIndex < progression.Length; barIndex++)
        {
            int[] chord = Chords[progression[barIndex]];
            float barStart = barIndex * bar;

            // パッド(和音を伸ばす)
            foreach (int note in chord) Pad(b, barStart, bar, Midi(note + 12), padLevel);

            // ベース
            int steps = bassEighths ? 8 : 2;
            for (int s = 0; s < steps; s++)
            {
                float length = bar / steps;
                Pluck(b, barStart + s * length, length * 0.9f, Midi(chord[0] - 12), bassEighths ? 0.45f : 0.4f, Wave.Saw, 700f, bassEighths ? 6f : 1.5f);
            }

            // アルペジオ
            int arpSteps = arpSixteenths ? 16 : 8;
            for (int s = 0; s < arpSteps; s++)
            {
                int note = chord[s % 3] + 24 + (s % 6 >= 3 ? 12 : 0);
                float length = bar / arpSteps;
                Pluck(b, barStart + s * length, length * 1.6f, Midi(note), arpSixteenths ? 0.12f : 0.16f, arpSixteenths ? Wave.Square : Wave.Triangle, 3500f, 10f);
            }

            if (!drums) continue;
            for (int q = 0; q < 4; q++)
            {
                float t = barStart + q * beat;
                if (!softDrums || q % 2 == 0) Kick(b, t, softDrums ? 0.45f : 0.8f);
                if (q % 2 == 1) Snare(b, random, t, softDrums ? 0.15f : 0.4f);
                Hat(b, random, t + beat * 0.5f, softDrums ? 0.08f : 0.14f);
                if (!softDrums) Hat(b, random, t, 0.08f);
            }
        }
        return b;
    }

    private static void Pad(float[] b, float start, float length, float freq, float amp)
    {
        int s0 = Mathf.RoundToInt(start * Rate);
        int n = Mathf.RoundToInt(length * Rate);
        float phaseA = 0f, phaseB = 0f, low = 0f;
        float a = Coefficient(1400f);
        for (int i = 0; i < n + Rate / 2; i++)
        {
            float t = i / (float)Rate;
            float env = Mathf.Min(1f, t / 0.25f) * (i < n ? 1f : Mathf.Exp(-(t - length) * 8f));
            phaseA += freq / Rate;
            phaseB += freq * 1.006f / Rate;
            float x = (Saw(phaseA) + Saw(phaseB)) * 0.5f;
            low += a * (x - low);
            Add(b, s0 + i, low * env * amp);
        }
    }

    private static void Pluck(float[] b, float start, float length, float freq, float amp, Wave wave, float cutoff, float decay)
    {
        int s0 = Mathf.RoundToInt(start * Rate);
        int n = Mathf.RoundToInt(length * Rate);
        float phase = 0f, low = 0f;
        float a = Coefficient(cutoff);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float env = Mathf.Min(1f, t / 0.004f) * Mathf.Exp(-t * decay) * (1f - (float)i / n);
            phase += freq / Rate;
            low += a * (Osc(wave, phase) - low);
            Add(b, s0 + i, low * env * amp);
        }
    }

    private static void Kick(float[] b, float start, float amp)
    {
        int s0 = Mathf.RoundToInt(start * Rate);
        float phase = 0f;
        for (int i = 0; i < Rate * 0.3f; i++)
        {
            float t = i / (float)Rate;
            float freq = 45f + 110f * Mathf.Exp(-t * 30f);
            phase += freq / Rate;
            Add(b, s0 + i, Mathf.Sin(phase * Mathf.PI * 2f) * Mathf.Exp(-t * 12f) * amp);
        }
    }

    private static void Snare(float[] b, System.Random random, float start, float amp)
    {
        int s0 = Mathf.RoundToInt(start * Rate);
        float low = 0f, a = Coefficient(5000f), phase = 0f;
        for (int i = 0; i < Rate * 0.22f; i++)
        {
            float t = i / (float)Rate;
            low += a * ((float)(random.NextDouble() * 2.0 - 1.0) - low);
            phase += 190f / Rate;
            Add(b, s0 + i, (low * 0.8f + Mathf.Sin(phase * Mathf.PI * 2f) * 0.4f) * Mathf.Exp(-t * 18f) * amp);
        }
    }

    private static void Hat(float[] b, System.Random random, float start, float amp)
    {
        int s0 = Mathf.RoundToInt(start * Rate);
        float low = 0f, a = Coefficient(3000f);
        for (int i = 0; i < Rate * 0.05f; i++)
        {
            float t = i / (float)Rate;
            float x = (float)(random.NextDouble() * 2.0 - 1.0);
            low += a * (x - low);
            Add(b, s0 + i, (x - low) * Mathf.Exp(-t * 70f) * amp);
        }
    }

    // ==================== 合成の部品 ====================

    private enum Wave { Sine, Triangle, Square, Saw }

    private static float[] Buffer(float seconds) => new float[Mathf.Max(1, Mathf.RoundToInt(seconds * Rate))];

    private static float Midi(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);

    private static float Coefficient(float cutoff) => 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);

    private static float Saw(float phase) => 2f * (phase - Mathf.Floor(phase)) - 1f;

    private static float Osc(Wave wave, float phase)
    {
        float p = phase - Mathf.Floor(phase);
        switch (wave)
        {
            case Wave.Triangle: return 1f - 4f * Mathf.Abs(p - 0.5f);
            case Wave.Square: return p < 0.5f ? 0.7f : -0.7f;
            case Wave.Saw: return 2f * p - 1f;
            default: return Mathf.Sin(p * Mathf.PI * 2f);
        }
    }

    /// <summary>ループ再生用のBGMは末尾の余韻を先頭に回り込ませる(継ぎ目で途切れないように)。</summary>
    private static void Add(float[] b, int index, float value)
    {
        if (index < 0) return;
        b[index % b.Length] += value;
    }

    private static void Tone(float[] b, float start, float length, float freq, float amp, float decay, Wave wave, float cutoff = 0f)
    {
        int s0 = Mathf.RoundToInt(start * Rate);
        int n = Mathf.RoundToInt(length * Rate);
        float phase = 0f, low = 0f;
        float a = cutoff > 0f ? Coefficient(cutoff) : 1f;
        for (int i = 0; i < n && s0 + i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float env = Mathf.Min(1f, t / 0.005f) * Mathf.Exp(-t * decay) * Mathf.Min(1f, (n - i) / (Rate * 0.01f));
            phase += freq / Rate;
            low += a * (Osc(wave, phase) - low);
            b[s0 + i] += low * env * amp;
        }
    }

    private static void Bell(float[] b, float start, float length, float freq, float amp)
    {
        Tone(b, start, length, freq, amp, 4f, Wave.Sine);
        Tone(b, start, length * 0.6f, freq * 2.76f, amp * 0.4f, 7f, Wave.Sine);
        Tone(b, start, length * 0.4f, freq * 5.4f, amp * 0.2f, 11f, Wave.Sine);
    }

    /// <summary>周波数を f0 から f1 へ指数的に動かす音。decay=0なら減衰なしで、最後に向けて消える。curve&gt;1で終盤に急に上がる。</summary>
    private static void Sweep(float[] b, float start, float length, float f0, float f1, float amp, float decay, Wave wave, float curve = 1f)
    {
        int s0 = Mathf.RoundToInt(start * Rate);
        int n = Mathf.RoundToInt(length * Rate);
        float phase = 0f;
        for (int i = 0; i < n && s0 + i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float p = Mathf.Pow((float)i / n, curve);
            float freq = f0 * Mathf.Pow(f1 / f0, p);
            phase += freq / Rate;
            float env = Mathf.Min(1f, t / 0.004f) * (decay > 0f ? Mathf.Exp(-t * decay) : Mathf.Sin(Mathf.Clamp01((float)i / n) * Mathf.PI));
            b[s0 + i] += Osc(wave, phase) * env * amp;
        }
    }

    private static void Noise(float[] b, System.Random random, float start, float length, float amp, float cutoff, float decay)
    {
        int s0 = Mathf.RoundToInt(start * Rate);
        int n = Mathf.RoundToInt(length * Rate);
        float low = 0f, a = Coefficient(cutoff);
        for (int i = 0; i < n && s0 + i < b.Length; i++)
        {
            float t = i / (float)Rate;
            low += a * ((float)(random.NextDouble() * 2.0 - 1.0) - low);
            b[s0 + i] += low * Mathf.Exp(-t * decay) * amp * Mathf.Min(1f, t / 0.002f);
        }
    }

    /// <summary>通過させる帯域を動かすノイズ(「シュッ」「ゴォッ」という音)。</summary>
    private static void NoiseSweep(float[] b, System.Random random, float start, float length, float amp, float f0, float f1)
    {
        int s0 = Mathf.RoundToInt(start * Rate);
        int n = Mathf.RoundToInt(length * Rate);
        float low = 0f, lower = 0f;
        for (int i = 0; i < n && s0 + i < b.Length; i++)
        {
            float p = (float)i / n;
            float cutoff = f0 * Mathf.Pow(f1 / f0, p);
            float x = (float)(random.NextDouble() * 2.0 - 1.0);
            low += Coefficient(cutoff) * (x - low);
            lower += Coefficient(cutoff * 0.25f) * (low - lower);
            b[s0 + i] += (low - lower) * Mathf.Sin(p * Mathf.PI) * amp * 1.8f;
        }
    }

    private static void Drive(float[] b, float gain)
    {
        for (int i = 0; i < b.Length; i++) b[i] = (float)Math.Tanh(b[i] * gain);
    }

    private static void Normalize(float[] b, float peak)
    {
        float max = 0f;
        foreach (float v in b) max = Mathf.Max(max, Mathf.Abs(v));
        if (max < 1e-5f) return;
        float scale = peak / max;
        for (int i = 0; i < b.Length; i++) b[i] *= scale;
    }

    /// <summary>末尾を先頭へクロスフェードさせて、繰り返し再生しても継ぎ目が聞こえないようにする。</summary>
    private static float[] MakeLoopable(float[] b, float fadeSeconds)
    {
        int fade = Mathf.Min(b.Length / 2, Mathf.RoundToInt(fadeSeconds * Rate));
        for (int i = 0; i < fade; i++)
        {
            float w = (float)i / fade;
            int tail = b.Length - fade + i;
            b[i] = b[i] * w + b[tail] * (1f - w);
        }
        Array.Resize(ref b, b.Length - fade);
        return b;
    }
}

/// <summary>OrisamoAudio の再生担当(シーンをまたいで残る)。</summary>
public sealed class OrisamoAudioHost : MonoBehaviour
{
    private const int PoolSize = 14;
    private readonly List<AudioSource> pool = new List<AudioSource>();
    private int next;
    private AudioSource bgmA;
    private AudioSource bgmB;
    private AudioSource crowd;
    private OrisamoAudio.Bgm currentBgm = OrisamoAudio.Bgm.None;
    private float crowdTarget;
    private float crowdLastSet = -10f;

    private void Awake()
    {
        for (int i = 0; i < PoolSize; i++) pool.Add(CreateSource(false));
        bgmA = CreateSource(true);
        bgmB = CreateSource(true);
        crowd = CreateSource(true);
    }

    private AudioSource CreateSource(bool loop)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.volume = 0f;
        return source;
    }

    public void PlayOneShot(AudioClip clip, float volume, float pitch)
    {
        AudioSource source = pool[next];
        next = (next + 1) % pool.Count;
        source.Stop();
        source.clip = clip;
        source.volume = volume;
        source.pitch = pitch;
        source.Play();
    }

    public void SwitchBgm(OrisamoAudio.Bgm bgm, AudioClip clip)
    {
        if (bgm == currentBgm) return;
        currentBgm = bgm;
        // 鳴っている方をフェードアウトさせ、もう一方で新しい曲を始める
        AudioSource temp = bgmA;
        bgmA = bgmB;
        bgmB = temp;
        bgmA.Stop();
        bgmA.clip = clip;
        bgmA.volume = 0f;
        if (clip != null) bgmA.Play();
    }

    public void SetCrowd(AudioClip clip, float excitement)
    {
        if (crowd.clip != clip) crowd.clip = clip;
        if (!crowd.isPlaying && clip != null) crowd.Play();
        crowdTarget = (0.15f + excitement * 0.85f) * OrisamoAudio.CrowdVolume;
        crowdLastSet = Time.unscaledTime;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        bgmA.volume = Mathf.MoveTowards(bgmA.volume, bgmA.clip != null ? OrisamoAudio.BgmVolume : 0f, dt);
        bgmB.volume = Mathf.MoveTowards(bgmB.volume, 0f, dt);
        if (bgmB.volume <= 0f && bgmB.isPlaying) bgmB.Stop();

        // スタジアムがないシーン(SetCrowdが呼ばれなくなった)では、ざわめきを消す
        float target = Time.unscaledTime - crowdLastSet < 0.5f ? crowdTarget : 0f;
        crowd.volume = Mathf.MoveTowards(crowd.volume, target, dt * 0.8f);
        if (crowd.volume <= 0f && crowd.isPlaying && target <= 0f) crowd.Stop();
    }
}
