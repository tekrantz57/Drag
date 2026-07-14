using System.IO.Ports;
using System.Text;

namespace DragWin;

public sealed class DragSerialClient : IDisposable
{
    private const int MaximumReceivedLineLength = 512;

    private readonly object syncRoot = new();
    private readonly object receiveSyncRoot = new();
    private readonly StringBuilder receiveBuffer = new();
    private readonly SerialLog log = new();
    private SerialPort? serialPort;
    private bool discardingOversizedLine;
    private string? lastTreeEventSignature;
    private long lastTreeEventAtMs;

    public event EventHandler<ProtocolMessage>? MessageReceived;
    public event EventHandler<string>? ProtocolError;

    public string LogPath => log.Path;

    public DateTimeOffset? LastFrameReceivedAt { get; private set; }

    public DateTimeOffset? LastHelloReceivedAt { get; private set; }

    public DateTimeOffset? LastHeartbeatReceivedAt { get; private set; }

    public bool IsConnected
    {
        get
        {
            lock (syncRoot)
            {
                return serialPort?.IsOpen == true;
            }
        }
    }

    public static string[] GetPortNames() =>
        SerialPort.GetPortNames()
            .OrderBy(port => port, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public void Connect(string portName, int baudRate = 115200)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);

        lock (syncRoot)
        {
            DisconnectCore();

            var port = new SerialPort(portName, baudRate)
            {
                NewLine = "\n",
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                // Opening a Mega serial port with DTR asserted can reset it.
                DtrEnable = false,
                RtsEnable = false
            };

            try
            {
                port.Open();
                port.DiscardInBuffer();
                port.DataReceived += SerialPortOnDataReceived;
                ResetReceiveBuffer();
                LastFrameReceivedAt = null;
                LastHelloReceivedAt = null;
                LastHeartbeatReceivedAt = null;
                lastTreeEventSignature = null;
                lastTreeEventAtMs = 0;
                serialPort = port;
                log.Info($"serial port open on {portName} at {baudRate} baud");
            }
            catch (Exception exception)
            {
                port.Dispose();
                log.Error(exception, $"serial open failed on {portName}");
                throw;
            }
        }
    }

    public void Disconnect()
    {
        lock (syncRoot)
        {
            DisconnectCore();
        }
    }

    public void Send(params string[] parts)
    {
        var line = ProtocolMessage.Create(parts).Encode();

        lock (syncRoot)
        {
            if (serialPort?.IsOpen != true)
            {
                throw new InvalidOperationException("The serial port is not connected.");
            }

            try
            {
                serialPort.WriteLine(line);
                log.Info($"TX {line}");
            }
            catch (Exception exception) when (
                exception is IOException or InvalidOperationException or TimeoutException)
            {
                log.Error(exception, "serial write failed");
                throw;
            }
        }
    }

    private void SerialPortOnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var port = (SerialPort)sender;

        try
        {
            var receivedText = port.ReadExisting();
            if (receivedText.Length == 0)
            {
                return;
            }

            var completedLines = new List<string>();
            var oversizedLineCount = 0;

            lock (receiveSyncRoot)
            {
                foreach (var character in receivedText)
                {
                    if (character == '\n')
                    {
                        if (discardingOversizedLine)
                        {
                            oversizedLineCount++;
                        }
                        else
                        {
                            completedLines.Add(receiveBuffer.ToString().TrimEnd('\r'));
                        }

                        receiveBuffer.Clear();
                        discardingOversizedLine = false;
                        continue;
                    }

                    if (discardingOversizedLine)
                    {
                        continue;
                    }

                    if (receiveBuffer.Length >= MaximumReceivedLineLength)
                    {
                        receiveBuffer.Clear();
                        discardingOversizedLine = true;
                        continue;
                    }

                    receiveBuffer.Append(character);
                }
            }

            for (var index = 0; index < oversizedLineCount; index++)
            {
                var error =
                    $"Received line exceeded {MaximumReceivedLineLength} characters and was discarded.";
                log.Warn(error);
                ProtocolError?.Invoke(this, error);
            }

            foreach (var line in completedLines)
            {
                ProcessReceivedLine(line);
            }
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException)
        {
            log.Error(exception, $"serial read failed on {port.PortName}");
            ProtocolError?.Invoke(this, exception.Message);
        }
    }

    private void ProcessReceivedLine(string line)
    {
        if (line.Length == 0)
        {
            return;
        }

        log.Raw(line);
        if (ProtocolMessage.TryParse(line, out var message, out var error))
        {
            UpdateControllerPresence(message!);
            if (!IsDuplicateTreeEvent(line, message!))
            {
                MessageReceived?.Invoke(this, message!);
            }
            return;
        }

        log.Warn($"rejected serial line '{line}': {error}");
        ProtocolError?.Invoke(this, $"{error} Raw line: {line}");
    }

    private void UpdateControllerPresence(ProtocolMessage message)
    {
        LastFrameReceivedAt = DateTimeOffset.Now;
        if (message.Type == "HELLO")
        {
            LastHelloReceivedAt = LastFrameReceivedAt;
        }
        else if (message.Type == "HEARTBEAT")
        {
            LastHeartbeatReceivedAt = LastFrameReceivedAt;
        }
    }

    private bool IsDuplicateTreeEvent(string line, ProtocolMessage message)
    {
        if (message.Parts.Count < 2 ||
            message.Type != "EVENT" ||
            message.Parts[1] != "TREE")
        {
            return false;
        }

        var signature = MessageSignatureWithoutMetadata(message);
        var nowMs = Environment.TickCount64;
        var duplicate =
            signature == lastTreeEventSignature &&
            nowMs - lastTreeEventAtMs <= 500;

        lastTreeEventSignature = signature;
        lastTreeEventAtMs = nowMs;
        return duplicate;
    }

    private static string MessageSignatureWithoutMetadata(ProtocolMessage message)
    {
        var metadataStart = message.Parts.Count;
        for (var index = 0; index < message.Parts.Count; index++)
        {
            if (message.Parts[index] is "SEQ" or "MS")
            {
                metadataStart = index;
                break;
            }
        }

        return string.Join(':', message.Parts.Take(metadataStart));
    }

    private void DisconnectCore()
    {
        if (serialPort is null)
        {
            return;
        }

        serialPort.DataReceived -= SerialPortOnDataReceived;
        var portName = serialPort.PortName;
        if (serialPort.IsOpen)
        {
            serialPort.Close();
        }

        serialPort.Dispose();
        serialPort = null;
        ResetReceiveBuffer();
        LastFrameReceivedAt = null;
        LastHelloReceivedAt = null;
        LastHeartbeatReceivedAt = null;
        lastTreeEventSignature = null;
        lastTreeEventAtMs = 0;
        log.Info($"serial port closed on {portName}");
    }

    private void ResetReceiveBuffer()
    {
        lock (receiveSyncRoot)
        {
            receiveBuffer.Clear();
            discardingOversizedLine = false;
        }
    }

    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }
}
