using ScreenPortal.App.Connection;
using ScreenPortal.Host.Domain.Pairing;

namespace ScreenPortal.Host.Transport.Control;

/// <summary>Lado do host do handshake (spec 1B §2): Hello → pareamento → Welcome → StartStream.</summary>
public sealed class HeadsetSession(
    IControlConnection connection,
    PairingStore pairing,
    Action savePairing,
    IPairingApprover approver,
    IMonitorProvider monitors,
    IStreamRegistry streams,
    string hostName,
    Func<string> newCode,
    Action<string> log)
{
    private string? _deviceId;
    private volatile bool _welcomed;

    public void OnMessage(ControlMessage message)
    {
        switch (message)
        {
            case HelloMessage hello:
                _deviceId = hello.DeviceId;
                if (pairing.IsPaired(hello.DeviceId, hello.PairingToken)) SendWelcome();
                else BeginPairing(hello);
                break;
            case StartStreamMessage start:
                OnStartStream(start);
                break;
            case PingMessage ping:
                connection.Send(new PongMessage(ping.SentUs));
                break;
            case ByeMessage:
                connection.Close();
                break;
        }
    }

    private void BeginPairing(HelloMessage hello)
    {
        string code = newCode();
        log($"pareamento: '{hello.DeviceName}' ({connection.RemoteAddress}) código {code}");
        connection.Send(new PairingRequiredMessage(code));
        approver.ApproveAsync(hello.DeviceName, code).ContinueWith(task =>
        {
            bool accepted = task.Status == TaskStatus.RanToCompletion && task.Result;
            if (accepted)
            {
                string token = pairing.Pair(hello.DeviceId, hello.DeviceName, DateTimeOffset.Now);
                savePairing();
                connection.Send(new PairingResultMessage(true, token));
                log($"pareado: '{hello.DeviceName}'");
                SendWelcome();
            }
            else
            {
                connection.Send(new PairingResultMessage(false, ""));
                connection.Send(new ByeMessage(ByeReason.PairingRejected));
                log($"pareamento recusado: '{hello.DeviceName}'");
                connection.Close();
            }
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    private void SendWelcome()
    {
        _welcomed = true;
        connection.Send(new WelcomeMessage(hostName, monitors.GetMonitors()));
    }

    private void OnStartStream(StartStreamMessage start)
    {
        if (!_welcomed || _deviceId == null) return;
        var monitor = monitors.GetMonitors().FirstOrDefault(m => m.Id == start.ScreenKey);
        if (monitor == null)
        {
            connection.Send(new StreamStoppedMessage(start.StreamId, "monitor desconhecido"));
            return;
        }
        if (streams.TryStart(_deviceId, connection.RemoteAddress, start.UdpPort, start.StreamId, monitor, out int w, out int h))
            connection.Send(new StreamStartedMessage(start.StreamId, (uint)w, (uint)h));
        else
            connection.Send(new StreamStoppedMessage(start.StreamId, "falha ao iniciar o stream"));
    }

    public void OnClosed()
    {
        if (_deviceId != null) streams.Release(_deviceId);
    }
}
