using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Client-side presentation for collected treasure: a floating '+$value  Name' toast and a pickup chime.
/// Created on demand, so there is nothing to place in the scene.
/// </summary>
public class TreasureFeedback : MonoBehaviour
{
    private const float ToastDuration = 2.4f;
    private const int MaxToasts = 5;

    private struct Toast
    {
        public string text;
        public Color color;
        public float startTime;
    }

    private static TreasureFeedback instance;
    private static readonly Dictionary<TreasureRarity, AudioClip> chimes = new Dictionary<TreasureRarity, AudioClip>();

    private readonly List<Toast> toasts = new List<Toast>();
    private GUIStyle toastStyle;
    private GUIStyle shadowStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        chimes.Clear();
    }

    private static TreasureFeedback Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("TreasureFeedback");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<TreasureFeedback>();
            }
            return instance;
        }
    }

    /// <summary>Shows the '+$value Name' toast for the local player.</summary>
    public static void ShowCollected(TreasureData treasure, int value)
    {
        string treasureName = treasure != null ? treasure.DisplayName : "Treasure";
        Color color = treasure != null ? treasure.AccentColor : Color.white;
        Instance.AddToast($"+${value}   {treasureName}", color);
    }

    /// <summary>Plays the treasure's pickup sound (or a procedural chime) at a world position.</summary>
    public static void PlayCollectSound(TreasureData treasure, Vector3 position)
    {
        AudioClip clip = treasure != null ? treasure.PickupSound : null;
        float volume = treasure != null ? treasure.PickupVolume : 0.7f;

        if (clip == null)
        {
            TreasureRarity rarity = treasure != null ? treasure.Rarity : TreasureRarity.Common;
            clip = GetChime(rarity);
        }

        if (clip != null)
        {
            AudioSource.PlayClipAtPoint(clip, position, volume);
        }
    }

    private static AudioClip GetChime(TreasureRarity rarity)
    {
        if (chimes.TryGetValue(rarity, out AudioClip cached) && cached != null)
        {
            return cached;
        }

        AudioClip clip = BuildChime(rarity);
        chimes[rarity] = clip;
        return clip;
    }

    // Short rising arpeggio; rarer treasure plays more notes.
    private static AudioClip BuildChime(TreasureRarity rarity)
    {
        const int sampleRate = 44100;
        int noteCount = 2 + (int)rarity;
        float noteSpacing = 0.075f;
        float noteLength = 0.35f;
        float totalLength = noteSpacing * (noteCount - 1) + noteLength;
        int totalSamples = Mathf.CeilToInt(totalLength * sampleRate);
        float[] data = new float[totalSamples];
        float[] ratios = { 1f, 1.25f, 1.5f, 2f, 2.5f, 3f };
        float baseFrequency = 740f;

        for (int n = 0; n < noteCount; n++)
        {
            float frequency = baseFrequency * ratios[Mathf.Min(n, ratios.Length - 1)];
            int start = Mathf.RoundToInt(n * noteSpacing * sampleRate);
            int length = Mathf.RoundToInt(noteLength * sampleRate);

            for (int i = 0; i < length && start + i < totalSamples; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Exp(-t * 9f) * Mathf.Clamp01(t * 600f);
                float tone = Mathf.Sin(2f * Mathf.PI * frequency * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * frequency * t);
                data[start + i] += tone * envelope * 0.22f;
            }
        }

        for (int i = 0; i < data.Length; i++)
        {
            data[i] = Mathf.Clamp(data[i], -1f, 1f);
        }

        AudioClip clip = AudioClip.Create("TreasureChime_" + rarity, totalSamples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private void AddToast(string text, Color color)
    {
        toasts.Add(new Toast { text = text, color = color, startTime = Time.unscaledTime });
        while (toasts.Count > MaxToasts)
        {
            toasts.RemoveAt(0);
        }
    }

    private void OnGUI()
    {
        if (toasts.Count == 0 || Event.current.type != EventType.Repaint)
        {
            return;
        }

        if (toastStyle == null)
        {
            toastStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            shadowStyle = new GUIStyle(toastStyle);
        }

        float now = Time.unscaledTime;
        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            if (now - toasts[i].startTime > ToastDuration)
            {
                toasts.RemoveAt(i);
            }
        }

        float centerX = Screen.width * 0.5f;
        float baseY = Screen.height * 0.5f - 80f;

        for (int i = 0; i < toasts.Count; i++)
        {
            Toast toast = toasts[i];
            float age = now - toast.startTime;
            float alpha = Mathf.Min(Mathf.Clamp01(age / 0.12f), Mathf.Clamp01((ToastDuration - age) / 0.6f));
            float rise = Mathf.Clamp01(age / ToastDuration) * 36f;
            int slot = toasts.Count - 1 - i;
            Rect rect = new Rect(centerX - 220f, baseY - slot * 28f - rise, 440f, 28f);

            shadowStyle.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.8f);
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), toast.text, shadowStyle);

            toastStyle.normal.textColor = new Color(toast.color.r, toast.color.g, toast.color.b, alpha);
            GUI.Label(rect, toast.text, toastStyle);
        }
    }
}
