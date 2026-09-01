using System;
using UnityEngine;

namespace ProceduralTerrain
{
     //
    /// 生成地形和草地系统所需的所有纹理（草地 / 沙地 / 岩石 / 雪地 / 草叶），
    /// 完全不需要外部美术资源。每个像素都由带种子的噪声驱动。
    /// 
    public static class ProceduralTextures
    {
        const int LayerSize = 256;   // 地形层纹理分辨率（在地形瓦片尺度下 256 足够清晰，比 512 快约 4 倍）
        const int BladeW = 64;       // 草叶纹理宽度
        const int BladeH = 128;      // 草叶纹理高度

        struct Blade
        {
            public float center; // 水平方向根部位置 0..1
            public float height; // 草叶高度（占纹理的比例）
            public float curve;  // 弯曲程度
            public float width;  // 宽度
            public float tint;   // 色调变化
        }

        // ---------------- 地形层纹理 ----------------

         //泥土/砾石路面：暖棕色基底加上砾石斑点（可平铺的周期噪声）。
        public static Texture2D CreateRoadTexture(int seed)
        {
            return BuildNoiseTexture(seed, LayerSize, LayerSize, (px, py) =>
            {
                float n = Noise.FbmWrapped(px * 6f, py * 6f, seed + 2, 4, 6);
                float gravel = Noise.FbmWrapped(px * 24f, py * 24f, seed + 7, 3, 24);
                Color col = Color.Lerp(
                    new Color(0.40f, 0.34f, 0.28f),
                    new Color(0.55f, 0.49f, 0.42f), n);
                return col * (0.85f + 0.3f * gravel);
            });
        }

         //草地：绿色基底 + 低频斑块 + 高频斑点（可平铺的周期噪声）。
        public static Texture2D CreateGrassTexture(int seed)
        {
            return BuildNoiseTexture(seed, LayerSize, LayerSize, (px, py) =>
            {
                float n = Noise.FbmWrapped(px * 5f, py * 5f, seed, 4, 5);
                float speckle = Noise.FbmWrapped(px * 22f, py * 22f, seed + 5, 3, 22);
                Color col = Color.Lerp(
                    new Color(0.16f, 0.42f, 0.13f),
                    new Color(0.34f, 0.58f, 0.23f), n);
                return col * (0.8f + 0.4f * speckle);
            });
        }

         //沙地：米色/奶油色并带细腻颗粒噪声（可平铺的周期噪声）。
        public static Texture2D CreateSandTexture(int seed)
        {
            return BuildNoiseTexture(seed, LayerSize, LayerSize, (px, py) =>
            {
                float n = Noise.FbmWrapped(px * 8f, py * 8f, seed, 3, 8);
                float grain = Noise.FbmWrapped(px * 32f, py * 32f, seed + 3, 2, 32);
                Color col = Color.Lerp(
                    new Color(0.76f, 0.68f, 0.50f),
                    new Color(0.86f, 0.79f, 0.60f), n);
                return col * (0.92f + 0.16f * grain);
            });
        }

         //沙漠：暖黄色并带有沙丘明暗变化（可平铺的周期噪声）。
        public static Texture2D CreateDesertTexture(int seed)
        {
            return BuildNoiseTexture(seed, LayerSize, LayerSize, (px, py) =>
            {
                float n = Noise.FbmWrapped(px * 3f, py * 3f, seed + 1, 4, 3);
                float dune = Noise.FbmWrapped(px * 9f, py * 9f, seed + 4, 3, 9);
                Color col = Color.Lerp(
                    new Color(0.85f, 0.72f, 0.38f),
                    new Color(0.95f, 0.84f, 0.52f), n);
                return col * (0.8f + 0.45f * dune);
            });
        }

         //岩石：灰色基底并带有高对比度的裂纹图案（可平铺的周期噪声）。
        public static Texture2D CreateRockTexture(int seed)
        {
            return BuildNoiseTexture(seed, LayerSize, LayerSize, (px, py) =>
            {
                float n = Noise.FbmWrapped(px * 4f, py * 4f, seed + 2, 4, 4);
                float crack = Noise.FbmWrapped(px * 14f, py * 14f, seed + 5, 3, 14);
                Color col = Color.Lerp(
                    new Color(0.32f, 0.32f, 0.35f),
                    new Color(0.54f, 0.52f, 0.49f), n);
                return col * (0.65f + 0.7f * crack);
            });
        }

         //雪地：白色并带有冷蓝色调阴影（可平铺的周期噪声）。
        public static Texture2D CreateSnowTexture(int seed)
        {
            return BuildNoiseTexture(seed, LayerSize, LayerSize, (px, py) =>
            {
                float n = Noise.FbmWrapped(px * 6f, py * 6f, seed + 4, 4, 6);
                float shade = Noise.FbmWrapped(px * 18f, py * 18f, seed + 7, 3, 18);
                Color col = Color.Lerp(
                    new Color(0.82f, 0.88f, 0.96f),
                    new Color(0.97f, 0.98f, 1.00f), n);
                return col * (0.85f + 0.3f * shade);
            });
        }

         //平原：干燥的浅绿色土壤并带有淡淡的杂色斑驳（可平铺的周期噪声）。
        public static Texture2D CreatePlainsTexture(int seed)
        {
            return BuildNoiseTexture(seed, LayerSize, LayerSize, (px, py) =>
            {
                float n = Noise.FbmWrapped(px * 5f, py * 5f, seed, 4, 5);
                float mote = Noise.FbmWrapped(px * 20f, py * 20f, seed + 5, 3, 20);
                Color col = Color.Lerp(
                    new Color(0.52f, 0.58f, 0.32f),
                    new Color(0.66f, 0.70f, 0.42f), n);
                return col * (0.82f + 0.35f * mote);
            });
        }

         //森林地表：深绿色并带有更暗的树冠落叶斑块（可平铺的周期噪声）。
        public static Texture2D CreateForestTexture(int seed)
        {
            return BuildNoiseTexture(seed, LayerSize, LayerSize, (px, py) =>
            {
                float n = Noise.FbmWrapped(px * 4f, py * 4f, seed, 4, 4);
                float litter = Noise.FbmWrapped(px * 16f, py * 16f, seed + 6, 3, 16);
                Color col = Color.Lerp(
                    new Color(0.10f, 0.26f, 0.09f),
                    new Color(0.20f, 0.38f, 0.14f), n);
                return col * (0.75f + 0.5f * litter);
            });
        }

        // ---------------- 草地细节纹理 ----------------

         //
        /// 草叶纹理：若干条带 Alpha 渐变的弯曲草叶，用于 Terrain 的 GrassBillboard 细节层。
        /// 根部更暗，尖端更亮。
        /// 
        public static Texture2D CreateGrassBladeTexture(int seed)
        {
            Texture2D tex = NewTex(BladeW, BladeH, TextureWrapMode.Clamp);
            var rng = new System.Random(seed);

            int bladeCount = 6;
            var blades = new Blade[bladeCount];
            for (int i = 0; i < bladeCount; i++)
            {
                blades[i] = new Blade
                {
                    center = 0.15f + 0.70f * (float)rng.NextDouble(),
                    height = 0.75f + 0.25f * (float)rng.NextDouble(),
                    curve = -0.25f + 0.50f * (float)rng.NextDouble(),
                    width = 0.05f + 0.05f * (float)rng.NextDouble(),
                    tint = 0.80f + 0.40f * (float)rng.NextDouble(),
                };
            }

            for (int y = 0; y < BladeH; y++)
            {
                for (int x = 0; x < BladeW; x++)
                {
                    float fy = y / (float)BladeH;          // 0 = 根部末端
                    Color col = new Color(0f, 0f, 0f, 0f);

                    for (int b = 0; b < bladeCount; b++)
                    {
                        Blade blade = blades[b];
                        if (fy > blade.height) continue;

                        float t = fy / blade.height;        // 0 = 根部，1 = 尖端
                        float tipX = blade.center + blade.curve * t * t * 0.35f; // 尖端弯曲
                        float halfW = blade.width * (1f - t * 0.7f);             // 草叶向尖端逐渐变细
                        float dist = Mathf.Abs(x / (float)BladeW - tipX);
                        float alpha = 1f - Noise.Smoothstep(halfW * 0.5f, halfW, dist);
                        if (alpha <= 0.01f) continue;

                        float g = blade.tint * (0.35f + 0.65f * t);
                        col.r = Mathf.Max(col.r, 0.24f * g);
                        col.g = Mathf.Max(col.g, 0.62f * g);
                        col.b = Mathf.Max(col.b, 0.18f * g);
                        col.a = Mathf.Max(col.a, alpha);
                    }

                    tex.SetPixel(x, y, col);
                }
            }

            tex.Apply(false, false);
            return tex;
        }

        // ---------------- 工具方法 ----------------

         //逐像素噪声填充：px/py 是 0..1 范围内的 UV 坐标。使用 mipmap 处理远处的 LOD。
        static Texture2D BuildNoiseTexture(int seed, int w, int h, Func<float, float, Color> shader)
        {
            Texture2D tex = NewTex(w, h, TextureWrapMode.Repeat, true); // mipChain=true
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float px = x / (float)w;
                    float py = y / (float)h;
                    tex.SetPixel(x, y, shader(px, py));
                }
            }
            tex.Apply(true, false); // 生成 mipmap，防止远处纹理模糊
            return tex;
        }

        static Texture2D NewTex(int w, int h, TextureWrapMode wrapMode, bool mipChain = false)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain);
            tex.wrapMode = wrapMode;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 4;
            return tex;
        }
    }
}
