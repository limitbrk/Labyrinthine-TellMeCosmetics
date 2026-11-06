namespace TellMeCosmetics.Predict;

// Plain C# copies of the game's two RNGs, so the maze replay runs off the main thread (the il2cpp/Unity ones are main-thread only).
// Checked against the real ones by GameRandomTest (DebugMazeTests).

// Il2CppSystem.Random: Knuth subtractive generator, same as .NET's seeded System.Random (field names kept for the test).
public sealed class NetRandom
{
    private const int MBIG = int.MaxValue, MSEED = 161803398;
    internal readonly int[] _seedArray = new int[56];
    internal int _inext, _inextp;

    public NetRandom(int seed)
    {
        int mj = MSEED - (seed == int.MinValue ? int.MaxValue : System.Math.Abs(seed)), mk = 1;
        _seedArray[55] = mj;
        for (int i = 1; i < 55; i++)
        {
            int ii = 21 * i % 55;
            _seedArray[ii] = mk;
            mk = mj - mk;
            if (mk < 0) mk += MBIG;
            mj = _seedArray[ii];
        }
        for (int k = 1; k < 5; k++)
            for (int i = 1; i < 56; i++)
            {
                _seedArray[i] -= _seedArray[1 + (i + 30) % 55];
                if (_seedArray[i] < 0) _seedArray[i] += MBIG;
            }
        _inext = 0;
        _inextp = 21;
    }

    internal NetRandom(int[] seedArray, int inext, int inextp) { seedArray.CopyTo(_seedArray, 0); _inext = inext; _inextp = inextp; }

    private int InternalSample()
    {
        if (++_inext >= 56) _inext = 1;
        if (++_inextp >= 56) _inextp = 1;
        int r = _seedArray[_inext] - _seedArray[_inextp];
        if (r == MBIG) r--;
        if (r < 0) r += MBIG;
        _seedArray[_inext] = r;
        return r;
    }

    private double Sample() => InternalSample() * (1.0 / MBIG);
    public int Next() => InternalSample();
    public double NextDouble() => Sample();

    public int Next(int maxValue)
    {
        if (maxValue < 0) throw new System.ArgumentOutOfRangeException(nameof(maxValue));
        return (int)(Sample() * maxValue);
    }

    public int Next(int minValue, int maxValue)
    {
        if (minValue > maxValue) throw new System.ArgumentOutOfRangeException(nameof(minValue));
        long range = (long)maxValue - minValue;
        if (range <= int.MaxValue) return (int)(Sample() * range) + minValue;
        // GetSampleForLargeRange
        int result = InternalSample();
        if (InternalSample() % 2 == 0) result = -result;
        double d = (result + (double)(int.MaxValue - 1)) / (2.0 * int.MaxValue - 1);
        return (int)((long)(d * range) + minValue);
    }
}

// UnityEngine.Random: xorshift128 seeded like InitState.
public sealed class UnityRandom
{
    internal uint s0, s1, s2, s3;

    public UnityRandom(int seed)
    {
        s0 = (uint)seed;
        s1 = unchecked(s0 * 1812433253u + 1);
        s2 = unchecked(s1 * 1812433253u + 1);
        s3 = unchecked(s2 * 1812433253u + 1);
    }

    private uint NextUInt()
    {
        uint t = s0 ^ (s0 << 11);
        s0 = s1; s1 = s2; s2 = s3;
        s3 = s3 ^ (s3 >> 19) ^ t ^ (t >> 8);
        return s3;
    }

    public float Value => (NextUInt() & 0x7FFFFF) * (1f / 8388607f);
}
