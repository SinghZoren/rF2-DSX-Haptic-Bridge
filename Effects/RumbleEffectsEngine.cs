using Rf2DsxBridge.Config;
using Rf2DsxBridge.Telemetry;

namespace Rf2DsxBridge.Effects;

public sealed class RumbleEffectsEngine
{
    private readonly AppConfig _config;

    private double _impactDecay;
    private int _absPhase;
    private int _tcPhase;
    private int _prevGear;
    private double _gearShiftDecay;

    private double _suspAvgSlow;
    private bool _suspAvgInit;
    private double _curbDecay;
    private float _curbSideBiasLeft;
    private int _curbPulsePhase;

    public RumbleEffect Output { get; private set; }

    public RumbleEffectsEngine(AppConfig config)
    {
        _config = config;
    }

    public void Update(in TelemetryFrame frame)
    {
        UpdateCurbBaseline(in frame);

        var curb = ComputeCurb(in frame);
        bool curbActive = _curbDecay > 0.05;

        var road = ComputeRoadSurface(in frame, curbActive);
        var gforce = ComputeGForce(in frame);
        var abs = ComputeAbs(in frame);
        var tc = ComputeTc(in frame);
        var engine = ComputeEngine(in frame);
        var impact = ComputeImpact(in frame);
        var spin = ComputeOversteer(in frame);

        var combined = road * _config.RoadRumbleGain
                     + curb * _config.CurbRumbleGain
                     + gforce * _config.GForceRumbleGain
                     + abs * _config.AbsRumbleGain
                     + tc * _config.TcRumbleGain
                     + engine * _config.EngineRumbleGain
                     + impact * _config.ImpactRumbleGain
                     + spin * _config.SpinRumbleGain;

        combined = combined * _config.MasterRumbleGain;

        combined.MotorRight = SoftClip(combined.MotorRight);
        combined.MotorLeft = SoftClip(combined.MotorLeft);
        combined.Clamp();

        Output = combined;
    }

    private void UpdateCurbBaseline(in TelemetryFrame frame)
    {
        double avgSusp = (Math.Abs(frame.WheelFL.SuspensionVelocity) + Math.Abs(frame.WheelFR.SuspensionVelocity)
                        + Math.Abs(frame.WheelRL.SuspensionVelocity) + Math.Abs(frame.WheelRR.SuspensionVelocity)) / 4.0;

        if (!_suspAvgInit)
        {
            _suspAvgSlow = avgSusp;
            _suspAvgInit = true;
        }
        else
        {
            const double alpha = 0.015;
            _suspAvgSlow = _suspAvgSlow * (1.0 - alpha) + avgSusp * alpha;
        }
    }

    private RumbleEffect ComputeRoadSurface(in TelemetryFrame frame, bool curbActive)
    {
        if (frame.IsStationary) return RumbleEffect.None;

        float speedFactor = (float)Math.Clamp((frame.SpeedKph - 30.0) / 80.0, 0, 1);

        double fl = Math.Abs(frame.WheelFL.SuspensionVelocity);
        double fr = Math.Abs(frame.WheelFR.SuspensionVelocity);
        double rl = Math.Abs(frame.WheelRL.SuspensionVelocity);
        double rr = Math.Abs(frame.WheelRR.SuspensionVelocity);
        double avg = (fl + fr + rl + rr) * 0.25;
        double variance = ((fl - avg) * (fl - avg) + (fr - avg) * (fr - avg)
                         + (rl - avg) * (rl - avg) + (rr - avg) * (rr - avg)) * 0.25;

        float intensity = (float)Math.Clamp(avg * 0.08 + Math.Sqrt(variance) * 0.4, 0, 0.3) * speedFactor;

        float roadSuppression = curbActive ? 0.1f : 1.0f;
        intensity *= roadSuppression;

        return new RumbleEffect
        {
            MotorRight = intensity,
            MotorLeft = intensity * 0.15f
        };
    }

    private RumbleEffect ComputeGForce(in TelemetryFrame frame)
    {
        if (frame.IsStationary) return RumbleEffect.None;

        float speedFactor = (float)Math.Clamp((frame.SpeedKph - 20.0) / 40.0, 0, 1);

        float longG = (float)Math.Abs(frame.LongitudinalAccel);
        float longIntensity = (float)Math.Clamp((longG - 1.5) / 12.0, 0, 0.5) * speedFactor;

        float latG = (float)Math.Abs(frame.LateralAccel);
        float latIntensity = (float)Math.Clamp((latG - 3.0) / 20.0, 0, 0.5) * speedFactor;

        float motorRight = longIntensity;
        float motorLeft = longIntensity;

        if (latIntensity > 0.01f)
        {
            motorRight += frame.LateralAccel < 0 ? latIntensity * 0.8f : latIntensity * 0.3f;
            motorLeft += frame.LateralAccel > 0 ? latIntensity * 0.8f : latIntensity * 0.3f;
        }

        motorRight = Math.Min(motorRight, 0.7f);
        motorLeft = Math.Min(motorLeft, 0.7f);

        if (motorRight < 0.02f && motorLeft < 0.02f) return RumbleEffect.None;

        return new RumbleEffect
        {
            MotorRight = motorRight,
            MotorLeft = motorLeft
        };
    }

    private RumbleEffect ComputeCurb(in TelemetryFrame frame)
    {
        if (frame.IsStationary) { _curbDecay = 0; _curbPulsePhase = 0; return RumbleEffect.None; }

        double maxSuspVel = frame.MaxSuspVelocity;
        double baseline = Math.Max(_suspAvgSlow, 0.01);
        double spikeRatio = maxSuspVel / baseline;

        double threshold = _curbDecay > 0.1
            ? _config.CurbSuspVelocityThreshold * 0.6
            : _config.CurbSuspVelocityThreshold;
        double requiredRatio = _curbDecay > 0.1 ? 1.8 : 2.5;

        if (spikeRatio > requiredRatio && maxSuspVel > threshold)
        {
            double hitIntensity = Math.Clamp(
                (maxSuspVel - threshold) * _config.CurbRumbleScale, 0.5, 1.0);
            _curbDecay = Math.Max(_curbDecay, hitIntensity);
            _curbSideBiasLeft = frame.MaxLeftSuspVelocity > frame.MaxRightSuspVelocity ? 1.0f : 0.3f;
        }

        if (_curbDecay > 0)
            _curbDecay = Math.Max(0, _curbDecay - frame.DeltaTime * 4.5);

        if (_curbDecay <= 0.02) { _curbPulsePhase = 0; return RumbleEffect.None; }

        _curbPulsePhase++;
        float pulseMultiplier = (_curbPulsePhase % 4 < 3) ? 1.0f : 0.2f;

        float intensity = (float)_curbDecay * pulseMultiplier;
        float rightBias = _curbSideBiasLeft < 0.5f ? 1.0f : 0.5f;

        return new RumbleEffect
        {
            MotorRight = intensity * rightBias,
            MotorLeft = intensity * _curbSideBiasLeft
        };
    }

    private RumbleEffect ComputeAbs(in TelemetryFrame frame)
    {
        bool absActive = frame.BrakePedal > 0.15 && frame.AvgFrontGrip > 0.01 && frame.AvgFrontGrip < _config.AbsGripThreshold && !frame.IsStationary;
        if (!absActive) { _absPhase = 0; return RumbleEffect.None; }

        _absPhase++;
        bool on = (_absPhase % 3) < 2;
        double severity = Math.Clamp((1.0 - frame.AvgFrontGrip) / (1.0 - _config.AbsGripThreshold), 0, 1);
        float amp = on ? (float)Math.Clamp(severity * 0.8, 0.15, 0.8) : 0f;

        return new RumbleEffect
        {
            MotorRight = amp,
            MotorLeft = amp * 0.3f
        };
    }

    private RumbleEffect ComputeTc(in TelemetryFrame frame)
    {
        bool tcActive = frame.ThrottlePedal > 0.5 && frame.AvgRearGrip > 0.01 && frame.AvgRearGrip < _config.TcGripThreshold && !frame.IsStationary;
        if (!tcActive) { _tcPhase = 0; return RumbleEffect.None; }

        _tcPhase++;
        bool on = (_tcPhase % 4) < 2;
        double severity = Math.Clamp((1.0 - frame.AvgRearGrip) / (1.0 - _config.TcGripThreshold), 0, 1);
        float amp = on ? (float)Math.Clamp(severity * 0.7, 0.15, 0.7) : 0f;

        return new RumbleEffect
        {
            MotorRight = amp * 0.5f,
            MotorLeft = amp * 0.4f
        };
    }

    private RumbleEffect ComputeEngine(in TelemetryFrame frame)
    {
        if (frame.Gear != _prevGear && _prevGear != 0 && frame.Gear > 0)
            _gearShiftDecay = 0.7;
        _prevGear = frame.Gear;

        if (_gearShiftDecay > 0)
            _gearShiftDecay = Math.Max(0, _gearShiftDecay - frame.DeltaTime * 6.0);

        if (_gearShiftDecay <= 0) return RumbleEffect.None;

        float intensity = (float)_gearShiftDecay;
        return new RumbleEffect
        {
            MotorRight = intensity * 0.4f,
            MotorLeft = intensity * 0.3f
        };
    }

    private RumbleEffect ComputeImpact(in TelemetryFrame frame)
    {
        if (frame.ImpactThisTick)
            _impactDecay = Math.Clamp(frame.LastImpactMagnitude / 50.0, 0.3, 1.0);
        else
            _impactDecay = Math.Max(0, _impactDecay - frame.DeltaTime * 4.0);

        if (_impactDecay <= 0) return RumbleEffect.None;

        return new RumbleEffect
        {
            MotorRight = (float)_impactDecay * 0.5f,
            MotorLeft = (float)_impactDecay
        };
    }

    private RumbleEffect ComputeOversteer(in TelemetryFrame frame)
    {
        if (frame.OversteerAngle < _config.OversteerThresholdDeg || frame.IsStationary)
            return RumbleEffect.None;

        double severity = Math.Clamp(
            (frame.OversteerAngle - _config.OversteerThresholdDeg) / 30.0, 0, 1);

        return new RumbleEffect
        {
            MotorRight = (float)severity * 0.5f,
            MotorLeft = (float)severity * 0.7f
        };
    }

    private static float SoftClip(float x)
    {
        if (x <= 0f) return 0f;
        if (x <= 0.7f) return x;
        return 0.7f + 0.3f * MathF.Tanh((x - 0.7f) / 0.3f);
    }

    public void Reset()
    {
        Output = RumbleEffect.None;
        _impactDecay = 0;
        _absPhase = 0;
        _tcPhase = 0;
        _prevGear = 0;
        _gearShiftDecay = 0;
        _curbDecay = 0;
        _curbPulsePhase = 0;
        _suspAvgSlow = 0;
        _suspAvgInit = false;
        _curbSideBiasLeft = 0;
    }
}
