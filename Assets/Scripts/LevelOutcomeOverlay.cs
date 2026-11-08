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
        stateStartedAt = Time.unscaledTime;
    }

    private void Update()
    {
        if (state == OutcomeState.Completed &&
            Time.unscaledTime - stateStartedAt >= nextLevelPromptDelay)
        {
            state = LevelProgression.CanAdvance
                ? OutcomeState.NextLevel
                : OutcomeState.FinalRestart;
            stateStartedAt = Time.unscaledTime;
        }

        if (state == OutcomeState.NextLevel && Keyboard.current != null &&
            Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            LevelProgression.Advance();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        if (state == OutcomeState.Failed && Keyboard.current != null &&
            Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        if (state == OutcomeState.FinalRestart && Keyboard.current != null &&
            Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            LevelProgression.RestartCampaign();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    private void OnGUI()
    {
        if (state == OutcomeState.Hidden)
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

        var headlineRect = new Rect(centreX - width * 0.5f, centreY - 44f, width, 88f);
        switch (state)
        {
            case OutcomeState.Completed:
                GUI.Label(headlineRect, completedLabel, headlineStyle);
                break;
            case OutcomeState.NextLevel:
                GUI.Label(new Rect(headlineRect.x, headlineRect.y - 26f, headlineRect.width, 78f), completedLabel, headlineStyle);
                GUI.Label(new Rect(headlineRect.x, headlineRect.y + 50f, headlineRect.width, 42f), nextLevelLabel, restartStyle);
                break;
            case OutcomeState.Failed:
                GUI.Label(new Rect(headlineRect.x, headlineRect.y - 26f, headlineRect.width, 78f), failedLabel, headlineStyle);
                GUI.Label(new Rect(headlineRect.x, headlineRect.y + 50f, headlineRect.width, 42f), restartLabel, restartStyle);
                break;
            case OutcomeState.FinalRestart:
                GUI.Label(new Rect(headlineRect.x, headlineRect.y - 26f, headlineRect.width, 78f), completedLabel, headlineStyle);
                GUI.Label(new Rect(headlineRect.x, headlineRect.y + 50f, headlineRect.width, 42f), restartLabel, restartStyle);
                break;
        }
    }
}
