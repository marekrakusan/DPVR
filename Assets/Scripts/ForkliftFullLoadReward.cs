using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A runtime-only celebration for transferring a completely filled forklift.
/// It grows a transparent bubble around the vehicle, pops it into small
/// droplets, then invokes its callback at the exact pop moment.
/// </summary>
[DisallowMultipleComponent]
public sealed class ForkliftFullLoadReward : MonoBehaviour
{
    private const float BubbleGrowDuration = 0.60f;
    private const float BurstDuration = 0.65f;
    private const float MessageDuration = 2.50f;
    private const int RingSegments = 48;
    private const int BurstParticleCount = 18;

    private enum RewardState
    {
        Idle,
        Growing,
        Bursting
    }

    private sealed class BurstParticle
    {
        public Transform Transform;
        public Vector3 Velocity;
        public float InitialScale;
        public float SpinSpeed;
    }

    private readonly List<BurstParticle> burstParticles = new();
    private RewardState state;
    private Action onBubblePopped;
    private GameObject bubbleRoot;
    private GameObject messageRoot;
    private Material bubbleMaterial;
    private Material burstMaterial;
    private float stateElapsed;
    private float messageElapsed;

    /// <summary>
    /// Starts a single celebration. The callback is invoked when the bubble
    /// visibly pops, not while it is still growing.
    /// </summary>
    public void Play(Action bubblePopped)
    {
        if (state != RewardState.Idle)
        {
            return;
        }

        onBubblePopped = bubblePopped;
        stateElapsed = 0f;
        state = RewardState.Growing;
        CreateBubble();
    }

    private void Update()
    {
        switch (state)
        {
            case RewardState.Growing:
                UpdateGrowingBubble();
                break;
            case RewardState.Bursting:
                UpdateBurst();
                break;
        }

        UpdateMessage();
    }

    private void OnDestroy()
    {
        DestroyRuntimeObject(bubbleRoot);
        DestroyRuntimeObject(messageRoot);
        DestroyRuntimeObject(bubbleMaterial);
        DestroyRuntimeObject(burstMaterial);
    }

    private void CreateBubble()
    {
        bubbleMaterial = CreateRuntimeMaterial(new Color(0.42f, 1f, 0.70f, 0.78f));
        bubbleRoot = new GameObject("Full_Load_Reward_Bubble");
        bubbleRoot.hideFlags = HideFlags.DontSave;
        bubbleRoot.transform.SetParent(transform, false);
        bubbleRoot.transform.localPosition = new Vector3(0f, 1.45f, -0.10f);
        bubbleRoot.transform.localScale = Vector3.one * 0.12f;

        CreateBubbleRing("Bubble_Ring_XY", Quaternion.identity);
        CreateBubbleRing("Bubble_Ring_XZ", Quaternion.Euler(90f, 0f, 0f));
        CreateBubbleRing("Bubble_Ring_YZ", Quaternion.Euler(0f, 90f, 0f));
    }

    private void CreateBubbleRing(string ringName, Quaternion localRotation)
    {
        var ring = new GameObject(ringName);
        ring.hideFlags = HideFlags.DontSave;
        ring.transform.SetParent(bubbleRoot.transform, false);
        ring.transform.localRotation = localRotation;

        var line = ring.AddComponent<LineRenderer>();
        line.hideFlags = HideFlags.DontSave;
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = RingSegments;
        line.widthMultiplier = 0.035f;
        line.numCapVertices = 4;
        line.numCornerVertices = 4;
        line.material = bubbleMaterial;
        line.startColor = new Color(0.45f, 1f, 0.70f, 0.78f);
        line.endColor = new Color(0.45f, 1f, 0.70f, 0.78f);

        var radius = 1f;
        for (var index = 0; index < RingSegments; index++)
        {
            var angle = index * Mathf.PI * 2f / RingSegments;
            line.SetPosition(index, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }
    }

    private void UpdateGrowingBubble()
    {
        stateElapsed += Time.deltaTime;
        var progress = Mathf.Clamp01(stateElapsed / BubbleGrowDuration);
        var scale = Mathf.Lerp(0.12f, 2.25f, Mathf.SmoothStep(0f, 1f, progress));
        if (bubbleRoot != null)
        {
            bubbleRoot.transform.localScale = Vector3.one * scale;
        }

        if (progress >= 1f)
        {
            PopBubble();
        }
    }

    private void PopBubble()
    {
        DestroyRuntimeObject(bubbleRoot);
        bubbleRoot = null;
        CreateBurstParticles();
        ShowWellDoneMessage();

        var callback = onBubblePopped;
        onBubblePopped = null;
        callback?.Invoke();

        stateElapsed = 0f;
        state = RewardState.Bursting;
    }

    private void CreateBurstParticles()
    {
        burstMaterial = CreateRuntimeMaterial(new Color(0.16f, 0.95f, 0.32f, 1f));
        var origin = transform.position + Vector3.up * 1.5f;
        for (var index = 0; index < BurstParticleCount; index++)
        {
            var particle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            particle.name = "Full_Load_Reward_Droplet";
            particle.hideFlags = HideFlags.DontSave;
            particle.transform.position = origin;
            particle.transform.localScale = Vector3.one * 0.11f;

            var collider = particle.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            var renderer = particle.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = burstMaterial;
            }

            var direction = UnityEngine.Random.onUnitSphere;
            direction.y = Mathf.Abs(direction.y) + 0.22f;
            direction.Normalize();
            burstParticles.Add(new BurstParticle
            {
                Transform = particle.transform,
                Velocity = direction * UnityEngine.Random.Range(2.4f, 4.2f),
                InitialScale = UnityEngine.Random.Range(0.07f, 0.15f),
                SpinSpeed = UnityEngine.Random.Range(-540f, 540f)
            });
        }
    }

    private void UpdateBurst()
    {
        stateElapsed += Time.deltaTime;
        var progress = Mathf.Clamp01(stateElapsed / BurstDuration);
        foreach (var particle in burstParticles)
        {
            if (particle.Transform == null)
            {
                continue;
            }

            particle.Velocity += Physics.gravity * (0.42f * Time.deltaTime);
            particle.Transform.position += particle.Velocity * Time.deltaTime;
            particle.Transform.Rotate(Vector3.up, particle.SpinSpeed * Time.deltaTime, Space.World);
            particle.Transform.localScale = Vector3.one * particle.InitialScale * (1f - progress);
        }

        if (progress < 1f)
        {
            return;
        }

        foreach (var particle in burstParticles)
        {
            if (particle.Transform != null)
            {
                DestroyRuntimeObject(particle.Transform.gameObject);
            }
        }

        burstParticles.Clear();
        DestroyRuntimeObject(burstMaterial);
        burstMaterial = null;
        state = RewardState.Idle;
    }

    private void ShowWellDoneMessage()
    {
        DestroyRuntimeObject(messageRoot);
        messageRoot = new GameObject("Full_Load_Well_Done");
        messageRoot.hideFlags = HideFlags.DontSave;
        messageRoot.transform.SetParent(transform, false);
        messageRoot.transform.localPosition = new Vector3(2.35f, 2.65f, 0f);

        var text = messageRoot.AddComponent<TextMesh>();
        text.text = "Well Done!";
        text.anchor = TextAnchor.MiddleLeft;
        text.alignment = TextAlignment.Left;
        text.fontSize = 96;
        text.characterSize = 0.15f;
        text.fontStyle = FontStyle.Bold;
        text.color = new Color(0.34f, 1f, 0.42f, 1f);

        messageElapsed = MessageDuration;
        FaceMessageCamera();
    }

    private void UpdateMessage()
    {
        if (messageRoot == null)
        {
            return;
        }

        FaceMessageCamera();
        messageElapsed -= Time.deltaTime;
        if (messageElapsed <= 0f)
        {
            DestroyRuntimeObject(messageRoot);
            messageRoot = null;
        }
    }

    private void FaceMessageCamera()
    {
        if (messageRoot == null || Camera.main == null)
        {
            return;
        }

        // TextMesh renders toward its local -Z face, so point local +Z away
        // from the camera to keep the "Well Done!" text readable.
        var direction = messageRoot.transform.position - Camera.main.transform.position;
        if (direction.sqrMagnitude > 0.0001f)
        {
            messageRoot.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }
    }

    private static Material CreateRuntimeMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        var material = new Material(shader)
        {
            hideFlags = HideFlags.DontSave
        };
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        return material;
    }

    private static void DestroyRuntimeObject(UnityEngine.Object target)
    {
        if (target != null)
        {
            Destroy(target);
        }
    }
}
