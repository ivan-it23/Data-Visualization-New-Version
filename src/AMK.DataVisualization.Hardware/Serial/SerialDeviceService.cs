using System.Buffers.Binary;
using System.IO.Ports;
using System.Runtime.InteropServices;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.Core.Binary;

namespace AMK.DataVisualization.Hardware.Serial;

public sealed class SerialDeviceService : IDisposable
{
    private const byte CommandData = 0x11;
    private const byte CommandMetrology = 0x13;
    private const byte CommandSignature = 0x16;
    private const int DefaultBaudRate = 125000;
    private const int MinimumPollInterval = 500;
    private const int MaximumPollInterval = 30000;
    private const int ReadTimeoutMilliseconds = 1000;
    private const int WriteTimeoutMilliseconds = 1000;
    private const int PacketHeaderSize = 3;
    private const int PacketCrcSize = 2;
    private const int MinimumPacketSize = PacketHeaderSize + PacketCrcSize;
    private const int MaximumPacketSize = 4096;
    private const int FailuresBeforeReopen = 8;

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
    private string recordFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private string appendFilePath = string.Empty;
    private bool recordingEnabled = true;
    private bool disposed;
    private SerialConnectionState state = SerialConnectionState.Disconnected;

    public event EventHandler<SerialFrameReceivedEventArgs>? FrameReceived;

    public event EventHandler<SerialStatusChangedEventArgs>? StatusChanged;

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
            recordFolder = string.IsNullOrWhiteSpace(folder)
                ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : folder;
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
        desiredPortName = portName.Trim();
        desiredBaudRate = baudRate;
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
        consecutiveFailures = 0;
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

    private SerialOperationResult ConnectCore(bool resetSession)
    {
        lock (ioSync)
        {
            CloseSerialPortCore();
            try
            {
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
                consecutiveFailures = 0;
                if (resetSession)
                {
                    Interlocked.Exchange(ref packetsReceived, 0);
                    Interlocked.Exchange(ref packetErrors, 0);
                    Interlocked.Exchange(ref recordedFrames, 0);
                    lastFrameNumber = 0;
                    recordFilePath = string.Empty;
                }

                lastError = string.Empty;
                return SerialOperationResult.Success;
            }
            catch (UnauthorizedAccessException)
            {
                CloseSerialPortCore();
                return SerialOperationResult.PortBusy;
            }
            catch (IOException)
            {
                CloseSerialPortCore();
                return SerialOperationResult.PortNotFound;
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                CloseSerialPortCore();
                return SerialOperationResult.ConnectionFailed;
            }
        }
    }

    private async Task PollingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
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
                Interlocked.Increment(ref packetsReceived);
                lastFrameNumber = frame.CanonicalData.Frame;
                WriteRecordCore(frame.CanonicalData);
                RaiseFrameReceived(frame, rawPayload);
                lastError = string.Empty;
            }
            else
            {
                consecutiveFailures++;
                Interlocked.Increment(ref packetErrors);
                lastError = ResultToDisplayText(result);
                if (consecutiveFailures >= FailuresBeforeReopen)
                {
                    lock (ioSync)
                    {
                        CloseSerialPortCore();
                    }
                    SetState(SerialConnectionState.Disconnected, "Связь потеряна, выполняется переподключение...");
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
            SerialOperationResult result = ExchangePacketCore(CommandData, [], out byte responseCommand, out byte[] payload);
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

    private void ProbeDeviceInformationCore()
    {
        lock (ioSync)
        {
            if (ExchangePacketCore(CommandSignature, [], out byte signatureCommand, out byte[] signaturePayload) == SerialOperationResult.Success
                && signatureCommand == CommandSignature
                && signaturePayload.Length >= sizeof(uint))
            {
                DeviceSignature = BinaryPrimitives.ReadUInt32LittleEndian(signaturePayload);
            }

            if (ExchangePacketCore(CommandMetrology, [], out byte metrologyCommand, out byte[] metrologyPayload) == SerialOperationResult.Success
                && metrologyCommand == CommandMetrology
                && metrologyPayload.Length == BinaryLayoutConstants.MetrologyDataSize)
            {
                Metrology = new DeviceMetrologySummary(
                    BinaryPrimitives.ReadUInt32LittleEndian(metrologyPayload.AsSpan(0, 4)),
                    BinaryPrimitives.ReadUInt32LittleEndian(metrologyPayload.AsSpan(4, 4)),
                    BinaryPrimitives.ReadUInt32LittleEndian(metrologyPayload.AsSpan(72, 4)));
            }
        }
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
            port.Write(packet, 0, packet.Length);

            byte[] header = ReadExactly(port, PacketHeaderSize);
            int totalLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(1, 2));
            if (totalLength < MinimumPacketSize || totalLength > MaximumPacketSize)
            {
                return SerialOperationResult.InvalidResponse;
            }

            byte[] tail = ReadExactly(port, totalLength - PacketHeaderSize);
            byte[] response = new byte[totalLength];
            header.CopyTo(response, 0);
            tail.CopyTo(response, PacketHeaderSize);
            if (!ValidateCrc(response))
            {
                return SerialOperationResult.CrcError;
            }

            responseCommand = response[0];
            responsePayload = response.AsSpan(PacketHeaderSize, totalLength - MinimumPacketSize).ToArray();
            return SerialOperationResult.Success;
        }
        catch (TimeoutException)
        {
            return SerialOperationResult.Timeout;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
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
