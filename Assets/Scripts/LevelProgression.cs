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
    // This count belongs to the whole campaign rather than one scene. A
    // fully loaded forklift earns a new appearance only after its parcels
    // have actually been transferred into the truck.
    private static int fullLoadDeliveryCount;
    // The colour codes are a temporary in-game visual choice. They take
    // priority over the earned tier and survive scene reloads, including a
    // campaign restart, until the current Play session ends.
    private static int forkliftAppearanceOverride = -1;

    public static int CurrentLevel => currentLevel;
    public static bool CanAdvance => currentLevel < MaximumLevel;
    public static int RequiredLoadedPackages => Mathf.Min(30, 24 + currentLevel);
    public static int ForkliftAppearanceTier =>
        forkliftAppearanceOverride >= 1 ? forkliftAppearanceOverride : fullLoadDeliveryCount;
    public static bool HasFullLoadForkliftReward => ForkliftAppearanceTier > 0;
    public static int FullLoadDeliveryCount => fullLoadDeliveryCount;

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
    /// Selects a particular round without resetting the campaign rewards.
    /// It is used by the in-game skip codes for quick level checks.
    /// </summary>
    public static bool TrySelectLevel(int level)
    {
        if (level < 1 || level > MaximumLevel)
        {
            return false;
        }

        currentLevel = level;
        return true;
    }

    /// <summary>
    /// Records one complete 30-parcel delivery. The appearance thresholds are
    /// evaluated by the forklift: green at one delivery, silver at two, gold
    /// at three, and an animated rainbow from four onward.
    /// </summary>
    public static void RecordFullLoadDelivery()
    {
        // Four is the final visual tier. Keeping the counter capped makes the
        // value stable even if a future campaign contains additional rounds.
        fullLoadDeliveryCount = Mathf.Min(4, fullLoadDeliveryCount + 1);
    }

    /// <summary>
    /// Applies one of the four forklift paint tiers selected by an in-game
    /// colour code. Tier 1 is green, then silver, gold and animated rainbow.
    /// </summary>
    public static bool TrySetForkliftAppearanceTier(int tier)
    {
        if (tier < 1 || tier > 4)
        {
            return false;
        }

        forkliftAppearanceOverride = tier;
        return true;
    }

    public static void RestartCampaign()
    {
        currentLevel = 1;
        fullLoadDeliveryCount = 0;
    }

    // Runs once when a new Play session starts, including projects configured
    // to keep static data between editor Play sessions. It deliberately does
    // not run on SceneManager.LoadScene, so the next level survives the scene reset.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForNewPlaySession()
    {
        currentLevel = 1;
        fullLoadDeliveryCount = 0;
        forkliftAppearanceOverride = -1;
    }
}
