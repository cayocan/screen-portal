using ScreenPortal.App.Connection;
using ScreenPortal.Host.Domain.Pairing;

namespace ScreenPortal.Host.Transport.Control;

/// <summary>
/// Lado do host do handshake (spec 1B §2) e das telas (spec 2C §4): Hello → pareamento → Welcome(leases) →
/// AcquireScreen/StartStream/ReleaseScreen. O trabalho de telas roda numa fila própria, em ordem, fora da thread de
/// leitura do socket (criar monitor leva até ~3 s e não pode atrasar o Pong).
/// </summary>
public sealed class HeadsetSession(
    IControlConnection connection,
    PairingStore pairing,
    Action savePairing,
    IPairingApprover approver,
    IMonitorProvider monitors,
    IScreenHost screens,
    string hostName,
    Func<string> newCode,
    Action<string> log)
{
    private string? _deviceId;
    private long _generation; // publicado pela escrita volátil de _welcomed
    private volatile bool _welcomed;
    private readonly object _queueLock = new();

    // Contrato de ordem (o headset depende dele): Acquire/StartStream/Release/Disconnected de uma sessão rodam TODOS
    // nesta única cadeia, um por vez. Assim um ReleaseScreen é tratado por inteiro (stream parado) antes de um
    // StartStream posterior reaproveitar a mesma porta UDP. Nunca criar uma segunda fila.
    private Task _tail = Task.CompletedTask;

    /// <summary>Fim da fila de trabalho de telas (os testes esperam por ela).</summary>
    public Task Idle
    {
        get { lock (_queueLock) return _tail; }
    }

    public void OnMessage(ControlMessage message)
    {
        switch (message)
        {
            case HelloMessage hello:
                if (_deviceId != null) break; // um Hello por sessão: o device id não muda depois
                _deviceId = hello.DeviceId;
                if (pairing.IsPaired(hello.DeviceId, hello.PairingToken)) SendWelcome();
                else BeginPairing(hello);
                break;
            // Antes do Welcome, mensagens de tela são descartadas sem entrar na fila: um peer não pareado não mexe em
            // telas nem faz a fila crescer.
            case AcquireScreenMessage acquire when _welcomed:
                Enqueue(() => OnAcquire(acquire));
                break;
            case StartStreamMessage start when _welcomed:
                Enqueue(() => OnStartStream(start));
                break;
            case ReleaseScreenMessage release when _welcomed:
                Enqueue(() => OnRelease(release));
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
        _generation = screens.Claim(_deviceId!, OnReplaced);
        _welcomed = true;
        connection.Send(new WelcomeMessage(hostName, monitors.GetMonitors(), screens.Leases(_deviceId!)));
    }

    /// <summary>Outra sessão do mesmo headset recebeu Welcome (reconexão): esta encerra.</summary>
    private void OnReplaced()
    {
        log($"sessão de {_deviceId} ({connection.RemoteAddress}) substituída por uma nova conexão");
        connection.Send(new ByeMessage(ByeReason.Replaced));
        connection.Close();
    }

    private void Enqueue(Action work)
    {
        lock (_queueLock)
        {
            _tail = _tail.ContinueWith(_ =>
            {
                try { work(); }
                catch (Exception e) { log($"telas: {e.GetType().Name}: {e.Message}"); }
            }, TaskScheduler.Default);
        }
    }

    private void OnAcquire(AcquireScreenMessage request)
    {
        if (!_welcomed || _deviceId == null) return;
        var result = screens.Acquire(_deviceId, _generation, request);
        connection.Send(result.Ok
            ? new ScreenReadyMessage(request.ScreenKey, (uint)result.Width, (uint)result.Height)
            : new ScreenFailedMessage(request.ScreenKey, result.Error));
    }

    private void OnStartStream(StartStreamMessage start)
    {
        if (!_welcomed || _deviceId == null) return;
        if (screens.StartStream(_deviceId, _generation, start.ScreenKey, connection.RemoteAddress, start.UdpPort, start.StreamId, out int w, out int h))
            connection.Send(new StreamStartedMessage(start.StreamId, (uint)w, (uint)h));
        else
            connection.Send(new StreamStoppedMessage(start.StreamId, "falha ao iniciar o stream"));
    }

    private void OnRelease(ReleaseScreenMessage release)
    {
        if (!_welcomed || _deviceId == null) return;
        screens.Release(_deviceId, _generation, release.ScreenKey, TimeSpan.FromMilliseconds(release.GraceMs));
    }

    public void OnClosed()
    {
        // Só uma sessão que recebeu Welcome fala por aquele device id: o Hello (em claro) de um peer não pareado não
        // pode derrubar os streams de outro headset.
        if (!_welcomed) return; // leitura volátil primeiro: publica _deviceId e _generation
        string? deviceId = _deviceId;
        long generation = _generation;
        if (deviceId != null) Enqueue(() => screens.Disconnected(deviceId, generation));
    }
}
