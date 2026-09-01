using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;
using System.Collections;
using UnityEngine.AI;



namespace ProceduralTerrain
{
    //
    /// 程序化 3D 地形生成器（Unity Terrain 组件）。
    ///
    /// 地表类型（共 6 种）：平原 / 草地 / 森林 / 雪地 / 湖泊 / 道路。
    ///   - 平原（Plains）   ：刚好位于水位线上方的平坦低地。
    ///   - 草地（Grassland）：起伏的绿色田野（草地细节）。
    ///   - 森林（Forest）   ：生物群系噪声林地斑块（树木）。
    ///   - 雪地（Snow）     ：高海拔山脉。
    ///   - 湖泊（Lake）     ：位于水位线以下的地形，被水面覆盖。
    ///   - 道路（Road）     ：仅连接各聚落。
    ///
    /// 聚落：2-10 个随机的平坦圆盘，绝不会落在湖泊内，尺寸处于
    /// 合理范围内，每个圆盘内部的地形被压平为水平台地
    /// （边缘混合），因此地面不会上下跳动。
    ///
    /// 所有地形高度带之间的过渡都用 smoothstep 缓动，
    /// 因此景观永远不会出现突然的台阶。所有纹理 / 树木 / 草地均为
    /// 程序生成——不需要任何外部美术资源。
    /// 
    public class TerrainGenerator : MonoBehaviour
    {
        public int seed = 1337;
        public Vector2Int terrainSize = new Vector2Int(500, 500);
        public float terrainHeight = 80f;
        [Range(65, 2049)] public int heightmapResolution = 513;
        [Range(64, 1024)] public int alphamapResolution = 512;
        [Range(64, 1024)] public int baseMapResolution = 1024;

        [Range(0f, 1f)] public float flatness = 0.6f;
        [Range(0f, 2f)] public float hillStrength = 1f;
        [Range(0f, 2f)] public float mountainStrength = 1.1f;

        [Range(0f, 1f)] public float lakeRatio = 0.16f;
        [Range(0f, 1f)] public float plainRatio = 0.30f;
        [Range(0f, 1f)] public float grassRatio = 0.28f;
        [Range(0f, 1f)] public float forestRatio = 0.16f;
        [Range(0f, 1f)] public float snowRatio = 0.10f;

        [Range(0f, 1f)] public float waterLevel = 0.35f;
        [Range(0f, 3f)] public float lakeDepthScale = 1.2f;
        [Range(0f, 0.06f)] public float beachWidth = 0.03f;

        [Range(0f, 1f)] public float snowLine = 0.76f;
        [Range(0f, 0.12f)] public float snowBand = 0.05f;

        [Range(2, 10)] public int settlementCount = 4;
        [Range(20f, 100f)] public float settlementRadiusMin = 30f;
        [Range(30f, 150f)] public float settlementRadiusMax = 60f;
        [Range(0.02f, 0.4f)] public float settlementMaxSlope = 0.25f;

        public bool roadEnabled = true;
        [Range(0.004f, 0.06f)] public float roadHalfWidth = 0.016f;
        [Range(0.001f, 0.02f)] public float roadEdgeWidth = 0.006f;

        public int treeCount = 9000;
        [Range(0f, 1f)] public float treeSlopeLimit = 0.45f;
        [Range(0f, 0.3f)] public float treeSnowMargin = 0.05f;
        public GameObject treePrefab;

        [Range(2f, 60f)] public float grassBladesPerSqrMeter = 8f;
        public float grassHeightMin = 0.2f;
        public float grassHeightMax = 0.4f;
        public float grassWidth = 0.09f;
        public float grassChunkSize = 16f;
        public float grassNearDistance = 90f;
        public float grassFarDistance = 200f;
        public bool grassDrawShadows = true;

        public bool generateOnStart = false;

        public Terrain terrain;

        public NavMeshSurface navMeshSurface;
        public bool carveLakeFromNavMesh = true;

        public bool useTerrainNavMeshAreas = true;
        public int navMeshAreaGrid = 16;
        public string navMeshRoadArea = "Road";
        public string navMeshForestArea = "Forest";
        public string navMeshSnowArea = "Snow";
        public float roadAreaCost = 0.6f;
        public float forestAreaCost = 3f;
        public float snowAreaCost = 5f;

        TerrainData terrainData;
        bool templateIsAsset;   // 地形材质是否来自 Resources 资产（资产不标 DontSave）
        float[,] heights;
        GameObject waterSurface;
        Vector2[] rangeCenters;   // 山脉锚点（0-1 坐标）
        Vector2[] settlements;     // 聚落中心点（0-1 坐标）
        float[] settlementRadii;   // 每个聚落的半径（米）
        List<Vector4> roadLines;   // 道路线段：x1,z1,x2,z2
        float[,] roadDistField;   // 缓存的最近道路距离（低分辨率）
        int roadFieldRes;         // 缓存距离场的分辨率
        float[,] biomeField;      // 缓存的森林-草地生物群系噪声（0..1）
        int biomeFieldRes;        // 缓存生物群系场的分辨率
        List<NavMeshModifierVolume> navMeshAreaVolumes;
        int roadAreaIdx, forestAreaIdx, snowAreaIdx;
        GameObject navMeshContainer;
        public TownGenerator town;
        readonly List<GameObject> spawnedTrees = new List<GameObject>();
        int lastTreeCount;
        GameObject lakeCarve;

        private readonly List<GrassChunk> grassNear = new List<GrassChunk>();
        private readonly List<GrassChunk> grassFar = new List<GrassChunk>();
        private Material grassMaterial;
        private float[,,] grassAlpha;
        private int[,] grassTreeCount;
        private int grassGridX, grassGridZ;

        private struct GrassChunk
        {
            public Mesh mesh;
            public Vector3 center;
        }

        void Awake()
        {
            if (grassNear.Count == 0)
                StartCoroutine(BuildGrassCoroutine());
        }

        void Start()
        {
            seed = Random.Range(0, 10000);
            if (generateOnStart) Generate();
        }


        public TerrainData TerrainData { get { return terrainData; } }

        //在地图坐标 (nx, nz) 处采样生成的高度图（归一化 0..1）。
        public float SampleHeight(float nx, float nz) { return Height01At(nx, nz); }

        //在地图坐标 (nx, nz) 处采样高度图坡度（归一化高度差）。
        public float SampleSlope(float nx, float nz) { return SlopeAt(nx, nz); }

        //地图点（0-1 坐标）到最近道路的距离（0-1 单位）。
        public float DistanceToNearestRoad(float nx, float nz) { return DistanceToNearestRoadInternal(nx, nz); }

        //向 splatmap 道路网络添加一条额外的道路线段（0-1 坐标）。
        public void RegisterRoadLine(float x1, float z1, float x2, float z2)
        {
            if (roadLines == null) roadLines = new List<Vector4>();
            roadLines.Add(new Vector4(x1, z1, x2, z2));
        }

        //聚落中心（0-1 地图坐标，生成期间填充）。
        public Vector2[] Settlements { get { return settlements; } }

        //索引 i 对应聚落的半径（米）。
        public float SettlementRadius(int i)
        {
            if (settlementRadii == null || i < 0 || i >= settlementRadii.Length) return settlementRadiusMax;
            return settlementRadii[i];
        }

        //当 (nx, nz) 位于任何聚落圆盘内时返回 true。
        public bool IsInsideSettlement(float nx, float nz)
        {
            if (settlements == null || settlementRadii == null) return false;
            float inv = 1f / Mathf.Max(1f, terrainSize.x);
            for (int i = 0; i < settlements.Length; i++)
            {
                float dx = settlements[i].x - nx;
                float dz = settlements[i].y - nz;
                float r = settlementRadii[i] * inv;
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        /// 地表类型枚举，与 splatmap 图层顺序一致。
        public enum SurfaceType
        {
            Plains,     // 平原
            Grassland,  // 草原
            Forest,     // 森林
            Snow,       // 雪地
            Sand,       // 沙地 / 湖岸
            Lake,       // 湖泊（水下）
        }

        /// 查询地图坐标 (nx, nz) 处的主要地表类型。
        /// 用于决定该位置应生成哪种动物。
        public SurfaceType GetSurfaceType(float nx, float nz)
        {
            float h = Height01At(nx, nz);
            if (h < waterLevel) return SurfaceType.Lake;      // 水下
            if (h >= snowLine) return SurfaceType.Snow;       // 高海拔雪地
            if (h < waterLevel + 0.06f) return SurfaceType.Sand; // 湖岸

            // 森林 / 草原 / 平原由 biome 场区分
            float biome = biomeField != null ? BiomeAt(nx, nz) : 0.5f;
            if (biome >= 0.62f && h < 0.72f) return SurfaceType.Forest;
            if (h < 0.44f) return SurfaceType.Plains;
            return SurfaceType.Grassland;
        }

        //生成地形（高度 / 6 种地表类型 / 聚落 / 道路 / 树木 / 草地 / 水体）。
        public void Generate()
        {
            EnsureTerrain();
            ClearSpawned();

            ConfigureTerrainData();
            BuildRangeCenters();
            BuildHeightmap();
            PickSettlementSites();
            BuildRoadPaths();
            SmoothRoads();
            FlattenSettlements();
            terrainData.SetHeights(0, 0, heights);
            terrainData.SyncHeightmap();
            if (town != null && town.enabled) town.Prepare(this);
            BuildRoadDistanceField();
            BuildBiomeField();
            EnsureTerrainMaterial();
            BuildSplatmapAndLayers();
            if (town != null && town.enabled) town.Place(this);
            PlaceTrees();
            BuildGrass();
            CreateWater();
            ApplyAndSave();
            RebuildNavMesh();

        }

        //清除所有内容（树木、草地、水体、纹理、高度）并重置为平坦地形。
        public void Clear()
        {
            ClearSpawned();
            ClearGrass();
            if (terrain == null) terrain = GetComponent<Terrain>();
            if (terrain == null) return;
            var data = terrain.terrainData;
            if (data == null) return;

            // 细节层：清除原型之前先把数据清零
            if (data.detailPrototypes.Length > 0)
            {
                int dw = data.detailWidth, dh = data.detailHeight;
                data.SetDetailLayer(0, 0, 0, new int[dw, dh]);
            }
            data.detailPrototypes = new DetailPrototype[0];

            // 树木：先清除实例再清除原型（反过来会产生
            // “Tree removed: invalid prototype”警告）
            data.treeInstances = new TreeInstance[0];
            data.treePrototypes = new TreePrototype[0];

            // 纹理：先清除 alphamap（使用当前图层），再清除 terrainLayers——
            // 反过来会导致 alphamapLayers 变为 0 且 SetAlphamaps 失败
            int aw = data.alphamapWidth, ah = data.alphamapHeight;
            int layers = data.alphamapLayers;
            if (layers > 0)
                data.SetAlphamaps(0, 0, new float[ah, aw, layers]);
            data.terrainLayers = new TerrainLayer[0];

            // 将高度清零
            int res = data.heightmapResolution;
            data.SetHeights(0, 0, new float[res, res]);

            if (terrain != null) terrain.Flush();
            ApplyAndSave();
            ClearNavMeshContainer();
            ClearNavMesh();
        }

        //查找或创建 Terrain 及其 TerrainData。
        void EnsureTerrain()
        {
            if (terrain == null) terrain = GetComponent<Terrain>();
            if (terrain == null)
            {
                terrainData = new TerrainData();
                GameObject go = Terrain.CreateTerrainGameObject(terrainData);
                go.transform.SetParent(transform, false);
                go.name = "ProceduralTerrain";
                terrain = go.GetComponent<Terrain>();
            }
            else if (terrain.terrainData == null)
            {
                terrainData = new TerrainData();
                terrain.terrainData = terrainData;
            }
            else
            {
                terrainData = terrain.terrainData;
            }
        }

        //根据 Inspector 参数配置 TerrainData 的分辨率和尺寸。
        void ConfigureTerrainData()
        {
            terrainData.heightmapResolution = heightmapResolution;
            terrainData.alphamapResolution = alphamapResolution;
            terrainData.baseMapResolution = baseMapResolution;
            terrainData.size = new Vector3(terrainSize.x, terrainHeight, terrainSize.y);
        }


        void EnsureTerrainMaterial()
        {
            if (terrain == null) return;

            // 低多边形地形材质优先，回退到内置 Terrain/Lit
            var shader = Shader.Find("ProceduralTerrain/LowPolyTerrain");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (shader == null) return;

            // 优先加载材质资产（构建时确保变体被包含）
            var assetMat = Resources.Load<Material>("Materials/LowPolyTerrain");
            if (assetMat == null) assetMat = Resources.Load<Material>("Materials/TerrainLit");
            templateIsAsset = assetMat != null;
            if (terrain.materialTemplate == null ||
                terrain.materialTemplate.shader == null ||
                terrain.materialTemplate.shader != shader)
            {
                terrain.materialTemplate = assetMat != null
                    ? assetMat
                    : new Material(shader);
                // 只有运行时新建的材质才标记 DontSave（资产材质不能标记，否则打包报错）
                if (Application.isPlaying && assetMat == null)
                    terrain.materialTemplate.hideFlags = HideFlags.DontSave;
            }

            // 更精细的网格 LOD，减少远处的模糊
            terrain.heightmapPixelError = 2f;
            terrain.basemapDistance = 3000f;

            // 增加树木等 Terrain 细节的渲染距离
            terrain.treeDistance = Mathf.Max(terrain.treeDistance, 600f);
            terrain.treeBillboardDistance = Mathf.Max(terrain.treeBillboardDistance, 700f);
            terrain.detailObjectDistance = Mathf.Max(terrain.detailObjectDistance, 400f);
        }



        //构建高度图。先采样平滑的噪声"势场"，
        /// 再通过百分位变换进行重映射，从而几乎精确匹配请求的地表混合比例
        /// （湖泊/平原/草地/森林/雪地）。高度带连续排布并用 smoothstep 缓动，
        /// 因此地形永远不会出现突然的台阶（规则：只做平滑过渡）。
        void BuildHeightmap()
        {
            int res = terrainData.heightmapResolution;
            heights = new float[res, res];
            int count = res * res;

            // 1) 采样势场（平滑、低频）
            var potential = new float[count];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float nx = x / (float)(res - 1);
                    float nz = z / (float)(res - 1);
                    potential[z * res + x] = SampleHeight01(nx, nz);
                }

            // 2) 百分位变换：值 -> 在 [0,1] 中的排名
            var sorted = (float[])potential.Clone();
            System.Array.Sort(sorted);

            for (int i = 0; i < count; i++)
            {
                int lo = 0, hi = count - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (sorted[mid] < potential[i]) lo = mid + 1;
                    else hi = mid;
                }
                float t01 = lo / (float)(count - 1);
                int z = i / res;
                int x = i % res;
                float nx = x / (float)(res - 1);
                float nz = z / (float)(res - 1);
                heights[z, x] = HeightFromMix(t01, nx, nz);
            }

            ApplyLakesAndBeaches();
            terrainData.SetHeights(0, 0, heights);
            terrainData.SyncHeightmap();
        }

        //把均匀分布的百分位 [0,1] 映射到地表类型的高度阶梯上。
        /// 各带连续排布，相邻格子不会跳变：
        ///   湖泊   [0.08 .. waterLevel]    （水下湖床）
        ///   平原   [waterLevel .. 0.44]    （平坦低地）
        ///   草地   [0.44 .. 0.62]          （起伏的田野）
        ///   森林   [0.62 .. 0.78]          （林地丘陵）
        ///   雪地   [0.78 .. 1.0]           （雪峰，强度跟随范围掩码）
        float HeightFromMix(float t, float nx, float nz)
        {
            float total = lakeRatio + plainRatio + grassRatio + forestRatio + snowRatio;
            if (total <= 0.0001f) total = 1f;
            float lake = lakeRatio / total;
            float plain = plainRatio / total;
            float grass = grassRatio / total;
            float forest = forestRatio / total;
            float b1 = lake;
            float b2 = lake + plain;
            float b3 = lake + plain + grass;
            float b4 = lake + plain + grass + forest;

            // 细微起伏，低频使相邻采样点保持接近
            float undulation = (Noise.Fbm(nx * 4f, nz * 4f, seed + 55, 3) - 0.5f)
                               * Mathf.Lerp(0.05f, 0.03f, flatness) * hillStrength;

            // 各带内的高度函数，带内线性且端点值匹配，
            // 使各带构成一条连续的高度阶梯
            float hLake = Mathf.Lerp(0.08f, waterLevel, Mathf.Clamp01(t / lake));
            float hPlain = Mathf.Lerp(waterLevel, 0.44f, Mathf.Clamp01((t - b1) / plain));
            float hGrass = Mathf.Lerp(0.44f, 0.62f, Mathf.Clamp01((t - b2) / grass));
            float hForest = Mathf.Lerp(0.62f, 0.78f, Mathf.Clamp01((t - b3) / forest));
            float mountainFrac = snowRatio / total;
            float str = Mathf.Lerp(0.55f, 1f, RangeMaskAt(nx, nz));
            float hSnow = 0.78f + 0.22f * Mathf.Clamp01((t - b4) / mountainFrac) * str;

            // 柔和的高度带过渡：每个边界附近用窄窗口混合相邻
            // 高度带的值，消除百分位抖动造成的台阶
            float delta = 0.028f;
            float w1 = Noise.Smoothstep(b1 - delta, b1 + delta, t);
            float w2 = Noise.Smoothstep(b2 - delta, b2 + delta, t);
            float w3 = Noise.Smoothstep(b3 - delta, b3 + delta, t);
            float w4 = Noise.Smoothstep(b4 - delta, b4 + delta, t);

            float h = Mathf.Lerp(hLake, hPlain, w1);
            h = Mathf.Lerp(h, hGrass, w2);
            h = Mathf.Lerp(h, hForest, w3);
            h = Mathf.Lerp(h, hSnow, w4);
            return Mathf.Clamp01(h + undulation);
        }

        //单个采样点高度（归一化 0..1），是用于百分位变换的平滑低频
        /// “势场”。
        float SampleHeight01(float nx, float nz)
        {
            float continental = Noise.Fbm(nx * 1.2f, nz * 1.2f, seed + 22, 3);

            // 轻柔的起伏，平坦度越高越平缓
            float rolling = Noise.Fbm(nx * 3.6f, nz * 3.6f, seed, 3)
                            * 0.28f * Mathf.Lerp(1f, 0.4f, flatness);

            float h = continental + rolling;

            // 围绕散布锚点的山脉：高地把山峰推高
            float rangeMask = RangeMaskAt(nx, nz);
            float highFactor = rangeMask * Mathf.Lerp(0.35f, 1f, continental);
            float ridges = Mathf.Pow(Noise.Ridge(nx * 3.1f, nz * 3.1f, seed + 11, 3), 1.4f)
                           * Mathf.Lerp(0.5f, 1.5f, mountainStrength);
            h = Mathf.Lerp(h, 0.72f + ridges * 0.35f, highFactor);

            // 细微细节，使高度带之间的地形不完全平坦
            float detail = (Noise.Fbm(nx * 12f, nz * 12f, seed + 44, 2) - 0.5f) * 0.04f;
            return Mathf.Clamp01(h + detail);
        }

        //放置山脉锚点（0.15-0.85 坐标，这样山脉不会紧贴地图边缘）。
        void BuildRangeCenters()
        {
            int count = Mathf.Max(2, Mathf.RoundToInt(Mathf.Lerp(5f, 2f, flatness)));
            rangeCenters = new Vector2[count];
            var rng = new System.Random(seed * 31 + 7);
            for (int i = 0; i < count; i++)
                rangeCenters[i] = new Vector2(
                    0.15f + (float)rng.NextDouble() * 0.7f,
                    0.15f + (float)rng.NextDouble() * 0.7f);
        }

        //用于山脉范围掩码的平滑衰减 [锚点处为 1 .. 半径外为 0]。
        float RangeMaskAt(float nx, float nz)
        {
            if (rangeCenters == null) BuildRangeCenters();
            float best = 0f;
            float radius = Mathf.Lerp(0.34f, 0.26f, flatness);
            for (int i = 0; i < rangeCenters.Length; i++)
            {
                float dx = nx - rangeCenters[i].x;
                float dz = nz - rangeCenters[i].y;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                best = Mathf.Max(best, Noise.Smoothstep(radius, 0f, d));
            }
            return best;
        }



        void PickSettlementSites()
        {
            int count = settlementCount;
            settlements = new Vector2[count];
            settlementRadii = new float[count];
            var rng = new System.Random(seed * 71 + 11);

            int placed = 0;
            int attempts = count * 800;
            float sizeN = Mathf.Max(1f, terrainSize.x);

            for (int i = 0; i < attempts && placed < count; i++)
            {
                // 聚落中心限制在 8%~92%，远离地图边界
                float nx = 0.08f + (float)rng.NextDouble() * 0.84f;
                float nz = 0.08f + (float)rng.NextDouble() * 0.84f;

                // 不能在湖泊里，且低于雪线
                float h = Height01At(nx, nz);
                if (h < waterLevel + 0.04f) continue;
                if (h > snowLine - 0.05f) continue;
                // 避开山脉山脊 / 厚重积雪
                if (RangeMaskAt(nx, nz) > 0.4f) continue;
                // 坡度平缓（真实坡度，米/米）
                if (RealSlopeAt(nx, nz) > settlementMaxSlope) continue;

                // 在“不太大 / 不太小”区间内取随机半径
                float radius = Mathf.Lerp(settlementRadiusMin, settlementRadiusMax,
                                          (float)rng.NextDouble());

                // 整个圆盘必须保持干燥且远离陡峭山脊
                if (!IsSettlementDiscValid(nx, nz, radius / sizeN)) continue;

                // 聚落之间保持最小间距
                if (IsTooCloseToPlaced(nx, nz, placed, radius, sizeN)) continue;

                settlements[placed] = new Vector2(nx, nz);
                settlementRadii[placed] = radius;
                placed++;
            }

            // 后备方案：用任意干燥地点填充剩余名额；坡度限制稍微放宽、
            // 间距也拉大，这样通常能满足要求的数量，圆盘之后仍会由
            // FlattenSettlements 进行平滑
            float fbSlope = settlementMaxSlope * 1.6f;
            int fb = 0;
            while (placed < count && fb++ < 6000)
            {
                float nx = (float)rng.NextDouble();
                float nz = (float)rng.NextDouble();
                float radius = (settlementRadiusMin + settlementRadiusMax) * 0.5f;
                if (IsTooCloseToPlaced(nx, nz, placed, radius, sizeN * 1.6f)) continue;
                if (!IsSettlementDiscValid(nx, nz, radius / sizeN, fbSlope)) continue;
                settlements[placed] = new Vector2(nx, nz);
                settlementRadii[placed] = radius;
                placed++;
            }

            // 绝不留下未使用的 (0,0) 槽位——收缩为实际放置的地点
            if (placed < count)
            {
                System.Array.Resize(ref settlements, placed);
                System.Array.Resize(ref settlementRadii, placed);
            }
        }

        bool IsTooCloseToPlaced(float nx, float nz, int placed, float radius, float sizeN)
        {
            for (int k = 0; k < placed; k++)
            {
                float dx = settlements[k].x - nx;
                float dz = settlements[k].y - nz;
                float minD = (settlementRadii[k] + radius) * 1.8f / sizeN;
                if (dx * dx + dz * dz < minD * minD) return true;
            }
            return false;
        }

        //检查整个聚落圆盘是否保持干燥、低于雪线、远离陡峭山脊且
        /// 大体平坦（基于已构建的高度图）。
        bool IsSettlementDiscValid(float nx, float nz, float r, float slopeLimit = -1f)
        {
            if (slopeLimit < 0f) slopeLimit = settlementMaxSlope;
            // 边界检查：聚落圆盘不能超出地图边缘（避免聚落贴边）
            float margin = 0.02f;
            if (nx - r < margin || nx + r > 1f - margin ||
                nz - r < margin || nz + r > 1f - margin)
                return false;
            // 环上的点加少数内部点，都必须满足坡度限制
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                float px = nx + Mathf.Cos(a) * r;
                float pz = nz + Mathf.Sin(a) * r;
                if (!IsSiteOk(px, pz, r * 0.35f, slopeLimit)) return false;
            }
            return true;
        }

        bool IsSiteOk(float px, float pz, float r, float slopeLimit)
        {
            int ring = 12;
            for (int i = 0; i < ring; i++)
            {
                float a = i / (float)ring * Mathf.PI * 2f;
                float qx = Mathf.Clamp01(px + Mathf.Cos(a) * r);
                float qz = Mathf.Clamp01(pz + Mathf.Sin(a) * r);
                float h = Height01At(qx, qz);
                if (h < waterLevel + 0.03f) return false;
                if (h > snowLine - 0.05f) return false;
                if (RangeMaskAt(qx, qz) > 0.45f) return false;
                if (RealSlopeAt(qx, qz) > slopeLimit) return false;
            }
            return true;
        }

        //归一化坐标处的真实地面坡度（高差比水平距离，米/米）。
        float RealSlopeAt(float nx, float nz)
        {
            return SlopeAt(nx, nz) * (terrainHeight / Mathf.Max(1f, terrainSize.x));
        }

        //平滑每个聚落圆盘（多次宽的盒式模糊）使圆盘内部保持柔和
        /// 的整体形状——小丘/小谷仍会保留，但表面不会上下颠簸。
        /// 圆盘边缘通过柔和的缓动混合重新融入周围地形。
        void FlattenSettlements()
        {
            if (settlements == null || settlementRadii == null) return;
            int res = terrainData.heightmapResolution;
            float sizeN = Mathf.Max(1f, terrainData.size.x);

            // 原始高度图的平滑副本：两遍盒式模糊保留大尺度
            // 坡度，同时去除看起来“凹凸不平”的小鼓包。
            // 可分离模糊（先水平后垂直，滑动窗口求和）把
            // O(n * r^2) 的滤波变成 O(n * r)，使压平保持快速。
            var smooth = (float[,])heights.Clone();
            int blurRadius = Mathf.Max(2, Mathf.RoundToInt(res * 0.008f));   // 约 8 个格子
            int passes = 2;
            for (int p = 0; p < passes; p++)
            {
                // 水平遍
                var tmp = (float[,])smooth.Clone();
                for (int z = 0; z < res; z++)
                {
                    float sum = 0f;
                    int count = 0;
                    for (int x = -blurRadius; x <= blurRadius; x++)
                    {
                        int xx = Mathf.Clamp(x, 0, res - 1);
                        sum += tmp[z, xx]; count++;
                    }
                    smooth[z, 0] = sum / count;
                    for (int x = 1; x < res; x++)
                    {
                        int xOut = Mathf.Clamp(x + blurRadius, 0, res - 1);
                        int xIn = Mathf.Clamp(x - blurRadius - 1, 0, res - 1);
                        sum += tmp[z, xOut] - tmp[z, xIn];
                        smooth[z, x] = sum / count;
                    }
                }
                // 垂直遍
                tmp = (float[,])smooth.Clone();
                for (int x = 0; x < res; x++)
                {
                    float sum = 0f;
                    int count = 0;
                    for (int z = -blurRadius; z <= blurRadius; z++)
                    {
                        int zz = Mathf.Clamp(z, 0, res - 1);
                        sum += tmp[zz, x]; count++;
                    }
                    smooth[0, x] = sum / count;
                    for (int z = 1; z < res; z++)
                    {
                        int zOut = Mathf.Clamp(z + blurRadius, 0, res - 1);
                        int zIn = Mathf.Clamp(z - blurRadius - 1, 0, res - 1);
                        sum += tmp[zOut, x] - tmp[zIn, x];
                        smooth[z, x] = sum / count;
                    }
                }
            }

            for (int s = 0; s < settlements.Length; s++)
            {
                float cx = settlements[s].x;
                float cz = settlements[s].y;
                float r = settlementRadii[s] / sizeN;
                float inner = r * 0.55f;   // 完全平滑的核心区域
                float outer = r;           // 缓动混合带，过渡回原始地形

                for (int z = 0; z < res; z++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        float hx = x / (float)(res - 1);
                        float hz = z / (float)(res - 1);
                        float dx = hx - cx, dz = hz - cz;
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        if (d > outer) continue;
                        // 混合：核心区域完全平滑，边缘缓动回到原始
                        // 地形，使聚落无缝融入
                        float w = 1f - Noise.Smoothstep(inner, outer, d);
                        float merged = Mathf.Lerp(heights[z, x], smooth[z, x], w);
                        // 聚落绝不能低于水位线——钳制，确保它
                        // 永远不可能落在湖泊里
                        heights[z, x] = Mathf.Max(merged, waterLevel + 0.015f);
                    }
                }
            }
        }



        //构建道路网络：每个聚落连接到它的两个最近邻居，
        /// 因此道路只存在于聚落之间。
        void BuildRoadPaths()
        {
            roadLines = new List<Vector4>();
            if (!roadEnabled || settlements == null || settlements.Length < 2) return;

            var rng = new System.Random(seed * 89 + 5);
            var pairs = new HashSet<int>();
            for (int i = 0; i < settlements.Length; i++)
            {
                for (int k = 0; k < 2; k++)
                {
                    int best = -1;
                    float bestD = float.MaxValue;
                    for (int j = 0; j < settlements.Length; j++)
                    {
                        if (j == i) continue;
                        int key = i < j ? i * 1000 + j : j * 1000 + i;
                        if (pairs.Contains(key)) continue;
                        float dx = settlements[i].x - settlements[j].x;
                        float dz = settlements[i].y - settlements[j].y;
                        float d = dx * dx + dz * dz;
                        if (d < bestD) { bestD = d; best = j; }
                    }
                    if (best < 0) continue;
                    pairs.Add(i < best ? i * 1000 + best : best * 1000 + i);

                    // 丢弃大部分被阻塞的道路（铺设的线段不足一半）——
                    // 被切成碎片的道路看起来很破，所以干脆不画它
                    int laid = AddWindingRoad(settlements[i], settlements[best], rng);
                    if (laid < 20)
                    {
                        for (int r = 0; r < laid; r++)
                            roadLines.RemoveAt(roadLines.Count - 1);
                    }
                }
            }
        }

        //在两个节点之间构建一条蜿蜒的道路。道路大致沿直线前进，
        /// 被轻微的正弦鼓包扰动，但会主动绕开湖泊和雪峰：
        /// 每段都会被侧向推开，直到落在干燥无雪的地面上，
        /// 因此道路永远不会穿过水体或高海拔积雪。
        /// 返回实际铺设的线段数（数量低意味着路线基本无法通行，
        /// 调用方可以整个丢弃它）。
        int AddWindingRoad(Vector2 a, Vector2 b, System.Random rng)
        {
            Vector2 dir = (b - a);
            float len = dir.magnitude;
            if (len < 0.001f) return 0;
            dir /= len;
            Vector2 norm = new Vector2(-dir.y, dir.x);
            float amp = 0.008f + (float)rng.NextDouble() * 0.018f;
            float bumps = 1.5f + (float)rng.NextDouble() * 2.0f;
            float phase = (float)rng.NextDouble() * Mathf.PI * 2f;

            float waterThresh = waterLevel + 0.015f;   // 水位线以上的干燥余量
            float snowThresh = snowLine - 0.12f;       // 远低于雪线
            int segs = 40;
            int laid = 0;

            Vector2 prev = a;
            for (int s = 1; s <= segs; s++)
            {
                float t = s / (float)segs;
                Vector2 p = Vector2.Lerp(a, b, t);
                p += norm * Mathf.Sin(t * Mathf.PI * bumps + phase) * amp;

                // 绕开湖泊和积雪：向侧面滑动，直到找到可用的地面
                if (!IsRoadable(p.x, p.y, waterThresh, snowThresh))
                {
                    bool found = false;
                    // 1) 向侧面滑动，左右交替，距离递增
                    for (int side = 1; side <= 60; side++)
                    {
                        float off = side * 0.008f;
                        Vector2 q1 = p + norm * off;
                        Vector2 q2 = p - norm * off;
                        if (IsRoadable(q1.x, q1.y, waterThresh, snowThresh)) { p = q1; found = true; break; }
                        if (IsRoadable(q2.x, q2.y, waterThresh, snowThresh)) { p = q2; found = true; break; }
                    }
                    // 2) 仍被阻塞时，沿道路方向滑动（朝向目标）
                    if (!found)
                    {
                        for (int step = 1; step <= 12; step++)
                        {
                            float along = step * (1f / segs) * 0.5f;
                            Vector2 qF = p + dir * along;
                            Vector2 qB = p - dir * along;
                            if (IsRoadable(qF.x, qF.y, waterThresh, snowThresh)) { p = qF; found = true; break; }
                            if (IsRoadable(qB.x, qB.y, waterThresh, snowThresh)) { p = qB; found = true; break; }
                        }
                    }
                    if (!found)
                    {
                        // 确实无法通行——跳过前方，保持道路连续，
                        // 沿走廊前进到下一个可通行的采样点
                        bool advanced = false;
                        for (int sk = 1; sk <= 6; sk++)
                        {
                            float ts = (s + sk) / (float)segs;
                            if (ts > 1f) break;
                            Vector2 qs = Vector2.Lerp(a, b, ts);
                            qs += norm * Mathf.Sin(ts * Mathf.PI * bumps + phase) * amp;
                            if (IsRoadable(qs.x, qs.y, waterThresh, snowThresh)) { p = qs; advanced = true; s += sk; break; }
                        }
                        if (!advanced) continue;
                    }
                }

                // 最终安全检查：绝不生成会进入水体/积雪的线段
                if (!IsRoadable(p.x, p.y, waterThresh, snowThresh)) continue;

                roadLines.Add(new Vector4(prev.x, prev.y, p.x, p.y));
                prev = p;
                laid++;
            }
            return laid;
        }

        //道路点必须位于地图内部、水位线以上且雪线以下。
        bool IsRoadable(float nx, float nz, float waterThresh, float snowThresh)
        {
            if (nx < 0.02f || nx > 0.98f || nz < 0.02f || nz > 0.98f) return false;
            float h = Height01At(nx, nz);
            return h >= waterThresh && h <= snowThresh;
        }

        //到最近道路的距离（归一化单位）。使用道路网络铺好后一次性
        /// 构建的缓存低分辨率距离场，这样每次查询都是 O(1)，
        /// 而无需扫描每条道路线段。
        float DistanceToNearestRoadInternal(float nx, float nz)
        {
            if (roadDistField == null || roadFieldRes <= 0) return DistanceToRoadExact(nx, nz);
            float fx = Mathf.Clamp01(nx) * (roadFieldRes - 1);
            float fz = Mathf.Clamp01(nz) * (roadFieldRes - 1);
            int x0 = (int)fx, z0 = (int)fz;
            int x1 = Mathf.Min(x0 + 1, roadFieldRes - 1);
            int z1 = Mathf.Min(z0 + 1, roadFieldRes - 1);
            float tx = fx - x0, tz = fz - z0;
            float a = roadDistField[z0, x0], b = roadDistField[z0, x1];
            float c = roadDistField[z1, x0], d = roadDistField[z1, x1];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        //到最近道路线段的精确距离（较慢，只在构建距离场时使用）。
        float DistanceToRoadExact(float nx, float nz)
        {
            float best = float.MaxValue;
            if (roadLines != null)
            {
                for (int i = 0; i < roadLines.Count; i++)
                {
                    Vector4 l = roadLines[i];
                    float dx = l.z - l.x;
                    float dz = l.w - l.y;
                    float len2 = dx * dx + dz * dz;
                    float t = ((nx - l.x) * dx + (nz - l.y) * dz) / Mathf.Max(len2, 0.0001f);
                    t = Mathf.Clamp01(t);
                    float px = l.x + dx * t;
                    float pz = l.y + dz * t;
                    float d = Mathf.Sqrt((nx - px) * (nx - px) + (nz - pz) * (nz - pz));
                    if (d < best) best = d;
                }
            }
            return best;
        }

        //在所有道路线段和城镇交叉街道都已知后，一次性构建缓存的道路距离场。
        void BuildRoadDistanceField()
        {
            roadFieldRes = Mathf.Clamp(alphamapResolution / 4, 64, 256);   // 512 alpha 时为 128
            roadDistField = new float[roadFieldRes, roadFieldRes];
            for (int z = 0; z < roadFieldRes; z++)
            {
                float nz = z / (float)(roadFieldRes - 1);
                for (int x = 0; x < roadFieldRes; x++)
                {
                    float nx = x / (float)(roadFieldRes - 1);
                    roadDistField[z, x] = DistanceToRoadExact(nx, nz);
                }
            }
        }

        //构建缓存的生物群系噪声场（森林 vs 草地）。以较粗的分辨率采样
        /// 并插值，这样 splatmap 和树木放置都可以 O(1) 读取它，
        /// 而不是逐像素运行 fBm。
        void BuildBiomeField()
        {
            biomeFieldRes = 128;
            biomeField = new float[biomeFieldRes, biomeFieldRes];
            for (int z = 0; z < biomeFieldRes; z++)
            {
                float nz = z / (float)(biomeFieldRes - 1);
                for (int x = 0; x < biomeFieldRes; x++)
                {
                    float nx = x / (float)(biomeFieldRes - 1);
                    biomeField[z, x] = Noise.Fbm(nx * 2.2f, nz * 2.2f, seed + 100, 3);
                }
            }
        }

        //双线性查询缓存的生物群系场。
        float BiomeAt(float nx, float nz)
        {
            if (biomeField == null || biomeFieldRes <= 0) return Noise.Fbm(nx * 2.2f, nz * 2.2f, seed + 100, 3);
            float fx = Mathf.Clamp01(nx) * (biomeFieldRes - 1);
            float fz = Mathf.Clamp01(nz) * (biomeFieldRes - 1);
            int x0 = (int)fx, z0 = (int)fz;
            int x1 = Mathf.Min(x0 + 1, biomeFieldRes - 1);
            int z1 = Mathf.Min(z0 + 1, biomeFieldRes - 1);
            float tx = fx - x0, tz = fz - z0;
            float a = biomeField[z0, x0], b = biomeField[z0, x1];
            float c = biomeField[z1, x0], d = biomeField[z1, x1];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }



        //湖床加深 + 岸边轻微抬升。
        void ApplyLakesAndBeaches()
        {
            int res = terrainData.heightmapResolution;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float h = heights[z, x];
                    if (h < waterLevel)
                    {
                        // 湖床加深，靠近岸边时缓动，使湖岸平缓地
                        // 倾斜进入水中而不是陡降；水位线上方的
                        // 边缘保持不变，这样湖岸紧贴水面
                        float depth = waterLevel - h;
                        float shoreEase = Mathf.Clamp01(depth / Mathf.Max(0.02f, waterLevel * 0.4f));
                        h = waterLevel - depth * (1f + lakeDepthScale * shoreEase);
                    }
                    heights[z, x] = Mathf.Clamp01(h);
                }
            }
        }

        //压平道路条带内的小尺度鼓包，使道路即使在丘陵地形上
        /// 也呈现为连续的条带。
        void SmoothRoads()
        {
            int res = terrainData.heightmapResolution;
            float half = roadHalfWidth * 0.85f;
            var smooth = (float[,])heights.Clone();
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float nx = x / (float)(res - 1);
                    float nz = z / (float)(res - 1);
                    if (DistanceToNearestRoadInternal(nx, nz) > half) continue;
                    float sum = 0f;
                    int cnt = 0;
                    for (int dz = -2; dz <= 2; dz++)
                    {
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int zz = Mathf.Clamp(z + dz, 0, res - 1);
                            int xx = Mathf.Clamp(x + dx, 0, res - 1);
                            sum += heights[zz, xx];
                            cnt++;
                        }
                    }
                    smooth[z, x] = sum / cnt;
                }
            }
            heights = smooth;
        }



        //构建 6 层 splatmap + TerrainLayers：
        ///   0 平原 / 1 草地 / 2 森林 / 3 雪地 / 4 沙地 / 5 道路。
        /// 所有权重都经过缓动处理，生物群系边界看起来不会生硬。
        void BuildSplatmapAndLayers()
        {
            var plains = MakeLayer(ProceduralTextures.CreatePlainsTexture(seed + 10), 16f, 0.15f);
            var grass = MakeLayer(ProceduralTextures.CreateGrassTexture(seed), 14f, 0.15f);
            var forest = MakeLayer(ProceduralTextures.CreateForestTexture(seed + 11), 16f, 0.15f);
            var snow = MakeLayer(ProceduralTextures.CreateSnowTexture(seed + 3), 30f, 0.15f);
            var sand = MakeLayer(ProceduralTextures.CreateSandTexture(seed + 1), 10f, 0.15f);
            var road = MakeLayer(ProceduralTextures.CreateRoadTexture(seed + 4), 8f, 0.15f);
            plains.name = "Plains"; grass.name = "Grassland"; forest.name = "Forest";
            snow.name = "Snow"; sand.name = "Sand"; road.name = "Road";
            terrainData.terrainLayers = new[] { plains, grass, forest, snow, sand, road };

            int aw = terrainData.alphamapWidth;
            int ah = terrainData.alphamapHeight;
            var map = new float[ah, aw, 6];

            for (int z = 0; z < ah; z++)
            {
                for (int x = 0; x < aw; x++)
                {
                    float nx = x / (float)(aw - 1);
                    float nz = z / (float)(aw - 1);
                    float h = Height01At(nx, nz);
                    float slope = SlopeAt(nx, nz);
                    float realSlope = slope * (terrainHeight / Mathf.Max(1f, terrainSize.x));
                    float biome = BiomeAt(nx, nz);   // 森林 vs 草地（缓存）

                    // 雪地：高海拔
                    float snowW = Noise.Smoothstep(snowLine, snowLine + snowBand, h);
                    // 沙地：湖床（水下）+ 紧贴湖岸的狭窄海滩带
                    float sandW = 1f - Noise.Smoothstep(waterLevel - 0.004f, waterLevel + beachWidth * 1.2f, h);
                    sandW = Mathf.Clamp01(sandW);
                    // 绿色地面掩码 = 生物群系可以存活的干燥无雪地面
                    float greenLand = (1f - sandW) * (1f - snowW);
                    // 平原：水位以上平坦低地带（低海拔 + 缓坡）
                    float lowFlat = (1f - Noise.Smoothstep(0.38f, 0.48f, h))
                                    * (1f - Noise.Smoothstep(0.05f, 0.16f, realSlope));
                    float plainsW = lowFlat * greenLand;
                    // 森林：在剩余绿色地面上的生物群系噪声林地斑块
                    float forestZone = Noise.Smoothstep(0.46f, 0.64f, biome);
                    float forestW = forestZone * greenLand * (1f - plainsW);
                    // 草地：剩余绿色地面
                    float grassW = greenLand * (1f - forestW) * (1f - plainsW);
                    grassW = Mathf.Clamp01(grassW);

                    // 道路：只在聚落之间，会被水体与积雪打断
                    float roadW = 0f;
                    if (roadEnabled && roadLines != null && roadLines.Count > 0)
                    {
                        float rd = DistanceToNearestRoadInternal(nx, nz);
                        roadW = 1f - Noise.Smoothstep(roadHalfWidth - roadEdgeWidth,
                                                      roadHalfWidth + roadEdgeWidth, rd);
                        if (h < waterLevel + 0.02f) roadW = 0f;                  // 湖泊 / 湖岸上没有道路
                        roadW *= 1f - Noise.Smoothstep(0.9f, 1.4f, realSlope);   // 悬崖会打断道路
                        roadW *= 1f - Noise.Smoothstep(snowLine - 0.10f, snowLine - 0.04f, h);   // 道路在雪线以下淡出
                    }

                    float other = Mathf.Max(0.0001f, 1f - roadW);
                    map[z, x, 0] = plainsW * other;
                    map[z, x, 1] = grassW * other;
                    map[z, x, 2] = forestW * other;
                    map[z, x, 3] = snowW * other;
                    map[z, x, 4] = sandW * other;
                    map[z, x, 5] = roadW;
                }
            }

            terrainData.SetAlphamaps(0, 0, map);
        }

        static TerrainLayer MakeLayer(Texture2D tex, float tileSize, float smoothness)
        {
            return new TerrainLayer
            {
                diffuseTexture = tex,
                tileSize = new Vector2(tileSize, tileSize),
                metallic = 0f,
                smoothnessSource = TerrainLayerSmoothnessSource.Constant,
                smoothness = smoothness,
            };
        }



        //仅限森林区域的拒绝采样树木放置：
        /// 排除水体、平原、雪地、道路、陡坡和聚落。
        void PlaceTrees()
        {
            var list = new List<TreeInstance>();
            var rng = new System.Random(seed * 31 + 7);
            int maxAttempts = treeCount * 40;

            // 最小间距：每 4m 格子最多 maxPerCell 棵树（约 2m 间距，森林级密度）
            const int spacingGrid = 4;
            int sg = Mathf.Max(1, Mathf.CeilToInt(terrainSize.x / spacingGrid));
            int[,] cellCount = new int[sg, sg];
            const int maxPerCell = 4;

            for (int i = 0; i < maxAttempts && list.Count < treeCount; i++)
            {
                float nx = (float)rng.NextDouble();
                float nz = (float)rng.NextDouble();
                float h = Height01At(nx, nz);
                float slope = SlopeAt(nx, nz);
                float realSlope = slope * (terrainHeight / Mathf.Max(1f, terrainSize.x));

                if (h < waterLevel + 0.02f) continue;          // 跳过水体
                if (h < waterLevel + 0.10f) continue;          // 水边留出较宽缓冲，避免树木贴水/长在湖里
                if (IsInsideSettlement(nx, nz)) continue;      // 跳过聚落
                if (roadEnabled && DistanceToNearestRoadInternal(nx, nz) < roadHalfWidth + 0.03f) continue;   // 跳过道路
                if (town != null && town.enabled && town.IsNearBuilding(nx, nz, 0.02f)) continue;  // 跳过建筑
                if (h > snowLine - treeSnowMargin) continue;   // 跳过雪线以上
                if (realSlope > treeSlopeLimit) continue;      // 跳过陡坡

                // 森林生物群系掩码：只有林地斑块才有树
                float biome = BiomeAt(nx, nz);
                if (biome < 0.46f) continue;
                float accept = Noise.Smoothstep(0.46f, 0.64f, biome);
                if (rng.NextDouble() > accept) continue;

                // 最小间距：如果 8m 格子已达到容量则跳过
                int cx = Mathf.Clamp((int)(nx * sg), 0, sg - 1);
                int cz = Mathf.Clamp((int)(nz * sg), 0, sg - 1);
                if (cellCount[cz, cx] >= maxPerCell) continue;
                cellCount[cz, cx]++;

                list.Add(new TreeInstance
                {
                    position = new Vector3(nx, h, nz),
                    rotation = (float)(rng.NextDouble() * Mathf.PI * 2.0),
                    heightScale = RandRange(rng, 1.0f, 1.8f) * 3f,   // 树整体放大 3 倍
                    widthScale = RandRange(rng, 1.0f, 1.8f) * 3f,
                    color = Color.white * RandRange(rng, 0.9f, 1.1f),
                });
            }
            lastTreeCount = list.Count;

            if (treePrefab != null)
            {
                terrainData.treePrototypes = new[] { new TreePrototype { prefab = treePrefab } };
                terrainData.treeInstances = list.ToArray();
            }

        }

        void BuildGrass()
        {
            StopAllCoroutines();
            ClearGrass();
            if (!PrepareGrass()) return;

            for (int gz = 0; gz < grassGridZ; gz++)
                for (int gx = 0; gx < grassGridX; gx++)
                    BuildGrassChunk(gx, gz);
            EnsureGrassMaterial();
        }

        //按帧分片构建草地（避免在开始播放时卡顿）。
        IEnumerator BuildGrassCoroutine()
        {
            if (!PrepareGrass()) yield break;
            int batch = 30;
            for (int gz = 0; gz < grassGridZ; gz++)
            {
                for (int gx = 0; gx < grassGridX; gx++)
                {
                    BuildGrassChunk(gx, gz);
                    if ((gz * grassGridX + gx) % batch == 0) yield return null;
                }
            }
            EnsureGrassMaterial();
        }

        bool PrepareGrass()
        {
            if (terrain == null) terrain = GetComponent<Terrain>();
            if (terrain == null) terrain = FindFirstObjectByType<Terrain>();
            if (terrain == null || terrain.terrainData == null) return false;
            terrainData = terrain.terrainData;

            grassGridX = Mathf.CeilToInt(terrainData.size.x / grassChunkSize);
            grassGridZ = Mathf.CeilToInt(terrainData.size.z / grassChunkSize);
            grassAlpha = new float[grassGridZ, grassGridX, 6];
            grassTreeCount = new int[grassGridZ, grassGridX];

            // 重新加载高度数组（域重载后为 null）
            int res = terrainData.heightmapResolution;
            heights = terrainData.GetHeights(0, 0, res, res);

            // 采样全部 6 层 splatmap
            if (terrainData.alphamapLayers >= 6)
            {
                float[,,] alpha = terrainData.GetAlphamaps(0, 0, terrainData.alphamapWidth, terrainData.alphamapHeight);
                for (int gz = 0; gz < grassGridZ; gz++)
                {
                    for (int gx = 0; gx < grassGridX; gx++)
                    {
                        float cx = (gx * grassChunkSize + grassChunkSize * 0.5f) / terrainData.size.x;
                        float cz = (gz * grassChunkSize + grassChunkSize * 0.5f) / terrainData.size.z;
                        int ax = Mathf.Clamp(Mathf.RoundToInt(cx * (alpha.GetLength(1) - 1)), 0, alpha.GetLength(1) - 1);
                        int az = Mathf.Clamp(Mathf.RoundToInt(cz * (alpha.GetLength(0) - 1)), 0, alpha.GetLength(0) - 1);
                        for (int l = 0; l < 6; l++)
                            grassAlpha[gz, gx, l] = alpha[az, ax, l];
                    }
                }
            }
            else
            {
                for (int gz = 0; gz < grassGridZ; gz++)
                    for (int gx = 0; gx < grassGridX; gx++)
                        grassAlpha[gz, gx, 1] = 1f;
            }

            // 统计每块的树木实例数量（森林中的草地会被树木替代）
            var trees = terrainData.treeInstances;
            if (trees != null)
            {
                for (int i = 0; i < trees.Length; i++)
                {
                    int gx = Mathf.Clamp((int)(trees[i].position.x * grassGridX), 0, grassGridX - 1);
                    int gz = Mathf.Clamp((int)(trees[i].position.z * grassGridZ), 0, grassGridZ - 1);
                    grassTreeCount[gz, gx]++;
                }
            }
            return true;
        }

        //草密度乘数：只判断该区域是否允许生成草，
        /// 允许的区域统一密度（不再按草地/平原区分浓稀）。
        ///  - 森林/雪地/沙地/道路/湖泊 → 无草
        ///  - 其余（平原/草地）→ 统一密度 1
        float GrassDensityScale(int gz, int gx)
        {
            if (grassAlpha == null) return 1f;
            float forest = grassAlpha[gz, gx, 2];
            float snow = grassAlpha[gz, gx, 3];
            float sand = grassAlpha[gz, gx, 4];
            float road = grassAlpha[gz, gx, 5];

            if (forest > 0.25f) return 0f;       // 森林——无草地
            if (snow > 0.25f) return 0f;         // 雪地——无草地
            if (sand > 0.3f) return 0f;          // 沙地——无草地
            if (road > 0.3f) return 0f;          // 道路——无草地

            // 森林：当每块树木超过 3 棵时草地消失
            int trees = grassTreeCount != null ? grassTreeCount[gz, gx] : 0;
            if (trees > 3) return 0f;

            return 1f;   // 统一密度
        }

        void BuildGrassChunk(int gx, int gz)
        {
            // 边界检查：块必须完全位于地形范围内，否则草会漂到地形外
            // （size 可能不是 chunkSize 整数倍，最后一个块会超出边界）
            float xMin = gx * grassChunkSize;
            float zMin = gz * grassChunkSize;
            if (xMin >= terrainData.size.x || zMin >= terrainData.size.z) return;

            float cx = xMin + grassChunkSize * 0.5f;
            float cz = zMin + grassChunkSize * 0.5f;
            float h = GrassSampleHeight(cx, cz);
            if (h < waterLevel + 0.04f || h > 0.95f) return;   // 水面留出缓冲，避免草生成在水里
            if (h >= snowLine - 0.04f) return;                 // 雪山：雪线以下留出缓冲
            if (GrassSlope(cx, cz) > 0.5f) return;

            float scale = GrassDensityScale(gz, gx);
            // 只采样一次草叶位置，然后在近处和远处网格之间共享
            var blades = SampleGrassBlades(gx, gz, grassBladesPerSqrMeter * scale);
            if (blades == null || blades.Count == 0) return;

            var nearMesh = BuildGrassMeshFromBlades(blades, 0, blades.Count);
            if (nearMesh != null)
                grassNear.Add(new GrassChunk { mesh = nearMesh, center = GrassChunkCenter(gx, gz, h) });

            // 远处 LOD 每 3 根草叶取 1 根（仍均匀分布，三角形数量约为 1/3）
            var farMesh = BuildGrassMeshFromBlades(blades, 0, blades.Count, 3);
            if (farMesh != null)
                grassFar.Add(new GrassChunk { mesh = farMesh, center = GrassChunkCenter(gx, gz, h) });
        }

        //草地块中心（世界坐标，含 Terrain 偏移），用于距离与视锥体剔除。
        Vector3 GrassChunkCenter(int gx, int gz, float h01)
        {
            float cx = gx * grassChunkSize + grassChunkSize * 0.5f;
            float cz = gz * grassChunkSize + grassChunkSize * 0.5f;
            float terrainY = terrain != null ? terrain.transform.position.y : 0f;
            return new Vector3(cx + (terrain != null ? terrain.transform.position.x : 0f),
                               h01 * terrainData.size.y + terrainY,
                               cz + (terrain != null ? terrain.transform.position.z : 0f));
        }

        // 采样的草叶：局部位置 + 高度 + 随机变化
        private struct GrassBlade
        {
            public float x, z, h01;
            public float height, width, phase, variant;
        }

        //在块内采样有效的草叶位置（干燥、缓坡、远离道路）。
        /// 近处和远处 LOD 网格共用。
        List<GrassBlade> SampleGrassBlades(int gx, int gz, float density)
        {
            var rng = new System.Random(gx * 73856093 ^ gz * 19349663 ^ seed);
            int want = Mathf.RoundToInt(density * grassChunkSize * grassChunkSize);
            if (want <= 0) return null;

            var result = new List<GrassBlade>(want);
            float baseX = gx * grassChunkSize;
            float baseZ = gz * grassChunkSize;
            float sizeX = terrainData.size.x;

            for (int i = 0; i < want * 3 && result.Count < want; i++)
            {
                float lx = Mathf.Min(baseX + (float)rng.NextDouble() * grassChunkSize, sizeX - 0.01f);
                float lz = Mathf.Min(baseZ + (float)rng.NextDouble() * grassChunkSize, terrainData.size.z - 0.01f);
                float h01 = GrassSampleHeight(lx, lz);
                if (h01 < waterLevel + 0.04f) continue;   // 水面及岸边留出缓冲，避免草生成在水里
                if (h01 >= snowLine - 0.04f) continue;    // 雪山：雪线以下留出缓冲，避免草生成在雪山
                if (GrassSlope(lx, lz) > 0.5f) continue;
                float lxN = lx / sizeX, lzN = lz / sizeX;
                if (roadEnabled && DistanceToNearestRoadInternal(lxN, lzN) < roadHalfWidth + 0.012f) continue;

                result.Add(new GrassBlade
                {
                    x = lx,
                    z = lz,
                    h01 = h01,
                    height = Mathf.Lerp(grassHeightMin, grassHeightMax, (float)rng.NextDouble()),
                    width = grassWidth * (0.7f + 0.6f * (float)rng.NextDouble()),
                    phase = (float)rng.NextDouble(),
                    variant = (float)rng.NextDouble(),
                });
            }
            return result;
        }

        //从共享的草叶列表构建草地网格。stride > 1 时取子集
        /// （每第 N 根草叶）用于远处 LOD。
        Mesh BuildGrassMeshFromBlades(List<GrassBlade> blades, int start, int count, int stride = 1)
        {
            int n = 0;
            for (int i = start; i < start + count && i < blades.Count; i++)
                if ((i - start) % stride == 0) n++;
            if (n <= 0) return null;

            var verts = new List<Vector3>(n * 12);
            var colors = new List<Color>(n * 12);
            var tris = new List<int>(n * 18);
            float sizeY = terrainData.size.y;
            // 草叶用 Matrix4x4.identity 在世界坐标绘制：顶点先算本地坐标，
            // 再整体叠加 Terrain 的世界位置（terrainOffset 已含 y 偏移）。
            Vector3 terrainOffset = terrain != null ? terrain.transform.position : Vector3.zero;

            int outIdx = 0;
            for (int i = start; i < start + count && i < blades.Count; i++)
            {
                if ((i - start) % stride != 0) continue;
                var b = blades[i];
                float worldY = b.h01 * sizeY;

                int baseVert = verts.Count;
                for (int k = 0; k < 3; k++)
                {
                    float ang = k / 3f * Mathf.PI;
                    Vector3 dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                    Vector3 p0 = terrainOffset + new Vector3(b.x, worldY, b.z) - dir * b.width * 0.5f;
                    Vector3 p1 = terrainOffset + new Vector3(b.x, worldY, b.z) + dir * b.width * 0.5f;
                    Vector3 p2 = terrainOffset + new Vector3(b.x, worldY, b.z) + dir * b.width * 0.2f + new Vector3(0, b.height, 0);
                    verts.Add(p0); colors.Add(new Color(b.variant, 0, b.phase, 0f));
                    verts.Add(p1); colors.Add(new Color(b.variant, 0, b.phase, 0f));
                    verts.Add(p2); colors.Add(new Color(b.variant, 0, b.phase, 1f));
                    tris.Add(baseVert + k * 3 + 0);
                    tris.Add(baseVert + k * 3 + 2);
                    tris.Add(baseVert + k * 3 + 1);
                }
                outIdx++;
            }

            if (verts.Count == 0) return null;
            var mesh = new Mesh();
            mesh.name = "GrassChunk";
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }


        //在本地地形坐标处采样高度（双线性插值，与 terrainData
        /// 的 GetInterpolatedHeight 一致，保证草根精确贴地）。
        /// 不能用最近格——坡度大的地方误差可达数米。
        float GrassSampleHeight(float x, float z)
        {
            int res = terrainData.heightmapResolution;
            float nx = Mathf.Clamp01(x / terrainData.size.x) * (res - 1);
            float nz = Mathf.Clamp01(z / terrainData.size.z) * (res - 1);
            int x0 = (int)nx, z0 = (int)nz;
            int x1 = Mathf.Min(x0 + 1, res - 1), z1 = Mathf.Min(z0 + 1, res - 1);
            float tx = nx - x0, tz = nz - z0;
            float h00 = heights[z0, x0], h10 = heights[z0, x1];
            float h01 = heights[z1, x0], h11 = heights[z1, x1];
            float top = Mathf.Lerp(h00, h10, tx);
            float bottom = Mathf.Lerp(h01, h11, tx);
            return Mathf.Lerp(top, bottom, tz);
        }

        float GrassSlope(float x, float z)
        {
            int res = terrainData.heightmapResolution;
            float invSize = 1f / terrainData.size.x;
            float fx = Mathf.Clamp01(x * invSize) * (res - 1);
            float fz = Mathf.Clamp01(z * invSize) * (res - 1);
            int ix = (int)fx, iz = (int)fz;
            ix = Mathf.Clamp(ix, 1, res - 2);
            iz = Mathf.Clamp(iz, 1, res - 2);
            float s = 1f;
            float hL = heights[iz, Mathf.Max(0, ix - 1)];
            float hR = heights[iz, Mathf.Min(res - 1, ix + 1)];
            float hU = heights[Mathf.Max(0, iz - 1), ix];
            float hD = heights[Mathf.Min(res - 1, iz + 1), ix];
            float dx = (hR - hL) / (2 * s);
            float dz = (hU - hD) / (2 * s);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        void EnsureGrassMaterial()
        {
            if (grassMaterial != null && grassMaterial.shader != null) return;
            // 优先用材质资产（构建时确保 GrassURP 变体被包含，避免 WebGL 缺失 fallback 到 Lit 变白/不动）
            grassMaterial = Resources.Load<Material>("Materials/GrassURP");
            if (grassMaterial == null)
            {
                Shader shader = Shader.Find("ProceduralTerrain/GrassURP");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                grassMaterial = new Material(shader);
                grassMaterial.name = "GrassMaterial";
            }
        }

        void ClearGrass()
        {
            for (int i = 0; i < grassNear.Count; i++)
                if (grassNear[i].mesh != null) SafeDestroy(grassNear[i].mesh);
            for (int i = 0; i < grassFar.Count; i++)
                if (grassFar[i].mesh != null) SafeDestroy(grassFar[i].mesh);
            grassNear.Clear();
            grassFar.Clear();
            grassMaterial = null;   // 材质来自 Resources 资产，不销毁（避免 Destroying assets 报错）
        }

        //草地渲染：距离 + 视锥体裁剪，两个 LOD 级别。
        void LateUpdate()
        {
            if (grassNear.Count == 0) return;
            if (grassMaterial == null || grassMaterial.shader == null) EnsureGrassMaterial();
            if (grassMaterial == null) return;

            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 camPos = cam.transform.position;
            Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(cam);

            DrawGrassChunks(grassNear, camPos, frustum, grassNearDistance);
            DrawGrassChunks(grassFar, camPos, frustum, grassFarDistance);
        }

        void DrawGrassChunks(List<GrassChunk> list, Vector3 camPos, Plane[] frustum, float maxDist)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c.mesh == null) continue;
                if (Vector3.Distance(camPos, c.center) > maxDist) continue;
                if (!GeometryUtility.TestPlanesAABB(frustum, new Bounds(c.center, new Vector3(grassChunkSize, 2f, grassChunkSize)))) continue;
                Graphics.DrawMesh(c.mesh, Matrix4x4.identity, grassMaterial, 0, null, 0, null,
                    grassDrawShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off);
            }
        }


        //放置水面（烘焙顶点颜色的水深渐变）。
        void CreateWater()
        {
            float terrainY = terrain != null ? terrain.transform.position.y : 0f;
            float terrainX = terrain != null ? terrain.transform.position.x : 0f;
            float terrainZ = terrain != null ? terrain.transform.position.z : 0f;
            float waterHeightWorld = waterLevel * terrainData.size.y + terrainY;
            float[,] depth01 = SampleWaterDepth(terrainData.alphamapWidth, terrainData.alphamapHeight);
            waterSurface = WaterSurface.Create(
                terrainData.size.x, terrainData.size.z, waterHeightWorld, depth01, 160, null);
            waterSurface.transform.SetParent(transform, false);
            // 对齐地形世界偏移（Create 默认放在 (0, waterHeight, 0)，会与地形错位）
            waterSurface.transform.position = new Vector3(terrainX, waterHeightWorld, terrainZ);

            if (Application.isPlaying)
            {
                var mr = waterSurface.GetComponent<MeshRenderer>();
                if (mr != null && mr.sharedMaterial != null) mr.sharedMaterial.hideFlags = HideFlags.DontSave;
                var mf = waterSurface.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) mf.sharedMesh.hideFlags = HideFlags.DontSave;
            }
        }

        //根据地形高度采样每个格子的水深（归一化 0..1）。
        float[,] SampleWaterDepth(int w, int h)
        {
            var depth = new float[h, w];
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    float nx = x / (float)(w - 1);
                    float nz = z / (float)(h - 1);
                    float dh = waterLevel - Height01At(nx, nz);
                    depth[z, x] = Mathf.Clamp01(dh / Mathf.Max(0.06f, waterLevel));
                }
            }
            return depth;
        }


        //在刚生成的地形上烘焙 NavMeshSurface。
        void RebuildNavMesh()
        {
            if (navMeshSurface == null) navMeshSurface = GetComponent<NavMeshSurface>();
            if (navMeshSurface == null) return;
            if (terrainData == null) return;

            if (terrain != null) terrain.Flush();

            ClearNavMeshCarve();
            ClearNavMeshAreaVolumes();
            if (carveLakeFromNavMesh) CreateLakeCarveVolume();
            if (useTerrainNavMeshAreas) CreateTerrainAreaVolumes();

            ApplyAreaCosts();
            navMeshSurface.BuildNavMesh();
            var obstacles = FindObjectsByType<NavMeshObstacle>(FindObjectsSortMode.None);
            for (int i = 0; i < obstacles.Length; i++)
            {
                obstacles[i].enabled = false;
                obstacles[i].enabled = true;
            }
        }

        //获取名为 "NavMesh" 的容器 GameObject；不存在则创建。
        GameObject GetNavMeshContainer()
        {
            if (navMeshContainer != null) return navMeshContainer;
            var existing = transform.Find("NavMesh");
            if (existing != null) { navMeshContainer = existing.gameObject; return navMeshContainer; }
            var go = new GameObject("NavMesh");
            go.transform.SetParent(transform, false);
            go.transform.position = Vector3.zero;
            navMeshContainer = go;
            if (Application.isPlaying) go.hideFlags = HideFlags.DontSave;
            return go;
        }

        //把自定义区域名注册到 NavMesh，并按地形区域设置通行代价。
        void ApplyAreaCosts()
        {
            if (!useTerrainNavMeshAreas) return;

            roadAreaIdx = ResolveAreaIndex(navMeshRoadArea);
            forestAreaIdx = ResolveAreaIndex(navMeshForestArea);
            snowAreaIdx = ResolveAreaIndex(navMeshSnowArea);

            NavMesh.SetAreaCost(roadAreaIdx, roadAreaCost);
            NavMesh.SetAreaCost(forestAreaIdx, forestAreaCost);
            NavMesh.SetAreaCost(snowAreaIdx, snowAreaCost);
        }

        int ResolveAreaIndex(string areaName)
        {
            if (string.IsNullOrEmpty(areaName)) return 0;
            int idx = NavMesh.GetAreaFromName(areaName);
            if (idx != -1) return idx;
            return 0;
        }

        void CreateTerrainAreaVolumes()
        {
            if (navMeshAreaVolumes == null) navMeshAreaVolumes = new List<NavMeshModifierVolume>();
            navMeshAreaVolumes.Clear();

            // 先解析区域索引，确保 AreaForSurface 使用正确的 area 值
            roadAreaIdx = ResolveAreaIndex(navMeshRoadArea);
            forestAreaIdx = ResolveAreaIndex(navMeshForestArea);
            snowAreaIdx = ResolveAreaIndex(navMeshSnowArea);

            int grid = Mathf.Clamp(navMeshAreaGrid, 4, 64);
            float sizeX = terrainData.size.x;
            float sizeZ = terrainData.size.z;
            float cellX = sizeX / grid;
            float cellZ = sizeZ / grid;

            for (int gz = 0; gz < grid; gz++)
            {
                for (int gx = 0; gx < grid; gx++)
                {
                    // 3×3 子采样取"最严格"区域（任一子点是雪/森林则整格标记），
                    // 避免格子中心判定导致的森林缺口（车从缺口开进森林）
                    int area = -1;
                    for (int sy = 0; sy < 3; sy++)
                    {
                        for (int sx = 0; sx < 3; sx++)
                        {
                            float nx = (gx + (sx + 0.5f) / 3f) / grid;
                            float nz = (gz + (sy + 0.5f) / 3f) / grid;
                            int a = AreaForSurface(nx, nz);
                            if (a > area) area = a;
                        }
                    }
                    if (area < 0) continue;

                    var go = new GameObject("NavMeshArea_" + gx + "_" + gz);
                    go.transform.SetParent(GetNavMeshContainer().transform, false);
                    var vol = go.AddComponent<NavMeshModifierVolume>();
                    vol.center = new Vector3(gx * cellX + cellX * 0.5f, terrainData.size.y * 0.5f, gz * cellZ + cellZ * 0.5f);
                    vol.size = new Vector3(cellX + 0.5f, terrainData.size.y + 1f, cellZ + 0.5f);
                    vol.area = area;
                    if (Application.isPlaying) go.hideFlags = HideFlags.DontSave;
                    navMeshAreaVolumes.Add(vol);
                }
            }
        }

        //返回地图点 (nx, nz) 对应的 NavMesh 区域索引；-1 表示用默认 Walkable。
        int AreaForSurface(float nx, float nz)
        {
            var type = GetSurfaceType(nx, nz);

            if (type == SurfaceType.Snow) return snowAreaIdx >= 0 ? snowAreaIdx : -1;
            if (type == SurfaceType.Forest) return forestAreaIdx >= 0 ? forestAreaIdx : -1;
            if (type == SurfaceType.Lake) return -1;

            if (DistanceToNearestRoadInternal(nx, nz) < roadHalfWidth + 0.01f)
                return roadAreaIdx >= 0 ? roadAreaIdx : -1;

            return -1;
        }

        void CreateLakeCarveVolume()
        {
            var go = new GameObject("NavMeshLakeCarve");
            go.transform.SetParent(GetNavMeshContainer().transform, false);
            // 体积必须覆盖地形实际位置：world 位置设为地形位置（地形可能不在原点）
            go.transform.position = terrain != null ? terrain.transform.position : Vector3.zero;
            var vol = go.AddComponent<NavMeshModifierVolume>();
            float waterY = waterLevel * terrainData.size.y;
            vol.size = new Vector3(terrainData.size.x, waterY + 1f, terrainData.size.z);
            vol.center = new Vector3(terrainData.size.x * 0.5f, (waterY + 1f) * 0.5f, terrainData.size.z * 0.5f);
            vol.area = 1; // 不可行走（Not Walkable）
            lakeCarve = go;
            if (Application.isPlaying) go.hideFlags = HideFlags.DontSave;
        }

        //销毁由 CreateLakeCarveVolume 创建的湖泊挖除体积。
        void ClearNavMeshCarve()
        {
            if (lakeCarve != null)
            {
                SafeDestroy(lakeCarve);
                lakeCarve = null;
            }
        }

        //销毁按地形标记的区域体积。
        void ClearNavMeshAreaVolumes()
        {
            if (navMeshAreaVolumes == null) return;
            for (int i = 0; i < navMeshAreaVolumes.Count; i++)
            {
                var v = navMeshAreaVolumes[i];
                if (v != null) SafeDestroy(v.gameObject);
            }
            navMeshAreaVolumes.Clear();
        }

        void ClearNavMeshContainer()
        {
            var toDestroy = new List<GameObject>();
            if (navMeshContainer != null)
            {
                toDestroy.Add(navMeshContainer);
                navMeshContainer = null;
            }
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i).gameObject;
                if (child == null) continue;
                if (child.name.StartsWith("NavMesh"))
                    toDestroy.Add(child);
            }
            for (int i = 0; i < toDestroy.Count; i++)
                SafeDestroyImmediate(toDestroy[i]);
            lakeCarve = null;
            if (navMeshAreaVolumes != null) navMeshAreaVolumes.Clear();
        }

        static void SafeDestroyImmediate(Object o)
        {
            if (o == null) return;
            Object.DestroyImmediate(o);
        }

        void ClearNavMesh()
        {
            if (navMeshSurface == null) navMeshSurface = GetComponent<NavMeshSurface>();
            if (navMeshSurface == null) return;
            navMeshSurface.RemoveData();
            navMeshSurface.navMeshData = null;
        }


        //在归一化坐标 (nx, nz) 处采样高度图。
        float Height01At(float nx, float nz)
        {
            int res = terrainData.heightmapResolution;
            int x = Mathf.RoundToInt(Mathf.Clamp01(nx) * (res - 1));
            int z = Mathf.RoundToInt(Mathf.Clamp01(nz) * (res - 1));
            return heights[z, x];
        }

        //近似坡度（0..1，1=垂直）。基于高度图相邻格子的差值。
        float SlopeAt(float nx, float nz)
        {
            int res = terrainData.heightmapResolution;
            int x = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(nx) * (res - 1)), 1, res - 2);
            int z = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(nz) * (res - 1)), 1, res - 2);
            float scale = 1f / (2f / (res - 1));
            float dx = (heights[z, x + 1] - heights[z, x - 1]) * scale;
            float dz = (heights[z + 1, x] - heights[z - 1, x]) * scale;
            return Mathf.Clamp01(Mathf.Sqrt(dx * dx + dz * dz));
        }

        void ClearSpawned()
        {
            var toDestroy = new List<GameObject>();
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child == null) continue;
                string n = child.name;
                if (n == "WaterSurface" || n == "ProceduralTree" || n.StartsWith("Town") || n.EndsWith("(Clone)"))
                    toDestroy.Add(child);
            }
            for (int i = 0; i < toDestroy.Count; i++)
                SafeDestroy(toDestroy[i]);
            spawnedTrees.Clear();
        }

        void ApplyAndSave()
        {
            if (terrainData == null && terrain != null) terrainData = terrain.terrainData;
            if (terrainData == null) return;
            if (terrain != null) terrain.Flush();
            MarkDontSave();
        }

        void MarkDontSave()
        {
            if (terrainData != null) terrainData.hideFlags = HideFlags.DontSave;
            // 只对运行时新建的材质标 DontSave（资产材质如 LowPolyTerrain.mat 不能标，否则打包报错）
            if (terrain != null && terrain.materialTemplate != null && !templateIsAsset)
                terrain.materialTemplate.hideFlags = HideFlags.DontSave;
            for (int i = 0; i < terrainData.terrainLayers.Length; i++)
            {
                var layer = terrainData.terrainLayers[i];
                if (layer == null) continue;
                layer.hideFlags = HideFlags.DontSave;
                if (layer.diffuseTexture != null) layer.diffuseTexture.hideFlags = HideFlags.DontSave;
            }
            for (int i = 0; i < terrainData.detailPrototypes.Length; i++)
            {
                var tex = terrainData.detailPrototypes[i].prototypeTexture;
                if (tex != null) tex.hideFlags = HideFlags.DontSave;
            }
        }

        static float RandRange(System.Random rng, float min, float max)
        {
            return Mathf.Lerp(min, max, (float)rng.NextDouble());
        }


        static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        [ContextMenu("Generate Terrain")]
        void GenerateFromMenu() => Generate();

        [ContextMenu("Clear Terrain")]
        void ClearFromMenu() => Clear();
    }
}
