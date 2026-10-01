using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// Fast, deterministic 3D noise for the rock.
    ///
    /// Unity only ships 2D Perlin, and faking 3D from three 2D lookups leaves obvious streaks
    /// along the axes - which on a mineshaft wall reads as corduroy, not stone. So this is a
    /// plain integer-hash value noise: no tables, no trig, no allocations, and the same result
    /// on every machine, which matters because the same seed has to carve the same crater on
    /// every player's client once this is networked.
    ///
    /// Two flavours, mixed by the "jagged" parameter:
    ///   fBm     - rolling lumps, like weathered dirt
    ///   ridged  - sharp creases and edges, like fractured stone
    /// </summary>
    public static class RockNoise
    {
        /// <summary>Hash of a grid point to [0,1). Cheap enough to call eight times per sample.</summary>
        private static float Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                int h = x * 73856093 ^ y * 19349663 ^ z * 83492791 ^ seed * 15485863;
                h ^= h >> 13;
                h *= 1274126177;
                h ^= h >> 16;
                return (h & 0x00ffffff) * (1f / 16777216f);
            }
        }

        /// <summary>Smooth value noise in [0,1].</summary>
        public static float Value(float x, float y, float z, int seed)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            int zi = Mathf.FloorToInt(z);

            float xf = x - xi, yf = y - yi, zf = z - zi;

            // Smoothstep the interpolants, or the grid shows through as diamond creases.
            float u = xf * xf * (3f - 2f * xf);
            float v = yf * yf * (3f - 2f * yf);
            float w = zf * zf * (3f - 2f * zf);

            float c000 = Hash(xi, yi, zi, seed);
            float c100 = Hash(xi + 1, yi, zi, seed);
            float c010 = Hash(xi, yi + 1, zi, seed);
            float c110 = Hash(xi + 1, yi + 1, zi, seed);
            float c001 = Hash(xi, yi, zi + 1, seed);
            float c101 = Hash(xi + 1, yi, zi + 1, seed);
            float c011 = Hash(xi, yi + 1, zi + 1, seed);
            float c111 = Hash(xi + 1, yi + 1, zi + 1, seed);

            float x00 = c000 + (c100 - c000) * u;
            float x10 = c010 + (c110 - c010) * u;
            float x01 = c001 + (c101 - c001) * u;
            float x11 = c011 + (c111 - c011) * u;

            float y0 = x00 + (x10 - x00) * v;
            float y1 = x01 + (x11 - x01) * v;

            return y0 + (y1 - y0) * w;
        }

        /// <summary>
        /// Layered noise in roughly [-1,1]. <paramref name="jagged"/> blends from rolling lumps (0)
        /// toward sharp fractured creases (1).
        /// </summary>
        public static float Rock(float x, float y, float z, float frequency, int octaves, float jagged, int seed)
        {
            if (octaves < 1) octaves = 1;

            float sum = 0f;
            float amp = 1f;
            float norm = 0f;
            float f = frequency;

            for (int i = 0; i < octaves; i++)
            {
                float n = Value(x * f, y * f, z * f, seed + i * 1013) * 2f - 1f;

                // Ridged: fold the noise at zero so the valleys become sharp creases.
                if (jagged > 0f)
                {
                    float ridge = 1f - 2f * Mathf.Abs(n);
                    n += (ridge - n) * jagged;
                }

                sum += n * amp;
                norm += amp;

                // 2.07 rather than 2, so octaves don't line up on the same grid points.
                f *= 2.07f;
                amp *= 0.5f;
            }

            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>
        /// A seed derived from a world position, rounded to 25 cm. Two blasts in different places
        /// tear different shapes, but the same blast tears the same shape on every machine - which
        /// is what keeps destructible terrain syncable without sending the geometry.
        /// </summary>
        public static int SeedFromPosition(Vector3 p)
        {
            unchecked
            {
                int x = Mathf.RoundToInt(p.x * 4f);
                int y = Mathf.RoundToInt(p.y * 4f);
                int z = Mathf.RoundToInt(p.z * 4f);
                int h = x * 73856093 ^ y * 19349663 ^ z * 83492791;
                h ^= h >> 13;
                h *= 1274126177;
                return h ^ (h >> 16);
            }
        }
    }
}
