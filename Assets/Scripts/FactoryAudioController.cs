using UnityEngine;

public enum TruckMotion
{
    Stopped,
    Forward,
    Reverse
}

/// <summary>
/// Provides a quiet, licence-free procedural work ambience and the short
/// feedback sounds used by the parcel and truck-loading interactions.
/// All sounds are 2D so they remain legible from every camera distance.
/// </summary>
[DisallowMultipleComponent]
public sealed class FactoryAudioController : MonoBehaviour
{
    private const int SampleRate = 22050;
    private const float AmbientDuration = 14f;
    // The scene already serializes the public engine volume. Keep a small
    // runtime gain here so older saved scenes receive the fuller, louder
    // diesel mix too, without overwriting the user's Inspector setting.
    private const float ForwardEngineGain = 1.5f;
    private const float ReverseEngineGain = 1.12f;
    // Keep the engine a touch lower than before: this is a loaded work truck,
    // not a small petrol car.
    private const float ForwardEnginePitch = 0.95f;
    private const float ReverseEnginePitch = 0.80f;

    public bool playAmbientMusic = true;
    [Range(0f, 1f)] public float ambientVolume = 0.045f;
    [Range(0f, 1f)] public float packageCatchVolume = 0.16f;
    [Range(0f, 1f)] public float packageDropVolume = 0.22f;
    [Range(0f, 1f)] public float truckLoadVolume = 0.24f;
    [Range(0f, 1f)] public float truckEngineVolume = 0.48f;
    [Range(0f, 1f)] public float truckReverseBeepVolume = 0.22f;

    private static FactoryAudioController instance;
    private AudioSource ambientSource;
    private AudioSource effectsSource;
    private AudioSource truckEngineSource;
    private AudioSource truckReverseBeepSource;
    private AudioClip ambienceClip;
    private AudioClip packageCatchClip;
    private AudioClip packageDropClip;
    private AudioClip truckLoadClip;
    private AudioClip truckEngineClip;
    private AudioClip truckReverseBeepClip;
    private TruckMotion truckMotion = TruckMotion.Stopped;
    // The truck remains a 2D sound source for legibility, but its engine
    // still fades according to its authored road departure, independent of
    // how far the player has zoomed the camera.
    private float truckDepartureVolumeMultiplier = 1f;

    public static void PlayPackageCaught()
    {
        var controller = FindController();
        if (controller != null)
        {
            // Intentionally use the former ground-impact sound for a catch.
            // Keep its original volume together with the sound when swapping
            // the two package feedback cues.
            controller.PlayOneShot(controller.packageDropClip, controller.packageDropVolume);
        }
    }

    public static void PlayTruckLoaded()
    {
        var controller = FindController();
        if (controller != null)
        {
            controller.PlayOneShot(controller.truckLoadClip, controller.truckLoadVolume);
        }
    }

    public static void PlayPackageDropped()
    {
        var controller = FindController();
        if (controller != null)
        {
            controller.PlayOneShot(controller.packageCatchClip, controller.packageCatchVolume);
        }
    }

    public static void SetTruckMotion(TruckMotion motion)
    {
        var controller = FindController();
        if (controller != null)
        {
            controller.SetTruckMotionInternal(motion);
        }
    }

    public static void StopTruckMotion()
    {
        SetTruckMotion(TruckMotion.Stopped);
    }

    /// <summary>
    /// Applies the truck's progress along the outbound road. Zero is the gate
    /// and one is the road end, where the engine is fully silent.
    /// </summary>
    public static void SetTruckDepartureProgress(float progress)
    {
        var controller = FindController();
        if (controller != null)
        {
            controller.SetTruckDepartureProgressInternal(progress);
        }
    }

    private static FactoryAudioController FindController()
    {
        if (instance == null)
        {
            instance = Object.FindAnyObjectByType<FactoryAudioController>();
        }

        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        // Older versions of this scene use a camera without an audio listener.
        // The ambience and feedback are 2D, so a single listener here is safe
        // and keeps sound available without changing the user's camera setup.
        if (Object.FindAnyObjectByType<AudioListener>() == null)
        {
            gameObject.AddComponent<AudioListener>();
        }

        ambientSource = CreateSource(true);
        effectsSource = CreateSource(false);
        truckEngineSource = CreateSource(true);
        truckReverseBeepSource = CreateSource(true);
        ambienceClip = CreateWorkAmbience();
        packageCatchClip = CreatePackageCatchClip();
        packageDropClip = CreatePackageDropClip();
        truckLoadClip = CreateTruckLoadClip();
        truckEngineClip = CreateTruckEngineClip();
        truckReverseBeepClip = CreateTruckReverseBeepClip();
    }

    private void Start()
    {
        if (!playAmbientMusic || ambientSource == null || ambienceClip == null)
        {
            return;
        }

        ambientSource.clip = ambienceClip;
        ambientSource.volume = ambientVolume;
        ambientSource.Play();
    }

    private void OnValidate()
    {
        if (ambientSource != null)
        {
            ambientSource.volume = ambientVolume;
        }

        if (truckEngineSource != null)
        {
            truckEngineSource.volume = GetTruckEngineVolume(truckMotion);
            truckEngineSource.pitch = GetTruckEnginePitch(truckMotion);
        }

        if (truckReverseBeepSource != null)
        {
            truckReverseBeepSource.volume = truckReverseBeepVolume;
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }

        SetTruckMotionInternal(TruckMotion.Stopped);
        DestroyRuntimeClip(ambienceClip);
        DestroyRuntimeClip(packageCatchClip);
        DestroyRuntimeClip(packageDropClip);
        DestroyRuntimeClip(truckLoadClip);
        DestroyRuntimeClip(truckEngineClip);
        DestroyRuntimeClip(truckReverseBeepClip);
    }

    private AudioSource CreateSource(bool looping)
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = looping;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.priority = looping ? 200 : 120;
        return source;
    }

    private void PlayOneShot(AudioClip clip, float volume)
    {
        if (effectsSource != null && clip != null)
        {
            effectsSource.PlayOneShot(clip, volume);
        }
    }

    private void SetTruckMotionInternal(TruckMotion motion)
    {
        if (truckMotion == motion)
        {
            return;
        }

        truckMotion = motion;
        if (motion == TruckMotion.Forward)
        {
            StopSource(truckReverseBeepSource);
            PlayLoop(
                truckEngineSource,
                truckEngineClip,
                GetTruckEngineVolume(TruckMotion.Forward),
                ForwardEnginePitch);
            return;
        }

        if (motion == TruckMotion.Reverse)
        {
            // A reversing lorry still idles under its warning beeper. Keeping
            // this quieter low diesel layer makes the manoeuvre sound like a
            // vehicle rather than a floating warning tone.
            PlayLoop(
                truckEngineSource,
                truckEngineClip,
                GetTruckEngineVolume(TruckMotion.Reverse),
                ReverseEnginePitch);
            PlayLoop(truckReverseBeepSource, truckReverseBeepClip, truckReverseBeepVolume);
            return;
        }

        StopSource(truckEngineSource);
        StopSource(truckReverseBeepSource);
    }

    private void SetTruckDepartureProgressInternal(float progress)
    {
        // SmoothStep makes the truck remain fully present just outside the
        // gate, then lets the sound recede gradually along the long road.
        truckDepartureVolumeMultiplier = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));

        if (truckEngineSource != null && truckEngineSource.isPlaying)
        {
            truckEngineSource.volume = GetTruckEngineVolume(truckMotion);
        }
    }

    private float GetTruckEngineVolume(TruckMotion motion)
    {
        var gain = motion == TruckMotion.Reverse ? ReverseEngineGain : ForwardEngineGain;
        return Mathf.Clamp01(truckEngineVolume * gain * truckDepartureVolumeMultiplier);
    }

    private static float GetTruckEnginePitch(TruckMotion motion)
    {
        return motion == TruckMotion.Reverse ? ReverseEnginePitch : ForwardEnginePitch;
    }

    private static void PlayLoop(AudioSource source, AudioClip clip, float volume, float pitch = 1f)
    {
        if (source == null || clip == null)
        {
            return;
        }

        source.clip = clip;
        source.volume = volume;
        source.pitch = pitch;
        if (!source.isPlaying)
        {
            source.Play();
        }
    }

    private static void StopSource(AudioSource source)
    {
        if (source != null && source.isPlaying)
        {
            source.Stop();
        }
    }

    private static AudioClip CreateWorkAmbience()
    {
        var sampleCount = Mathf.CeilToInt(AmbientDuration * SampleRate);
        var samples = new float[sampleCount];
        for (var index = 0; index < sampleCount; index++)
        {
            var time = index / (float)SampleRate;
            var slowPad = Mathf.Sin(time * Mathf.PI * 2f * 73.4f) * 0.34f +
                          Mathf.Sin(time * Mathf.PI * 2f * 110.0f) * 0.18f +
                          Mathf.Sin(time * Mathf.PI * 2f * 146.8f) * 0.12f;
            var movement = 0.76f + 0.24f * Mathf.Sin(time * Mathf.PI * 2f / 7f);
            var beatTime = Mathf.Repeat(time, 1.25f);
            var mechanicalPulse = Mathf.Exp(-beatTime * 6.2f) *
                                  Mathf.Sin(time * Mathf.PI * 2f * 47f) * 0.20f;
            var tickTime = Mathf.Repeat(time + 0.19f, 2.5f);
            var lightTick = Mathf.Exp(-tickTime * 18f) *
                            Mathf.Sin(time * Mathf.PI * 2f * 680f) * 0.045f;
            samples[index] = Mathf.Clamp((slowPad * movement + mechanicalPulse + lightTick) * 0.16f, -0.45f, 0.45f);
        }

        var clip = AudioClip.Create("Factory_Work_Ambience", sampleCount, 1, SampleRate, false);
        clip.SetData(samples, 0);
        clip.hideFlags = HideFlags.DontSave;
        return clip;
    }

    private static AudioClip CreatePackageCatchClip()
    {
        const float duration = 0.11f;
        var sampleCount = Mathf.CeilToInt(duration * SampleRate);
        var samples = new float[sampleCount];
        for (var index = 0; index < sampleCount; index++)
        {
            var time = index / (float)SampleRate;
            var envelope = Mathf.Exp(-time * 25f);
            var frequency = Mathf.Lerp(980f, 520f, time / duration);
            samples[index] = Mathf.Sin(time * Mathf.PI * 2f * frequency) * envelope * 0.42f;
        }

        var clip = AudioClip.Create("Package_Catch_Click", sampleCount, 1, SampleRate, false);
        clip.SetData(samples, 0);
        clip.hideFlags = HideFlags.DontSave;
        return clip;
    }

    private static AudioClip CreatePackageDropClip()
    {
        const float duration = 0.22f;
        var sampleCount = Mathf.CeilToInt(duration * SampleRate);
        var samples = new float[sampleCount];
        for (var index = 0; index < sampleCount; index++)
        {
            var time = index / (float)SampleRate;
            var envelope = Mathf.Exp(-time * 16f);
            var cardboardThud = Mathf.Sin(time * Mathf.PI * 2f * 118f) * 0.48f;
            var rattle = Mathf.Sin(time * Mathf.PI * 2f * 970f) * 0.11f +
                         Mathf.Sin(time * Mathf.PI * 2f * 1430f) * 0.06f;
            samples[index] = Mathf.Clamp((cardboardThud + rattle) * envelope, -0.58f, 0.58f);
        }

        var clip = AudioClip.Create("Package_Ground_Drop", sampleCount, 1, SampleRate, false);
        clip.SetData(samples, 0);
        clip.hideFlags = HideFlags.DontSave;
        return clip;
    }

    private static AudioClip CreateTruckLoadClip()
    {
        const float duration = 0.34f;
        var sampleCount = Mathf.CeilToInt(duration * SampleRate);
        var samples = new float[sampleCount];
        for (var index = 0; index < sampleCount; index++)
        {
            var time = index / (float)SampleRate;
            var firstThud = Mathf.Exp(-time * 10f) * Mathf.Sin(time * Mathf.PI * 2f * 92f);
            var secondTime = time - 0.13f;
            var secondThud = secondTime > 0f
                ? Mathf.Exp(-secondTime * 15f) * Mathf.Sin(secondTime * Mathf.PI * 2f * 118f)
                : 0f;
            samples[index] = Mathf.Clamp((firstThud * 0.46f + secondThud * 0.34f), -0.65f, 0.65f);
        }

        var clip = AudioClip.Create("Truck_Load_Thud", sampleCount, 1, SampleRate, false);
        clip.SetData(samples, 0);
        clip.hideFlags = HideFlags.DontSave;
        return clip;
    }

    private static AudioClip CreateTruckEngineClip()
    {
        const float duration = 3.2f;
        var sampleCount = Mathf.CeilToInt(duration * SampleRate);
        var samples = new float[sampleCount];
        for (var index = 0; index < sampleCount; index++)
        {
            var time = index / (float)SampleRate;
            // The engine needs a slightly uneven, low-frequency exhaust
            // character: a smooth sine reads as an electric motor, while
            // short cylinder bursts make a small work truck sound diesel.
            const float firingFrequency = 24.5f;
            var firingPhase = Mathf.Repeat(time * firingFrequency, 1f);
            var cylinderBurst = Mathf.Exp(-firingPhase * 15f);
            var rumble = Mathf.Sin(time * Mathf.PI * 2f * 31.5f) * 0.35f +
                         Mathf.Sin(time * Mathf.PI * 2f * 63f) * 0.20f +
                         Mathf.Sin(time * Mathf.PI * 2f * 94.5f) * 0.085f;
            var exhaustChug = Mathf.Sin(time * Mathf.PI * 2f * 54f) * cylinderBurst * 0.34f +
                             Mathf.Sin(time * Mathf.PI * 2f * 108f) * cylinderBurst * 0.11f;
            var slowMisfire = Mathf.Sin(time * Mathf.PI * 2f * 1.8f) * 0.04f;
            var roughness = (Mathf.PerlinNoise(time * 9.7f, 0.41f) * 2f - 1f) *
                            (0.015f + cylinderBurst * 0.045f);
            samples[index] = Mathf.Clamp(
                (rumble + exhaustChug + slowMisfire + roughness) * 0.68f,
                -0.78f,
                0.78f);
        }

        var clip = AudioClip.Create("Truck_Forward_Engine", sampleCount, 1, SampleRate, false);
        clip.SetData(samples, 0);
        clip.hideFlags = HideFlags.DontSave;
        return clip;
    }

    private static AudioClip CreateTruckReverseBeepClip()
    {
        // Reverse runs at 80% of its prior tempo, matching the truck's new
        // 20% slower reversing movement.
        const float duration = 1.25f;
        const float beepCycle = 0.625f;
        const float beepDuration = 0.2375f;
        var sampleCount = Mathf.CeilToInt(duration * SampleRate);
        var samples = new float[sampleCount];
        for (var index = 0; index < sampleCount; index++)
        {
            var time = index / (float)SampleRate;
            var beepPhase = Mathf.Repeat(time, beepCycle);
            var inBeep = beepPhase < beepDuration;
            var envelope = inBeep ? Mathf.Sin(beepPhase / beepDuration * Mathf.PI) : 0f;
            samples[index] = Mathf.Sin(time * Mathf.PI * 2f * 1184f) * envelope * 0.28f;
        }

        var clip = AudioClip.Create("Truck_Reverse_Beep", sampleCount, 1, SampleRate, false);
        clip.SetData(samples, 0);
        clip.hideFlags = HideFlags.DontSave;
        return clip;
    }

    private static void DestroyRuntimeClip(AudioClip clip)
    {
        if (clip != null)
        {
            Object.Destroy(clip);
        }
    }
}
