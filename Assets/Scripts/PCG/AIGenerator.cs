using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace EcsFramework
{
    public class AIGenerator : MonoBehaviour
    {

        public enum NpcKind
        {
            Resident,   // 居民：只在聚落附近闲逛
            Explorer,   // 探索者：游荡整个地图
            Driver,     // 司机：开车在城镇间往返
        }
        public int npcPerSettlement = 3;
        public GameObject[] npcPrefabs;   // NPC 预制体列表，生成时随机挑选

        // 居民 / 探索者 比例（居民占 residentRatio，其余为探索者；司机每聚落固定 1 个）
        [Range(0f, 1f)] public float residentRatio = 0.7f;
        public float explorerWanderRadius = 250f;

        // 司机找车范围
        public float driverCarSearchRadius = 60f;

        public float birdSpread = 60f;
        public GameObject[] birdPrefabs;   // 鸟的模型列表，生成时随机挑选

        public int animalsPerType = 8;

        public GameObject[] grasslandAnimal = null;
        public GameObject[] forestAnimal = null;
        public GameObject[] snowAnimal = null;
        public GameObject[] plainsAnimal = null;
        public GameObject[] sandAnimal = null;

        public float animalWanderRadius = 20f;
        public float animalSettleDuration = 4f;

        // 可行走区域掩码：排除 area 1（Not Walkable，湖泊被挖除为不可行走）
        private const int WalkableMask = ~(1 << 1);

        // 车辆生成配置
        public int carsPerSettlement = 2;
        public GameObject[] carPrefabs;   // 车辆模型列表，生成时随机挑选

        private ProceduralTerrain.TerrainGenerator terrain;

        private void Start()
        {
            StartCoroutine(GenerateWhenReady());
        }

        //重新生成全部 AI 生成物（Restart 按钮用）：先销毁旧的，再重新执行生成流程。
        /// 只销毁 AI 实体，保留建筑（建筑由 TerrainGenerator.Generate 重建）。
        public void Regenerate()
        {
            StopAllCoroutines();
            DestroyGenerated(false);
            StartCoroutine(GenerateWhenReady());
        }

        private IEnumerator GenerateWhenReady()
        {
            // 等待 ECS 世界、地形生成器、聚落都就绪，并烘焙好 NavMesh。
            // 确保生成时 World 与聚落一定可用，这样每个物体都能注册为 ECS 实体。
            float timeout = 30f;
            float t = 0f;
            while (t < timeout)
            {
                terrain = FindFirstObjectByType<ProceduralTerrain.TerrainGenerator>();
                if (terrain != null && terrain.TerrainData != null &&
                    terrain.Settlements != null && terrain.Settlements.Length > 0 &&
                    EcsFramework.EcsRunner.World != null &&
                    NavMesh.SamplePosition(Vector3.zero, out _, 400f, NavMesh.AllAreas))
                    break;
                yield return null;
                t += Time.deltaTime;
            }

            if (terrain == null || terrain.TerrainData == null ||
                terrain.Settlements == null || terrain.Settlements.Length == 0 ||
                EcsFramework.EcsRunner.World == null)
            {
                yield break;
            }

            SpawnNpcs();
            SpawnBirds();
            SpawnAnimals();
            SpawnCars();
        }

        // ---------------- 1. 聚落 NPC ----------------

        /// 把世界坐标放到地面：优先 NavMesh 采样（排除湖泊），失败则用地形高度
        /// （WebGL 等 NavMesh 缺失环境的 fallback，保证 NPC/动物/车至少能生成显示）。
        /// 
        private bool PlaceOnGround(Vector3 world, float searchRadius, out Vector3 pos)
        {
            pos = world;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(world, out hit, searchRadius, WalkableMask))
            {
                pos = hit.position;
                return true;
            }
            // fallback：地形高度（排除湖泊）
            var tPos = terrain.transform.position;
            var tSize = terrain.TerrainData.size;
            float nx = (world.x - tPos.x) / tSize.x;
            float nz = (world.z - tPos.z) / tSize.z;
            if (nx < 0.01f || nx > 0.99f || nz < 0.01f || nz > 0.99f) return false;
            if (terrain.GetSurfaceType(nx, nz) == ProceduralTerrain.TerrainGenerator.SurfaceType.Lake) return false;
            float h = terrain.SampleHeight(nx, nz) * terrain.TerrainData.size.y + tPos.y;
            pos = new Vector3(world.x, h, world.z);
            return true;
        }

        //销毁所有运行时生成物：AI 实体（NPC/鸟/动物/车）与聚落建筑。
        /// includeBuildings=false 时保留建筑（Restart 重建前调用用）。
        public void DestroyGenerated(bool includeBuildings = true)
        {
            var all = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                string n = all[i].name;
                if (n.StartsWith("AI_")) Destroy(all[i]);
                else if (includeBuildings && n.StartsWith("Building_")) Destroy(all[i]);
            }
        }

        private void SpawnNpcs()
        {
            var sites = terrain.Settlements;
            if (sites == null) return;

            for (int i = 0; i < sites.Length; i++)
            {
                Vector2 c = sites[i];
                float radius = terrain.SettlementRadius(i);
                float cx = c.x * terrain.TerrainData.size.x;
                float cz = c.y * terrain.TerrainData.size.z;

                for (int n = 0; n < npcPerSettlement; n++)
                {
                    // 在聚落圆盘内随机取点
                    var offset = Random.insideUnitSphere * radius * 0.6f;
                    var world = new Vector3(cx + offset.x, 0f, cz + offset.z);
                    if (!PlaceOnGround(world, 50f, out var hit))
                        continue;

                    // 随机挑选一个 NPC 预制体
                    var prefab = PickRandomPrefab(npcPrefabs);
                    if (prefab == null) continue;

                    var go = Instantiate(prefab, hit, Quaternion.identity);
                    go.name = "AINPC_" + i + "_" + n;
                    var ctrl = CreateHuman(go, hit, 3f);

                    // 每个聚落第一个 NPC 是司机；其余按 residentRatio 分为居民 / 探索者
                    NpcKind kind;
                    if (n == 0)
                        kind = NpcKind.Driver;
                    else
                        kind = Random.value < residentRatio ? NpcKind.Resident : NpcKind.Explorer;

                    ctrl.Init(go, BuildTreeForKind(kind, new Vector3(cx, 0f, cz), radius));
                    ctrl.SetupModelAnimator(go, prefab.name);
                }
            }
        }

        //
        /// 按 NPC 种类构建行为树。新增 NPC 类型时：
        /// 1. 在 NpcKind 枚举加值
        /// 2. 在此 switch 加对应分支
        /// 
        private BTNode BuildTreeForKind(NpcKind kind, Vector3 settlementCenter, float settlementRadius)
        {
            switch (kind)
            {
                case NpcKind.Explorer:
                    return BuildExplorerTree();
                case NpcKind.Driver:
                    return BuildDriverTree();
                case NpcKind.Resident:
                default:
                    return BuildNpcTree(settlementCenter, settlementRadius);
            }
        }

        //
        /// 探索者 NPC 行为树：游荡整个地图（无聚落约束，半径大）。
        /// 
        private BTNode BuildExplorerTree()
        {
            return new Repeater(new Sequence(
                new Wander
                {
                    Radius = explorerWanderRadius,
                    Loop = false,
                    Name = "explore",
                    MinTargetDistance = 40f,
                },
                new Selector(
                    new Chat
                    {
                        SearchRadius = 8f,
                        TalkDuration = 5f,
                        Cooldown = 10f,
                        Name = "chat",
                    },
                    new Idle { Duration = animalSettleDuration, Name = "rest" }
                )
            ), 0)
            { Name = "explorer-behaviour" };
        }

        //
        /// 司机 NPC 行为树：上闲置车辆并在各聚落（城镇）之间往返。
        /// 被玩家选中下发指令时会被命令系统打断。
        /// 
        private BTNode BuildDriverTree()
        {
            var sites = terrain.Settlements;
            var towns = new Vector3[sites != null ? sites.Length : 0];
            float sizeX = terrain.TerrainData.size.x;
            float sizeZ = terrain.TerrainData.size.z;
            float terrainY = terrain.transform.position.y;
            for (int s = 0; s < towns.Length; s++)
            {
                Vector3 world = new Vector3(sites[s].x * sizeX, terrainY, sites[s].y * sizeZ);
                // 采样到 NavMesh 上（y 取地面高度），确保车能 SetDestination 到该点
                if (NavMesh.SamplePosition(world, out var hit, 200f, WalkableMask))
                    towns[s] = hit.position;
                else
                    towns[s] = world;
            }
            return new Repeater(new TaxiRound
            {
                Towns = towns,
                CarSearchRadius = driverCarSearchRadius,
                StayDuration = 3f,
                Name = "taxi-round",
            }, 0)
            { Name = "driver-behaviour" };
        }

        //人类 NPC 行为树：聚落内闲逛 + 待机交替。
        private BTNode BuildNpcTree(Vector3 settlementCenter, float settlementRadius)
        {
            var wander = new Wander
            {
                Radius = animalWanderRadius,
                Loop = false,
                Name = "walk",
                // 约束闲逛范围在聚落圆盘内（缩小一点避免边缘），NPC 不会离开聚落
                ConstrainCenter = settlementCenter,
                ConstrainRadius = settlementRadius * 0.7f,
                // 目标点至少离当前位置这么远，避免"走两步就停"造成 idle/walk 抖动
                MinTargetDistance = 10f,
            };
            return new Repeater(new Sequence(
                wander,
                new Selector(
                    new Chat
                    {
                        SearchRadius = 8f,
                        TalkDuration = 5f,
                        Cooldown = 10f,
                        Name = "chat",
                    },
                    new Idle { Duration = animalSettleDuration, Name = "idle" }
                )
            ), 0)
            { Name = "npc-behaviour" };
        }

        private void SpawnBirds()
        {
            var sites = terrain.Settlements;
            if (sites == null || sites.Length == 0) return;

            for (int i = 0; i < animalsPerType; i++)
            {
                // 在随机聚落上方生成鸟
                Vector2 c = sites[Random.Range(0, sites.Length)];
                float cx = c.x * terrain.TerrainData.size.x;
                float cz = c.y * terrain.TerrainData.size.z;
                var spread = Random.insideUnitSphere * birdSpread;
                float altitude = 15f + Random.Range(0f, 10f);
                float groundY = terrain.TerrainData.size.y * 0.5f;

                // 随机挑选一个鸟模型；没有配置则用占位 Cube
                GameObject go;
                var birdPrefab = PickRandomPrefab(birdPrefabs);
                if (birdPrefab != null)
                {
                    go = Instantiate(birdPrefab);
                    go.transform.localScale = Vector3.one * 0.8f;
                }
                else
                {
                    go = CreateAgentBody(PrimitiveType.Cube, 0.6f);
                }
                go.name = "AI_Bird_" + i;

                // 鸟用天空模式（AnimalEntity Bird 类型），NavMeshAgent 会被 FlyMove 禁用
                var agent = go.AddComponent<NavMeshAgent>();
                agent.enabled = false;
                go.transform.position = new Vector3(cx + spread.x, groundY, cz + spread.z);

                var entity = go.AddComponent<AnimalEntity>();
                entity.kind = AnimalType.Bird;
                entity.birdSkyMode = true;
                entity.birdAltitude = altitude;
                // AddComponent 时 Awake 已用默认 kind 构建过一次树，这里按 Bird 配置重建
                entity.RebuildTree();
                // 加载鸟的 AnimatorController（否则飞行时无扑翼动画）
                if (birdPrefab != null)
                    entity.GetAIController().SetupModelAnimator(go, birdPrefab.name);
            }
        }

        // ---------------- 3. 按地形生成动物 ----------------

        private void SpawnAnimals()
        {
            SpawnAnimalsForType(ProceduralTerrain.TerrainGenerator.SurfaceType.Grassland, grasslandAnimal);
            SpawnAnimalsForType(ProceduralTerrain.TerrainGenerator.SurfaceType.Forest, forestAnimal);
            SpawnAnimalsForType(ProceduralTerrain.TerrainGenerator.SurfaceType.Snow, snowAnimal);
            SpawnAnimalsForType(ProceduralTerrain.TerrainGenerator.SurfaceType.Plains, plainsAnimal);
            SpawnAnimalsForType(ProceduralTerrain.TerrainGenerator.SurfaceType.Sand, sandAnimal);
        }

        private void SpawnAnimalsForType(ProceduralTerrain.TerrainGenerator.SurfaceType type, GameObject[] prefabs)
        {
            if (prefabs == null || prefabs.Length == 0) return;

            int placed = 0;
            int attempts = animalsPerType * 60;
            var tSize = terrain.TerrainData.size;
            Vector3 tPos = terrain.transform.position;   // 地形世界偏移

            for (int i = 0; i < attempts && placed < animalsPerType; i++)
            {
                float nx = Random.value;
                float nz = Random.value;
                if (terrain.GetSurfaceType(nx, nz) != type) continue;
                if (terrain.IsInsideSettlement(nx, nz)) continue;   // 避开聚落

                // 世界坐标（含地形偏移），确保 NavMesh.SamplePosition 正确命中
                var world = new Vector3(nx * tSize.x + tPos.x, 0f, nz * tSize.z + tPos.z);
                if (!PlaceOnGround(world, 100f, out var hit))
                    continue;

                // 随机挑选该地形的一个动物模型
                var prefab = PickRandomPrefab(prefabs);
                if (prefab == null) continue;

                var go = Instantiate(prefab, hit, Quaternion.identity);
                go.name = "AI_Animal_" + type + "_" + placed;
                var ctrl = CreateAnimal(go, hit, 2f);
                ctrl.SetupModelAnimator(go, prefab.name);
                ctrl.Init(go, BuildAnimalTree(type));

                placed++;
            }
        }

        //
        /// 地面动物行为树：按地形类型决定与人的互动方式。
        ///  - 森林（Forest）：遇到人逃跑
        ///  - 草地（Grassland）/ 高原（Plains）：遇到人跟随（可互动）
        ///  - 其他：默认漫游 + 吃草
        /// 
        private BTNode BuildAnimalTree(ProceduralTerrain.TerrainGenerator.SurfaceType type)
        {
            var roam = new Sequence(
                new Wander { Radius = animalWanderRadius, Loop = false, Name = "roam" },
                new Graze { Duration = animalSettleDuration, Name = "graze" }
            );

            switch (type)
            {
                case ProceduralTerrain.TerrainGenerator.SurfaceType.Forest:
                    // 森林动物：遇人逃跑，否则正常漫游
                    return new Repeater(new Selector(
                        new FleePerson { DetectRadius = 40f, Name = "flee" },
                        roam
                    ), 0)
                    { Name = "forest-animal" };

                case ProceduralTerrain.TerrainGenerator.SurfaceType.Grassland:
                case ProceduralTerrain.TerrainGenerator.SurfaceType.Plains:
                    // 草地 / 高原动物：遇人跟随（互动），否则正常漫游
                    return new Repeater(new Selector(
                        new FollowPerson { DetectRadius = 40f, FollowDistance = 4f, Name = "follow" },
                        roam
                    ), 0)
                    { Name = "plains-animal" };

                default:
                    // 雪地 / 沙地：默认漫游 + 吃草
                    return new Repeater(roam, 0) { Name = "animal-behaviour" };
            }
        }

        // ---------------- 4. 车辆 ----------------

        //在聚落旁生成车辆（可被 NPC 互动上车）。
        private void SpawnCars()
        {
            if (carPrefabs == null || carPrefabs.Length == 0) return;
            var sites = terrain.Settlements;
            if (sites == null || sites.Length == 0) return;

            float sizeX = terrain.TerrainData.size.x;
            float sizeZ = terrain.TerrainData.size.z;
            Vector3 tPos = terrain.transform.position;   // 地形世界偏移
            for (int s = 0; s < sites.Length; s++)
            {
                Vector2 c = sites[s];
                float radius = terrain.SettlementRadius(s);
                float cx = c.x * sizeX + tPos.x;
                float cz = c.y * sizeZ + tPos.z;

                // 每聚落 1~2 辆车（受 carsPerSettlement 上限约束）
                int carCount = Random.Range(1, Mathf.Max(2, Mathf.Min(carsPerSettlement, 2)) + 1);
                for (int n = 0; n < carCount; n++)
                {
                    var prefab = PickRandomPrefab(carPrefabs);
                    if (prefab == null) continue;

                    // 尝试在聚落内靠近道路的位置停车（车停在路边，靠近聚落中心）
                    Vector3? spot = null;
                    for (int t = 0; t < 12; t++)
                    {
                        float ang = Random.Range(0f, Mathf.PI * 2f);
                        float dist = radius * Random.Range(0.3f, 0.8f);
                        var world = new Vector3(cx + Mathf.Cos(ang) * dist, 0f, cz + Mathf.Sin(ang) * dist);
                        if (!PlaceOnGround(world, 50f, out var hit))
                            continue;
                        // 靠近道路（<35m）才停车，否则继续找
                        float nx = hit.x / sizeX;
                        float nz = hit.z / sizeZ;
                        if (terrain.DistanceToNearestRoad(nx, nz) < 0.07f)
                        {
                            spot = hit;
                            break;
                        }
                    }
                    // 找不到道路旁位置，退回聚落内任意点
                    if (spot == null)
                    {
                        float ang = Random.Range(0f, Mathf.PI * 2f);
                        float dist = radius * Random.Range(0.3f, 0.8f);
                        var world = new Vector3(cx + Mathf.Cos(ang) * dist, 0f, cz + Mathf.Sin(ang) * dist);
                        if (PlaceOnGround(world, 50f, out var fallback))
                            spot = fallback;
                    }
                    if (spot == null) continue;

                    var go = Instantiate(prefab, spot.Value, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                    go.name = "AI_Car_" + s + "_" + n;
                    CreateCar(go, spot.Value);
                }
            }
        }

        //给车辆挂 NavMeshAgent、CarEntity、CarComponent 并注册进 ECS。
        /// 车的导航排除森林（车不开进森林），只走道路/平原等区域。
        /// 车模型整体缩放（模型单位较大），缩小后导航顺畅、避让准确。
        private void CreateCar(GameObject go, Vector3 pos)
        {
            // 车模型以较大单位导入，统一缩放（默认 0.5，约 10m 长）
            // 车模型以较大单位导入，统一缩放（0.35，约 7m 长），缩小后导航顺畅、避让准确
            const float carScale = 0.35f;
            go.transform.localScale = Vector3.one * carScale;

            // 计算缩放后车身包围盒（本地坐标），用于 NavMeshAgent 半径和碰撞体
            Bounds bounds = CalcLocalBounds(go);
            float carHalfWidth = Mathf.Max(1f, bounds.size.x * 0.5f);
            float carHeight = Mathf.Max(1f, bounds.size.y);

            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = Mathf.Clamp(carHalfWidth, 1f, 8f);   // 匹配车宽（半宽），避让留出真实空间
            agent.height = carHeight;
            agent.speed = 30f;
            agent.acceleration = 80f;   // 无惯性：起步/停车响应干脆
            // 车避开森林：areaMask 排除 Forest 区域（保留 Walkable/Road 等）
            int forestIdx = NavMesh.GetAreaFromName("Forest");
            if (forestIdx >= 0)
                agent.areaMask &= ~(1 << forestIdx);
            // 车不能进水：排除 Not Walkable（area 1，湖泊被挖除后的区域）
            agent.areaMask &= ~(1 << 1);
            agent.Warp(pos);
            go.transform.position = pos;

            // 碰撞体用于玩家射线选中（RTS 相机右键点车）。
            // 按模型实际包围盒生成，贴合车身
            if (go.GetComponent<Collider>() == null)
            {
                var col = go.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.center = bounds.center;
                col.size = bounds.size + new Vector3(1f, 0.5f, 1f);   // 略放宽，便于点击
            }

            var entity = go.AddComponent<CarEntity>();
            entity.BindToWorld(EcsRunner.World);
        }

        //计算模型所有 Renderer 在本地坐标（相对根）的包围盒。
        private static Bounds CalcLocalBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                // 世界 bounds 转本地
                Bounds wb = r.bounds;
                Vector3 min = root.transform.InverseTransformPoint(wb.min);
                Vector3 max = root.transform.InverseTransformPoint(wb.max);
                if (first) { b = new Bounds((min + max) * 0.5f, max - min); first = false; }
                else b.Encapsulate(new Bounds((min + max) * 0.5f, max - min));
            }
            return b;
        }

        // ---------------- 工具 ----------------

        //从数组中随机挑选一个有效的预制体；数组为空或全空时返回 null。
        private static GameObject PickRandomPrefab(GameObject[] presets)
        {
            if (presets == null || presets.Length == 0) return null;
            var valid = new List<GameObject>();
            for (int i = 0; i < presets.Length; i++)
                if (presets[i] != null) valid.Add(presets[i]);
            if (valid.Count == 0) return null;
            return valid[Random.Range(0, valid.Count)];
        }

        //创建一个简单的占位物体（Primitive），用于鸟等不需要真实模型的情况。
        private GameObject CreateAgentBody(PrimitiveType type, float scale)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        private AIComponent CreateHuman(GameObject go, Vector3 pos, float speed)
        {
            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f;
            agent.height = 1.6f;
            agent.Warp(pos);
            go.transform.position = pos;

            // 碰撞体用于玩家射线选中（RTS 相机左键选 NPC）
            if (go.GetComponent<Collider>() == null)
            {
                var col = go.AddComponent<CapsuleCollider>();
                col.isTrigger = true;
                col.radius = 0.45f;
                col.height = 1.8f;
                col.center = new Vector3(0f, 0.9f, 0f);
            }

            var e = go.AddComponent<HumanEntity>();
            e.BindToWorld(EcsRunner.World);
            var ctrl = e.GetAIController();
            ctrl.walkSpeed = speed;
            return ctrl;
        }
        private AIComponent CreateAnimal(GameObject go, Vector3 pos, float speed)
        {
            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f;
            agent.height = 1.6f;
            agent.Warp(pos);
            go.transform.position = pos;

            // 碰撞体用于玩家射线选中（RTS 相机左键选动物）
            if (go.GetComponent<Collider>() == null)
            {
                var col = go.AddComponent<CapsuleCollider>();
                col.isTrigger = true;
                col.radius = 0.45f;
                col.height = 1.6f;
                col.center = new Vector3(0f, 0.8f, 0f);
            }

            var e = go.AddComponent<AnimalEntity>();
            e.BindToWorld(EcsRunner.World);
            var ctrl = e.GetAIController();
            ctrl.walkSpeed = speed;
            return ctrl;
        }

    }
}
