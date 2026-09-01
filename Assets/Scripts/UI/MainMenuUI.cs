using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    void Start()
    {
        var start = transform.Find("StartBtn");
        if (start != null) start.GetComponent<Button>().onClick.AddListener(StartGame);
        var quit = transform.Find("QuitBtn");
        if (quit != null) quit.GetComponent<Button>().onClick.AddListener(QuitGame);
    }

    void StartGame()
    {
        SceneManager.LoadScene("MainScene");
    }

    void QuitGame()
    {
        Application.Quit();
    }
}
