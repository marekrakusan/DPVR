using UnityEngine;

/// <summary>
/// Displays the first-level objective at the centre of the game view.
/// It deliberately uses Unity's built-in IMGUI, so the scene needs no canvas,
/// fonts, prefabs or manual inspector setup.
/// </summary>
public sealed class LevelIntroOverlay : MonoBehaviour
{
    [Min(0.1f)] public float duration = 3f;
    public string levelLabel = "Level 1";
    public string objectiveLabel = "25/30\nto pass";

    private float startedAt;

    private void Awake()
    {
        startedAt = Time.unscaledTime;
        levelLabel = $"Level {LevelProgression.CurrentLevel}";
        objectiveLabel = $"{LevelProgression.RequiredLoadedPackages}/30\nto pass";
    }

    private void OnGUI()
    {
        if (Time.unscaledTime - startedAt >= duration)
        {
            return;
        }

        var width = Mathf.Min(420f, Screen.width * 0.55f);
        var centreX = Screen.width * 0.5f;
        var centreY = Screen.height * 0.5f;

        // The objective is intentionally shown directly on the scene.  A
        // background panel made the short introduction look like a dialog and
        // obscured the factory behind it, so keep only the text itself.
        var panel = new Rect(centreX - width * 0.5f, centreY - 116f, width, 232f);

        var levelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.061f, 32f, 56f)),
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
        };
        var objectiveStyle = new GUIStyle(levelStyle)
        {
            fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.039f, 22f, 36f)),
            fontStyle = FontStyle.Normal,
        };

        GUI.Label(new Rect(panel.x, panel.y + 28f, panel.width, 70f), levelLabel, levelStyle);
        GUI.Label(new Rect(panel.x, panel.y + 106f, panel.width, 88f), objectiveLabel, objectiveStyle);
    }
}
