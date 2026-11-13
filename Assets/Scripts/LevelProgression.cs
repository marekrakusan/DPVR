using UnityEngine;

/// <summary>
/// Keeps the current round while FactoryYard reloads itself. A fresh Play
/// session starts at Level 1 and successful rounds advance through Level 5.
/// </summary>
public static class LevelProgression
{
    private const int MaximumLevel = 5;
    private const float FirstLevelSpeedMultiplier = 1.2f;
    private const float FirstLevelDelayMultiplier = 0.8f;
    private const float PerLevelSpeedMultiplier = 1.25f;
    private const float PerLevelDelayMultiplier = 0.75f;
    private static int currentLevel = 1;
    private static bool forkliftRewardUnlocked;

    public static int CurrentLevel => currentLevel;
    public static bool CanAdvance => currentLevel < MaximumLevel;
    public static int RequiredLoadedPackages => Mathf.Min(30, 24 + currentLevel);
    public static bool HasFullLoadForkliftReward => forkliftRewardUnlocked;

    // Level 1 adopts the former Level-2 tempo. Each following level becomes
    // another 25% faster, while its fall-delay window becomes 25% shorter.
    public static float ForkliftSpeedMultiplier =>
        FirstLevelSpeedMultiplier * Mathf.Pow(PerLevelSpeedMultiplier, currentLevel - 1);
    public static float ConveyorSpeedMultiplier => ForkliftSpeedMultiplier;
    public static float FallDelayMultiplier =>
        FirstLevelDelayMultiplier * Mathf.Pow(PerLevelDelayMultiplier, currentLevel - 1);

    public static void Advance()
    {
        currentLevel = Mathf.Min(MaximumLevel, currentLevel + 1);
    }

    /// <summary>
    /// Keeps the forklift's full-load appearance for later levels in this
    /// play session. It resets only for a new campaign or a new Play session.
    /// </summary>
    public static void UnlockFullLoadForkliftReward()
    {
        forkliftRewardUnlocked = true;
    }

    public static void RestartCampaign()
    {
        currentLevel = 1;
        forkliftRewardUnlocked = false;
    }

    // Runs once when a new Play session starts, including projects configured
    // to keep static data between editor Play sessions. It deliberately does
    // not run on SceneManager.LoadScene, so the next level survives the scene reset.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForNewPlaySession()
    {
        currentLevel = 1;
        forkliftRewardUnlocked = false;
    }
}
