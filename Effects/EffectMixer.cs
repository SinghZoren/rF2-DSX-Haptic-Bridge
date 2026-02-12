using Rf2DsxBridge.Config;

namespace Rf2DsxBridge.Effects;

public sealed class EffectMixer
{
    private readonly AppConfig _config;

    private float _smoothMotorRight;
    private float _smoothMotorLeft;
    private bool _rumbleInitialized;

    private TriggerMode _prevLeftMode;
    private TriggerMode _prevRightMode;
    private int _leftModeHoldFrames;
    private int _rightModeHoldFrames;
    private const int ModeHoldMinFrames = 6;

    public EffectMixer(AppConfig config)
    {
        _config = config;
    }

    public RumbleEffect SmoothRumble(RumbleEffect raw)
    {
        float alpha = (float)_config.RumbleSmoothingAlpha;

        if (!_rumbleInitialized)
        {
            _smoothMotorRight = raw.MotorRight;
            _smoothMotorLeft = raw.MotorLeft;
            _rumbleInitialized = true;
        }
        else
        {
            float maxDelta = Math.Max(
                Math.Abs(raw.MotorRight - _smoothMotorRight),
                Math.Abs(raw.MotorLeft - _smoothMotorLeft));
            if (maxDelta > 0.15f)
                alpha = Math.Min(alpha + maxDelta * 1.5f, 0.85f);

            _smoothMotorRight = alpha * raw.MotorRight + (1f - alpha) * _smoothMotorRight;
            _smoothMotorLeft = alpha * raw.MotorLeft + (1f - alpha) * _smoothMotorLeft;
        }

        float gate = (float)_config.RumbleNoiseGate;
        float right = _smoothMotorRight < gate ? 0f : _smoothMotorRight;
        float left = _smoothMotorLeft < gate ? 0f : _smoothMotorLeft;

        return new RumbleEffect { MotorRight = right, MotorLeft = left };
    }

    public TriggerEffect StabilizeTrigger(TriggerEffect effect, bool isLeft)
    {
        ref TriggerMode prevMode = ref (isLeft ? ref _prevLeftMode : ref _prevRightMode);
        ref int holdFrames = ref (isLeft ? ref _leftModeHoldFrames : ref _rightModeHoldFrames);

        if (effect.Mode != prevMode)
        {
            holdFrames++;
            if (holdFrames < ModeHoldMinFrames)
            {
                if (effect.Mode != TriggerMode.Off && prevMode != TriggerMode.Off)
                {
                    effect.Mode = prevMode;
                }
                else
                {
                    prevMode = effect.Mode;
                    holdFrames = 0;
                }
            }
            else
            {
                prevMode = effect.Mode;
                holdFrames = 0;
            }
        }
        else
        {
            holdFrames = 0;
        }

        return effect;
    }

    public void Reset()
    {
        _smoothMotorRight = 0;
        _smoothMotorLeft = 0;
        _rumbleInitialized = false;
        _prevLeftMode = TriggerMode.Off;
        _prevRightMode = TriggerMode.Off;
        _leftModeHoldFrames = 0;
        _rightModeHoldFrames = 0;
    }
}
