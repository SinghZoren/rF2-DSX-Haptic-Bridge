using Rf2DsxBridge.Config;
using Rf2DsxBridge.Telemetry;

namespace Rf2DsxBridge.Effects;

public sealed class TriggerEffectsEngine
{
    private readonly AppConfig _config;
    private double _impactDecay;

    private double _smoothThrottle;
    private double _smoothBrake;
    private bool _pedalInit;

    private double _curbTriggerDecay;

    private int _prevGear;
    private double _gearShiftTriggerDecay;

    private double _peakDecel;

    public TriggerEffect LeftTrigger { get; private set; }
    public TriggerEffect RightTrigger { get; private set; }

    public TriggerEffectsEngine(AppConfig config)
    {
        _config = config;
    }

    public void Update(in TelemetryFrame frame)
    {
        const double pedalAlpha = 0.15;
        if (!_pedalInit)
        {
            _smoothThrottle = frame.ThrottlePedal;
            _smoothBrake = frame.BrakePedal;
            _pedalInit = true;
        }
        else
        {
            _smoothThrottle = pedalAlpha * frame.ThrottlePedal + (1.0 - pedalAlpha) * _smoothThrottle;
            _smoothBrake = pedalAlpha * frame.BrakePedal + (1.0 - pedalAlpha) * _smoothBrake;
        }

        if (frame.ImpactThisTick)
            _impactDecay = Math.Clamp(frame.LastImpactMagnitude * _config.ImpactTriggerGain / 50.0, 0.3, 1.0);
        else
            _impactDecay = Math.Max(0, _impactDecay - frame.DeltaTime * 5.0);

        if (frame.Gear != _prevGear && _prevGear != 0 && frame.Gear > 0)
            _gearShiftTriggerDecay = 0.6;
        _prevGear = frame.Gear;
        if (_gearShiftTriggerDecay > 0)
            _gearShiftTriggerDecay = Math.Max(0, _gearShiftTriggerDecay - frame.DeltaTime * 7.0);

        if (frame.MaxSuspVelocity > 0.6 && !frame.IsStationary)
        {
            double intensity = Math.Clamp(frame.MaxSuspVelocity * 1.5, 0.3, 1.0);
            _curbTriggerDecay = Math.Max(_curbTriggerDecay, intensity);
        }
        if (_curbTriggerDecay > 0)
            _curbTriggerDecay = Math.Max(0, _curbTriggerDecay - frame.DeltaTime * 8.0);

        LeftTrigger = ComputeBrakeTrigger(in frame);
        RightTrigger = ComputeThrottleTrigger(in frame);
    }

    private TriggerEffect ComputeBrakeTrigger(in TelemetryFrame frame)
    {
        if (_smoothBrake < _config.BrakeDeadzone)
        {
            _peakDecel = 0;
            return TriggerEffect.Off();
        }

        double decel = Math.Abs(frame.LongitudinalAccel);

        if (_smoothBrake > 0.3 && frame.SpeedMps > 5.0)
        {
            if (decel > _peakDecel)
                _peakDecel = decel;
            else
                _peakDecel = Math.Max(decel, _peakDecel - frame.DeltaTime * 4.0);

            if (_smoothBrake > 0.5 && _peakDecel > 6.0 && decel < _peakDecel * 0.55)
            {
                return TriggerEffect.Resistance(_config.BrakeStartPos, 1);
            }
        }
        else
        {
            _peakDecel = 0;
        }

        int strength = Math.Clamp((int)Math.Round(_smoothBrake * _config.MaxBrakeStrength), 1, 8);
        return TriggerEffect.Resistance(_config.BrakeStartPos, strength);
    }

    private TriggerEffect ComputeThrottleTrigger(in TelemetryFrame frame)
    {
        if (_smoothThrottle < _config.ThrottleDeadzone)
            return TriggerEffect.Off();

        if (_impactDecay > 0.2)
        {
            int amp = Math.Clamp((int)(6 * _impactDecay), 1, 8);
            return TriggerEffect.Vibrate(_config.ThrottleStartPos, amp, 50);
        }

        if (_gearShiftTriggerDecay > 0.1)
        {
            int amp = Math.Clamp((int)(4 * _gearShiftTriggerDecay), 1, 5);
            return TriggerEffect.Vibrate(_config.ThrottleStartPos, amp, 35);
        }

        if (_curbTriggerDecay > 0.15)
        {
            int amp = Math.Clamp((int)(5 * _curbTriggerDecay * _config.CurbTriggerGain), 1, 8);
            return TriggerEffect.Vibrate(_config.ThrottleStartPos, amp, (byte)_config.CurbTriggerFreqHz);
        }

        return TriggerEffect.Resistance(_config.ThrottleStartPos, _config.FixedThrottleStrength);
    }

    public void Reset()
    {
        LeftTrigger = TriggerEffect.Off();
        RightTrigger = TriggerEffect.Off();
        _impactDecay = 0;
        _smoothThrottle = 0;
        _smoothBrake = 0;
        _pedalInit = false;
        _curbTriggerDecay = 0;
        _prevGear = 0;
        _gearShiftTriggerDecay = 0;
        _peakDecel = 0;
    }
}
