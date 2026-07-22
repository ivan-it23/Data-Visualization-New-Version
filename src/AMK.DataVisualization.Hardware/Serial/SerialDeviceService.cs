using System.Buffers.Binary;
using System.IO.Ports;
using System.Runtime.InteropServices;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.Core.Binary;

namespace AMK.DataVisualization.Hardware.Serial;

public sealed class SerialDeviceService : IDisposable
{
    private const byte CommandData = 0x11;
    private const byte CommandMetrology = 0x13;
    private const byte CommandSignature = 0x16;
    private const byte CommandSelectedKey = 0x17;
    private const byte CommandAirCalibration = 0x1A;
    private const int DefaultBaudRate = 125000;
    private const int MinimumPollInterval = 500;
    private const int MaximumPollInterval = 30000;
    private const int ReadTimeoutMilliseconds = 2000;
    private const int ControlReadTimeoutMilliseconds = 5000;
    private const int WriteTimeoutMilliseconds = 2000;
    private const int PacketHeaderSize = 3;
    private const int PacketCrcSize = 2;
    private const int MinimumPacketSize = PacketHeaderSize + PacketCrcSize;
    private const int MaximumPacketSize = 4096;
    private const int FailuresBeforeReopen = 8;
    // LOOP_NEXT принимает 242 байта payload: два служебных байта (на реальном
    // Cartograph 4tx это 0x05 0x06), затем 240 байт GP_TOOL_DATA/метрологии.
    private const int LegacyServicePrefixSize = 2;
    private const int LegacyPayloadSize = LegacyServicePrefixSize + BinaryLayoutConstants.GpToolDataSize;
    private const int LegacyMetrologyReadAttempts = 3;
    private const int LegacyFrameReadAttempts = 2;
    private const int LegacyPostOpenDelayMilliseconds = 150;
    private const int LegacyMetrologyPreReceiveDelayMilliseconds = 100;
    private const int LegacyRetryDelayMilliseconds = 100;
    private const int LegacyFramePreReceiveDelayMilliseconds = 50;
    private const int LegacyMetrologyWritePreReceiveDelayMilliseconds = 150;
    private const byte LegacyReadSubCommand = 0x05;
    private const byte LegacyWriteSubCommand = 0xF5;

    private readonly object ioSync = new();
    private readonly object lifecycleSync = new();
    private SerialPort? serialPort;
    private CancellationTokenSource? pollingCancellation;
    private Task? pollingTask;
    private FileStream? recordStream;
    private string desiredPortName = string.Empty;
    private int desiredBaudRate = DefaultBaudRate;
    private int pollIntervalMilliseconds = 2000;
    private int consecutiveFailures;
    private long packetsReceived;
    private long packetErrors;
    private long recordedFrames;
    private uint lastFrameNumber;
    private string lastError = string.Empty;
    private string recordFilePath = string.Empty;
    private string recordFolder = SerialRecordingFolder.DefaultPath;
    private string appendFilePath = string.Empty;
    private bool recordingEnabled = true;
    private bool useLegacyCartographProtocol;
    private bool disposed;
    private int controlOperationCount;
    private SerialConnectionState state = SerialConnectionState.Disconnected;

    public event EventHandler<SerialFrameReceivedEventArgs>? FrameReceived;

    public event EventHandler<SerialStatusChangedEventArgs>? StatusChanged;

    public SerialDeviceService()
    {
        SerialRecordingFolder.TryEnsureExists(recordFolder);
    }

    public SerialConnectionState State => state;

    public bool IsConnected => state == SerialConnectionState.Connected && serialPort?.IsOpen == true;

    public bool IsPolling
    {
        get
        {
            lock (lifecycleSync)
            {
                return pollingTask is { IsCompleted: false };
            }
        }
    }

    public string ConnectedPort => desiredPortName;

    public int PollIntervalMilliseconds => Volatile.Read(ref pollIntervalMilliseconds);

    public uint? DeviceSignature { get; private set; }

    public DeviceMetrologySummary? Metrology { get; private set; }

    public MetrologyData? CurrentMetrology { get; private set; }

    public static IReadOnlyList<string> GetAvailablePorts()
    {
        return SerialPort.GetPortNames()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetPortNumber)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void ConfigureRecording(bool enabled, string folder, string existingFilePath)
    {
        lock (ioSync)
        {
            recordingEnabled = enabled;
            recordFolder = SerialRecordingFolder.Resolve(folder);
            SerialRecordingFolder.TryEnsureExists(recordFolder);
            appendFilePath = existingFilePath ?? string.Empty;
            CloseRecordStreamCore();
            recordFilePath = string.Empty;
            Interlocked.Exchange(ref recordedFrames, 0);
        }

        PublishStatus();
    }

    public void SetPollInterval(int intervalMilliseconds)
    {
        Volatile.Write(ref pollIntervalMilliseconds, Math.Clamp(intervalMilliseconds, MinimumPollInterval, MaximumPollInterval));
        PublishStatus();
    }

    public async Task<SerialOperationResult> ConnectAsync(
        string portName,
        int baudRate = DefaultBaudRate,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(portName))
        {
            return SerialOperationResult.PortNotFound;
        }

        await StopPollingAsync().ConfigureAwait(false);
        Interlocked.Exchange(ref packetsReceived, 0);
        Interlocked.Exchange(ref packetErrors, 0);
        Interlocked.Exchange(ref recordedFrames, 0);
        lastFrameNumber = 0;
        desiredPortName = portName.Trim();
        desiredBaudRate = baudRate;
        DeviceProtocolLog.BeginSession(desiredPortName, desiredBaudRate);
        DeviceProtocolLog.Write($"CONNECT REQUEST | log={DeviceProtocolLog.LogPath}");
        SetState(SerialConnectionState.Connecting);

        SerialOperationResult result;
        try
        {
            result = await Task.Run(() => ConnectCore(resetSession: true), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = SerialOperationResult.Cancelled;
        }

        if (result != SerialOperationResult.Success)
        {
            SetState(SerialConnectionState.ConnectionError, ResultToDisplayText(result));
            return result;
        }

        SetState(SerialConnectionState.Connected);
        await Task.Run(ProbeDeviceInformationCore, cancellationToken).ConfigureAwait(false);
        PublishStatus();
        return SerialOperationResult.Success;
    }

    public async Task<bool> StartPollingAsync(int intervalMilliseconds, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await StopPollingAsync().ConfigureAwait(false);
        if (!IsConnected)
        {
            return false;
        }

        SetPollInterval(intervalMilliseconds);
        CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (lifecycleSync)
        {
            pollingCancellation = source;
            pollingTask = Task.Run(() => PollingLoopAsync(source.Token), CancellationToken.None);
        }

        PublishStatus();
        return true;
    }

    public async Task StopPollingAsync()
    {
        CancellationTokenSource? source;
        Task? task;
        lock (lifecycleSync)
        {
            source = pollingCancellation;
            task = pollingTask;
            pollingCancellation = null;
            pollingTask = null;
        }

        if (source is null)
        {
            return;
        }

        source.Cancel();
        try
        {
            if (task is not null)
            {
                await task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            source.Dispose();
            PublishStatus();
        }
    }

    public async Task DisconnectAsync()
    {
        await StopPollingAsync().ConfigureAwait(false);
        await Task.Run(() =>
        {
            lock (ioSync)
            {
                CloseSerialPortCore();
                CloseRecordStreamCore();
            }
        }).ConfigureAwait(false);
        DeviceSignature = null;
        Metrology = null;
        CurrentMetrology = null;
        useLegacyCartographProtocol = false;
        consecutiveFailures = 0;
        DeviceProtocolLog.Write("DISCONNECT COMPLETED");
        SetState(SerialConnectionState.Disconnected);
    }

    public SerialStatusSnapshot GetStatus()
    {
        return new SerialStatusSnapshot(
            state,
            desiredPortName,
            IsPolling,
            PollIntervalMilliseconds,
            Interlocked.Read(ref packetsReceived),
            Interlocked.Read(ref packetErrors),
            lastFrameNumber,
            lastError,
            DeviceSignature,
            Metrology,
            recordFilePath,
            Interlocked.Read(ref recordedFrames));
    }

    public static string ResultToDisplayText(SerialOperationResult result)
    {
        return result switch
        {
            SerialOperationResult.Success => "Успешно",
            SerialOperationResult.PortNotFound => "Порт не найден",
            SerialOperationResult.PortBusy => "Порт занят",
            SerialOperationResult.ConnectionFailed => "Ошибка подключения",
            SerialOperationResult.Timeout => "Таймаут ответа",
            SerialOperationResult.CrcError => "Ошибка CRC",
            SerialOperationResult.InvalidSignature => "Неверная сигнатура",
            SerialOperationResult.InvalidResponse => "Неверный ответ прибора",
            SerialOperationResult.NotConnected => "Прибор не подключён",
            SerialOperationResult.Cancelled => "Операция отменена",
            _ => "Неизвестная ошибка"
        };
    }

    public async Task<MetrologyReadResult> RequestMetrologyAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref controlOperationCount);
        try
        {
            MetrologyReadResult result = await Task.Run(RequestMetrologyCore, cancellationToken).ConfigureAwait(false);
            PublishStatus();
            return result;
        }
        catch (OperationCanceledException)
        {
            return new MetrologyReadResult(SerialOperationResult.Cancelled, default);
        }
        finally
        {
            Interlocked.Decrement(ref controlOperationCount);
        }
    }

    public async Task<SerialOperationResult> WriteMetrologyAsync(
        MetrologyData data,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref controlOperationCount);
        try
        {
            SerialOperationResult result = await Task.Run(() => WriteMetrologyCore(data), cancellationToken).ConfigureAwait(false);
            PublishStatus();
            return result;
        }
        catch (OperationCanceledException)
        {
            return SerialOperationResult.Cancelled;
        }
        finally
        {
            Interlocked.Decrement(ref controlOperationCount);
        }
    }

    public async Task<SerialOperationResult> SetSelectedKeyAsync(
        uint key,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref controlOperationCount);
        try
        {
            return await Task.Run(() => SetSelectedKeyCore(key), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SerialOperationResult.Cancelled;
        }
        finally
        {
            Interlocked.Decrement(ref controlOperationCount);
        }
    }

    public async Task<SerialOperationResult> RequestAirCalibrationAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref controlOperationCount);
        try
        {
            return await Task.Run(RequestAirCalibrationCore, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SerialOperationResult.Cancelled;
        }
        finally
        {
            Interlocked.Decrement(ref controlOperationCount);
        }
    }

    private SerialOperationResult ConnectCore(bool resetSession)
    {
        lock (ioSync)
        {
            CloseSerialPortCore();
            try
            {
                DeviceProtocolLog.Write(
                    $"PORT OPEN START | port={desiredPortName} | baud={desiredBaudRate} | " +
                    $"read_timeout={ReadTimeoutMilliseconds} | write_timeout={WriteTimeoutMilliseconds}");
                SerialPort port = new(desiredPortName, desiredBaudRate, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None,
                    DtrEnable = true,
                    RtsEnable = true,
                    ReadTimeout = ReadTimeoutMilliseconds,
                    WriteTimeout = WriteTimeoutMilliseconds,
                    ReadBufferSize = MaximumPacketSize,
                    WriteBufferSize = MaximumPacketSize
                };
                port.Open();
                port.DiscardInBuffer();
                port.DiscardOutBuffer();
                serialPort = port;
                Thread.Sleep(LegacyPostOpenDelayMilliseconds);
                consecutiveFailures = 0;
                if (resetSession)
                {
                    DeviceSignature = null;
                    Metrology = null;
                    CurrentMetrology = null;
                    useLegacyCartographProtocol = false;
                    Interlocked.Exchange(ref packetsReceived, 0);
                    Interlocked.Exchange(ref packetErrors, 0);
                    Interlocked.Exchange(ref recordedFrames, 0);
                    lastFrameNumber = 0;
                    recordFilePath = string.Empty;
                }

                lastError = string.Empty;
                DeviceProtocolLog.Write(
                    $"PORT OPEN OK | port={port.PortName} | dtr={port.DtrEnable} | rts={port.RtsEnable} | " +
                    $"data_bits={port.DataBits} | parity={port.Parity} | stop_bits={port.StopBits}");
                return SerialOperationResult.Success;
            }
            catch (UnauthorizedAccessException exception)
            {
                DeviceProtocolLog.Write(exception, "PORT OPEN BUSY");
                CloseSerialPortCore();
                return SerialOperationResult.PortBusy;
            }
            catch (IOException exception)
            {
                DeviceProtocolLog.Write(exception, "PORT OPEN IO FAILURE");
                CloseSerialPortCore();
                return SerialOperationResult.PortNotFound;
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                DeviceProtocolLog.Write(exception, "PORT OPEN FAILURE");
                CloseSerialPortCore();
                return SerialOperationResult.ConnectionFailed;
            }
        }
    }

    private async Task PollingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (Volatile.Read(ref controlOperationCount) > 0)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!IsConnected)
            {
                SerialOperationResult reconnect = ConnectCore(resetSession: false);
                if (reconnect == SerialOperationResult.Success)
                {
                    SetState(SerialConnectionState.Connected);
                    ProbeDeviceInformationCore();
                }
                else
                {
                    SetState(SerialConnectionState.Disconnected, "Ожидание переподключения...");
                }
            }

            SerialOperationResult result = RequestDataCore(out ParsedToolFrame frame, out byte[] rawPayload);
            if (result == SerialOperationResult.Success)
            {
                consecutiveFailures = 0;
                long acceptedPackets = Interlocked.Increment(ref packetsReceived);
                lastFrameNumber = frame.CanonicalData.Frame;
                WriteRecordCore(frame.CanonicalData);
                RaiseFrameReceived(frame, rawPayload);
                lastError = string.Empty;
                DeviceProtocolLog.Write(
                    $"POLL FRAME ACCEPTED | protocol={(useLegacyCartographProtocol ? "LEGACY_LOOP_NEXT" : "FRAMED_LEN")} | " +
                    $"accepted_total={acceptedPackets} | frame={frame.CanonicalData.Frame} | " +
                    $"signature=0x{frame.CanonicalData.Signature:X8}");
            }
            else
            {
                consecutiveFailures++;
                long rejectedPackets = Interlocked.Increment(ref packetErrors);
                lastError = ResultToDisplayText(result);
                DeviceProtocolLog.Write(
                    $"POLL FRAME REJECTED | protocol={(useLegacyCartographProtocol ? "LEGACY_LOOP_NEXT" : "FRAMED_LEN")} | " +
                    $"result={result} | rejected_total={rejectedPackets} | consecutive_failures={consecutiveFailures}");
                if (consecutiveFailures >= FailuresBeforeReopen)
                {
                    lock (ioSync)
                    {
                        CloseSerialPortCore();
                    }
                    SetState(SerialConnectionState.Disconnected, "Связь потеряна, выполняется переподключение...");
                    DeviceProtocolLog.Write("POLL REOPEN SCHEDULED | reason=consecutive_failures");
                    consecutiveFailures = 0;
                }
            }

            PublishStatus();
            await Task.Delay(PollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
        }
    }

    private SerialOperationResult RequestDataCore(out ParsedToolFrame frame, out byte[] rawPayload)
    {
        frame = default;
        rawPayload = [];
        lock (ioSync)
        {
            if (useLegacyCartographProtocol)
            {
                return RequestLegacyCartographDataCore(out frame, out rawPayload);
            }

            DeviceProtocolLog.Write("DATA ROUTE | protocol=FRAMED_LEN");
            SerialOperationResult result = ExchangePacketCore(
                CommandData,
                [],
                out byte responseCommand,
                out byte[] payload);
            if (result != SerialOperationResult.Success)
            {
                return result;
            }

            if (responseCommand != CommandData || payload.Length != BinaryLayoutConstants.GpToolDataSize)
            {
                return SerialOperationResult.InvalidResponse;
            }

            if (!ToolDataAdapterRegistry.Default.TryParseRawFrame(payload, out frame))
            {
                return SerialOperationResult.InvalidSignature;
            }

            rawPayload = payload;
            return SerialOperationResult.Success;
        }
    }

    private SerialOperationResult RequestLegacyCartographDataCore(
        out ParsedToolFrame frame,
        out byte[] rawPayload)
    {
        frame = default;
        rawPayload = [];
        SerialPort? port = serialPort;
        if (port?.IsOpen != true)
        {
            return SerialOperationResult.NotConnected;
        }

        Span<byte> request = stackalloc byte[] { LegacyReadSubCommand, 0x00 };
        SerialOperationResult lastResult = SerialOperationResult.Timeout;
        DeviceProtocolLog.Write(
            $"DATA ROUTE | protocol=LEGACY_LOOP_NEXT | expected_payload={LegacyPayloadSize} | attempts={LegacyFrameReadAttempts}");

        for (int attempt = 1; attempt <= LegacyFrameReadAttempts; attempt++)
        {
            TryDiscardBuffers(port, discardOutput: false);
            DeviceProtocolLog.Write($"LEGACY DATA ATTEMPT | attempt={attempt}/{LegacyFrameReadAttempts}");
            SerialOperationResult sendResult = LegacyToolSerialTransport.Send(port, CommandData, request);
            if (sendResult != SerialOperationResult.Success)
            {
                lastResult = sendResult;
                break;
            }

            Thread.Sleep(LegacyFramePreReceiveDelayMilliseconds);
            SerialOperationResult receiveResult = LegacyToolSerialTransport.Receive(
                port,
                LegacyPayloadSize,
                acceptZeroCrc: true,
                out byte responseCommand,
                out byte[] payload);
            lastResult = receiveResult;
            if (receiveResult == SerialOperationResult.Timeout && attempt < LegacyFrameReadAttempts)
            {
                DeviceProtocolLog.Write("LEGACY DATA RETRY | reason=timeout");
                continue;
            }

            if (receiveResult != SerialOperationResult.Success)
            {
                return receiveResult;
            }

            DeviceProtocolLog.Write(
                $"LEGACY DATA PAYLOAD | response_cmd=0x{responseCommand:X2} | " +
                $"service={Convert.ToHexString(payload.AsSpan(0, LegacyServicePrefixSize))}");
            ReadOnlySpan<byte> canonicalPayload = payload.AsSpan(
                LegacyServicePrefixSize,
                BinaryLayoutConstants.GpToolDataSize);
            if (!ToolDataAdapterRegistry.Default.TryParseRawFrame(canonicalPayload, out frame))
            {
                uint receivedSignature = BinaryPrimitives.ReadUInt32LittleEndian(canonicalPayload);
                DeviceProtocolLog.Write(
                    $"LEGACY DATA REJECTED | reason=unsupported_signature | signature=0x{receivedSignature:X8}");
                return SerialOperationResult.InvalidSignature;
            }

            uint expectedSignature = CurrentMetrology?.Signature ?? DeviceSignature ?? 0u;
            if (expectedSignature == 0u || frame.CanonicalData.Signature != expectedSignature)
            {
                DeviceProtocolLog.Write(
                    $"LEGACY DATA REJECTED | reason=signature_mismatch | expected=0x{expectedSignature:X8} | " +
                    $"received=0x{frame.CanonicalData.Signature:X8}");
                return SerialOperationResult.CrcError;
            }

            ToolIdentity identity = UniversalCanonicalAdapter.BuildIdentity(frame.CanonicalData.Signature);
            if (!identity.IsCartographLwd4TxMode)
            {
                DeviceProtocolLog.Write(
                    $"LEGACY DATA REJECTED | reason=not_cartograph_4tx | tool_code={identity.ToolCode} | " +
                    $"type={identity.DeviceType} | tx={identity.TransmitterCount} | modification={identity.Modification}");
                return SerialOperationResult.InvalidSignature;
            }

            rawPayload = canonicalPayload.ToArray();
            DeviceProtocolLog.Write(
                $"LEGACY DATA ACCEPTED | signature=0x{frame.CanonicalData.Signature:X8} | " +
                $"tool_code={identity.ToolCode} | type={identity.DeviceType} | tx={identity.TransmitterCount} | " +
                $"modification={identity.Modification} | frame={frame.CanonicalData.Frame}");
            return SerialOperationResult.Success;
        }

        return lastResult;
    }

    private void ProbeDeviceInformationCore()
    {
        lock (ioSync)
        {
            DeviceProtocolLog.Write(
                $"PROBE START | previous_protocol={(useLegacyCartographProtocol ? "LEGACY_LOOP_NEXT" : "UNKNOWN_OR_FRAMED")}");
            if (useLegacyCartographProtocol)
            {
                ConfigureLegacyLineSignals(serialPort);
                MetrologyReadResult knownLegacyResult = ReadLegacyMetrologyCore(
                    preReceiveDelayMilliseconds: LegacyMetrologyPreReceiveDelayMilliseconds);
                if (knownLegacyResult.IsSuccess && TryActivateLegacyCartographMode(knownLegacyResult.Data, "reconnect"))
                {
                    return;
                }

                DeviceProtocolLog.Write(
                    $"PROBE LEGACY RECONNECT FAILED | result={knownLegacyResult.Result}; continuing with full detection");
                useLegacyCartographProtocol = false;
                ConfigureFramedLineSignals(serialPort);
            }

            SerialOperationResult signatureResult = ExchangePacketCore(
                CommandSignature,
                [],
                out byte signatureCommand,
                out byte[] signaturePayload);
            DeviceProtocolLog.Write(
                $"PROBE FRAMED SIGNATURE | result={signatureResult} | response_cmd=0x{signatureCommand:X2} | " +
                $"payload_length={signaturePayload.Length}");
            if (signatureResult == SerialOperationResult.Success
                && signatureCommand == CommandSignature
                && signaturePayload.Length >= sizeof(uint))
            {
                DeviceSignature = BinaryPrimitives.ReadUInt32LittleEndian(signaturePayload);
                LogIdentity("PROBE FRAMED SIGNATURE DECODED", DeviceSignature.Value);
            }

            SerialOperationResult metrologyResult = ExchangePacketCore(
                CommandMetrology,
                [],
                out byte metrologyCommand,
                out byte[] metrologyPayload);
            DeviceProtocolLog.Write(
                $"PROBE FRAMED METROLOGY | result={metrologyResult} | response_cmd=0x{metrologyCommand:X2} | " +
                $"payload_length={metrologyPayload.Length}");
            if (metrologyResult == SerialOperationResult.Success
                && metrologyCommand == CommandMetrology
                && metrologyPayload.Length == BinaryLayoutConstants.MetrologyDataSize)
            {
                MetrologyData data = MemoryMarshal.Read<MetrologyData>(metrologyPayload);
                UpdateMetrologyCache(data);
                ToolIdentity identity = LogIdentity("PROBE FRAMED METROLOGY DECODED", data.Signature);
                if (identity.IsCartographLwd4TxMode)
                {
                    ConfigureLegacyLineSignals(serialPort);
                    useLegacyCartographProtocol = true;
                    DeviceProtocolLog.Write(
                        "PROTOCOL SELECTED | protocol=LEGACY_LOOP_NEXT | source=framed_metrology_signature");
                }

                return;
            }

            DeviceProtocolLog.Write(
                "PROBE FALLBACK | target=LEGACY_LOOP_NEXT | reason=framed_metrology_not_available");
            ConfigureLegacyLineSignals(serialPort);
            TryDiscardBuffers(serialPort, discardOutput: true);
            MetrologyReadResult legacyResult = ReadLegacyMetrologyCore(
                preReceiveDelayMilliseconds: LegacyMetrologyPreReceiveDelayMilliseconds);
            if (legacyResult.IsSuccess && TryActivateLegacyCartographMode(legacyResult.Data, "legacy_metrology"))
            {
                return;
            }

            DeviceProtocolLog.Write(
                $"PROBE FINISHED WITHOUT LEGACY ACTIVATION | legacy_result={legacyResult.Result} | " +
                $"device_signature={(DeviceSignature is uint value ? $"0x{value:X8}" : "unknown")}");
            ConfigureFramedLineSignals(serialPort);
        }
    }

    private MetrologyReadResult RequestMetrologyCore()
    {
        lock (ioSync)
        {
            if (useLegacyCartographProtocol)
            {
                MetrologyReadResult legacyResult = ReadLegacyMetrologyCore(preReceiveDelayMilliseconds: 0);
                if (!legacyResult.IsSuccess)
                {
                    return legacyResult;
                }

                if (!TryActivateLegacyCartographMode(legacyResult.Data, "manual_metrology_read"))
                {
                    return new MetrologyReadResult(SerialOperationResult.InvalidSignature, default);
                }

                return legacyResult;
            }

            SerialOperationResult result = ExchangeControlPacketCore(
                CommandMetrology,
                [],
                out byte responseCommand,
                out byte[] payload);
            if (result != SerialOperationResult.Success)
            {
                return new MetrologyReadResult(result, default);
            }

            if (responseCommand != CommandMetrology || payload.Length != BinaryLayoutConstants.MetrologyDataSize)
            {
                return new MetrologyReadResult(SerialOperationResult.InvalidResponse, default);
            }

            MetrologyData data = MemoryMarshal.Read<MetrologyData>(payload);
            UpdateMetrologyCache(data);
            return new MetrologyReadResult(SerialOperationResult.Success, data);
        }
    }

    private SerialOperationResult WriteMetrologyCore(MetrologyData data)
    {
        if (!IsConnected)
        {
            return SerialOperationResult.NotConnected;
        }

        Thread.Sleep(2000);
        byte[] bytes = new byte[BinaryLayoutConstants.MetrologyDataSize];
        MemoryMarshal.Write(bytes.AsSpan(), in data);
        MetrologyData applied = data;

        lock (ioSync)
        {
            if (useLegacyCartographProtocol)
            {
                SerialOperationResult legacyWriteResult = WriteLegacyMetrologyCore(data);
                if (legacyWriteResult != SerialOperationResult.Success)
                {
                    return legacyWriteResult;
                }

                UpdateMetrologyCache(data);
            }
            else
            {
                SerialOperationResult result = ExchangeControlPacketCore(
                    CommandMetrology,
                    bytes,
                    out byte responseCommand,
                    out byte[] responsePayload);
                if (result != SerialOperationResult.Success)
                {
                    return result;
                }

                if (responseCommand != CommandMetrology
                    || responsePayload.Length is not (0 or 1 or BinaryLayoutConstants.MetrologyDataSize))
                {
                    return SerialOperationResult.InvalidResponse;
                }

                if (responsePayload.Length == BinaryLayoutConstants.MetrologyDataSize)
                {
                    applied = MemoryMarshal.Read<MetrologyData>(responsePayload);
                }

                UpdateMetrologyCache(applied);
            }
        }

        Thread.Sleep(300);
        MetrologyReadResult verification = RequestMetrologyCore();
        if (verification.IsSuccess
            && (verification.Data.Signature != data.Signature || verification.Data.Serial != data.Serial))
        {
            return SerialOperationResult.InvalidResponse;
        }

        return SerialOperationResult.Success;
    }

    private SerialOperationResult SetSelectedKeyCore(uint key)
    {
        if (useLegacyCartographProtocol)
        {
            DeviceProtocolLog.Write(
                $"LEGACY CONTROL REJECTED | command=0x{CommandSelectedKey:X2} | reason=no_source_backed_LOOP_NEXT_contract");
            return SerialOperationResult.InvalidResponse;
        }

        Span<byte> payload = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, key);
        lock (ioSync)
        {
            SerialOperationResult result = ExchangeControlPacketCore(
                CommandSelectedKey,
                payload,
                out byte responseCommand,
                out _);
            if (result == SerialOperationResult.Timeout)
            {
                return SerialOperationResult.Success;
            }

            return result == SerialOperationResult.Success && responseCommand != CommandSelectedKey
                ? SerialOperationResult.InvalidResponse
                : result;
        }
    }

    private SerialOperationResult RequestAirCalibrationCore()
    {
        if (!IsConnected)
        {
            return SerialOperationResult.NotConnected;
        }

        Thread.Sleep(2000);
        if (useLegacyCartographProtocol)
        {
            DeviceProtocolLog.Write(
                $"LEGACY CONTROL REJECTED | command=0x{CommandAirCalibration:X2} | reason=no_source_backed_LOOP_NEXT_contract");
            return SerialOperationResult.InvalidResponse;
        }

        lock (ioSync)
        {
            SerialOperationResult result = ExchangeControlPacketCore(
                CommandAirCalibration,
                [],
                out byte responseCommand,
                out _);
            return result == SerialOperationResult.Success && responseCommand != CommandAirCalibration
                ? SerialOperationResult.InvalidResponse
                : result;
        }
    }

    private MetrologyReadResult ReadLegacyMetrologyCore(int preReceiveDelayMilliseconds)
    {
        SerialPort? port = serialPort;
        if (port?.IsOpen != true)
        {
            return new MetrologyReadResult(SerialOperationResult.NotConnected, default);
        }

        Span<byte> request = stackalloc byte[] { LegacyReadSubCommand, 0x00 };
        SerialOperationResult lastResult = SerialOperationResult.Timeout;
        DeviceProtocolLog.Write(
            $"LEGACY METROLOGY START | attempts={LegacyMetrologyReadAttempts} | " +
            $"pre_receive_delay_ms={preReceiveDelayMilliseconds} | expected_payload={LegacyPayloadSize}");

        for (int attempt = 1; attempt <= LegacyMetrologyReadAttempts; attempt++)
        {
            TryDiscardBuffers(port, discardOutput: false);
            DeviceProtocolLog.Write(
                $"LEGACY METROLOGY ATTEMPT | attempt={attempt}/{LegacyMetrologyReadAttempts}");
            SerialOperationResult sendResult = LegacyToolSerialTransport.Send(port, CommandMetrology, request);
            if (sendResult != SerialOperationResult.Success)
            {
                lastResult = sendResult;
            }
            else
            {
                if (preReceiveDelayMilliseconds > 0)
                {
                    Thread.Sleep(preReceiveDelayMilliseconds);
                }

                SerialOperationResult receiveResult = LegacyToolSerialTransport.Receive(
                    port,
                    LegacyPayloadSize,
                    acceptZeroCrc: false,
                    out byte responseCommand,
                    out byte[] payload);
                lastResult = receiveResult;
                if (receiveResult == SerialOperationResult.Success)
                {
                    DeviceProtocolLog.Write(
                        $"LEGACY METROLOGY PAYLOAD | response_cmd=0x{responseCommand:X2} | " +
                        $"service={Convert.ToHexString(payload.AsSpan(0, LegacyServicePrefixSize))}");
                    MetrologyData data = MemoryMarshal.Read<MetrologyData>(
                        payload.AsSpan(LegacyServicePrefixSize, BinaryLayoutConstants.MetrologyDataSize));
                    LogIdentity("LEGACY METROLOGY DECODED", data.Signature);
                    return new MetrologyReadResult(SerialOperationResult.Success, data);
                }
            }

            DeviceProtocolLog.Write(
                $"LEGACY METROLOGY ATTEMPT FAILED | attempt={attempt} | result={lastResult}");
            if (attempt < LegacyMetrologyReadAttempts)
            {
                TryDiscardBuffers(port, discardOutput: false);
                Thread.Sleep(LegacyRetryDelayMilliseconds);
            }
        }

        return new MetrologyReadResult(lastResult, default);
    }

    private SerialOperationResult WriteLegacyMetrologyCore(MetrologyData data)
    {
        SerialPort? port = serialPort;
        if (port?.IsOpen != true)
        {
            return SerialOperationResult.NotConnected;
        }

        byte[] request = new byte[LegacyPayloadSize];
        request[0] = LegacyWriteSubCommand;
        request[1] = 0x00;
        MemoryMarshal.Write(request.AsSpan(LegacyServicePrefixSize), in data);
        TryDiscardBuffers(port, discardOutput: true);
        DeviceProtocolLog.Write(
            $"LEGACY METROLOGY WRITE START | signature=0x{data.Signature:X8} | serial={data.Serial} | " +
            $"payload_length={request.Length}");

        SerialOperationResult sendResult = LegacyToolSerialTransport.Send(port, CommandMetrology, request);
        if (sendResult != SerialOperationResult.Success)
        {
            return sendResult;
        }

        Thread.Sleep(LegacyMetrologyWritePreReceiveDelayMilliseconds);
        SerialOperationResult receiveResult = LegacyToolSerialTransport.Receive(
            port,
            payloadLength: LegacyServicePrefixSize,
            acceptZeroCrc: false,
            out byte responseCommand,
            out byte[] responsePayload);
        DeviceProtocolLog.Write(
            $"LEGACY METROLOGY WRITE RESULT | result={receiveResult} | response_cmd=0x{responseCommand:X2} | " +
            $"response_payload={DeviceProtocolLog.FormatBytes(responsePayload)}");
        return receiveResult;
    }

    private bool TryActivateLegacyCartographMode(MetrologyData data, string source)
    {
        ToolIdentity identity = LogIdentity($"LEGACY ACTIVATION CHECK | source={source}", data.Signature);
        if (!identity.IsCartographLwd4TxMode)
        {
            DeviceProtocolLog.Write(
                $"LEGACY ACTIVATION REJECTED | source={source} | tool_code={identity.ToolCode} | " +
                $"type={identity.DeviceType} | tx={identity.TransmitterCount} | modification={identity.Modification}");
            return false;
        }

        DeviceSignature = data.Signature;
        UpdateMetrologyCache(data);
        useLegacyCartographProtocol = true;
        DeviceProtocolLog.Write(
            $"PROTOCOL SELECTED | protocol=LEGACY_LOOP_NEXT | source={source} | signature=0x{data.Signature:X8} | " +
            $"tool_code={identity.ToolCode} | type={identity.DeviceType} | tx={identity.TransmitterCount} | " +
            $"modification={identity.Modification} | modification_used_for_routing=false");
        return true;
    }

    private static ToolIdentity LogIdentity(string operation, uint signature)
    {
        ToolIdentity identity = UniversalCanonicalAdapter.BuildIdentity(signature);
        DeviceProtocolLog.Write(
            $"{operation} | signature=0x{signature:X8} | tool_code={identity.ToolCode} | " +
            $"type={identity.DeviceType} | tx={identity.TransmitterCount} | modification={identity.Modification} | " +
            $"cartograph_lwd_4tx={identity.IsCartographLwd4TxMode}");
        return identity;
    }

    private static void TryDiscardBuffers(SerialPort? port, bool discardOutput)
    {
        if (port?.IsOpen != true)
        {
            return;
        }

        try
        {
            int bytesAvailable = port.BytesToRead;
            port.DiscardInBuffer();
            if (discardOutput)
            {
                port.DiscardOutBuffer();
            }

            DeviceProtocolLog.Write(
                $"PORT BUFFERS DISCARDED | input_bytes={bytesAvailable} | output={discardOutput}");
        }
        catch (Exception exception)
        {
            DeviceProtocolLog.Write(exception, "PORT BUFFER DISCARD FAILED");
        }
    }

    private static void ConfigureLegacyLineSignals(SerialPort? port)
    {
        ConfigureLineSignals(port, dtrEnable: false, rtsEnable: false, protocol: "LEGACY_LOOP_NEXT");
    }

    private static void ConfigureFramedLineSignals(SerialPort? port)
    {
        ConfigureLineSignals(port, dtrEnable: true, rtsEnable: true, protocol: "FRAMED_LEN");
    }

    private static void ConfigureLineSignals(
        SerialPort? port,
        bool dtrEnable,
        bool rtsEnable,
        string protocol)
    {
        if (port?.IsOpen != true)
        {
            return;
        }

        try
        {
            if (port.DtrEnable == dtrEnable && port.RtsEnable == rtsEnable)
            {
                DeviceProtocolLog.Write(
                    $"PORT LINE SIGNALS UNCHANGED | protocol={protocol} | dtr={dtrEnable} | rts={rtsEnable}");
                return;
            }

            port.DtrEnable = dtrEnable;
            port.RtsEnable = rtsEnable;
            TryDiscardBuffers(port, discardOutput: true);
            Thread.Sleep(LegacyPostOpenDelayMilliseconds);
            DeviceProtocolLog.Write(
                $"PORT LINE SIGNALS APPLIED | protocol={protocol} | dtr={port.DtrEnable} | rts={port.RtsEnable} | " +
                $"settle_delay_ms={LegacyPostOpenDelayMilliseconds}");
        }
        catch (Exception exception)
        {
            DeviceProtocolLog.Write(
                exception,
                $"PORT LINE SIGNALS FAILED | protocol={protocol} | requested_dtr={dtrEnable} | requested_rts={rtsEnable}");
        }
    }

    private SerialOperationResult ExchangeControlPacketCore(
        byte command,
        ReadOnlySpan<byte> payload,
        out byte responseCommand,
        out byte[] responsePayload)
    {
        SerialPort? port = serialPort;
        if (port?.IsOpen != true)
        {
            responseCommand = 0;
            responsePayload = [];
            return SerialOperationResult.NotConnected;
        }

        int previousTimeout = port.ReadTimeout;
        try
        {
            port.ReadTimeout = ControlReadTimeoutMilliseconds;
            return ExchangePacketCore(command, payload, out responseCommand, out responsePayload);
        }
        finally
        {
            if (port.IsOpen)
            {
                port.ReadTimeout = previousTimeout;
            }
        }
    }

    private void UpdateMetrologyCache(MetrologyData data)
    {
        CurrentMetrology = data;
        Metrology = new DeviceMetrologySummary(data.Signature, data.Serial, data.SondeDiameterMm);
    }

    private SerialOperationResult ExchangePacketCore(
        byte command,
        ReadOnlySpan<byte> payload,
        out byte responseCommand,
        out byte[] responsePayload)
    {
        responseCommand = 0;
        responsePayload = [];
        SerialPort? port = serialPort;
        if (port?.IsOpen != true)
        {
            return SerialOperationResult.NotConnected;
        }

        try
        {
            port.DiscardInBuffer();
            port.DiscardOutBuffer();
            byte[] packet = BuildPacket(command, payload);
            DeviceProtocolLog.Write(
                $"FRAMED TX | port={port.PortName} | cmd=0x{command:X2} | payload_length={payload.Length} | " +
                $"packet_length={packet.Length} | packet={DeviceProtocolLog.FormatBytes(packet)}");
            port.Write(packet, 0, packet.Length);

            byte[] header = ReadExactly(port, PacketHeaderSize);
            int totalLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(1, 2));
            DeviceProtocolLog.Write(
                $"FRAMED RX HEADER | header={Convert.ToHexString(header)} | declared_total_length={totalLength}");
            if (totalLength < MinimumPacketSize || totalLength > MaximumPacketSize)
            {
                DeviceProtocolLog.Write(
                    $"FRAMED RX REJECTED | reason=invalid_length | declared_total_length={totalLength}");
                return SerialOperationResult.InvalidResponse;
            }

            byte[] tail = ReadExactly(port, totalLength - PacketHeaderSize);
            byte[] response = new byte[totalLength];
            header.CopyTo(response, 0);
            tail.CopyTo(response, PacketHeaderSize);
            if (!ValidateCrc(response))
            {
                ushort calculated = CalculateCrc16(response.AsSpan(0, totalLength - PacketCrcSize));
                ushort received = BinaryPrimitives.ReadUInt16LittleEndian(response.AsSpan(totalLength - PacketCrcSize));
                DeviceProtocolLog.Write(
                    $"FRAMED RX CRC_ERROR | calculated=0x{calculated:X4} | received=0x{received:X4} | " +
                    $"packet={DeviceProtocolLog.FormatBytes(response)}");
                return SerialOperationResult.CrcError;
            }

            responseCommand = response[0];
            responsePayload = response.AsSpan(PacketHeaderSize, totalLength - MinimumPacketSize).ToArray();
            DeviceProtocolLog.Write(
                $"FRAMED RX OK | cmd=0x{responseCommand:X2} | payload_length={responsePayload.Length} | " +
                $"packet={DeviceProtocolLog.FormatBytes(response)}");
            return SerialOperationResult.Success;
        }
        catch (TimeoutException exception)
        {
            DeviceProtocolLog.Write(exception, $"FRAMED RX TIMEOUT | cmd=0x{command:X2}");
            return SerialOperationResult.Timeout;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            DeviceProtocolLog.Write(exception, $"FRAMED EXCHANGE FAILED | cmd=0x{command:X2}");
            return SerialOperationResult.ConnectionFailed;
        }
    }

    private void WriteRecordCore(GpToolData canonicalData)
    {
        if (!recordingEnabled)
        {
            return;
        }

        lock (ioSync)
        {
            try
            {
                recordStream ??= OpenRecordStreamCore();
                Span<byte> buffer = stackalloc byte[BinaryLayoutConstants.GpToolDataSize];
                MemoryMarshal.Write(buffer, in canonicalData);
                recordStream.Write(buffer);
                recordStream.Flush();
                Interlocked.Increment(ref recordedFrames);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastError = $"Ошибка записи: {exception.Message}";
                CloseRecordStreamCore();
            }
        }
    }

    private FileStream OpenRecordStreamCore()
    {
        if (!string.IsNullOrWhiteSpace(appendFilePath))
        {
            FileInfo existing = new(appendFilePath);
            if (!existing.Exists || existing.Length % BinaryLayoutConstants.GpToolDataSize != 0)
            {
                throw new IOException("Файл дозаписи отсутствует или имеет неверный размер.");
            }

            recordFilePath = existing.FullName;
            return new FileStream(existing.FullName, FileMode.Append, FileAccess.Write, FileShare.Read);
        }

        Directory.CreateDirectory(recordFolder);
        string identity = Metrology is { } metrology
            ? $"{metrology.SerialNumber}_{metrology.DiameterMillimeters}"
            : "Unknown";
        recordFilePath = Path.Combine(recordFolder, $"AMK_{identity}_{DateTime.Now:yyyyMMdd_HHmmss}.bin");
        return new FileStream(recordFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    }

    private static byte[] BuildPacket(byte command, ReadOnlySpan<byte> payload)
    {
        int totalLength = payload.Length + MinimumPacketSize;
        byte[] packet = new byte[totalLength];
        packet[0] = command;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(1, 2), checked((ushort)totalLength));
        payload.CopyTo(packet.AsSpan(PacketHeaderSize));
        ushort crc = CalculateCrc16(packet.AsSpan(0, totalLength - PacketCrcSize));
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(totalLength - PacketCrcSize), crc);
        return packet;
    }

    private static byte[] ReadExactly(SerialPort port, int count)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = port.Read(buffer, offset, count - offset);
            DeviceProtocolLog.Write(
                $"FRAMED RX CHUNK | read={read} | accumulated={offset + Math.Max(read, 0)}/{count}");
            if (read <= 0)
            {
                throw new TimeoutException();
            }

            offset += read;
        }

        return buffer;
    }

    private static ushort CalculateCrc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
            }
        }

        return crc;
    }

    private static bool ValidateCrc(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < MinimumPacketSize)
        {
            return false;
        }

        ushort calculated = CalculateCrc16(packet[..^PacketCrcSize]);
        ushort received = BinaryPrimitives.ReadUInt16LittleEndian(packet[^PacketCrcSize..]);
        return calculated == received;
    }

    private static int GetPortNumber(string portName)
    {
        return portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(portName.AsSpan(3), out int number)
                ? number
                : int.MaxValue;
    }

    private void SetState(SerialConnectionState newState, string error = "")
    {
        state = newState;
        lastError = error;
        PublishStatus();
    }

    private void PublishStatus()
    {
        SerialStatusChangedEventArgs args = new(GetStatus());
        try
        {
            StatusChanged?.Invoke(this, args);
        }
        catch
        {
        }
    }

    private void RaiseFrameReceived(ParsedToolFrame frame, byte[] rawPayload)
    {
        try
        {
            FrameReceived?.Invoke(this, new SerialFrameReceivedEventArgs(frame, rawPayload));
        }
        catch
        {
        }
    }

    private void CloseSerialPortCore()
    {
        if (serialPort is null)
        {
            return;
        }

        try
        {
            if (serialPort.IsOpen)
            {
                DeviceProtocolLog.Write(
                    $"PORT CLOSE START | port={serialPort.PortName} | " +
                    $"protocol={(useLegacyCartographProtocol ? "LEGACY_LOOP_NEXT" : "FRAMED_LEN")}");
                serialPort.Close();
            }
        }
        catch
        {
        }
        finally
        {
            serialPort.Dispose();
            serialPort = null;
            DeviceProtocolLog.Write("PORT CLOSE COMPLETED");
        }
    }

    private void CloseRecordStreamCore()
    {
        recordStream?.Dispose();
        recordStream = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CancellationTokenSource? source;
        lock (lifecycleSync)
        {
            source = pollingCancellation;
            pollingCancellation = null;
            pollingTask = null;
        }
        source?.Cancel();
        source?.Dispose();
        lock (ioSync)
        {
            CloseRecordStreamCore();
            CloseSerialPortCore();
        }
    }
}
