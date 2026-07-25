using UnityEngine;
using UnityEngine.SceneManagement;

public class StartButton : MonoBehaviour
{
    public void StartTutorial()
    {
        SceneManager.LoadScene("tutorial");
    }
}