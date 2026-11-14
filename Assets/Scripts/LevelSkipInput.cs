using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Lets a tester jump directly to a round by typing skip2, skip3, skip4 or
/// skip5, or choose a forklift finish with col2 through col5, while the game
/// view has keyboard focus. No visible input field is created, so ordinary
/// play remains unchanged.
/// </summary>
[DisallowMultipleComponent]
public sealed class LevelSkipInput : MonoBehaviour
{
    private const int MaximumBufferedCharacters = 8;

    private Keyboard subscribedKeyboard;
    private string typedCharacters = string.Empty;

    private void OnEnable()
    {
        RefreshKeyboardSubscription();
    }

    private void Update()
    {
        RefreshKeyboardSubscription();
    }

    private void OnDisable()
    {
        if (subscribedKeyboard == null)
        {
            return;
        }

        subscribedKeyboard.onTextInput -= OnTextInput;
        subscribedKeyboard = null;
        typedCharacters = string.Empty;
    }

    private void RefreshKeyboardSubscription()
    {
        var currentKeyboard = Keyboard.current;
        if (currentKeyboard == subscribedKeyboard)
        {
            return;
        }

        if (subscribedKeyboard != null)
        {
            subscribedKeyboard.onTextInput -= OnTextInput;
        }

        subscribedKeyboard = currentKeyboard;
        if (subscribedKeyboard != null)
        {
            subscribedKeyboard.onTextInput += OnTextInput;
        }
    }

    private void OnTextInput(char character)
    {
        if (char.IsControl(character))
        {
            return;
        }

        var normalized = char.ToLowerInvariant(character);
        if (!char.IsLetterOrDigit(normalized) && normalized != ' ')
        {
            typedCharacters = string.Empty;
            return;
        }

        typedCharacters += normalized;
        if (typedCharacters.Length > MaximumBufferedCharacters)
        {
            typedCharacters = typedCharacters.Substring(
                typedCharacters.Length - MaximumBufferedCharacters,
                MaximumBufferedCharacters);
        }

        if (typedCharacters.EndsWith("skip2", StringComparison.Ordinal))
        {
            SkipToLevel(2);
        }
        else if (typedCharacters.EndsWith("skip3", StringComparison.Ordinal))
        {
            SkipToLevel(3);
        }
        else if (typedCharacters.EndsWith("skip4", StringComparison.Ordinal))
        {
            SkipToLevel(4);
        }
        else if (typedCharacters.EndsWith("skip5", StringComparison.Ordinal) ||
                 typedCharacters.EndsWith("skip 5", StringComparison.Ordinal))
        {
            SkipToLevel(5);
        }
        else if (typedCharacters.EndsWith("col2", StringComparison.Ordinal))
        {
            SetForkliftAppearance(1);
        }
        else if (typedCharacters.EndsWith("col3", StringComparison.Ordinal))
        {
            SetForkliftAppearance(2);
        }
        else if (typedCharacters.EndsWith("col4", StringComparison.Ordinal))
        {
            SetForkliftAppearance(3);
        }
        else if (typedCharacters.EndsWith("col5", StringComparison.Ordinal))
        {
            SetForkliftAppearance(4);
        }
    }

    private void SkipToLevel(int level)
    {
        if (!LevelProgression.TrySelectLevel(level))
        {
            return;
        }

        typedCharacters = string.Empty;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void SetForkliftAppearance(int tier)
    {
        if (!LevelProgression.TrySetForkliftAppearanceTier(tier))
        {
            return;
        }

        var loadArea = UnityEngine.Object.FindAnyObjectByType<ForkliftLoadArea>();
        if (loadArea != null)
        {
            loadArea.RefreshRewardAppearance();
        }

        typedCharacters = string.Empty;
    }
}
