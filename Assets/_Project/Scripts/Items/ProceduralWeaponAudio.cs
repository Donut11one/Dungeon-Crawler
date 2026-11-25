using UnityEngine;

/// <summary>
/// Procedural audio synthesizer for weapons, shots, reload clicks, and impacts.
/// Generates crisp audio clips dynamically without requiring external .wav assets.
/// </summary>
public static class ProceduralWeaponAudio
{
    private static AudioClip nailgunClip;
    private static AudioClip shotgunClip;
    private static AudioClip emptyClickClip;
    private static AudioClip reloadClip;
    private static AudioClip nailImpactClip;
    private static AudioClip healClip;

    public static AudioClip GetNailgunShotClip()
    {
        if (nailgunClip == null)
        {
            nailgunClip = CreateNailgunSound();
        }
        return nailgunClip;
    }

    public static AudioClip GetShotgunShotClip()
    {
        if (shotgunClip == null)
        {
            shotgunClip = CreateShotgunSound();
        }
        return shotgunClip;
    }

    public static AudioClip GetEmptyClickClip()
    {
        if (emptyClickClip == null)
        {
            emptyClickClip = CreateEmptyClickSound();
        }
        return emptyClickClip;
    }

    public static AudioClip GetReloadClip()
    {
        if (reloadClip == null)
        {
            reloadClip = CreateReloadSound();
        }
        return reloadClip;
    }

    public static AudioClip GetNailImpactClip()
    {
        if (nailImpactClip == null)
        {
            nailImpactClip = CreateNailImpactSound();
        }
        return nailImpactClip;
    }

    public static AudioClip GetHealClip()
    {
        if (healClip == null)
        {
            healClip = CreateHealSound();
        }
        return healClip;
    }

    private static AudioClip CreateNailgunSound()
    {
        int sampleRate = 44100;
        int samples = (int)(sampleRate * 0.18f); // 180ms pneumatic snap
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = Mathf.Exp(-t * 32f);

            // High frequency metallic punch + pneumatic white noise hiss
            float freq = 820f * Mathf.Exp(-t * 24f);
            float tone = Mathf.Sin(2f * Mathf.PI * freq * t);
            float noise = (Random.value * 2f - 1f) * Mathf.Exp(-t * 18f);

            data[i] = (tone * 0.65f + noise * 0.35f) * envelope;
        }

        AudioClip clip = AudioClip.Create("NailgunShot", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateShotgunSound()
    {
        int sampleRate = 44100;
        int samples = (int)(sampleRate * 0.45f); // 450ms heavy blast
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = Mathf.Exp(-t * 9.5f);

            // Deep punch bass boom + chaotic explosive noise
            float bass = Mathf.Sin(2f * Mathf.PI * 95f * t) * Mathf.Exp(-t * 12f);
            float mid = Mathf.Sin(2f * Mathf.PI * 280f * t) * Mathf.Exp(-t * 22f);
            float blastNoise = (Random.value * 2f - 1f) * Mathf.Exp(-t * 7.5f);

            data[i] = (bass * 0.45f + mid * 0.25f + blastNoise * 0.55f) * envelope;
        }

        AudioClip clip = AudioClip.Create("ShotgunBlast", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateEmptyClickSound()
    {
        int sampleRate = 44100;
        int samples = (int)(sampleRate * 0.06f); // 60ms click
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = Mathf.Exp(-t * 85f);
            float tone = Mathf.Sin(2f * Mathf.PI * 2400f * t);
            data[i] = tone * envelope * 0.7f;
        }

        AudioClip clip = AudioClip.Create("EmptyClick", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateReloadSound()
    {
        int sampleRate = 44100;
        int samples = (int)(sampleRate * 0.32f); // 320ms slide-clack
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float envelope1 = Mathf.Exp(-Mathf.Abs(t - 0.05f) * 60f);
            float envelope2 = Mathf.Exp(-Mathf.Abs(t - 0.18f) * 60f);
            float clack = Mathf.Sin(2f * Mathf.PI * 1400f * t) * envelope1 +
                          Mathf.Sin(2f * Mathf.PI * 1850f * t) * envelope2;
            float noise = (Random.value * 2f - 1f) * (envelope1 + envelope2) * 0.3f;

            data[i] = (clack * 0.7f + noise * 0.3f);
        }

        AudioClip clip = AudioClip.Create("ReloadClack", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateNailImpactSound()
    {
        int sampleRate = 44100;
        int samples = (int)(sampleRate * 0.10f); // 100ms metallic thwack
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = Mathf.Exp(-t * 45f);
            float metal = Mathf.Sin(2f * Mathf.PI * 1650f * t);
            float thud = Mathf.Sin(2f * Mathf.PI * 340f * t);
            data[i] = (metal * 0.6f + thud * 0.4f) * envelope;
        }

        AudioClip clip = AudioClip.Create("NailImpact", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateHealSound()
    {
        int sampleRate = 44100;
        int samples = (int)(sampleRate * 0.40f); // 400ms medical chime
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = Mathf.Exp(-t * 6f);
            float tone1 = Mathf.Sin(2f * Mathf.PI * 523.25f * t); // C5
            float tone2 = Mathf.Sin(2f * Mathf.PI * 659.25f * t); // E5
            float tone3 = Mathf.Sin(2f * Mathf.PI * 783.99f * t); // G5
            data[i] = ((tone1 + tone2 + tone3) / 3f) * envelope * 0.65f;
        }

        AudioClip clip = AudioClip.Create("HealChime", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
