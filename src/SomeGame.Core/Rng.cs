namespace SomeGame.Core;

public static class Rand
{
    private const ulong M = 0x9E3779B97F4A7C15UL;

    public static IRng Create(long seed) => new XorShift(seed);

    private sealed class XorShift : IRng
    {
        private ulong _s;
        public XorShift(long seed) => _s = (ulong)seed == 0 ? M : (ulong)seed | 1UL;

        public int Next(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextU64() % (ulong)(maxExclusive - minInclusive));
        }

        public bool Chance(double p) => (NextU64() % 10000) < (ulong)(p * 10000.0);

        public ulong NextU64()
        {
            _s ^= _s >> 12;
            _s ^= _s << 25;
            _s ^= _s >> 27;
            return _s * M;
        }
    }
}

public interface IRng
{
    int Next(int minInclusive, int maxExclusive);
    bool Chance(double p);
}


