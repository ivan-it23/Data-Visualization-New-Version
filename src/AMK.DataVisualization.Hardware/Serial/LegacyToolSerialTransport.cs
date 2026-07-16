using System.Buffers.Binary;
using System.IO.Ports;

namespace AMK.DataVisualization.Hardware.Serial;

// Точный пакетный контракт LOOP_NEXT/function.cpp:
// [CMD:1][PAYLOAD:N][CRC_LO:1][CRC_HI:1], CRC-16/MODBUS по CMD + PAYLOAD.
internal static class LegacyToolSerialTransport
{
    private const int PacketOverhead = 3;
    private const int CrcSize = 2;

    public static SerialOperationResult Send(SerialPort port, byte command, ReadOnlySpan<byte> payload)
    {
        byte[] packet = BuildPacket(command, payload);
        DeviceProtocolLog.Write(
            $"LEGACY TX | port={port.PortName} | cmd=0x{command:X2} | payload_length={payload.Length} | " +
            $"packet_length={packet.Length} | packet={DeviceProtocolLog.FormatBytes(packet)}");

        try
        {
            port.Write(packet, 0, packet.Length);
            return SerialOperationResult.Success;
        }
        catch (Exception exception)
        {
            DeviceProtocolLog.Write(exception, $"LEGACY TX FAILED | cmd=0x{command:X2}");
            return exception is TimeoutException
                ? SerialOperationResult.Timeout
                : SerialOperationResult.ConnectionFailed;
        }
    }

    public static SerialOperationResult Receive(
        SerialPort port,
        int payloadLength,
        bool acceptZeroCrc,
        out byte responseCommand,
        out byte[] responsePayload)
    {
        responseCommand = 0;
        responsePayload = [];
        int totalLength = checked(payloadLength + PacketOverhead);
        byte[] packet = new byte[totalLength];
        int bytesRead = 0;
        DateTime deadline = DateTime.Now.AddMilliseconds(port.ReadTimeout);

        DeviceProtocolLog.Write(
            $"LEGACY RX START | port={port.PortName} | expected_payload={payloadLength} | " +
            $"expected_packet={totalLength} | timeout_ms={port.ReadTimeout} | bytes_available={port.BytesToRead} | " +
            $"accept_zero_crc={acceptZeroCrc}");

        try
        {
            while (bytesRead < totalLength)
            {
                int read = port.Read(packet, bytesRead, totalLength - bytesRead);
                DeviceProtocolLog.Write(
                    $"LEGACY RX CHUNK | read={read} | accumulated={bytesRead + Math.Max(read, 0)}/{totalLength}");
                if (read <= 0)
                {
                    break;
                }

                bytesRead += read;
                if (DateTime.Now > deadline)
                {
                    DeviceProtocolLog.Write(
                        $"LEGACY RX DEADLINE | accumulated={bytesRead}/{totalLength}");
                    break;
                }
            }
        }
        catch (Exception exception)
        {
            DeviceProtocolLog.Write(
                exception,
                $"LEGACY RX EXCEPTION | accumulated={bytesRead}/{totalLength} | " +
                $"partial={DeviceProtocolLog.FormatBytes(packet.AsSpan(0, bytesRead))}");
        }

        if (bytesRead < totalLength)
        {
            TryDiscardInput(port);
            DeviceProtocolLog.Write(
                $"LEGACY RX TIMEOUT_OR_SHORT | accumulated={bytesRead}/{totalLength} | " +
                $"partial={DeviceProtocolLog.FormatBytes(packet.AsSpan(0, bytesRead))}");
            return SerialOperationResult.Timeout;
        }

        ushort calculatedCrc = CalculateCrc16(packet.AsSpan(0, payloadLength + 1));
        ushort receivedCrc = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(payloadLength + 1, CrcSize));
        bool zeroCrcAccepted = acceptZeroCrc && receivedCrc == 0;
        if (calculatedCrc != receivedCrc && !zeroCrcAccepted)
        {
            DeviceProtocolLog.Write(
                $"LEGACY RX CRC_ERROR | cmd=0x{packet[0]:X2} | calculated=0x{calculatedCrc:X4} | " +
                $"received=0x{receivedCrc:X4} | packet={DeviceProtocolLog.FormatBytes(packet)}");
            return SerialOperationResult.CrcError;
        }

        responseCommand = packet[0];
        responsePayload = packet.AsSpan(1, payloadLength).ToArray();
        DeviceProtocolLog.Write(
            $"LEGACY RX OK | cmd=0x{responseCommand:X2} | payload_length={payloadLength} | " +
            $"calculated_crc=0x{calculatedCrc:X4} | received_crc=0x{receivedCrc:X4} | " +
            $"zero_crc_accepted={zeroCrcAccepted} | packet={DeviceProtocolLog.FormatBytes(packet)}");
        return SerialOperationResult.Success;
    }

    internal static byte[] BuildPacket(byte command, ReadOnlySpan<byte> payload)
    {
        byte[] packet = new byte[payload.Length + PacketOverhead];
        packet[0] = command;
        payload.CopyTo(packet.AsSpan(1));
        ushort crc = CalculateCrc16(packet.AsSpan(0, payload.Length + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(payload.Length + 1, CrcSize), crc);
        return packet;
    }

    internal static ushort CalculateCrc16(ReadOnlySpan<byte> data)
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

    private static void TryDiscardInput(SerialPort port)
    {
        try
        {
            port.DiscardInBuffer();
        }
        catch (Exception exception)
        {
            DeviceProtocolLog.Write(exception, "LEGACY RX DISCARD FAILED");
        }
    }
}
