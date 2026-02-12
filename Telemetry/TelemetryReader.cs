using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace Rf2DsxBridge.Telemetry;

public sealed class TelemetryReader : IDisposable
{
    private MemoryMappedFile? _mmf;
    private MemoryMappedViewStream? _stream;
    private bool _connected;
    private readonly int _structSize;
    private readonly TelemetryFrameBuilder _frameBuilder;
    private byte[]? _rawBuffer;

    public bool IsConnected => _connected;
    public double BrakePedal { get; private set; }
    public double ThrottlePedal { get; private set; }
    public TelemetryFrame CurrentFrame { get; private set; }

    public TelemetryReader(double wheelbaseMeter = 2.6, double maxSteerAngleDeg = 20.0)
    {
        _structSize = Marshal.SizeOf<rF2Telemetry>();
        _frameBuilder = new TelemetryFrameBuilder(wheelbaseMeter, maxSteerAngleDeg);
    }

    public bool TryConnect()
    {
        if (_connected)
            return true;

        try
        {
            _mmf = MemoryMappedFile.OpenExisting(Rf2Constants.MM_TELEMETRY_FILE_NAME);
            _stream = _mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
            _rawBuffer = new byte[_structSize];
            _connected = true;
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TelemetryReader] Error connecting: {ex.Message}");
            return false;
        }
    }

    public bool Update()
    {
        if (!_connected || _stream == null || _rawBuffer == null)
        {
            BrakePedal = 0;
            ThrottlePedal = 0;
            return false;
        }

        try
        {
            _stream.Position = 0;
            int bytesRead = _stream.Read(_rawBuffer, 0, _structSize);
            if (bytesRead < _structSize)
            {
                BrakePedal = 0;
                ThrottlePedal = 0;
                return false;
            }

            var handle = GCHandle.Alloc(_rawBuffer, GCHandleType.Pinned);
            try
            {
                var telemetry = Marshal.PtrToStructure<rF2Telemetry>(handle.AddrOfPinnedObject());

                if (telemetry.mNumVehicles <= 0 || telemetry.mVehicles == null)
                {
                    BrakePedal = 0;
                    ThrottlePedal = 0;
                    return false;
                }

                var player = telemetry.mVehicles[0];
                CurrentFrame = _frameBuilder.Build(in player);
                BrakePedal = CurrentFrame.BrakePedal;
                ThrottlePedal = CurrentFrame.ThrottlePedal;
                return true;
            }
            finally
            {
                handle.Free();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TelemetryReader] Error reading: {ex.Message}");
            Disconnect();
            return false;
        }
    }

    public void Disconnect()
    {
        _connected = false;
        _stream?.Dispose();
        _stream = null;
        _mmf?.Dispose();
        _mmf = null;
        _rawBuffer = null;
        BrakePedal = 0;
        ThrottlePedal = 0;
        _frameBuilder.Reset();
        CurrentFrame = default;
    }

    public void Dispose()
    {
        Disconnect();
    }
}
