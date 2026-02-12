using System.Text.Json;

namespace Rf2DsxBridge.Config;

public sealed class AppConfig
{
    public string OutputMode { get; set; } = "hid";
    public int UpdateHz { get; set; } = 120;

    public string DsxFilePath { get; set; } = @"C:\ProgramData\DualSenseX\triggers.txt";

    public double BrakeDeadzone { get; set; } = 0.02;
    public double ThrottleDeadzone { get; set; } = 0.02;
    public int BrakeStartPos { get; set; } = 2;
    public int ThrottleStartPos { get; set; } = 1;

    public int MaxStrength { get; set; } = 8;
    public int MaxBrakeStrength { get; set; } = 8;
    public int MaxThrottleStrength { get; set; } = 5;
    public int FixedThrottleStrength { get; set; } = 2;
    public double SmoothingAlpha { get; set; } = 0.25;

    public double AbsGripThreshold { get; set; } = 0.85;
    public double AbsTriggerGain { get; set; } = 1.0;
    public int AbsTriggerFreqHz { get; set; } = 40;

    public double TcGripThreshold { get; set; } = 0.60;
    public double TcTriggerGain { get; set; } = 1.0;
    public int TcTriggerFreqHz { get; set; } = 30;

    public double ImpactTriggerGain { get; set; } = 0.5;

    public double OversteerThresholdDeg { get; set; } = 10.0;
    public double OversteerTriggerGain { get; set; } = 0.8;
    public double EstimatedWheelbaseMeter { get; set; } = 2.6;
    public double EstimatedMaxSteerAngleDeg { get; set; } = 20.0;

    public float MasterRumbleGain { get; set; } = 1.0f;
    public float RoadRumbleGain { get; set; } = 0.5f;
    public float GForceRumbleGain { get; set; } = 0.7f;
    public float CurbRumbleGain { get; set; } = 1.0f;
    public float AbsRumbleGain { get; set; } = 0.8f;
    public float TcRumbleGain { get; set; } = 0.3f;
    public float EngineRumbleGain { get; set; } = 0.3f;
    public float ImpactRumbleGain { get; set; } = 1.0f;
    public float SpinRumbleGain { get; set; } = 0.4f;

    public double CurbSuspVelocityThreshold { get; set; } = 0.15;
    public double CurbRumbleScale { get; set; } = 3.0;
    public double CurbTriggerGain { get; set; } = 0.8;
    public int CurbTriggerFreqHz { get; set; } = 45;

    public double RumbleSmoothingAlpha { get; set; } = 0.5;
    public double RumbleNoiseGate { get; set; } = 0.05;

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"Config not found at {path}, using defaults.");
            return new AppConfig();
        }

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<AppConfig>(json, options) ?? new AppConfig();
    }
}
