using UnityEngine;

namespace ProceduralTerrain
{
     //
    /// 创建带有默认水体材质的水面网格。
    /// 网格顶点的 R 通道颜色携带烘焙后的归一化水深值（0=岸边，1=深处），
    /// 这样 WaterURP 着色器就可以在浅水色与深水色之间进行混合。
    /// 
    public static class WaterSurface
    {
         //
        /// 创建一个水面 GameObject。
        /// 
        /// <param name="sizeX">水面的 X 方向范围（= 地形宽度）</param>
        /// <param name="sizeZ">水面的 Z 方向范围（= 地形长度）</param>
        /// <param name="waterHeight">水面的世界坐标 Y 高度</param>
        /// <param name="depth01">每个格子的水深 [0,1]，尺寸为 [z, x]；可以为 null</param>
        /// <param name="segments">网格细分数量（控制水面平滑度）</param>
        /// <param name="material">水体材质；为 null 时自动创建</param>
        public static GameObject Create(float sizeX, float sizeZ, float waterHeight,
            float[,] depth01, int segments = 64, Material material = null)
        {
            var go = new GameObject("WaterSurface");
            go.transform.position = new Vector3(0, waterHeight,0);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = CreateGrid(sizeX, sizeZ, segments, depth01);

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material != null ? material : CreateDefaultMaterial(waterHeight);

            return go;
        }

         //构建水面网格：顶点 R 通道存深度、A 通道存岸边透明度；
        /// 完整平面（陆地像素由 shader discard），depth 双线性插值采样，岸边 alpha 渐变消除锯齿。
        static Mesh CreateGrid(float sizeX, float sizeZ, int segments, float[,] depth01)
        {
            int vps = segments + 1;
            var verts = new Vector3[vps * vps];
            var uvs = new Vector2[vps * vps];
            var colors = new Color[vps * vps];
            var tris = new int[segments * segments * 6];

            int depthRows = depth01 != null ? depth01.GetLength(0) : 0;
            int depthCols = depth01 != null ? depth01.GetLength(1) : 0;

            for (int iz = 0; iz < vps; iz++)
            {
                for (int ix = 0; ix < vps; ix++)
                {
                    int idx = iz * vps + ix;
                    float u = ix / (float)segments;
                    float v = iz / (float)segments;
                    verts[idx] = new Vector3(u * sizeX, 0f, v * sizeZ);
                    uvs[idx] = new Vector2(u, v);

                    float depth = 0f;
                    if (depthRows > 0)
                        depth = SampleDepth(depth01, depthRows, depthCols, u, v);
                    // A 通道：岸边半透明渐隐（消除边缘锯齿），深水不透明；带下限避免浅湖全透明
                    float shoreAlpha = 0.7f + 0.3f * Mathf.SmoothStep(0f, 0.12f, depth);
                    colors[idx] = new Color(depth, 0f, 0f, shoreAlpha);
                }
            }

            int t = 0;
            for (int iz = 0; iz < segments; iz++)
            {
                for (int ix = 0; ix < segments; ix++)
                {
                    int a = iz * vps + ix;
                    int b = a + 1;
                    int c = a + vps;
                    int d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            }

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

         //双线性插值采样深度图（0~1 坐标 → depth01 值），岸边过渡平滑无锯齿。
        static float SampleDepth(float[,] depth01, int rows, int cols, float u, float v)
        {
            float fx = u * (cols - 1);
            float fy = v * (rows - 1);
            int x0 = Mathf.Clamp((int)fx, 0, cols - 1);
            int y0 = Mathf.Clamp((int)fy, 0, rows - 1);
            int x1 = Mathf.Clamp(x0 + 1, 0, cols - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, rows - 1);
            float tx = Mathf.Clamp01(fx - x0);
            float ty = Mathf.Clamp01(fy - y0);
            float d00 = depth01[y0, x0], d10 = depth01[y0, x1];
            float d01 = depth01[y1, x0], d11 = depth01[y1, x1];
            float top = Mathf.Lerp(d00, d10, tx);
            float bot = Mathf.Lerp(d01, d11, tx);
            return Mathf.Lerp(top, bot, ty);
        }

         //
        /// 创建默认水体材质。优先用 Resources 材质资产（构建时确保 WaterURP
        /// 变体被包含，避免 WebGL 变紫/缺失）；复制一份再设置水位。
        /// 
        public static Material CreateDefaultMaterial(float waterHeight)
        {
            // 先尝试资产
            var asset = Resources.Load<Material>("Materials/WaterURP");
            if (asset != null)
            {
                var mat = UnityEngine.Object.Instantiate(asset);
                if (mat.HasProperty("_WaterLevel"))
                    mat.SetFloat("_WaterLevel", waterHeight);
                if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", new Color(0.10f, 0.55f, 0.62f, 0.82f));
                return mat;
            }

            // 兜底：运行时创建
            bool isUrp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            Shader shader = isUrp
                ? Shader.Find("ProceduralTerrain/WaterURP")
                : Shader.Find("Legacy Shaders/Transparent/Diffuse");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null;

            var mat2 = new Material(shader);
            if (mat2.HasProperty("_WaterLevel"))
                mat2.SetFloat("_WaterLevel", waterHeight);
            if (mat2.HasProperty("_Color"))
                mat2.SetColor("_Color", new Color(0.10f, 0.55f, 0.62f, 0.82f));
            return mat2;
        }
    }
}
