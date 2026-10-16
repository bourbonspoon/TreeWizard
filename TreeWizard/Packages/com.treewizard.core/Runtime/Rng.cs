using UnityEngine;

namespace TreeWizard
{
    /// <summary>
    /// Small deterministic PRNG (xorshift32). Used instead of UnityEngine.Random or System.Random
    /// so a seed gives the same tree everywhere, and generation never touches global state.
    /// </summary>
    public sealed class Rng
    {
        uint _state;

        public Rng(int seed)
        {
            _state = Hash((uint)seed) | 1u; // xorshift must never hold zero
        }

        public static int Combine(int a, int b) => (int)Hash((uint)a * 0x9E3779B1u + (uint)b);

        static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }

        public uint NextUInt()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public float Value() => (NextUInt() >> 8) * (1f / (1 << 24));

        public float Range(float min, float max) => min + (max - min) * Value();

        public Vector3 OnUnitSphere()
        {
            var z = Range(-1f, 1f);
            var a = Range(0f, 2f * Mathf.PI);
            var r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), z);
        }

        /// <summary>An independent stream that costs this one exactly one draw.</summary>
        public Rng Fork() => new Rng((int)NextUInt());
    }
}
