using System.Collections.Generic;
using UnityEngine;

namespace ProceduralTerrain
{
    //
    /// 独立的城镇生成器，可选择性地挂接到 TerrainGenerator（可选）。
    /// 每个聚落只生成一个 prefab 实例。
    /// 
    public class TownGenerator : MonoBehaviour
    {
        [Range(0.02f, 0.4f)] public float slopeLimit = 0.2f;
        [Range(0.03f, 0.3f)] public float exclusionRadius = 0.12f;
        public bool buildStreets = true;

        Vector2 TownCentre;

        readonly List<Vector2> BuildingPositions =
          new List<Vector2>();


        public bool IsNearBuilding(float nx, float nz, float margin)
        {
            for (int i = 0; i < BuildingPositions.Count; i++)
            {
                float dx = BuildingPositions[i].x - nx;
                float dz = BuildingPositions[i].y - nz;
                if (dx * dx + dz * dz < margin * margin) return true;
            }
            return false;
        }

        public void Prepare(TerrainGenerator terrain)
        {
            Vector2 c;
            var sites = terrain.Settlements;
            if (sites != null && sites.Length > 0)
            {
                c = sites[0];
            }
            else
            {
                float slopeScale = terrain.terrainHeight / Mathf.Max(1f, terrain.terrainSize.x);
                var rng = new System.Random(terrain.seed * 43 + 7);
                float best = float.MaxValue;
                c = new Vector2(0.5f, 0.5f);
                for (int i = 0; i < 160; i++)
                {
                    float nx = 0.06f + (float)rng.NextDouble() * 0.88f;
                    float nz = 0.06f + (float)rng.NextDouble() * 0.88f;
                    float h = terrain.SampleHeight(nx, nz);
                    if (h < terrain.waterLevel + 0.02f) continue;
                    float flatRatio = 0f;
                    int cnt = 0;
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            float s2x = nx + dx * 0.012f;
                            float s2z = nz + dz * 0.012f;
                            if (s2x < 0.01f || s2x > 0.99f || s2z < 0.01f || s2z > 0.99f) continue;
                            float h2 = terrain.SampleHeight(s2x, s2z);
                            if (h2 < terrain.waterLevel + 0.02f) continue;
                            if (terrain.SampleSlope(s2x, s2z) * slopeScale <= slopeLimit) flatRatio++;
                            cnt++;
                        }
                    }
                    if (cnt == 0) continue;
                    flatRatio /= cnt;
                    float score = (1f - flatRatio) * 2f + Mathf.Abs(h - 0.48f) * 0.4f;
                    if (score < best) { best = score; c = new Vector2(nx, nz); }
                }
            }
            TownCentre = c;
            if (buildStreets)
            {
                float half = Mathf.Min(0.25f, terrain.SettlementRadius(0) / terrain.terrainSize.x);
                terrain.RegisterRoadLine(Mathf.Max(0f, c.x - half), c.y, Mathf.Min(1f, c.x + half), c.y);
                terrain.RegisterRoadLine(c.x, Mathf.Max(0f, c.y - half), c.x, Mathf.Min(1f, c.y + half));
            }
        }

        public void Place(TerrainGenerator terrain)
        {
            BuildingPositions.Clear();
            SpawnBuildings(terrain);
        }


        public float buildingMinSpacing = 3f;

        private void SpawnBuildings(TerrainGenerator terrain)
        {
            var buildings = Resources.LoadAll<GameObject>("Prefabs/Buildings");
            if (buildings == null || buildings.Length == 0)
            {
                return;
            }
            var data = terrain.TerrainData;
            float sx = data.size.x, sz = data.size.z, sy = data.size.y;
            float tY = terrain.transform.position.y;   // 地形世界偏移
            var sites = terrain.Settlements;
            if (sites == null) return;

            for (int s = 0; s < sites.Length; s++)
            {
                Vector2 c = sites[s];
                float radius = terrain.SettlementRadius(s);
                // 按聚落大小决定建筑数量（半径越大越多）
                int count = Mathf.Clamp(Mathf.RoundToInt(radius * 0.12f), 3, 8);
                var placed = new List<GameObject>();
                for (int n = 0; n < count; n++)
                {
                    var prefab = buildings[Random.Range(0, buildings.Length)];
                    float bRadius = BuildingRadius(prefab);

                    Vector2? spot = null;
                    for (int t = 0; t < 40 && spot == null; t++)
                    {
                        float ang = Random.Range(0f, Mathf.PI * 2f);
                        float dist = radius * Random.Range(0.1f, 0.75f);
                        var cand = new Vector2(c.x + Mathf.Cos(ang) * dist / sx,
                                               c.y + Mathf.Sin(ang) * dist / sz);
                        if (cand.x < 0.01f || cand.x > 0.99f || cand.y < 0.01f || cand.y > 0.99f) continue;
                        float h = terrain.SampleHeight(cand.x, cand.y);
                        if (h < terrain.waterLevel + 0.02f) continue;

                        // 避免叠加：与已放置建筑的中心距 > 两栋半径和 + 缓冲
                        bool overlap = false;
                        for (int p = 0; p < placed.Count; p++)
                        {
                            float pr = BuildingRadius(placed[p]);
                            var diff = new Vector3(cand.x * sx - placed[p].transform.position.x,
                                                   0f,
                                                   cand.y * sz - placed[p].transform.position.z);
                            if (diff.magnitude < pr + bRadius + buildingMinSpacing) { overlap = true; break; }
                        }
                        if (overlap) continue;
                        spot = cand;
                    }
                    if (spot == null) continue;

                    var go = Instantiate(prefab, new Vector3(spot.Value.x * sx,
                                                             terrain.SampleHeight(spot.Value.x, spot.Value.y) * sy + tY,
                                                             spot.Value.y * sz),
                                         Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                    go.name = "Building_" + s + "_" + n;
                    placed.Add(go);
                    // 登记位置，树生成时避开建筑
                    BuildingPositions.Add(spot.Value);
                }
            }
        }

        //建筑占地半径（对角线半长，含 5 倍缩放后的 footprint）；无 BuildingEntity 时用默认 4m。
        private static float BuildingRadius(GameObject building)
        {
            var be = building.GetComponent<BuildingEntity>();
            if (be == null) return 4f;
            return Mathf.Sqrt(be.width * be.width + be.depth * be.depth) * 0.5f;
        }

      
    }
}
