using System;
using UnityEngine;

public class SigmoidFunction
{
    public enum Preset { Low = 1, Medium = 2, High = 3 }

    public readonly struct Settings
    {
        public readonly float GMin, GMax, K;
        public Settings(float gMin, float gMax, float k)
        {
            GMax = gMax;
            GMin = gMin;
            K = k;
        }
    }

    // private static readonly Settings LowSettings = new Settings(0.3f, 1.3f, 47.7f);
    // private static readonly Settings MediumSettings = new Settings(0.3f, 1.575f, 31.7f);
    // private static readonly Settings HighSettings = new Settings(0.3f, 2.45f, 21.2f);

    private static readonly Settings LowSettings = new Settings(0.3f, 1.3f, 49.9102f);
    private static readonly Settings MediumSettings = new Settings(0.3f, 1.7f, 36.0201f);
    private static readonly Settings HighSettings = new Settings(0.3f, 2.45f, 24.0817f);
    private readonly float vMin = 0.013f, vMax = 0.42f, vIdle = 0.074f; // vMin, vMax are not used

    public static Settings GetSettings(Preset preset) => preset switch
    {
        Preset.Low => LowSettings,
        Preset.Medium => MediumSettings,
        Preset.High => HighSettings,
        _ => LowSettings
    };

    public float CalculateSigmoid(Preset preset, float v)
    {
        Settings settings = GetSettings(preset);
        float pInf = GetInflectionPoint(preset);
        // float ratioInf = GetTunedRatioInf(preset);
        float gMax = settings.GMax;
        float gMin = settings.GMin;
        float k = settings.K;

        // float pInf = ratioInf * (vMax - vMin) + vMin;
        float exponent = k * (pInf - v);

        return (gMax - gMin) / (1f + Mathf.Exp(exponent)) + gMin;
    }

    public float GetInflectionPoint(Preset preset)
    {
        Settings settings = GetSettings(preset);
        float gMax = settings.GMax;
        float gMin = settings.GMin;
        float k = settings.K;

        return vIdle + (1f / k) * Mathf.Log((gMax - 1f) / (1f - gMin));
    }

    public float GetTunedRatioInf(Preset preset)
    {
        Settings settings = GetSettings(preset);
        float gMax = settings.GMax;
        float gMin = settings.GMin;
        float k = settings.K;

        float targetPinf = vIdle + (1f / k) * Mathf.Log((gMax - 1f) / (1f - gMin));
        float ratioInf = (targetPinf - vMin) / (vMax - vMin);

        return ratioInf;
    }
}