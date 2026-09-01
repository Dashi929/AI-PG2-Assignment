using UnityEditor;
using UnityEngine;
using ProceduralTerrain;

namespace ProceduralTerrain.EditorTools
{
     //
    /// TerrainGenerator 的 Inspector 扩展：在参数下方添加 Generate/Clear 按钮。
    /// 
    [CustomEditor(typeof(TerrainGenerator))]
    public class TerrainGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var gen = (TerrainGenerator)target;

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate Terrain", GUILayout.Height(30f)))
                gen.Generate();
            if (GUILayout.Button("Clear Terrain", GUILayout.Height(30f)))
                gen.Clear();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Tweak parameters then click Generate Terrain to regenerate (same seed = same result).\n" +
                "Check generateOnStart to auto-generate when the scene runs.",
                MessageType.Info);
        }
    }
}
