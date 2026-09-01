using UnityEngine;

namespace ProceduralTerrain
{
     //
    /// 用于地形生成器各处的带种子的二维数值噪声及其分形扩展。
    ///
    /// 底层方法是经典的晶格数值噪声（lattice value-noise），由 Perlin（1985）提出，
    /// 后经 Musgrave 等人在《Texturing &amp; Modeling: a Procedural
    /// Approach》（1998）中加以改进，并结合简单的 32 位整数哈希为每个格子生成随机值
    /// （基于 Wang hash 模式，JGT 2007）。
    ///
    /// 所有方法都是确定性的：相同的 (x, y, seed) 总会产生相同的输出。
    /// 
    public static class Noise
    {
         //取值范围为 [0, 1] 的单倍频程数值噪声。
        public static float Value(float x, float y, int seed)
        {
            int ix = Mathf.FloorToInt(x);
            int iy = Mathf.FloorToInt(y);
            float fx = x - ix;
            float fy = y - iy;

            // Hermite 平滑（Perlin 1985）
            float u = fx * fx * (3f - 2f * fx);
            float v = fy * fy * (3f - 2f * fy);

            float a = Hash(ix,     iy,     seed);
            float b = Hash(ix + 1, iy,     seed);
            float c = Hash(ix,     iy + 1, seed);
            float d = Hash(ix + 1, iy + 1, seed);

            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

         //
        /// 可无缝平铺的数值噪声，可在 [0, periodX] x [0, periodY] 范围内循环包裹。
        /// 用于消除重复地形层纹理中的接缝。
        /// 技术原理：用模运算包裹整数晶格坐标，
        /// 参见 Ebert 等人所著《Texturing &amp; Modeling》第 2 章。
        /// 
        public static float ValueWrapped(float x, float y, int seed, int periodX, int periodY)
        {
            int ix = Mathf.FloorToInt(x);
            int iy = Mathf.FloorToInt(y);
            float fx = x - ix;
            float fy = y - iy;

            int Wrap(int v, int period) { v %= period; return v < 0 ? v + period : v; }

            int x0 = Wrap(ix,     periodX);
            int y0 = Wrap(iy,     periodY);
            int x1 = Wrap(ix + 1, periodX);
            int y1 = Wrap(iy + 1, periodY);

            float u = fx * fx * (3f - 2f * fx);
            float v = fy * fy * (3f - 2f * fy);

            float a = Hash(x0, y0, seed);
            float b = Hash(x1, y0, seed);
            float c = Hash(x0, y1, seed);
            float d = Hash(x1, y1, seed);

            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

         //分形布朗运动（Mandelbrot &amp; Van Ness 1968，Musgrave 1993）。
        public static float Fbm(float x, float y, int seed, int octaves = 4, float lacunarity = 2f, float gain = 0.5f)
        {
            if (octaves < 1) octaves = 1;
            float amp = 1f, freq = 1f, sum = 0f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            { sum += Value(x * freq, y * freq, seed + i * 131) * amp; norm += amp; freq *= lacunarity; amp *= gain; }
            return sum / norm;
        }

         //用于重复地形层纹理的可平铺 fBm。
        public static float FbmWrapped(float x, float y, int seed, int octaves = 4, int period = 4, float lacunarity = 2f, float gain = 0.5f)
        {
            if (octaves < 1) octaves = 1;
            float amp = 1f, freq = 1f, sum = 0f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            { int p = Mathf.Max(1, Mathf.RoundToInt(period * freq)); sum += ValueWrapped(x * freq, y * freq, seed + i * 131, p, p) * amp; norm += amp; freq *= lacunarity; amp *= gain; }
            return sum / norm;
        }

         //山脊噪声：将数值噪声折叠成尖锐的山脊/山谷结构（Musgrave 1993）。
        public static float Ridge(float x, float y, int seed, int octaves = 5, float lacunarity = 2f, float gain = 0.5f)
        {
            if (octaves < 1) octaves = 1;
            float amp = 1f, freq = 1f, sum = 0f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            { float n = Value(x * freq, y * freq, seed + i * 137); float ridge = 1f - Mathf.Abs(n * 2f - 1f); sum += ridge * ridge * amp; norm += amp; freq *= lacunarity; amp *= gain; }
            return sum / norm;
        }

         //平滑 Hermite 步进，用于柔和的区域掩码过渡。
        public static float Smoothstep(float edge0, float edge1, float x)
        { float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0)); return t * t * (3f - 2f * t); }

         //返回 [0,1] 范围内的整数晶格哈希。基于 Wang-hash 风格的混合（JGT 2007）。
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF;
            }
        }
    }
}