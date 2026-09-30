using System;
using System.Collections.Generic;
using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Tests
{
    internal sealed class FakeClock : IClock
    {
        public double Now;
        public double NowSeconds => Now;
    }

    internal sealed class FakeDiscovery : IHostDiscovery
    {
        public bool Started;
        public HostEndpoint? Host;
        public void Start() => Started = true;
        public void Stop() => Started = false;

        public bool TryGetHost(out HostEndpoint host)
        {
            host = Host ?? default;
            return Host.HasValue;
        }
    }

    internal sealed class FakeChannel : IControlChannel
    {
        public readonly Queue<(ChannelEventKind kind, ControlMessage message)> Incoming = new Queue<(ChannelEventKind, ControlMessage)>();
        public readonly List<ControlMessage> Sent = new List<ControlMessage>();
        public readonly List<HostEndpoint> Connects = new List<HostEndpoint>();
        public int Closes;

        public void Connect(HostEndpoint host) => Connects.Add(host);
        public void Send(ControlMessage message) => Sent.Add(message);
        public void Close() => Closes++;
        public void Push(ChannelEventKind kind, ControlMessage message = null) => Incoming.Enqueue((kind, message));

        public ChannelEventKind Poll(out ControlMessage message)
        {
            if (Incoming.Count == 0)
            {
                message = null;
                return ChannelEventKind.None;
            }
            var e = Incoming.Dequeue();
            message = e.message;
            return e.kind;
        }

        public T Last<T>() where T : ControlMessage
        {
            for (int i = Sent.Count - 1; i >= 0; i--)
                if (Sent[i] is T t) return t;
            return null;
        }

        public int Count<T>() where T : ControlMessage
        {
            int n = 0;
            foreach (var m in Sent) if (m is T) n++;
            return n;
        }
    }

    internal sealed class FakePairing : IPairingStore
    {
        public readonly Dictionary<string, string> Tokens = new Dictionary<string, string>();
        public HostEndpoint? LastHost;
        public string DeviceId => "dev-1";
        public string GetToken(string hostName) => Tokens.TryGetValue(hostName, out var t) ? t : "";
        public void SaveToken(string hostName, string token) => Tokens[hostName] = token;
        public void SaveLastHost(HostEndpoint host) => LastHost = host;

        public bool TryGetLastHost(out HostEndpoint host)
        {
            host = LastHost ?? default;
            return LastHost.HasValue;
        }
    }

    internal sealed class ConnectionRig
    {
        public static readonly HostEndpoint Pc = new HostEndpoint("CAYO-PC", "192.168.0.64", 9200);
        public static readonly WelcomeMessage Welcome =
            new WelcomeMessage("CAYO-PC", new[] { new MonitorDescriptor(1, 2560, 1080, "DISPLAY1", true) });

        public readonly FakeClock Clock = new FakeClock();
        public readonly FakeDiscovery Discovery = new FakeDiscovery();
        public readonly FakeChannel Channel = new FakeChannel();
        public readonly FakePairing Pairing = new FakePairing();
        public readonly WorkspaceStore Store = new WorkspaceStore();
        public readonly CommandBus Bus;
        public readonly ConnectionStateMachine Machine;

        public ConnectionRig()
        {
            Bus = new CommandBus(Store, new ICommandObserver[0]);
            Machine = new ConnectionStateMachine(Discovery, Channel, Pairing, Clock, new ConnectionOptions());
        }

        public void Advance(double seconds, double step = 0.05)
        {
            double end = Clock.Now + seconds;
            while (Clock.Now < end - 1e-9)
            {
                Clock.Now = Math.Min(end, Clock.Now + step);
                Machine.Tick();
            }
        }

        /// <summary>Leva a máquina até Streaming (Welcome recebido) com um host já pareado.</summary>
        public void GoToStreaming(WelcomeMessage welcome = null)
        {
            Pairing.Tokens["CAYO-PC"] = "tok";
            Machine.Start();
            Discovery.Host = Pc;
            Machine.Tick();
            Channel.Push(ChannelEventKind.Connected);
            Machine.Tick();
            Channel.Push(ChannelEventKind.Message, welcome ?? Welcome);
            Machine.Tick();
        }
    }

    internal sealed class FakeScreenVideo : IScreenVideo
    {
        public bool Ready = true;
        public bool FirstFrame;
        public int Opens;
        public int Closes;
        public ushort LastStreamId;
        public int LastPort;
        public Resolution LastResolution;
        public readonly HashSet<ScreenId> Open = new HashSet<ScreenId>();

        public bool TryOpen(ScreenId screen, ushort streamId, int udpPort, Resolution resolution)
        {
            if (!Ready) return false;
            Opens++;
            LastStreamId = streamId;
            LastPort = udpPort;
            LastResolution = resolution;
            Open.Add(screen);
            return true;
        }

        public void Close(ScreenId screen)
        {
            Closes++;
            Open.Remove(screen);
        }

        public bool HasFirstFrame(ScreenId screen) => FirstFrame && Open.Contains(screen);

        public int FirstFrameResets;

        public void ResetFirstFrame(ScreenId screen)
        {
            FirstFrameResets++;
            FirstFrame = false;
        }
    }
}
