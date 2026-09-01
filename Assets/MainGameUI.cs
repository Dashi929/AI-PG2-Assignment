using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainGameUI : MonoBehaviour
{
    public RectTransform content;
    public GameObject parameterPrefab;

    void Start()
    {
        var quit = transform.Find("QuitBtn");
        if (quit != null) quit.GetComponent<Button>().onClick.AddListener(BackToMenu);
        var restart = transform.Find("Restart");
        if (restart != null) restart.GetComponent<Button>().onClick.AddListener(RestartWorld);
        if (content == null)
        {
            var c = transform.Find("parameters/View/content");
            if (c != null) content = c as RectTransform;
        }
        if (parameterPrefab == null)
            parameterPrefab = Resources.Load<GameObject>("UIPrefab/Parameter");
        if (content == null || parameterPrefab == null) return;

        var names = UIUtil.ParamNames;
        for (int i = 0; i < names.Length; i++)
        {
            var go = Instantiate(parameterPrefab, content);
            go.name = "Param_" + names[i];
            var label = go.GetComponentInChildren<Text>();
            if (label != null) label.text = names[i];
            var input = go.GetComponentInChildren<InputField>();
            if (input != null)
            {
                input.text = UIUtil.GetParam(names[i]);
                string paramName = names[i];
                input.onValueChanged.AddListener((string v) => UIUtil.SetParam(paramName, v));
            }
        }
        content.sizeDelta = new Vector2(content.sizeDelta.x, 50 * names.Length);
    }

    void BackToMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }

    void RestartWorld()
    {
        var tg = FindFirstObjectByType<ProceduralTerrain.TerrainGenerator>();
        var ai = FindFirstObjectByType<EcsFramework.AIGenerator>();
        if (ai != null) ai.DestroyGenerated();
        if (tg != null) tg.Generate();
        if (ai != null) ai.Regenerate();
    }

}
