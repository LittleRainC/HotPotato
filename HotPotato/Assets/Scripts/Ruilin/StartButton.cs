using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>兼容旧按钮绑定；新菜单请用 Ruilin.StartMenuController。</summary>
public class StartButton : MonoBehaviour
{
    public void StartTutorial()
    {
        SceneManager.LoadScene("Level1");
    }
}
