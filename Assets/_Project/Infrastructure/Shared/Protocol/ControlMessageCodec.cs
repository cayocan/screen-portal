using System;
using Google.FlatBuffers;
using ScreenPortal.App.Connection;
using Fb = ScreenPortal.Protocol.Fb;

namespace ScreenPortal.Infrastructure.Protocol
{
    public enum DecodeStatus
    {
        Ok,
        Invalid,
        IncompatibleVersion,
    }

    public readonly struct DecodeResult
    {
        public DecodeStatus Status { get; }
        public ushort Version { get; }
        public ControlMessage Message { get; }

        public DecodeResult(DecodeStatus status, ushort version, ControlMessage message)
        {
            Status = status;
            Version = version;
            Message = message;
        }
    }

    /// <summary>ControlMessage ↔ FlatBuffers (Envelope). Único codec do projeto: host e headset usam este arquivo.</summary>
    public static class ControlMessageCodec
    {
        public static byte[] Encode(ControlMessage message, ushort protocolVersion = ControlProtocol.Version)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            var b = new FlatBufferBuilder(256);
            Fb.Message type;
            int body;

            switch (message)
            {
                case HelloMessage m:
                {
                    var id = b.CreateString(m.DeviceId);
                    var name = b.CreateString(m.DeviceName);
                    var token = b.CreateString(m.PairingToken);
                    body = Fb.Hello.CreateHello(b, id, name, token).Value;
                    type = Fb.Message.Hello;
                    break;
                }
                case PairingRequiredMessage m:
                    body = Fb.PairingRequired.CreatePairingRequired(b, b.CreateString(m.Code)).Value;
                    type = Fb.Message.PairingRequired;
                    break;
                case PairingResultMessage m:
                    body = Fb.PairingResult.CreatePairingResult(b, m.Accepted, b.CreateString(m.PairingToken)).Value;
                    type = Fb.Message.PairingResult;
                    break;
                case WelcomeMessage m:
                {
                    var monitors = new Offset<Fb.MonitorInfo>[m.Monitors.Count];
                    for (int i = 0; i < monitors.Length; i++)
                    {
                        var mon = m.Monitors[i];
                        monitors[i] = Fb.MonitorInfo.CreateMonitorInfo(b, mon.Id, mon.Width, mon.Height, b.CreateString(mon.Name), mon.IsPrimary);
                    }
                    var leases = new Offset<Fb.ScreenLease>[m.Leases.Count];
                    for (int i = 0; i < leases.Length; i++)
                    {
                        var l = m.Leases[i];
                        leases[i] = Fb.ScreenLease.CreateScreenLease(b, l.ScreenKey, (Fb.ScreenKind)(byte)l.Kind, l.Width, l.Height);
                    }
                    var host = b.CreateString(m.HostName);
                    var monitorVector = Fb.Welcome.CreateMonitorsVector(b, monitors);
                    var leaseVector = Fb.Welcome.CreateLeasesVector(b, leases);
                    body = Fb.Welcome.CreateWelcome(b, host, monitorVector, leaseVector).Value;
                    type = Fb.Message.Welcome;
                    break;
                }
                case StartStreamMessage m:
                    body = Fb.StartStream.CreateStartStream(b, 0, m.UdpPort, m.StreamId, m.ScreenKey).Value;
                    type = Fb.Message.StartStream;
                    break;
                case StreamStartedMessage m:
                    body = Fb.StreamStarted.CreateStreamStarted(b, m.StreamId, m.Width, m.Height).Value;
                    type = Fb.Message.StreamStarted;
                    break;
                case StreamStoppedMessage m:
                    body = Fb.StreamStopped.CreateStreamStopped(b, m.StreamId, b.CreateString(m.Reason)).Value;
                    type = Fb.Message.StreamStopped;
                    break;
                case PingMessage m:
                    body = Fb.Ping.CreatePing(b, m.SentUs).Value;
                    type = Fb.Message.Ping;
                    break;
                case PongMessage m:
                    body = Fb.Pong.CreatePong(b, m.SentUs).Value;
                    type = Fb.Message.Pong;
                    break;
                case ByeMessage m:
                    body = Fb.Bye.CreateBye(b, (Fb.ByeReason)(byte)m.Reason).Value;
                    type = Fb.Message.Bye;
                    break;
                case AcquireScreenMessage m:
                    body = Fb.AcquireScreen.CreateAcquireScreen(b, m.ScreenKey, (Fb.ScreenKind)(byte)m.ScreenKind, m.Width, m.Height, m.PhysicalMonitorId).Value;
                    type = Fb.Message.AcquireScreen;
                    break;
                case ScreenReadyMessage m:
                    body = Fb.ScreenReady.CreateScreenReady(b, m.ScreenKey, m.Width, m.Height).Value;
                    type = Fb.Message.ScreenReady;
                    break;
                case ScreenFailedMessage m:
                    body = Fb.ScreenFailed.CreateScreenFailed(b, m.ScreenKey, b.CreateString(m.Reason)).Value;
                    type = Fb.Message.ScreenFailed;
                    break;
                case ReleaseScreenMessage m:
                    body = Fb.ReleaseScreen.CreateReleaseScreen(b, m.ScreenKey, m.GraceMs).Value;
                    type = Fb.Message.ReleaseScreen;
                    break;
                default:
                    throw new ArgumentException("Mensagem sem codec: " + message.GetType().Name, nameof(message));
            }

            var envelope = Fb.Envelope.CreateEnvelope(b, protocolVersion, type, body);
            Fb.Envelope.FinishEnvelopeBuffer(b, envelope); // grava o file_identifier "SPCT"
            return b.SizedByteArray();
        }

        public static DecodeResult Decode(byte[] payload)
        {
            if (payload == null || payload.Length < 8) return new DecodeResult(DecodeStatus.Invalid, 0, null);
            try
            {
                var bb = new ByteBuffer(payload);
                if (!new Verifier(bb).VerifyBuffer("SPCT", false, VerifyEnvelopeShell)) return new DecodeResult(DecodeStatus.Invalid, 0, null);
                if (!VerifyBody(bb)) return new DecodeResult(DecodeStatus.Invalid, 0, null);
                var envelope = Fb.Envelope.GetRootAsEnvelope(bb);
                ushort version = envelope.ProtocolVersion;
                if (version == 0) return new DecodeResult(DecodeStatus.Invalid, 0, null); // campo ausente = lixo
                if (version != ControlProtocol.Version) return new DecodeResult(DecodeStatus.IncompatibleVersion, version, null);

                var message = Read(envelope);
                return message == null
                    ? new DecodeResult(DecodeStatus.Invalid, version, null)
                    : new DecodeResult(DecodeStatus.Ok, version, message);
            }
            catch (Exception)
            {
                return new DecodeResult(DecodeStatus.Invalid, 0, null);
            }
        }

        /// <summary>
        /// Passo 1: verifica só o Envelope (identificador, tabela, protocol_version, body_type), sem a union.
        /// O Verifier.VerifyUnion do runtime Google.FlatBuffers 25.2.10 (não o flatc) passa à ação de verificação
        /// o tamanho da tabela em vez do tipo do corpo; ver <see cref="VerifyBody"/>.
        /// </summary>
        private static bool VerifyEnvelopeShell(Verifier verifier, uint tablePos)
        {
            return verifier.VerifyTableStart(tablePos)
                && verifier.VerifyField(tablePos, 4 /*ProtocolVersion*/, 2, 2, false)
                && verifier.VerifyField(tablePos, 6 /*BodyType*/, 1, 1, false)
                && verifier.VerifyTableEnd(tablePos);
        }

        /// <summary>
        /// Passo 2 (workaround do bug do runtime Google.FlatBuffers 25.2.10): o VerifyUnion entrega um type id errado,
        /// então ignoramos o id recebido e despachamos o verificador gerado pelo BodyType lido do envelope (seguro após o
        /// passo 1). Sem isto o corpo nunca seria verificado (vetores/strings com tamanho corrompido alocariam GBs).
        /// Reavaliar ao atualizar o pacote Google.FlatBuffers. Um closure por Decode: canal de controle, não é o caminho de vídeo.
        /// </summary>
        private static bool VerifyBody(ByteBuffer bb)
        {
            byte bodyType = (byte)Fb.Envelope.GetRootAsEnvelope(bb).BodyType;
            return new Verifier(bb).VerifyBuffer("SPCT", false, (v, tp) =>
                v.VerifyTableStart(tp)
                && v.VerifyUnion(tp, 6, 8, (v2, _, pos) => Fb.MessageVerify.Verify(v2, bodyType, pos), false)
                && v.VerifyTableEnd(tp));
        }

        private static ControlMessage Read(Fb.Envelope e)
        {
            switch (e.BodyType)
            {
                case Fb.Message.Hello:
                {
                    var h = e.BodyAsHello();
                    return new HelloMessage(h.DeviceId, h.DeviceName, h.PairingToken);
                }
                case Fb.Message.PairingRequired:
                    return new PairingRequiredMessage(e.BodyAsPairingRequired().Code);
                case Fb.Message.PairingResult:
                {
                    var r = e.BodyAsPairingResult();
                    return new PairingResultMessage(r.Accepted, r.PairingToken);
                }
                case Fb.Message.Welcome:
                {
                    var w = e.BodyAsWelcome();
                    var monitors = new MonitorDescriptor[w.MonitorsLength];
                    for (int i = 0; i < monitors.Length; i++)
                    {
                        var m = w.Monitors(i).Value;
                        monitors[i] = new MonitorDescriptor(m.Id, m.Width, m.Height, m.Name, m.IsPrimary);
                    }
                    var leases = new ScreenLeaseInfo[w.LeasesLength];
                    for (int i = 0; i < leases.Length; i++)
                    {
                        var l = w.Leases(i).Value;
                        leases[i] = new ScreenLeaseInfo(l.ScreenKey, (ScreenKind)(byte)l.Kind, l.Width, l.Height);
                    }
                    return new WelcomeMessage(w.HostName, monitors, leases);
                }
                case Fb.Message.StartStream:
                {
                    var s = e.BodyAsStartStream();
                    return new StartStreamMessage(s.ScreenKey, s.UdpPort, s.StreamId);
                }
                case Fb.Message.StreamStarted:
                {
                    var s = e.BodyAsStreamStarted();
                    return new StreamStartedMessage(s.StreamId, s.Width, s.Height);
                }
                case Fb.Message.StreamStopped:
                {
                    var s = e.BodyAsStreamStopped();
                    return new StreamStoppedMessage(s.StreamId, s.Reason);
                }
                case Fb.Message.Ping:
                    return new PingMessage(e.BodyAsPing().SentUs);
                case Fb.Message.Pong:
                    return new PongMessage(e.BodyAsPong().SentUs);
                case Fb.Message.Bye:
                    return new ByeMessage((ByeReason)(byte)e.BodyAsBye().Reason);
                case Fb.Message.AcquireScreen:
                {
                    var a = e.BodyAsAcquireScreen();
                    return new AcquireScreenMessage(a.ScreenKey, (ScreenKind)(byte)a.Kind, a.Width, a.Height, a.PhysicalMonitorId);
                }
                case Fb.Message.ScreenReady:
                {
                    var r = e.BodyAsScreenReady();
                    return new ScreenReadyMessage(r.ScreenKey, r.Width, r.Height);
                }
                case Fb.Message.ScreenFailed:
                {
                    var f = e.BodyAsScreenFailed();
                    return new ScreenFailedMessage(f.ScreenKey, f.Reason);
                }
                case Fb.Message.ReleaseScreen:
                {
                    var r = e.BodyAsReleaseScreen();
                    return new ReleaseScreenMessage(r.ScreenKey, r.GraceMs);
                }
                default:
                    return null;
            }
        }
    }
}
