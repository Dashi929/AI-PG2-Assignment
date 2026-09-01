using System.Globalization;
using EcsFramework;
using UnityEngine;
public static class UIUtil
{
    private static ProceduralTerrain.TerrainGenerator terrainGen;
    private static ProceduralTerrain.TownGenerator townGen;
    private static EcsFramework.AIGenerator aiGen;

    private static void Resolve()
    {
        if (terrainGen == null) terrainGen = Object.FindFirstObjectByType<ProceduralTerrain.TerrainGenerator>();
        if (townGen == null) townGen = Object.FindFirstObjectByType<ProceduralTerrain.TownGenerator>();
        if (aiGen == null) aiGen = Object.FindFirstObjectByType<EcsFramework.AIGenerator>();
    }

    public static string[] ParamNames => new string[]
    {
       "timeMultiplier","seed", "terrainSizeX","terrainSizeY", "lakeRatio", "plainRatio", "grassRatio", "forestRatio","snowRatio","settlementCount","npcPerSettlement",  "carsPerSettlement", "animalsPerType",
    };
    public static string GetParam(string name)
    {
        Resolve();
        var sys = EcsRunner.World.GetSystem<DayNightCycleSystem>();
        if (sys != null)
        {
            var dayNightSys = sys as DayNightCycleSystem;
            if (name == "timeMultiplier") return dayNightSys.config.timeMultiplier.ToString();
        }
        if (terrainGen != null)
        {
            switch (name)
            {
                case "seed": return terrainGen.seed.ToString();
                case "terrainSizeX": return terrainGen.terrainSize.x.ToString();
                case "terrainSizeY": return terrainGen.terrainSize.y.ToString();
                case "lakeRatio": return terrainGen.lakeRatio.ToString();
                case "plainRatio": return terrainGen.plainRatio.ToString();
                case "grassRatio": return terrainGen.grassRatio.ToString();
                case "forestRatio": return terrainGen.forestRatio.ToString();
                case "snowRatio": return terrainGen.snowRatio.ToString();
                case "settlementCount": return terrainGen.settlementCount.ToString();
            }
        }
        if (aiGen != null)
        {
            switch (name)
            {
                case "npcPerSettlement": return aiGen.npcPerSettlement.ToString();
                case "carsPerSettlement": return aiGen.carsPerSettlement.ToString();
                case "animalsPerType": return aiGen.animalsPerType.ToString();
            }
        }
        return null;
    }

    public static void SetParam(string name, string value)
    {
        Resolve();
        float f;
        int i;
        int.TryParse(value, out i);
        float.TryParse(value, out f);

        var sys = EcsRunner.World.GetSystem<DayNightCycleSystem>();
        if (sys != null)
        {
            if (name == "timeMultiplier")
            {
                var dayNightSys = sys as DayNightCycleSystem;
                dayNightSys.config.timeMultiplier = f;
                return;

            }

        }
        if (terrainGen != null)
        {
            switch (name)
            {
                case "seed":
                    terrainGen.seed = i;
                    break;
                case "terrainSizeX":
                    terrainGen.terrainSize.x = i;
                    break;
                case "terrainSizeY":
                    terrainGen.terrainSize.y = i;
                    break;
                case "lakeRatio":
                    terrainGen.lakeRatio = f;
                    break;
                case "plainRatio":
                    terrainGen.plainRatio = f;
                    break;
                case "grassRatio":
                    terrainGen.grassRatio = f;
                    break;
                case "forestRatio":
                    terrainGen.forestRatio = f;
                    break;
                case "snowRatio":
                    terrainGen.snowRatio = f;
                    break;
                case "settlementCount":
                    terrainGen.settlementCount = i;
                    break;

            }
        }
        if (aiGen != null)
        {
            switch (name)
            {
                case "npcPerSettlement":
                    aiGen.npcPerSettlement = i;
                    break;
                case "carsPerSettlement":
                    aiGen.carsPerSettlement = i;
                    break;
                case "animalsPerType":
                    aiGen.animalsPerType = i;
                    break;

            }
        }
        if (townGen != null)
        {
            switch (name)
            {
                case "slopeLimit":
                    townGen.slopeLimit = f;
                    break;

                case "buildingMinSpacing":
                    townGen.buildingMinSpacing = f;
                    break;
            }
        }
    }
}
