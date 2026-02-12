using Rf2DsxBridge.Config;
using Rf2DsxBridge.Effects;
using Rf2DsxBridge.Output;
using Rf2DsxBridge.Telemetry;

namespace Rf2DsxBridge;

internal static class Program
{
    private static volatile bool _running = true;

    static void Main(string[] args)
    {
        Console.WriteLine("=== rF2 -> DualSense Haptic Bridge ===");
        Console.WriteLine();

        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var config = AppConfig.Load(configPath);

        Console.WriteLine($"Output mode:      {config.OutputMode}");
        Console.WriteLine($"Update rate:      {config.UpdateHz} Hz");
        Console.WriteLine($"Brake:            deadzone={config.BrakeDeadzone} start={config.BrakeStartPos} max={config.MaxBrakeStrength}");
        Console.WriteLine($"Throttle:         deadzone={config.ThrottleDeadzone} start={config.ThrottleStartPos} max={config.MaxThrottleStrength}");
        Console.WriteLine($"ABS threshold:    grip<{config.AbsGripThreshold} freq={config.AbsTriggerFreqHz}Hz");
        Console.WriteLine($"TC threshold:     grip<{config.TcGripThreshold} freq={config.TcTriggerFreqHz}Hz");
        Console.WriteLine($"Oversteer:        >{config.OversteerThresholdDeg}deg");
        Console.WriteLine($"Rumble master:    {config.MasterRumbleGain:F1}x");
        Console.WriteLine();

        if (config.OutputMode.Equals("dsx", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[WARN] DSX mode: rumble not available, trigger vibration falls back to resistance.");
            Console.WriteLine("[WARN] For best feel, use outputMode=\"hid\" with DualSense connected via USB.");
            Console.WriteLine();
        }

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _running = false; };

        using var telemetry = new TelemetryReader(config.EstimatedWheelbaseMeter, config.EstimatedMaxSteerAngleDeg);
        var triggerEngine = new TriggerEffectsEngine(config);
        var rumbleEngine = new RumbleEffectsEngine(config);
        var mixer = new EffectMixer(config);

        using IOutputSink output = config.OutputMode.Equals("dsx", StringComparison.OrdinalIgnoreCase)
            ? new DsxOutputSink(config.DsxFilePath)
            : new HidOutputSink();

        int tickMs = 1000 / Math.Max(1, config.UpdateHz);
        bool telemetryWasConnected = false;
        bool outputWasConnected = false;
        int telemetryReconnectCounter = 0;
        int outputReconnectCounter = 0;
        int logCounter = 0;

        Console.WriteLine("Waiting for rF2 telemetry and DualSense controller...");
        Console.WriteLine("Press Ctrl+C to exit.");
        Console.WriteLine();

        while (_running)
        {
            if (!telemetry.IsConnected)
            {
                telemetryReconnectCounter++;
                if (telemetryReconnectCounter >= 2000 / tickMs)
                {
                    telemetryReconnectCounter = 0;
                    if (telemetry.TryConnect())
                    {
                        Console.WriteLine("[OK] Connected to rF2 shared memory.");
                        telemetryWasConnected = true;
                    }
                }
            }

            if (!output.IsConnected)
            {
                outputReconnectCounter++;
                if (outputReconnectCounter >= 2000 / tickMs)
                {
                    outputReconnectCounter = 0;
                    if (output.TryConnect())
                    {
                        if (!outputWasConnected)
                        {
                            Console.WriteLine($"[OK] Output connected ({config.OutputMode}).");
                            outputWasConnected = true;
                        }
                    }
                }
            }

            if (telemetry.IsConnected && output.IsConnected)
            {
                bool gotData = telemetry.Update();
                if (gotData)
                {
                    var frame = telemetry.CurrentFrame;

                    triggerEngine.Update(in frame);
                    rumbleEngine.Update(in frame);

                    var leftTrigger = mixer.StabilizeTrigger(triggerEngine.LeftTrigger, isLeft: true);
                    var rightTrigger = mixer.StabilizeTrigger(triggerEngine.RightTrigger, isLeft: false);
                    var rumble = mixer.SmoothRumble(rumbleEngine.Output);

                    output.Send(leftTrigger, rightTrigger, rumble);

                    logCounter++;
                    if (logCounter >= config.UpdateHz)
                    {
                        logCounter = 0;
                        string l2 = leftTrigger.Mode == TriggerMode.Vibration
                            ? $"Vib(a{leftTrigger.Amplitude}f{leftTrigger.FrequencyHz})"
                            : leftTrigger.Mode == TriggerMode.Feedback
                                ? $"Res({leftTrigger.Strength})"
                                : "Off";
                        string r2 = rightTrigger.Mode == TriggerMode.Vibration
                            ? $"Vib(a{rightTrigger.Amplitude}f{rightTrigger.FrequencyHz})"
                            : rightTrigger.Mode == TriggerMode.Feedback
                                ? $"Res({rightTrigger.Strength})"
                                : "Off";
                        double avgFrontAng = (Math.Abs(frame.WheelFL.AngularVelocity)
                                            + Math.Abs(frame.WheelFR.AngularVelocity)) * 0.5;
                        Console.Write($"\r{frame.SpeedKph,3:F0}kph G{frame.Gear} " +
                                      $"L2:{l2,-10} R2:{r2,-10} " +
                                      $"R:{rumble.MotorRightByte:D3}/{rumble.MotorLeftByte:D3} " +
                                      $"Gr:{frame.AvgFrontGrip:F2}/{frame.AvgRearGrip:F2} " +
                                      $"Sl:{frame.AvgFrontSlipRatio:F1} " +
                                      $"Ang:{avgFrontAng:F0} " +
                                      $"Gf:{frame.LateralAccel:F1}/{frame.LongitudinalAccel:F1}   ");
                    }
                }
                else
                {
                    ResetAll(triggerEngine, rumbleEngine, mixer, output);
                }
            }
            else
            {
                if (!telemetry.IsConnected && telemetryWasConnected)
                {
                    Console.WriteLine("\n[WARN] Lost rF2 connection. Reverting to safe state.");
                    ResetAll(triggerEngine, rumbleEngine, mixer, output);
                    telemetryWasConnected = false;
                }
                if (!output.IsConnected && outputWasConnected)
                {
                    Console.WriteLine("\n[WARN] Lost controller connection. Waiting for reconnect...");
                    outputWasConnected = false;
                }
            }

            Thread.Sleep(tickMs);
        }

        Console.WriteLine("\nShutting down - clearing effects...");
        ResetAll(triggerEngine, rumbleEngine, mixer, output);
        Console.WriteLine("Done.");
    }

    private static void ResetAll(TriggerEffectsEngine triggerEngine, RumbleEffectsEngine rumbleEngine,
        EffectMixer mixer, IOutputSink output)
    {
        triggerEngine.Reset();
        rumbleEngine.Reset();
        mixer.Reset();
        if (output.IsConnected)
            output.SendSafeState();
    }
}
