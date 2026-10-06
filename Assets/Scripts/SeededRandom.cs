/// <summary>
/// スマホWeb(docs/app.js)の createSeededRandom (mulberry32) と同じ乱数列を返すC#移植版。
/// JavaScriptの Math.imul / 符号なしシフトを uint 演算で再現しているため、
/// 同じシード値からはJSと完全に同じ値が得られる。
/// (System.Random / UnityEngine.Random はJSと結果が一致しないため、JSと突き合わせる値にはこちらを使う)
/// </summary>
public class SeededRandom
{
    private uint state;

    public SeededRandom(uint seed)
    {
        state = seed;
    }

    /// <summary>[0, 1) の乱数。JSの rand() と同じ値。</summary>
    public double NextDouble()
    {
        unchecked
        {
            state += 0x6d2b79f5u;
            uint r = (state ^ (state >> 15)) * (1u | state);
            r ^= r + (r ^ (r >> 7)) * (61u | r);
            return (r ^ (r >> 14)) / 4294967296.0;
        }
    }

    /// <summary>[min, maxExclusive) の整数。JSの nextInt(min, maxExclusive) と同じ値。</summary>
    public int NextInt(int min, int maxExclusive)
    {
        return min + (int)System.Math.Floor(NextDouble() * (maxExclusive - min));
    }
}
