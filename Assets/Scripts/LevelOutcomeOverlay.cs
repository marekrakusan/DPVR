using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Displays the outcome only after the delivery truck has departed. Each
/// result is paired with the relevant Space-key action without adding a
/// canvas or a background panel to the scene.
/// </summary>
[DisallowMultipleComponent]
public sealed class LevelOutcomeOverlay : MonoBehaviour
{
    [Min(1)] public int requiredLoadedPackages = 25;
    [Min(0.1f)] public float nextLevelPromptDelay = 2f;
    public string completedLabel = "Level completed";
    public string nextLevelLabel = "NEXT LEVEL";
    public string failedLabel = "You failed!";
    public string restartLabel = "RESTART";

    private enum OutcomeState
    {
        Hidden,
        Completed,
        NextLevel,
        Failed,
        FinalRestart
    }

    private OutcomeState state;
    private float stateStartedAt;

    private void Awake()
    {
        // The scene is reused for both rounds. Its success requirement is
        // selected before the truck evaluates the number of loaded parcels.
        requiredLoadedPackages = LevelProgression.RequiredLoadedPackages;
    }

    public void ShowResult(bool completed)
    {
        state = completed ? OutcomeState.Completed : OutcomeState.Failed;
        stateStartedAt = Time.time;
    }

    private void Update()
    {
        // A pause menu owns Space while it is open, so a paused result cannot
        // accidentally advance or restart the level underneath the menu.
        if (GamePauseOverlay.IsPaused)
        {
            return;
        }

        if (state == OutcomeState.Completed &&
            Time.time - stateStartedAt >= nextLevelPromptDelay)
        {
            state = LevelProgression.CanAdvance
                ? OutcomeState.NextLevel
                : OutcomeState.FinalRestart;
            stateStartedAt = Time.time;
        }

        if (state == OutcomeState.NextLevel && Keyboard.current != null &&
            Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            AdvanceToNextLevel();
        }

        if (state == OutcomeState.Failed && Keyboard.current != null &&
            Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            RestartCurrentLevel();
        }

        if (state == OutcomeState.FinalRestart && Keyboard.current != null &&
            Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            RestartCampaign();
        }
    }

    private static void AdvanceToNextLevel()
    {
        LevelProgression.Advance();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private static void RestartCurrentLevel()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private static void RestartCampaign()
    {
        LevelProgression.RestartCampaign();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void OnGUI()
    {
        // Avoid layering a level result underneath the pause menu. The
        // result state remains intact and returns as soon as the user resumes.
        if (state == OutcomeState.Hidden || GamePauseOverlay.IsPaused)
        {
            return;
        }

        var width = Mathf.Min(540f, Screen.width * 0.70f);
        var centreX = Screen.width * 0.5f;
        var centreY = Screen.height * 0.5f;
        var headlineStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.058f, 30f, 54f)),
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
        };
        var restartStyle = new GUIStyle(headlineStyle)
        {
            fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.031f, 17f, 28f)),
            fontStyle = FontStyle.Normal,
        };

        var buttonStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = restartStyle.fontSize,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(12, 12, 8, 8),
        };

        var headlineRect = new Rect(centreX - width * 0.5f, centreY - 44f, width, 88f);
        var actionRect = new Rect(centreX - 135f, headlineRect.y + 86f, 270f, 48f);
        switch (state)
        {
            case OutcomeState.Completed:
                GUI.Label(headlineRect, completedLabel, headlineStyle);
                break;
            case OutcomeState.NextLevel:
                GUI.Label(headlineRect, completedLabel, headlineStyle);
                if (GUI.Button(actionRect, nextLevelLabel, buttonStyle))
                {
                    AdvanceToNextLevel();
                }
                break;
            case OutcomeState.Failed:
                GUI.Label(headlineRect, failedLabel, headlineStyle);
                if (GUI.Button(actionRect, restartLabel, buttonStyle))
                {
                    RestartCurrentLevel();
                }
                break;
            case OutcomeState.FinalRestart:
                GUI.Label(headlineRect, completedLabel, headlineStyle);
                if (GUI.Button(actionRect, restartLabel, buttonStyle))
                {
                    RestartCampaign();
                }
                break;
        }
    }
}
