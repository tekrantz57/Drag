using System.IO.Ports;

namespace DragWin;

public sealed class DragSerialClient : IDisposable
{
    private readonly object syncRoot = new();
    private readonly object receiveSyncRoot = new();
    private readonly SerialLog log = new();
    private SerialPort? serialPort;
    private SerialReceiveQueue? receiveQueue;
    private string? lastTreeEventSignature;
    private long lastTreeEventAtMs;
    private ControllerIdentity? currentControllerIdentity;

    public event EventHandler<ProtocolMessage>? MessageReceived;
    public event EventHandler<string>? ProtocolError;

    public string LogPath => log.Path;

    public DateTimeOffset? LastFrameReceivedAt { get; private set; }

    public DateTimeOffset? LastHelloReceivedAt { get; private set; }

    public DateTimeOffset? LastHeartbeatReceivedAt { get; private set; }

    public ControllerIdentity? CurrentControllerIdentity =>
        Volatile.Read(ref currentControllerIdentity);

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
                LastFrameReceivedAt = null;
                LastHelloReceivedAt = null;
                LastHeartbeatReceivedAt = null;
                Volatile.Write(ref currentControllerIdentity, null);
                lastTreeEventSignature = null;
                lastTreeEventAtMs = 0;
                serialPort = port;
                receiveQueue = new SerialReceiveQueue(
                    line => { if (ReferenceEquals(Volatile.Read(ref serialPort), port)) ProcessReceivedLine(line); },
                    error => { if (ReferenceEquals(Volatile.Read(ref serialPort), port)) ReportReceiveError(error); });
                port.DataReceived += SerialPortOnDataReceived;
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

    public void SendBatch(IEnumerable<string[]> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var lines = commands
            .Select(parts => ProtocolMessage.Create(parts).Encode())
            .ToArray();
        if (lines.Length == 0)
        {
            return;
        }

        lock (syncRoot)
        {
            if (serialPort?.IsOpen != true)
            {
                throw new InvalidOperationException("The serial port is not connected.");
            }

            try
            {
                serialPort.Write(string.Join(serialPort.NewLine, lines) + serialPort.NewLine);
                foreach (var line in lines)
                {
                    log.Info($"TX {line}");
                }
            }
            catch (Exception exception) when (
                exception is IOException or InvalidOperationException or TimeoutException)
            {
                log.Error(exception, "serial batch write failed");
                throw;
            }
        }
    }

    private void SerialPortOnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var port = (SerialPort)sender;

        try
        {
            lock (receiveSyncRoot)
            {
                if (!ReferenceEquals(Volatile.Read(ref serialPort), port)) return;
                var receivedText = port.ReadExisting();
                if (receivedText.Length != 0) receiveQueue?.Feed(receivedText);
            }
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException)
        {
            log.Error(exception, $"serial read failed on {port.PortName}");
            ProtocolError?.Invoke(this, exception.Message);
        }
    }

    private void ReportReceiveError(string error)
    {
        log.Warn(error);
        ProtocolError?.Invoke(this, error);
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
            if (ControllerIdentity.TryParse(message, out var identity))
            {
                Volatile.Write(ref currentControllerIdentity, identity);
            }
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

        var port = serialPort;
        port.DataReceived -= SerialPortOnDataReceived;
        var portName = port.PortName;
        Volatile.Write(ref serialPort, null);
        receiveQueue?.Dispose();
        receiveQueue = null;
        if (port.IsOpen)
        {
            port.Close();
        }
        port.Dispose();
        LastFrameReceivedAt = null;
        LastHelloReceivedAt = null;
        LastHeartbeatReceivedAt = null;
        Volatile.Write(ref currentControllerIdentity, null);
        lastTreeEventSignature = null;
        lastTreeEventAtMs = 0;
        log.Info($"serial port closed on {portName}");
    }

    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }
}
