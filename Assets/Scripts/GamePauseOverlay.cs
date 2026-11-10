using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Pauses the active factory round with Escape. The menu uses IMGUI so it
/// works without a Canvas and its two actions are available to both mouse and
/// keyboard users.
/// </summary>
[DisallowMultipleComponent]
public sealed class GamePauseOverlay : MonoBehaviour
{
    public string pausedLabel = "Paused";
    public string continueLabel = "Continue";
    public string restartLabel = "Restart";

    public static bool IsPaused { get; private set; }

    private void Awake()
    {
        // A scene reload must always begin at normal speed, even if it was
        // initiated while the pause screen was visible.
        IsPaused = false;
        Time.timeScale = 1f;
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            if (IsPaused)
            {
                ContinueGame();
            }
            else
            {
                PauseGame();
            }

            return;
        }

        if (IsPaused && keyboard.spaceKey.wasPressedThisFrame)
        {
            RestartCurrentLevel();
        }
    }

    private void OnDestroy()
    {
        // Do not leave the editor or the next scene frozen after a restart.
        if (IsPaused)
        {
            IsPaused = false;
            Time.timeScale = 1f;
        }
    }

    private void PauseGame()
    {
        IsPaused = true;
        Time.timeScale = 0f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    private void ContinueGame()
    {
        IsPaused = false;
        Time.timeScale = 1f;
    }

    private void RestartCurrentLevel()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void OnGUI()
    {
        if (!IsPaused)
        {
            return;
        }

        var previousColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.38f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = previousColor;

        var width = Mathf.Min(360f, Screen.width * 0.55f);
        var centreX = Screen.width * 0.5f;
        var centreY = Screen.height * 0.5f;
        var headlineStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.058f, 30f, 54f)),
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
        };
        var buttonStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.030f, 17f, 28f)),
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(12, 12, 8, 8),
        };

        GUI.Label(new Rect(centreX - width * 0.5f, centreY - 116f, width, 72f), pausedLabel, headlineStyle);
        var continueRect = new Rect(centreX - 135f, centreY - 22f, 270f, 48f);
        var restartRect = new Rect(centreX - 135f, centreY + 42f, 270f, 48f);

        if (GUI.Button(continueRect, continueLabel, buttonStyle))
        {
            ContinueGame();
        }

        if (GUI.Button(restartRect, restartLabel, buttonStyle))
        {
            RestartCurrentLevel();
        }
    }
}
