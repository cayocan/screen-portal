# Fase 1B — Conexão PC ↔ headset — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Abrir o app no Quest com o host rodando no PC mostra a tela do PC na tela Demo, sem IP fixo: descoberta mDNS, pareamento na primeira vez, status visível e reconexão automática em < 3 s.

**Architecture:** Protocolo de controle TCP (porta 9200) com mensagens FlatBuffers versionadas. Todo o código **puro** (mensagens, codec, framing, cliente TCP, máquina de estados) vive em `Assets/_Project` e é compilado **também pelo host** via arquivos vinculados (`host/Host.Shared`), então existe um só codec e o teste de integração no PC usa a máquina de estados real do headset contra o servidor real. mDNS nativo nos dois lados (`DnsServiceRegister` no Windows, `NsdManager` no Android). Spec: `docs/specs/2026-09-28-fase1b-conexao-design.md`.

**Tech Stack:** FlatBuffers 25.2.10 (`flatc` + NuGet `Google.FlatBuffers`), .NET 10 (host), Unity 6000.3.19f1 C# 9, VContainer 1.19.0, Kotlin/Android NSD (`.aar`), xUnit (host), NUnit (Unity/harness).

## Global Constraints

- Porta TCP de controle **9200**; serviço mDNS **`_screenportal._tcp`**, TXT `v=1`; frame `[u32 LE tamanho][FlatBuffer]`, máximo **64 KiB**.
- `protocol_version` = **1**; versão diferente = incompatível → `Bye(IncompatibleVersion)`.
- Tempos: connect **2 s**; negociação **5 s** (sem timeout durante o pareamento); ping **500 ms**; `Degraded` após **1,5 s** sem `Pong`; `Reconnecting` após mais **3 s**; reconexão ao último IP a cada **250 ms**; desiste após **10 s**; dica "Instale o host" após **10 s** na 1ª abertura; recusa bloqueia o host por **10 s**; incompatível por **60 s**; `Bye(Shutdown)` por **3 s**.
- Vídeo: porta UDP **9000**, `stream_id` **0**; tela Demo a **1,4 m** de largura em `Pose.At(0, 1.2, 1.3)`, label `monitor:<id>`.
- Pareamento: código de **6 dígitos**; token de **32 bytes** aleatórios em base64; host em `%APPDATA%\ScreenPortal\paired.json`.
- Código Unity em **C# 9**; Domain/Application/Infrastructure.Shared com `noEngineReferences: true`; namespace da camada Application é `ScreenPortal.App`.
- **VContainer:** nunca registrar por tipo uma classe cujo construtor tenha parâmetro primitivo (int/string/bool): usar `RegisterInstance` (lição da Fase 1A).
- `.aar` sem kotlin-stdlib: nada de lambdas/funções da stdlib no Kotlin (usar `object :` para listeners).
- Nunca usar MCP/eval na `main`: branch `phase1b/connection`; automação do Editor via `tools/unity-cmd.ps1`.
- Commits pequenos, um propósito cada.

---

## File Structure

```
protocol/control.fbs                                   schema v1
protocol/control-v1.md                                 documentação do protocolo
tools/flatc/flatc.exe (+ VERSION.txt)                  compilador fixo 25.2.10
tools/gen-protocol.ps1                                 gera Generated/ControlProtocol.g.cs
Assets/_Project/ThirdParty/FlatBuffers/Google.FlatBuffers.dll   runtime (NuGet 25.2.10, netstandard2.1)
Assets/_Project/Domain/Screens/ResolutionFit.cs        captura → Resolution válida (usado nos dois lados)
Assets/_Project/Application/Connection/
  ControlMessages.cs          mensagens puras (HelloMessage, WelcomeMessage, …)
  HostEndpoint.cs             nome|ip|porta
  Ports.cs                    IControlChannel, IHostDiscovery, IPairingStore, IVideoStreamSink, IClock, IScreenSurfaceProvider, ChannelEventKind
  ConnectionStatus.cs         ConnectionState, ConnectionStatusKind, ConnectionStatus
  ConnectionOptions.cs        tempos e padrões
  ConnectionStatusText.cs     textos pt-BR do status
  ConnectionStateMachine.cs   a máquina de estados
Assets/_Project/Infrastructure/Shared/                 (asmdef ScreenPortal.Infrastructure.Shared, puro)
  Protocol/ControlProtocol.cs · FrameCodec.cs · ControlMessageCodec.cs · Generated/ControlProtocol.g.cs
  Net/TcpControlChannel.cs · MonotonicClock.cs · StaticHostDiscovery.cs · InMemoryPairingStore.cs
Assets/_Project/Infrastructure/Unity/                  (asmdef ScreenPortal.Infrastructure, Unity)
  AndroidHostDiscovery.cs · PlayerPrefsPairingStore.cs · NativeVideoJni.cs · NativeVideoSink.cs
Assets/_Project/Presentation/Connection/ConnectionStatusView.cs
Assets/_Project/Presentation/Screens/OverlayScreenViewFactory.cs (implementa IScreenSurfaceProvider)
Assets/_Project/Composition/AppEnvironment.cs · AppInstaller.cs · AppEntryPoint.cs · AppLifetimeScope.cs (reescritos)
Assets/_Project/Editor/MainSceneBuilder.cs (painel de status)
Assets/_Project/Tests/EditMode/Connection/*.cs          máquina de estados, status, ResolutionFit
Assets/_Project/Tests/Shared/*.cs (+ asmdef)            codec, framing, TcpControlChannel
headset-native/.../HostDiscovery.kt (+ manifest)
host/Host.Shared/Host.Shared.csproj                     vincula Domain + Application + Infrastructure/Shared
host/Host.Domain/Pairing/PairingStore.cs
host/Host.Transport/Control/*.cs                        IControlConnection, HeadsetSession, ControlServer, portas do host
host/Host.App/Control/*.cs                              StreamRegistry, WindowsMonitorProvider, MessageBoxPairingApprover, MdnsAdvertiser
host/Host.App/Program.cs · StreamPipeline.cs            comando serve; stream id + keyframe
host/Host.Tests/Control/*.cs                            PairingStore, HeadsetSession, integração
```

---

### Task 1: Schema, flatc, runtime FlatBuffers, codec e framing

**Files:**
- Create: `protocol/control.fbs`, `protocol/control-v1.md`, `tools/flatc/VERSION.txt`, `tools/gen-protocol.ps1`
- Create: `Assets/_Project/Infrastructure/Shared/ScreenPortal.Infrastructure.Shared.asmdef`
- Create: `Assets/_Project/Infrastructure/Shared/Protocol/ControlProtocol.cs`, `FrameCodec.cs`, `ControlMessageCodec.cs`
- Create: `Assets/_Project/Application/Connection/ControlMessages.cs`
- Generated: `Assets/_Project/Infrastructure/Shared/Protocol/Generated/ControlProtocol.g.cs`
- Create: `Assets/_Project/ThirdParty/FlatBuffers/Google.FlatBuffers.dll`
- Create: `Assets/_Project/Tests/Shared/ScreenPortal.Tests.Shared.asmdef`, `Assets/_Project/Tests/Shared/ProtocolTests.cs`
- Modify: `tests-dotnet/UnityPureCode.Tests/UnityPureCode.Tests.csproj`, `.gitignore`

**Interfaces:**
- Produces (namespace `ScreenPortal.App.Connection`): `enum ControlMessageKind`, `enum ByeReason { Shutdown, IncompatibleVersion, PairingRejected, Replaced }`, `abstract class ControlMessage { ControlMessageKind Kind }`, `HelloMessage(string deviceId, string deviceName, string pairingToken)`, `PairingRequiredMessage(string code)`, `PairingResultMessage(bool accepted, string pairingToken)`, `MonitorDescriptor(uint id, uint width, uint height, string name, bool isPrimary)`, `WelcomeMessage(string hostName, IReadOnlyList<MonitorDescriptor> monitors)`, `StartStreamMessage(uint monitorId, ushort udpPort, ushort streamId)`, `StreamStartedMessage(ushort streamId, uint width, uint height)`, `StreamStoppedMessage(ushort streamId, string reason)`, `PingMessage(ulong sentUs)`, `PongMessage(ulong sentUs)`, `ByeMessage(ByeReason reason)`.
- Produces (namespace `ScreenPortal.Infrastructure.Protocol`): `ControlProtocol.Version = 1`, `Port = 9200`, `ServiceType = "_screenportal._tcp"`, `MaxFrameBytes = 65536`; `FrameCodec.Frame(byte[] payload) → byte[]`; `FrameReader.Append(byte[], int, int)`, `bool TryRead(out byte[] payload)`, `bool IsCorrupt`; `ControlMessageCodec.Encode(ControlMessage) → byte[]`, `Decode(byte[]) → DecodeResult { DecodeStatus Status (Ok|Invalid|IncompatibleVersion), ushort Version, ControlMessage Message }`.

- [ ] **Step 1: Branch e ferramenta**

```powershell
git switch -c phase1b/connection
New-Item -ItemType Directory -Force tools/flatc | Out-Null
Invoke-WebRequest -Uri "https://github.com/google/flatbuffers/releases/download/v25.2.10/Windows.flatc.binary.zip" -OutFile "$env:TEMP\flatc.zip"
Expand-Archive -Force "$env:TEMP\flatc.zip" tools/flatc
tools/flatc/flatc.exe --version   # esperado: flatc version 25.2.10
```

`tools/flatc/VERSION.txt`:
```text
flatc 25.2.10 — https://github.com/google/flatbuffers/releases/tag/v25.2.10 (Windows.flatc.binary.zip)
Deve bater com o NuGet Google.FlatBuffers 25.2.10 (runtime). Mudar os dois juntos.
```

- [ ] **Step 2: Schema**

`protocol/control.fbs`:
```text
// Canal de controle PC <-> headset, v1. Ver protocol/control-v1.md.
// Regra (brief §8.9): campos novos sempre no FIM das tabelas e opcionais. Nunca reordenar nem remover.
namespace ScreenPortal.Protocol.Fb;

enum ByeReason : ubyte { Shutdown = 0, IncompatibleVersion = 1, PairingRejected = 2, Replaced = 3 }

table Hello { device_id:string; device_name:string; pairing_token:string; }
table PairingRequired { code:string; }
table PairingResult { accepted:bool; pairing_token:string; }
table MonitorInfo { id:uint; width:uint; height:uint; name:string; is_primary:bool; }
table Welcome { host_name:string; monitors:[MonitorInfo]; }
table StartStream { monitor_id:uint; udp_port:ushort; stream_id:ushort; }
table StreamStarted { stream_id:ushort; width:uint; height:uint; }
table StreamStopped { stream_id:ushort; reason:string; }
table Ping { sent_us:ulong; }
table Pong { sent_us:ulong; }
table Bye { reason:ByeReason; }

union Message { Hello, PairingRequired, PairingResult, Welcome, StartStream, StreamStarted, StreamStopped, Ping, Pong, Bye }

table Envelope { protocol_version:ushort; body:Message; }

root_type Envelope;
file_identifier "SPCT";   // 4 bytes no início do buffer: rejeita lixo cedo (e evita o bug do Verify sem identificador no C# 25.2.10)
```

`tools/gen-protocol.ps1`:
```powershell
# Gera o C# do protocolo de controle a partir de protocol/control.fbs (flatc fixo em tools/flatc).
# O arquivo gerado é commitado: builds não dependem do flatc.
#   powershell -ExecutionPolicy Bypass -File tools/gen-protocol.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "Assets\_Project\Infrastructure\Shared\Protocol\Generated"
$tmp = Join-Path $env:TEMP "sp-flatc"
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
& (Join-Path $PSScriptRoot "flatc\flatc.exe") --csharp --gen-onefile -o $tmp (Join-Path $root "protocol\control.fbs")
if ($LASTEXITCODE -ne 0) { throw "flatc falhou" }
New-Item -ItemType Directory -Force $out | Out-Null
$header = "// <auto-generated> flatc 25.2.10 a partir de protocol/control.fbs — NÃO EDITAR. Regenerar com tools/gen-protocol.ps1.`r`n"
$code = Get-Content (Join-Path $tmp "control_generated.cs") -Raw
[IO.File]::WriteAllText((Join-Path $out "ControlProtocol.g.cs"), $header + $code, (New-Object Text.UTF8Encoding($false)))
Write-Host "OK: $out\ControlProtocol.g.cs"
```

Run: `powershell -ExecutionPolicy Bypass -File tools/gen-protocol.ps1`
Expected: `OK: ...\Generated\ControlProtocol.g.cs`.

- [ ] **Step 3: Runtime FlatBuffers na Unity**

```powershell
$nupkg = "$env:TEMP\flatbuffers.nupkg"
Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/google.flatbuffers/25.2.10/google.flatbuffers.25.2.10.nupkg" -OutFile $nupkg
Expand-Archive -Force $nupkg "$env:TEMP\flatbuffers-pkg"
(Get-ChildItem "$env:TEMP\flatbuffers-pkg\lib").Name     # escolher netstandard2.1 (ou netstandard2.0 se não houver)
New-Item -ItemType Directory -Force Assets/_Project/ThirdParty/FlatBuffers | Out-Null
Copy-Item "$env:TEMP\flatbuffers-pkg\lib\netstandard2.1\Google.FlatBuffers.dll" Assets/_Project/ThirdParty/FlatBuffers/
```

- [ ] **Step 4: Mensagens puras (Application)**

`Assets/_Project/Application/Connection/ControlMessages.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace ScreenPortal.App.Connection
{
    public enum ControlMessageKind
    {
        Hello,
        PairingRequired,
        PairingResult,
        Welcome,
        StartStream,
        StreamStarted,
        StreamStopped,
        Ping,
        Pong,
        Bye,
    }

    public enum ByeReason
    {
        Shutdown,
        IncompatibleVersion,
        PairingRejected,
        Replaced,
    }

    /// <summary>Mensagem do canal de controle, independente de FlatBuffers (o codec fica na Infrastructure).</summary>
    public abstract class ControlMessage
    {
        public abstract ControlMessageKind Kind { get; }
    }

    public sealed class HelloMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.Hello;
        public string DeviceId { get; }
        public string DeviceName { get; }
        public string PairingToken { get; }

        public HelloMessage(string deviceId, string deviceName, string pairingToken)
        {
            DeviceId = deviceId ?? string.Empty;
            DeviceName = deviceName ?? string.Empty;
            PairingToken = pairingToken ?? string.Empty;
        }
    }

    public sealed class PairingRequiredMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.PairingRequired;
        public string Code { get; }

        public PairingRequiredMessage(string code) => Code = code ?? string.Empty;
    }

    public sealed class PairingResultMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.PairingResult;
        public bool Accepted { get; }
        public string PairingToken { get; }

        public PairingResultMessage(bool accepted, string pairingToken)
        {
            Accepted = accepted;
            PairingToken = pairingToken ?? string.Empty;
        }
    }

    public sealed class MonitorDescriptor
    {
        public uint Id { get; }
        public uint Width { get; }
        public uint Height { get; }
        public string Name { get; }
        public bool IsPrimary { get; }

        public MonitorDescriptor(uint id, uint width, uint height, string name, bool isPrimary)
        {
            Id = id;
            Width = width;
            Height = height;
            Name = name ?? string.Empty;
            IsPrimary = isPrimary;
        }
    }

    public sealed class WelcomeMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.Welcome;
        public string HostName { get; }
        public IReadOnlyList<MonitorDescriptor> Monitors { get; }

        public WelcomeMessage(string hostName, IReadOnlyList<MonitorDescriptor> monitors)
        {
            HostName = hostName ?? string.Empty;
            Monitors = monitors ?? Array.Empty<MonitorDescriptor>();
        }
    }

    public sealed class StartStreamMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.StartStream;
        public uint MonitorId { get; }
        public ushort UdpPort { get; }
        public ushort StreamId { get; }

        public StartStreamMessage(uint monitorId, ushort udpPort, ushort streamId)
        {
            MonitorId = monitorId;
            UdpPort = udpPort;
            StreamId = streamId;
        }
    }

    public sealed class StreamStartedMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.StreamStarted;
        public ushort StreamId { get; }
        public uint Width { get; }
        public uint Height { get; }

        public StreamStartedMessage(ushort streamId, uint width, uint height)
        {
            StreamId = streamId;
            Width = width;
            Height = height;
        }
    }

    public sealed class StreamStoppedMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.StreamStopped;
        public ushort StreamId { get; }
        public string Reason { get; }

        public StreamStoppedMessage(ushort streamId, string reason)
        {
            StreamId = streamId;
            Reason = reason ?? string.Empty;
        }
    }

    public sealed class PingMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.Ping;
        public ulong SentUs { get; }

        public PingMessage(ulong sentUs) => SentUs = sentUs;
    }

    public sealed class PongMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.Pong;
        public ulong SentUs { get; }

        public PongMessage(ulong sentUs) => SentUs = sentUs;
    }

    public sealed class ByeMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.Bye;
        public ByeReason Reason { get; }

        public ByeMessage(ByeReason reason) => Reason = reason;
    }
}
```

- [ ] **Step 5: asmdefs e harness**

`Assets/_Project/Infrastructure/Shared/ScreenPortal.Infrastructure.Shared.asmdef`:
```json
{
    "name": "ScreenPortal.Infrastructure.Shared",
    "rootNamespace": "ScreenPortal.Infrastructure",
    "references": ["ScreenPortal.Domain", "ScreenPortal.Application"],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["Google.FlatBuffers.dll"],
    "autoReferenced": false,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}
```

`Assets/_Project/Tests/Shared/ScreenPortal.Tests.Shared.asmdef`:
```json
{
    "name": "ScreenPortal.Tests.Shared",
    "rootNamespace": "ScreenPortal.Tests",
    "references": [
        "ScreenPortal.Domain",
        "ScreenPortal.Application",
        "ScreenPortal.Infrastructure.Shared",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll", "Google.FlatBuffers.dll"],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "versionDefines": [],
    "noEngineReferences": false
}
```

Em `tests-dotnet/UnityPureCode.Tests/UnityPureCode.Tests.csproj`, trocar os dois `ItemGroup` por:
```xml
  <ItemGroup>
    <Compile Include="../../Assets/_Project/Domain/**/*.cs" />
    <Compile Include="../../Assets/_Project/Application/**/*.cs" />
    <Compile Include="../../Assets/_Project/Infrastructure/Shared/**/*.cs" />
    <Compile Include="../../Assets/_Project/Tests/EditMode/**/*.cs" />
    <Compile Include="../../Assets/_Project/Tests/Shared/**/*.cs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="NUnit" Version="3.14.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
    <PackageReference Include="Google.FlatBuffers" Version="25.2.10" />
  </ItemGroup>
```

- [ ] **Step 6: Escrever os testes que falham**

`Assets/_Project/Tests/Shared/ProtocolTests.cs`:
```csharp
using System;
using NUnit.Framework;
using ScreenPortal.App.Connection;
using ScreenPortal.Infrastructure.Protocol;

namespace ScreenPortal.Tests
{
    public class ProtocolTests
    {
        private static ControlMessage RoundTrip(ControlMessage message)
        {
            var result = ControlMessageCodec.Decode(ControlMessageCodec.Encode(message));
            Assert.That(result.Status, Is.EqualTo(DecodeStatus.Ok));
            Assert.That(result.Version, Is.EqualTo(ControlProtocol.Version));
            Assert.That(result.Message.Kind, Is.EqualTo(message.Kind));
            return result.Message;
        }

        [Test]
        public void Hello_RoundTrips()
        {
            var m = (HelloMessage)RoundTrip(new HelloMessage("dev-1", "Quest 3", "tok"));
            Assert.That(m.DeviceId, Is.EqualTo("dev-1"));
            Assert.That(m.DeviceName, Is.EqualTo("Quest 3"));
            Assert.That(m.PairingToken, Is.EqualTo("tok"));
        }

        [Test]
        public void Welcome_RoundTripsMonitorList()
        {
            var m = (WelcomeMessage)RoundTrip(new WelcomeMessage("CAYO-PC", new[]
            {
                new MonitorDescriptor(1, 2560, 1080, "DISPLAY1", true),
                new MonitorDescriptor(2, 1920, 1080, "DISPLAY2", false),
            }));
            Assert.That(m.HostName, Is.EqualTo("CAYO-PC"));
            Assert.That(m.Monitors.Count, Is.EqualTo(2));
            Assert.That(m.Monitors[0].Width, Is.EqualTo(2560u));
            Assert.That(m.Monitors[0].IsPrimary, Is.True);
            Assert.That(m.Monitors[1].Name, Is.EqualTo("DISPLAY2"));
        }

        [Test]
        public void AllOtherMessages_RoundTrip()
        {
            Assert.That(((PairingRequiredMessage)RoundTrip(new PairingRequiredMessage("482913"))).Code, Is.EqualTo("482913"));
            var pr = (PairingResultMessage)RoundTrip(new PairingResultMessage(true, "t"));
            Assert.That(pr.Accepted, Is.True);
            Assert.That(pr.PairingToken, Is.EqualTo("t"));
            var ss = (StartStreamMessage)RoundTrip(new StartStreamMessage(1, 9000, 0));
            Assert.That(ss.UdpPort, Is.EqualTo((ushort)9000));
            var st = (StreamStartedMessage)RoundTrip(new StreamStartedMessage(0, 2560, 1080));
            Assert.That(st.Height, Is.EqualTo(1080u));
            Assert.That(((StreamStoppedMessage)RoundTrip(new StreamStoppedMessage(0, "x"))).Reason, Is.EqualTo("x"));
            Assert.That(((PingMessage)RoundTrip(new PingMessage(123456789UL))).SentUs, Is.EqualTo(123456789UL));
            Assert.That(((PongMessage)RoundTrip(new PongMessage(42UL))).SentUs, Is.EqualTo(42UL));
            Assert.That(((ByeMessage)RoundTrip(new ByeMessage(ByeReason.PairingRejected))).Reason, Is.EqualTo(ByeReason.PairingRejected));
        }

        [Test]
        public void Decode_Garbage_IsInvalid()
        {
            Assert.That(ControlMessageCodec.Decode(new byte[] { 1, 2, 3 }).Status, Is.EqualTo(DecodeStatus.Invalid));
            Assert.That(ControlMessageCodec.Decode(new byte[64]).Status, Is.EqualTo(DecodeStatus.Invalid));
        }

        [Test]
        public void Decode_OtherVersion_IsIncompatible()
        {
            var bytes = ControlMessageCodec.Encode(new PingMessage(1), protocolVersion: 2);
            var result = ControlMessageCodec.Decode(bytes);
            Assert.That(result.Status, Is.EqualTo(DecodeStatus.IncompatibleVersion));
            Assert.That(result.Version, Is.EqualTo((ushort)2));
        }

        [Test]
        public void FrameReader_HandlesSplitAndCoalescedFrames()
        {
            var a = FrameCodec.Frame(new byte[] { 1, 2, 3 });
            var b = FrameCodec.Frame(new byte[] { 9 });
            var stream = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, stream, 0, a.Length);
            Buffer.BlockCopy(b, 0, stream, a.Length, b.Length);

            var reader = new FrameReader();
            reader.Append(stream, 0, 2);                        // metade do header
            Assert.That(reader.TryRead(out _), Is.False);
            reader.Append(stream, 2, stream.Length - 2);        // resto + frame inteiro colado
            Assert.That(reader.TryRead(out var p1), Is.True);
            Assert.That(p1, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(reader.TryRead(out var p2), Is.True);
            Assert.That(p2, Is.EqualTo(new byte[] { 9 }));
            Assert.That(reader.TryRead(out _), Is.False);
        }

        [Test]
        public void FrameReader_RejectsOversizedFrame()
        {
            var reader = new FrameReader();
            var header = BitConverter.GetBytes(ControlProtocol.MaxFrameBytes + 1);
            reader.Append(header, 0, 4);
            Assert.That(reader.TryRead(out _), Is.False);
            Assert.That(reader.IsCorrupt, Is.True);
        }

        [Test]
        public void Frame_RejectsOversizedPayload() =>
            Assert.Throws<ArgumentException>(() => FrameCodec.Frame(new byte[ControlProtocol.MaxFrameBytes + 1]));
    }
}
```

- [ ] **Step 7: Rodar e ver falhar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: FAIL de compilação — `The type or namespace name 'Protocol' does not exist in the namespace 'ScreenPortal.Infrastructure'`.

- [ ] **Step 8: Implementar**

`Assets/_Project/Infrastructure/Shared/Protocol/ControlProtocol.cs`:
```csharp
namespace ScreenPortal.Infrastructure.Protocol
{
    /// <summary>Constantes do canal de controle v1 (protocol/control-v1.md).</summary>
    public static class ControlProtocol
    {
        public const ushort Version = 1;
        public const int Port = 9200;
        public const string ServiceType = "_screenportal._tcp";
        public const int MaxFrameBytes = 64 * 1024;
    }
}
```

`Assets/_Project/Infrastructure/Shared/Protocol/FrameCodec.cs`:
```csharp
using System;
using System.Buffers.Binary;

namespace ScreenPortal.Infrastructure.Protocol
{
    /// <summary>Frame = [u32 LE tamanho][payload]. TCP é um fluxo: o leitor remonta frames partidos ou colados.</summary>
    public static class FrameCodec
    {
        public const int HeaderSize = 4;

        public static byte[] Frame(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length > ControlProtocol.MaxFrameBytes)
                throw new ArgumentException("Payload acima de " + ControlProtocol.MaxFrameBytes + " bytes.", nameof(payload));
            var frame = new byte[HeaderSize + payload.Length];
            BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
            Buffer.BlockCopy(payload, 0, frame, HeaderSize, payload.Length);
            return frame;
        }
    }

    public sealed class FrameReader
    {
        private byte[] _buffer = new byte[4096];
        private int _count;

        /// <summary>Tamanho inválido recebido: a conexão deve ser encerrada.</summary>
        public bool IsCorrupt { get; private set; }

        public void Append(byte[] data, int offset, int count)
        {
            if (_count + count > _buffer.Length)
            {
                var bigger = new byte[Math.Max(_buffer.Length * 2, _count + count)];
                Buffer.BlockCopy(_buffer, 0, bigger, 0, _count);
                _buffer = bigger;
            }
            Buffer.BlockCopy(data, offset, _buffer, _count, count);
            _count += count;
        }

        public bool TryRead(out byte[] payload)
        {
            payload = null;
            if (IsCorrupt || _count < FrameCodec.HeaderSize) return false;
            int length = BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(_buffer, 0, FrameCodec.HeaderSize));
            if (length < 0 || length > ControlProtocol.MaxFrameBytes)
            {
                IsCorrupt = true;
                return false;
            }
            int total = FrameCodec.HeaderSize + length;
            if (_count < total) return false;

            payload = new byte[length];
            Buffer.BlockCopy(_buffer, FrameCodec.HeaderSize, payload, 0, length);
            Buffer.BlockCopy(_buffer, total, _buffer, 0, _count - total);
            _count -= total;
            return true;
        }
    }
}
```

`Assets/_Project/Infrastructure/Shared/Protocol/ControlMessageCodec.cs`:
```csharp
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
                    var host = b.CreateString(m.HostName);
                    var vector = Fb.Welcome.CreateMonitorsVector(b, monitors);
                    body = Fb.Welcome.CreateWelcome(b, host, vector).Value;
                    type = Fb.Message.Welcome;
                    break;
                }
                case StartStreamMessage m:
                    body = Fb.StartStream.CreateStartStream(b, m.MonitorId, m.UdpPort, m.StreamId).Value;
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
                if (!Fb.Envelope.VerifyEnvelope(bb)) return new DecodeResult(DecodeStatus.Invalid, 0, null);
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
                    return new WelcomeMessage(w.HostName, monitors);
                }
                case Fb.Message.StartStream:
                {
                    var s = e.BodyAsStartStream();
                    return new StartStreamMessage(s.MonitorId, s.UdpPort, s.StreamId);
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
                default:
                    return null;
            }
        }
    }
}
```

- [ ] **Step 9: Rodar e ver passar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: `Com falha: 0` (75 anteriores + 8 novos). Se o gerado acusar `__vector_as_arraysegment`/`Span` ausente, o NuGet foi compilado com `ENABLE_SPAN_T`: adicionar `<DefineConstants>$(DefineConstants);ENABLE_SPAN_T</DefineConstants>` no harness e registrar no ADR 0006 a mesma definição para a Unity (`Assets/csc.rsp` com `-define:ENABLE_SPAN_T`).

- [ ] **Step 10: Documentação do protocolo**

`protocol/control-v1.md`:
```markdown
# Canal de controle — v1

Schema: `protocol/control.fbs` (FlatBuffers 25.2.10). C# gerado: `Assets/_Project/Infrastructure/Shared/Protocol/Generated/ControlProtocol.g.cs`
(`tools/gen-protocol.ps1`). Codec único: `ControlMessageCodec.cs` (usado pela Unity e pelo host via `host/Host.Shared`).

- **TCP 9200.** Frame: `[u32 little-endian tamanho][FlatBuffer Envelope]`, máximo 64 KiB. Tamanho inválido encerra a conexão.
- **mDNS:** `_screenportal._tcp.local`, porta 9200, TXT `v=1`; nome da instância = nome do PC.
- **Envelope:** `protocol_version` (1) + `body` (union). Versão diferente → `Bye(IncompatibleVersion)`.
- **Evolução (brief §8.9):** campos novos só no fim das tabelas e opcionais; nunca reordenar nem remover.

| Mensagem | Direção | Campos |
|---|---|---|
| Hello | headset → host | device_id, device_name, pairing_token (vazio na 1ª vez) |
| PairingRequired | host → headset | code (6 dígitos) |
| PairingResult | host → headset | accepted, pairing_token |
| Welcome | host → headset | host_name, monitors[id, width, height, name, is_primary] |
| StartStream | headset → host | monitor_id, udp_port, stream_id |
| StreamStarted / StreamStopped | host → headset | stream_id, width/height ou reason |
| Ping / Pong | ambos | sent_us (ecoado) |
| Bye | ambos | reason: Shutdown, IncompatibleVersion, PairingRejected, Replaced |

**Handshake:** Hello → (token válido) Welcome · (sem token) PairingRequired → diálogo no PC → PairingResult → Welcome.
Depois do Welcome: o headset prepara o decoder e envia StartStream; o host transmite (protocol/video-packet-v1.md) para o
IP de origem da conexão e responde StreamStarted. Ping a cada 500 ms (headset); o host responde Pong.
```

Adicionar ao `.gitignore` (o binário do flatc é versionado em `tools/flatc/`; nada a ignorar lá). Nenhuma mudança.

- [ ] **Step 11: Commit**

```bash
git add protocol tools/flatc tools/gen-protocol.ps1 Assets/_Project/Infrastructure Assets/_Project/Application/Connection Assets/_Project/ThirdParty Assets/_Project/Tests/Shared tests-dotnet
git commit -m "phase1b: control protocol v1 (FlatBuffers 25.2.10), shared codec and TCP framing with tests"
```

---

### Task 2: Modelo da conexão — portas, status, opções, ResolutionFit

**Files:**
- Create: `Assets/_Project/Domain/Screens/ResolutionFit.cs`
- Create: `Assets/_Project/Application/Connection/HostEndpoint.cs`, `Ports.cs`, `ConnectionStatus.cs`, `ConnectionOptions.cs`, `ConnectionStatusText.cs`
- Test: `Assets/_Project/Tests/EditMode/Connection/ResolutionFitTests.cs`, `ConnectionStatusTextTests.cs`

**Interfaces:**
- Consumes: `Resolution`, `ScreenId`, `Pose`, `Vec3` (Fase 1A); mensagens da Task 1.
- Produces:
  - `ResolutionFit.FromCapture(int width, int height) → Resolution` (reduz proporcionalmente até ≤ 4096, alinha para baixo a múltiplos de 8; `default` se ficar abaixo do mínimo).
  - `readonly struct HostEndpoint(string name, string address, int port)` com `Name`, `Address`, `Port`, `static bool TryParse(string, out HostEndpoint)`, `ToString()` = `"name|address|port"`.
  - `enum ChannelEventKind { None, Connected, ConnectFailed, Message, Disconnected }`
  - `interface IControlChannel { void Connect(HostEndpoint host); void Send(ControlMessage message); void Close(); ChannelEventKind Poll(out ControlMessage message); }`
  - `interface IHostDiscovery { void Start(); void Stop(); bool TryGetHost(out HostEndpoint host); }`
  - `interface IPairingStore { string DeviceId { get; } string GetToken(string hostName); void SaveToken(string hostName, string token); bool TryGetLastHost(out HostEndpoint host); void SaveLastHost(HostEndpoint host); }`
  - `interface IVideoStreamSink { bool TryStart(ScreenId screen, ushort streamId, int udpPort, Resolution resolution); void Stop(ushort streamId); }`
  - `interface IScreenSurfaceProvider { bool TryGetSurface(ScreenId screen, out IntPtr surface); }`
  - `interface IClock { double NowSeconds { get; } }`
  - `enum ConnectionState { Idle, Discovering, Connecting, Negotiating, Streaming, Degraded, Reconnecting }`
  - `enum ConnectionStatusKind { Idle, Searching, InstallHost, Connecting, ConfirmOnPc, Connected, Unstable, Reconnecting, Rejected, IncompatibleVersion, HostShutdown }`
  - `readonly struct ConnectionStatus(ConnectionStatusKind kind, string hostName = "", string pairingCode = "")` com igualdade por valor.
  - `sealed class ConnectionOptions` (propriedades com os tempos da Global Constraints).
  - `ConnectionStatusText.Format(ConnectionStatus) → string`, `const string HostInstallHint`.

- [ ] **Step 1: Escrever os testes que falham**

`Assets/_Project/Tests/EditMode/Connection/ResolutionFitTests.cs`:
```csharp
using NUnit.Framework;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Tests
{
    public class ResolutionFitTests
    {
        [TestCase(2560, 1080, 2560, 1080)]
        [TestCase(1366, 768, 1360, 768)]
        [TestCase(5120, 1440, 4096, 1152)]
        [TestCase(7680, 2160, 4096, 1152)]
        [TestCase(1440, 2560, 1440, 2560)]
        public void FitsToEncoderRules(int w, int h, int expectedW, int expectedH)
        {
            var r = ResolutionFit.FromCapture(w, h);
            Assert.That(r.IsValid, Is.True);
            Assert.That(r.Width, Is.EqualTo(expectedW));
            Assert.That(r.Height, Is.EqualTo(expectedH));
        }

        [Test]
        public void TooSmall_IsInvalid() => Assert.That(ResolutionFit.FromCapture(320, 200).IsValid, Is.False);
    }
}
```

`Assets/_Project/Tests/EditMode/Connection/ConnectionStatusTextTests.cs`:
```csharp
using NUnit.Framework;
using ScreenPortal.App.Connection;

namespace ScreenPortal.Tests
{
    public class ConnectionStatusTextTests
    {
        [Test]
        public void PairingCode_IsShownGrouped() =>
            Assert.That(ConnectionStatusText.Format(new ConnectionStatus(ConnectionStatusKind.ConfirmOnPc, "CAYO-PC", "482913")),
                Does.Contain("482 913"));

        [Test]
        public void HostName_AppearsWhenConnecting() =>
            Assert.That(ConnectionStatusText.Format(new ConnectionStatus(ConnectionStatusKind.Connecting, "CAYO-PC")),
                Does.Contain("CAYO-PC"));

        [Test]
        public void EveryKind_HasText()
        {
            foreach (ConnectionStatusKind kind in System.Enum.GetValues(typeof(ConnectionStatusKind)))
                Assert.That(ConnectionStatusText.Format(new ConnectionStatus(kind, "PC", "123456")), Is.Not.Null, kind.ToString());
        }

        [Test]
        public void InstallHost_TellsWhatToDo() =>
            Assert.That(ConnectionStatusText.Format(new ConnectionStatus(ConnectionStatusKind.InstallHost)),
                Does.Contain(ConnectionStatusText.HostInstallHint));

        [Test]
        public void HostEndpoint_RoundTripsThroughString()
        {
            var e = new HostEndpoint("CAYO-PC", "192.168.0.64", 9200);
            Assert.That(HostEndpoint.TryParse(e.ToString(), out var parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(e));
            Assert.That(HostEndpoint.TryParse("lixo", out _), Is.False);
        }
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: FAIL de compilação — `'ResolutionFit' could not be found`.

- [ ] **Step 3: Implementar**

`Assets/_Project/Domain/Screens/ResolutionFit.cs`:
```csharp
using System;

namespace ScreenPortal.Domain.Screens
{
    /// <summary>
    /// Converte o tamanho capturado de um monitor numa <see cref="Resolution"/> válida para o encoder
    /// (≤ 4096 por dimensão, múltiplos de 8). Usado pelo host (tamanho do encode) e pelo headset (tamanho da tela).
    /// </summary>
    public static class ResolutionFit
    {
        public static Resolution FromCapture(int width, int height)
        {
            if (width <= 0 || height <= 0) return default;
            double scale = Math.Min(1.0, (double)Resolution.MaxDimension / Math.Max(width, height));
            // epsilon: 7680 * (4096 / 7680) pode dar 4095,999… em ponto flutuante
            int w = AlignDown((int)Math.Floor(width * scale + 1e-6));
            int h = AlignDown((int)Math.Floor(height * scale + 1e-6));
            return Resolution.TryCreate(w, h, out var r, out _) ? r : default;
        }

        private static int AlignDown(int value) => value - value % Resolution.Alignment;
    }
}
```

`Assets/_Project/Application/Connection/HostEndpoint.cs`:
```csharp
using System;

namespace ScreenPortal.App.Connection
{
    /// <summary>Host encontrado (mDNS) ou lembrado. Formato texto: "nome|endereço|porta".</summary>
    public readonly struct HostEndpoint : IEquatable<HostEndpoint>
    {
        public string Name { get; }
        public string Address { get; }
        public int Port { get; }

        public HostEndpoint(string name, string address, int port)
        {
            Name = name ?? string.Empty;
            Address = address ?? string.Empty;
            Port = port;
        }

        public bool IsValid => Address.Length > 0 && Port > 0 && Port < 65536;

        public static bool TryParse(string text, out HostEndpoint host)
        {
            host = default;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split('|');
            if (parts.Length != 3 || !int.TryParse(parts[2], out int port)) return false;
            host = new HostEndpoint(parts[0], parts[1], port);
            return host.IsValid;
        }

        public bool Equals(HostEndpoint other) => Name == other.Name && Address == other.Address && Port == other.Port;
        public override bool Equals(object obj) => obj is HostEndpoint other && Equals(other);
        public override int GetHashCode() => (Name, Address, Port).GetHashCode();
        public override string ToString() => Name + "|" + Address + "|" + Port;
    }
}
```

`Assets/_Project/Application/Connection/Ports.cs`:
```csharp
using System;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Connection
{
    public enum ChannelEventKind
    {
        None,
        Connected,
        ConnectFailed,
        Message,
        Disconnected,
    }

    /// <summary>Canal de controle (TCP). Eventos são consumidos por Poll na thread principal.</summary>
    public interface IControlChannel
    {
        void Connect(HostEndpoint host);
        void Send(ControlMessage message);
        void Close();
        ChannelEventKind Poll(out ControlMessage message);
    }

    /// <summary>Descoberta do host na rede (mDNS no Quest).</summary>
    public interface IHostDiscovery
    {
        void Start();
        void Stop();
        bool TryGetHost(out HostEndpoint host);
    }

    /// <summary>Identidade do headset e tokens de pareamento por host.</summary>
    public interface IPairingStore
    {
        string DeviceId { get; }
        string GetToken(string hostName);
        void SaveToken(string hostName, string token);
        bool TryGetLastHost(out HostEndpoint host);
        void SaveLastHost(HostEndpoint host);
    }

    /// <summary>Decoder de vídeo de uma tela. TryStart devolve false enquanto a Surface da layer não existe.</summary>
    public interface IVideoStreamSink
    {
        bool TryStart(ScreenId screen, ushort streamId, int udpPort, Resolution resolution);
        void Stop(ushort streamId);
    }

    /// <summary>Implementado pela Presentation: a Surface (jobject) da compositor layer de uma tela.</summary>
    public interface IScreenSurfaceProvider
    {
        bool TryGetSurface(ScreenId screen, out IntPtr surface);
    }

    public interface IClock
    {
        double NowSeconds { get; }
    }
}
```

`Assets/_Project/Application/Connection/ConnectionStatus.cs`:
```csharp
using System;

namespace ScreenPortal.App.Connection
{
    public enum ConnectionState
    {
        Idle,
        Discovering,
        Connecting,
        Negotiating,
        Streaming,
        Degraded,
        Reconnecting,
    }

    public enum ConnectionStatusKind
    {
        Idle,
        Searching,
        InstallHost,
        Connecting,
        ConfirmOnPc,
        Connected,
        Unstable,
        Reconnecting,
        Rejected,
        IncompatibleVersion,
        HostShutdown,
    }

    /// <summary>O que mostrar ao usuário (o texto em si fica em <see cref="ConnectionStatusText"/>).</summary>
    public readonly struct ConnectionStatus : IEquatable<ConnectionStatus>
    {
        public ConnectionStatusKind Kind { get; }
        public string HostName { get; }
        public string PairingCode { get; }

        public ConnectionStatus(ConnectionStatusKind kind, string hostName = "", string pairingCode = "")
        {
            Kind = kind;
            HostName = hostName ?? string.Empty;
            PairingCode = pairingCode ?? string.Empty;
        }

        public bool Equals(ConnectionStatus other) =>
            Kind == other.Kind && HostName == other.HostName && PairingCode == other.PairingCode;
        public override bool Equals(object obj) => obj is ConnectionStatus other && Equals(other);
        public override int GetHashCode() => (Kind, HostName, PairingCode).GetHashCode();
        public override string ToString() => Kind + " " + HostName + " " + PairingCode;
    }
}
```

`Assets/_Project/Application/Connection/ConnectionOptions.cs`:
```csharp
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Connection
{
    /// <summary>Tempos e padrões da conexão (spec 1B §3). Instância registrada no container.</summary>
    public sealed class ConnectionOptions
    {
        public string DeviceName { get; set; } = "Quest";
        public double ConnectTimeoutSeconds { get; set; } = 2.0;
        public double NegotiateTimeoutSeconds { get; set; } = 5.0;
        public double PingIntervalSeconds { get; set; } = 0.5;
        public double DegradedAfterSeconds { get; set; } = 1.5;
        public double ReconnectAfterDegradedSeconds { get; set; } = 3.0;
        public double RetryIntervalSeconds { get; set; } = 0.25;
        public double ReconnectGiveUpSeconds { get; set; } = 10.0;
        public double InstallHintAfterSeconds { get; set; } = 10.0;
        public double RejectCooldownSeconds { get; set; } = 10.0;
        public double IncompatibleCooldownSeconds { get; set; } = 60.0;
        public double ShutdownCooldownSeconds { get; set; } = 3.0;
        public int VideoPort { get; set; } = 9000;
        public ushort StreamId { get; set; } = 0;
        public Pose DefaultScreenPose { get; set; } = Pose.At(new Vec3(0f, 1.2f, 1.3f));
        public float DefaultScreenWidthMeters { get; set; } = 1.4f;
    }
}
```

`Assets/_Project/Application/Connection/ConnectionStatusText.cs`:
```csharp
namespace ScreenPortal.App.Connection
{
    /// <summary>Textos pt-BR do status da conexão (brief §1.1). Localização fica para depois do MVP.</summary>
    public static class ConnectionStatusText
    {
        /// <summary>Instrução até existir instalador + link/QR code (Fase 5, brief §1.1).</summary>
        public const string HostInstallHint = "Abra o ScreenPortalHost.exe no seu PC\n(mesma rede Wi-Fi do Quest).";

        public static string Format(ConnectionStatus status)
        {
            string pc = status.HostName.Length > 0 ? status.HostName : "seu PC";
            switch (status.Kind)
            {
                case ConnectionStatusKind.Idle: return "";
                case ConnectionStatusKind.Searching: return "Procurando seu PC…";
                case ConnectionStatusKind.InstallHost:
                    return "Não encontramos seu PC.\n" + HostInstallHint;
                case ConnectionStatusKind.Connecting: return "Conectando a " + pc + "…";
                case ConnectionStatusKind.ConfirmOnPc: return "Confirme no PC:\ncódigo " + Group(status.PairingCode);
                case ConnectionStatusKind.Connected: return "Conectado a " + pc;
                case ConnectionStatusKind.Unstable: return "Conexão instável…";
                case ConnectionStatusKind.Reconnecting: return "Reconectando a " + pc + "…";
                case ConnectionStatusKind.Rejected: return "Conexão recusada no PC.";
                case ConnectionStatusKind.IncompatibleVersion: return "Versões incompatíveis.\nAtualize o app ou o host.";
                case ConnectionStatusKind.HostShutdown: return pc + " encerrou o Screen Portal.";
                default: return status.Kind.ToString();
            }
        }

        private static string Group(string code) =>
            code != null && code.Length == 6 ? code.Substring(0, 3) + " " + code.Substring(3) : code;
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: `Com falha: 0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Domain/Screens/ResolutionFit.cs Assets/_Project/Application/Connection Assets/_Project/Tests/EditMode/Connection
git commit -m "phase1b: connection ports, status model, options, texts and ResolutionFit"
```

---

### Task 3: ConnectionStateMachine

**Files:**
- Create: `Assets/_Project/Application/Connection/ConnectionStateMachine.cs`
- Test: `Assets/_Project/Tests/EditMode/Connection/ConnectionFakes.cs`, `ConnectionStateMachineTests.cs`

**Interfaces:**
- Consumes: tudo da Task 2; `WorkspaceStore`, `ICommandBus`, `CreateScreen`, `ResolutionFit`.
- Produces: `sealed class ConnectionStateMachine(IHostDiscovery, IControlChannel, IPairingStore, IClock, ICommandBus, WorkspaceStore, IVideoStreamSink, ConnectionOptions)` com `ConnectionState State`, `ConnectionStatus Status`, `event Action<ConnectionStatus> StatusChanged`, `void Start()`, `void Tick()`, `const string MonitorLabelPrefix = "monitor:"`.

- [ ] **Step 1: Fakes de teste**

`Assets/_Project/Tests/EditMode/Connection/ConnectionFakes.cs`:
```csharp
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

    internal sealed class FakeVideo : IVideoStreamSink
    {
        public bool Ready = true;
        public int Starts;
        public int Stops;
        public Resolution LastResolution;

        public bool TryStart(ScreenId screen, ushort streamId, int udpPort, Resolution resolution)
        {
            if (!Ready) return false;
            Starts++;
            LastResolution = resolution;
            return true;
        }

        public void Stop(ushort streamId) => Stops++;
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
        public readonly FakeVideo Video = new FakeVideo();
        public readonly WorkspaceStore Store = new WorkspaceStore();
        public readonly ConnectionStateMachine Machine;

        public ConnectionRig()
        {
            var bus = new CommandBus(Store, new ICommandObserver[0]);
            Machine = new ConnectionStateMachine(Discovery, Channel, Pairing, Clock, bus, Store, Video, new ConnectionOptions());
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

        /// <summary>Leva a máquina até Streaming com um host já pareado.</summary>
        public void GoToStreaming()
        {
            Pairing.Tokens["CAYO-PC"] = "tok";
            Machine.Start();
            Discovery.Host = Pc;
            Machine.Tick();
            Channel.Push(ChannelEventKind.Connected);
            Machine.Tick();
            Channel.Push(ChannelEventKind.Message, Welcome);
            Machine.Tick();
            Channel.Push(ChannelEventKind.Message, new StreamStartedMessage(0, 2560, 1080));
            Machine.Tick();
        }
    }
}
```

- [ ] **Step 2: Escrever os testes que falham**

`Assets/_Project/Tests/EditMode/Connection/ConnectionStateMachineTests.cs`:
```csharp
using NUnit.Framework;
using ScreenPortal.App.Connection;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class ConnectionStateMachineTests
    {
        [Test]
        public void Start_SearchesAndConnectsToDiscoveredHost()
        {
            var r = new ConnectionRig();
            r.Machine.Start();
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Discovering));
            Assert.That(r.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.Searching));
            Assert.That(r.Discovery.Started, Is.True);

            r.Discovery.Host = ConnectionRig.Pc;
            r.Machine.Tick();
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Connecting));
            Assert.That(r.Channel.Connects, Is.EqualTo(new[] { ConnectionRig.Pc }));
            Assert.That(r.Machine.Status.HostName, Is.EqualTo("CAYO-PC"));
        }

        [Test]
        public void Connected_SendsHelloWithStoredToken()
        {
            var r = new ConnectionRig();
            r.Pairing.Tokens["CAYO-PC"] = "tok";
            r.Machine.Start();
            r.Discovery.Host = ConnectionRig.Pc;
            r.Machine.Tick();
            r.Channel.Push(ChannelEventKind.Connected);
            r.Machine.Tick();

            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Negotiating));
            var hello = r.Channel.Last<HelloMessage>();
            Assert.That(hello.DeviceId, Is.EqualTo("dev-1"));
            Assert.That(hello.PairingToken, Is.EqualTo("tok"));
        }

        [Test]
        public void Pairing_ShowsCode_NeverTimesOut_AndSavesToken()
        {
            var r = new ConnectionRig();
            r.Machine.Start();
            r.Discovery.Host = ConnectionRig.Pc;
            r.Machine.Tick();
            r.Channel.Push(ChannelEventKind.Connected);
            r.Channel.Push(ChannelEventKind.Message, new PairingRequiredMessage("482913"));
            r.Machine.Tick();

            Assert.That(r.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.ConfirmOnPc));
            Assert.That(r.Machine.Status.PairingCode, Is.EqualTo("482913"));

            r.Advance(60);
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Negotiating), "sem timeout enquanto o usuário decide");

            r.Channel.Push(ChannelEventKind.Message, new PairingResultMessage(true, "novo"));
            r.Machine.Tick();
            Assert.That(r.Pairing.Tokens["CAYO-PC"], Is.EqualTo("novo"));
        }

        [Test]
        public void Welcome_CreatesScreenForPrimaryMonitor_AndRequestsStream()
        {
            var r = new ConnectionRig();
            r.GoToStreaming();

            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Streaming));
            Assert.That(r.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.Connected));
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(1));
            var screen = r.Store.State.Screens[0];
            Assert.That(screen.Label, Is.EqualTo("monitor:1"));
            Assert.That(screen.Resolution.Width, Is.EqualTo(2560));
            Assert.That(r.Video.Starts, Is.EqualTo(1));
            var start = r.Channel.Last<StartStreamMessage>();
            Assert.That(start.MonitorId, Is.EqualTo(1u));
            Assert.That(start.UdpPort, Is.EqualTo((ushort)9000));
            Assert.That(r.Pairing.LastHost, Is.EqualTo(ConnectionRig.Pc));
        }

        [Test]
        public void SurfaceNotReady_RetriesStartStreamOnLaterTick()
        {
            var r = new ConnectionRig();
            r.Video.Ready = false;
            r.Pairing.Tokens["CAYO-PC"] = "tok";
            r.Machine.Start();
            r.Discovery.Host = ConnectionRig.Pc;
            r.Machine.Tick();
            r.Channel.Push(ChannelEventKind.Connected);
            r.Channel.Push(ChannelEventKind.Message, ConnectionRig.Welcome);
            r.Machine.Tick();
            Assert.That(r.Channel.Count<StartStreamMessage>(), Is.EqualTo(0));

            r.Video.Ready = true;
            r.Machine.Tick();
            Assert.That(r.Channel.Count<StartStreamMessage>(), Is.EqualTo(1));
        }

        [Test]
        public void MissingPongs_Degrade_ThenRecover_ThenReconnect()
        {
            var r = new ConnectionRig();
            r.GoToStreaming();

            r.Advance(1.6);
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Degraded));
            Assert.That(r.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.Unstable));
            Assert.That(r.Channel.Count<PingMessage>(), Is.GreaterThanOrEqualTo(3));

            r.Channel.Push(ChannelEventKind.Message, new PongMessage(0));
            r.Machine.Tick();
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Streaming));

            int closes = r.Channel.Closes;
            r.Advance(4.6);
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Reconnecting));
            Assert.That(r.Channel.Closes, Is.GreaterThan(closes));
        }

        [Test]
        public void Reconnect_GoesStraightToLastHost_AndReusesScreenAndDecoder()
        {
            var r = new ConnectionRig();
            r.GoToStreaming();
            r.Store.Dispatch(new MoveScreen(r.Store.State.Screens[0].Id, Pose.At(new Vec3(-1f, 1f, 1f))));
            r.Discovery.Host = null; // prova que não depende do mDNS

            r.Channel.Push(ChannelEventKind.Disconnected);
            r.Machine.Tick();
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Reconnecting));
            r.Machine.Tick();
            Assert.That(r.Channel.Connects[r.Channel.Connects.Count - 1], Is.EqualTo(ConnectionRig.Pc));

            r.Channel.Push(ChannelEventKind.Connected);
            r.Channel.Push(ChannelEventKind.Message, ConnectionRig.Welcome);
            r.Machine.Tick();
            r.Channel.Push(ChannelEventKind.Message, new StreamStartedMessage(0, 2560, 1080));
            r.Machine.Tick();

            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Streaming));
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(1), "não duplica a tela");
            Assert.That(r.Store.State.Screens[0].Pose.Position, Is.EqualTo(new Vec3(-1f, 1f, 1f)), "layout intacto");
            Assert.That(r.Video.Starts, Is.EqualTo(1), "decoder continua o mesmo");
            Assert.That(r.Channel.Count<StartStreamMessage>(), Is.EqualTo(2));
        }

        [Test]
        public void Reconnect_RetriesEvery250ms_AndGivesUpAfter10s()
        {
            var r = new ConnectionRig();
            r.GoToStreaming();
            r.Channel.Push(ChannelEventKind.Disconnected);
            r.Machine.Tick();
            r.Machine.Tick();
            int attempts = r.Channel.Connects.Count;

            r.Channel.Push(ChannelEventKind.ConnectFailed);
            r.Advance(0.1, 0.1);
            Assert.That(r.Channel.Connects.Count, Is.EqualTo(attempts), "ainda não passou 250 ms");
            r.Advance(0.2, 0.2);
            Assert.That(r.Channel.Connects.Count, Is.EqualTo(attempts + 1));

            r.Discovery.Host = null; // senão, ao desistir, a descoberta reconectaria na hora
            for (int i = 0; i < 60; i++)
            {
                r.Channel.Push(ChannelEventKind.ConnectFailed);
                r.Advance(0.25, 0.25);
            }
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Discovering));
            Assert.That(r.Video.Stops, Is.EqualTo(1));
        }

        [Test]
        public void Rejected_BlocksHostDuringCooldown()
        {
            var r = new ConnectionRig();
            r.Machine.Start();
            r.Discovery.Host = ConnectionRig.Pc;
            r.Machine.Tick();
            r.Channel.Push(ChannelEventKind.Connected);
            r.Channel.Push(ChannelEventKind.Message, new PairingResultMessage(false, ""));
            r.Machine.Tick();

            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Discovering));
            Assert.That(r.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.Rejected));
            int connects = r.Channel.Connects.Count;
            r.Advance(9);
            Assert.That(r.Channel.Connects.Count, Is.EqualTo(connects));
            r.Advance(1.5);
            Assert.That(r.Channel.Connects.Count, Is.EqualTo(connects + 1));
        }

        [Test]
        public void ByeMessages_ShowTheRightStatus()
        {
            var r = new ConnectionRig();
            r.GoToStreaming();
            r.Channel.Push(ChannelEventKind.Message, new ByeMessage(ByeReason.Shutdown));
            r.Machine.Tick();
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Discovering));
            Assert.That(r.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.HostShutdown));

            var r2 = new ConnectionRig();
            r2.GoToStreaming();
            r2.Channel.Push(ChannelEventKind.Message, new ByeMessage(ByeReason.IncompatibleVersion));
            r2.Machine.Tick();
            Assert.That(r2.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.IncompatibleVersion));
        }

        [Test]
        public void Timeouts_ConnectAndNegotiate()
        {
            var r = new ConnectionRig();
            r.Machine.Start();
            r.Discovery.Host = ConnectionRig.Pc;
            r.Machine.Tick();
            r.Discovery.Host = null;
            r.Advance(2.1);
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Discovering), "connect > 2 s");

            r.Discovery.Host = ConnectionRig.Pc;
            r.Machine.Tick();
            r.Channel.Push(ChannelEventKind.Connected);
            r.Machine.Tick();
            r.Discovery.Host = null;
            r.Advance(5.1);
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Discovering), "negociação > 5 s sem pareamento");
        }

        [Test]
        public void InstallHint_OnlyWhenNeverConnected()
        {
            var r = new ConnectionRig();
            r.Machine.Start();
            r.Advance(10.1);
            Assert.That(r.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.InstallHost));

            var r2 = new ConnectionRig();
            r2.Pairing.LastHost = ConnectionRig.Pc;
            r2.Machine.Start();
            r2.Advance(10.1);
            Assert.That(r2.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.Searching));
        }

        [Test]
        public void HostPing_IsAnsweredWithPong()
        {
            var r = new ConnectionRig();
            r.GoToStreaming();
            r.Channel.Push(ChannelEventKind.Message, new PingMessage(77));
            r.Machine.Tick();
            Assert.That(r.Channel.Last<PongMessage>().SentUs, Is.EqualTo(77UL));
        }
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: FAIL de compilação — `'ConnectionStateMachine' could not be found`.

- [ ] **Step 4: Implementar**

`Assets/_Project/Application/Connection/ConnectionStateMachine.cs`:
```csharp
using System;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Connection
{
    /// <summary>
    /// Ciclo de conexão do headset (brief §8.5, spec 1B §3):
    /// Idle → Discovering → Connecting → Negotiating → Streaming ⇄ Degraded → Reconnecting.
    /// Puro: tempo e I/O entram por portas; chamar <see cref="Tick"/> uma vez por frame na thread principal.
    /// </summary>
    public sealed class ConnectionStateMachine
    {
        public const string MonitorLabelPrefix = "monitor:";

        private readonly IHostDiscovery _discovery;
        private readonly IControlChannel _channel;
        private readonly IPairingStore _pairing;
        private readonly IClock _clock;
        private readonly ICommandBus _bus;
        private readonly WorkspaceStore _store;
        private readonly IVideoStreamSink _video;
        private readonly ConnectionOptions _options;

        private HostEndpoint _host;
        private double _stateSince;
        private double _lastPingAt;
        private double _lastPongAt;
        private double _lastAttemptAt;
        private bool _attemptInFlight;
        private bool _awaitingPairing;
        private bool _welcomed;
        private bool _streamRequested;
        private bool _videoStarted;
        private bool _hasConnectedBefore;
        private MonitorDescriptor _monitor;
        private ScreenId _screen;
        private string _blockedHost;
        private double _blockedUntil;

        public ConnectionState State { get; private set; } = ConnectionState.Idle;
        public ConnectionStatus Status { get; private set; } = new ConnectionStatus(ConnectionStatusKind.Idle);
        public event Action<ConnectionStatus> StatusChanged;

        public ConnectionStateMachine(
            IHostDiscovery discovery,
            IControlChannel channel,
            IPairingStore pairing,
            IClock clock,
            ICommandBus bus,
            WorkspaceStore store,
            IVideoStreamSink video,
            ConnectionOptions options)
        {
            _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _pairing = pairing ?? throw new ArgumentNullException(nameof(pairing));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _video = video ?? throw new ArgumentNullException(nameof(video));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public void Start()
        {
            if (State != ConnectionState.Idle) return;
            _hasConnectedBefore = _pairing.TryGetLastHost(out _);
            _discovery.Start();
            EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
        }

        public void Tick()
        {
            ChannelEventKind kind;
            while ((kind = _channel.Poll(out var message)) != ChannelEventKind.None)
                Handle(kind, message);

            double now = _clock.NowSeconds;
            switch (State)
            {
                case ConnectionState.Discovering:
                    TickDiscovering(now);
                    break;
                case ConnectionState.Connecting:
                    if (now - _stateSince > _options.ConnectTimeoutSeconds)
                    {
                        _channel.Close();
                        EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
                    }
                    break;
                case ConnectionState.Negotiating:
                    TickNegotiating(now);
                    break;
                case ConnectionState.Streaming:
                case ConnectionState.Degraded:
                    TickStreaming(now);
                    break;
                case ConnectionState.Reconnecting:
                    TickReconnecting(now);
                    break;
            }
        }

        private void TickDiscovering(double now)
        {
            if (_discovery.TryGetHost(out var host) && !IsBlocked(host, now))
            {
                _host = host;
                _channel.Connect(host);
                Enter(ConnectionState.Connecting, new ConnectionStatus(ConnectionStatusKind.Connecting, host.Name));
                return;
            }
            if (!_hasConnectedBefore
                && Status.Kind == ConnectionStatusKind.Searching
                && now - _stateSince > _options.InstallHintAfterSeconds)
            {
                SetStatus(new ConnectionStatus(ConnectionStatusKind.InstallHost));
            }
        }

        private void TickNegotiating(double now)
        {
            if (!_welcomed && !_awaitingPairing && now - _stateSince > _options.NegotiateTimeoutSeconds)
            {
                _channel.Close();
                EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
                return;
            }
            if (_welcomed && !_streamRequested) TryRequestStream();
        }

        private void TickStreaming(double now)
        {
            if (now - _lastPingAt >= _options.PingIntervalSeconds)
            {
                _lastPingAt = now;
                _channel.Send(new PingMessage((ulong)(now * 1_000_000.0)));
            }

            double silence = now - _lastPongAt;
            if (State == ConnectionState.Streaming && silence > _options.DegradedAfterSeconds)
            {
                Enter(ConnectionState.Degraded, new ConnectionStatus(ConnectionStatusKind.Unstable, _host.Name));
            }
            else if (State == ConnectionState.Degraded
                     && silence > _options.DegradedAfterSeconds + _options.ReconnectAfterDegradedSeconds)
            {
                _channel.Close();
                EnterReconnecting();
            }
        }

        private void TickReconnecting(double now)
        {
            if (now - _stateSince > _options.ReconnectGiveUpSeconds)
            {
                StopVideo();
                EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
                return;
            }
            if (!_attemptInFlight && now - _lastAttemptAt >= _options.RetryIntervalSeconds)
            {
                _lastAttemptAt = now;
                _attemptInFlight = true;
                _channel.Connect(_host);
            }
        }

        private void Handle(ChannelEventKind kind, ControlMessage message)
        {
            switch (kind)
            {
                case ChannelEventKind.Connected:
                    if (State == ConnectionState.Connecting || State == ConnectionState.Reconnecting)
                    {
                        _attemptInFlight = false;
                        EnterNegotiating();
                    }
                    break;
                case ChannelEventKind.ConnectFailed:
                    if (State == ConnectionState.Connecting)
                        EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
                    else if (State == ConnectionState.Reconnecting)
                        _attemptInFlight = false;
                    break;
                case ChannelEventKind.Disconnected:
                    if (State == ConnectionState.Streaming || State == ConnectionState.Degraded
                        || (State == ConnectionState.Negotiating && _welcomed))
                        EnterReconnecting();
                    else if (State == ConnectionState.Connecting || State == ConnectionState.Negotiating)
                        EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
                    break;
                case ChannelEventKind.Message:
                    if (message != null) HandleMessage(message);
                    break;
            }
        }

        private void HandleMessage(ControlMessage message)
        {
            double now = _clock.NowSeconds;
            switch (message)
            {
                case PairingRequiredMessage pairing:
                    _awaitingPairing = true;
                    SetStatus(new ConnectionStatus(ConnectionStatusKind.ConfirmOnPc, _host.Name, pairing.Code));
                    break;
                case PairingResultMessage result:
                    if (result.Accepted)
                    {
                        _pairing.SaveToken(_host.Name, result.PairingToken);
                        _awaitingPairing = false;
                        SetStatus(new ConnectionStatus(ConnectionStatusKind.Connecting, _host.Name));
                    }
                    else
                    {
                        Block(ConnectionStatusKind.Rejected, _options.RejectCooldownSeconds);
                    }
                    break;
                case WelcomeMessage welcome:
                    OnWelcome(welcome);
                    break;
                case StreamStartedMessage _:
                    if (State == ConnectionState.Negotiating)
                    {
                        _lastPongAt = now;
                        _lastPingAt = now;
                        _hasConnectedBefore = true;
                        Enter(ConnectionState.Streaming, new ConnectionStatus(ConnectionStatusKind.Connected, _host.Name));
                    }
                    break;
                case StreamStoppedMessage _:
                    _streamRequested = false; // tenta de novo no próximo Tick
                    break;
                case PingMessage ping:
                    _channel.Send(new PongMessage(ping.SentUs));
                    break;
                case PongMessage _:
                    _lastPongAt = now;
                    if (State == ConnectionState.Degraded)
                        Enter(ConnectionState.Streaming, new ConnectionStatus(ConnectionStatusKind.Connected, _host.Name));
                    break;
                case ByeMessage bye:
                    OnBye(bye.Reason);
                    break;
            }
        }

        private void OnWelcome(WelcomeMessage welcome)
        {
            _welcomed = true;
            _awaitingPairing = false;
            _pairing.SaveLastHost(_host);

            _monitor = null;
            for (int i = 0; i < welcome.Monitors.Count; i++)
                if (welcome.Monitors[i].IsPrimary) _monitor = welcome.Monitors[i];
            if (_monitor == null && welcome.Monitors.Count > 0) _monitor = welcome.Monitors[0];
            if (_monitor == null)
            {
                _channel.Close();
                EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
                return;
            }

            _screen = EnsureScreenFor(_monitor);
            TryRequestStream();
        }

        /// <summary>Só cria a tela se ainda não existir (spec 1B §3: reconexão não duplica nem mexe no layout).</summary>
        private ScreenId EnsureScreenFor(MonitorDescriptor monitor)
        {
            string label = MonitorLabelPrefix + monitor.Id;
            var screens = _store.State.Screens;
            for (int i = 0; i < screens.Count; i++)
                if (screens[i].Label == label) return screens[i].Id;

            var resolution = ResolutionFit.FromCapture((int)monitor.Width, (int)monitor.Height);
            if (!resolution.IsValid) resolution = Resolution.Create(1920, 1080);
            var result = _bus.Send(new CreateScreen(resolution, _options.DefaultScreenPose, _options.DefaultScreenWidthMeters, label: label));
            return result.Accepted ? _store.State.FocusedScreenId : ScreenId.None;
        }

        private void TryRequestStream()
        {
            if (_screen.IsNone || !_store.State.TryGetScreen(_screen, out var screen)) return;
            if (!_videoStarted)
                _videoStarted = _video.TryStart(_screen, _options.StreamId, _options.VideoPort, screen.Resolution);
            if (!_videoStarted) return; // Surface da layer ainda não existe: tenta no próximo Tick

            _channel.Send(new StartStreamMessage(_monitor.Id, (ushort)_options.VideoPort, _options.StreamId));
            _streamRequested = true;
        }

        private void OnBye(ByeReason reason)
        {
            switch (reason)
            {
                case ByeReason.Shutdown:
                    // Bloqueio curto: o anúncio mDNS demora a sumir; sem isso reconectaríamos na hora e a
                    // mensagem "encerrou" piscaria e sumiria.
                    Block(ConnectionStatusKind.HostShutdown, _options.ShutdownCooldownSeconds);
                    break;
                case ByeReason.IncompatibleVersion:
                    Block(ConnectionStatusKind.IncompatibleVersion, _options.IncompatibleCooldownSeconds);
                    break;
                case ByeReason.PairingRejected:
                    Block(ConnectionStatusKind.Rejected, _options.RejectCooldownSeconds);
                    break;
                default:
                    _channel.Close();
                    StopVideo();
                    EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
                    break;
            }
        }

        private void Block(ConnectionStatusKind statusKind, double cooldownSeconds)
        {
            _channel.Close();
            StopVideo();
            _blockedHost = _host.Name;
            _blockedUntil = _clock.NowSeconds + cooldownSeconds;
            EnterDiscovering(new ConnectionStatus(statusKind, _host.Name));
        }

        private bool IsBlocked(HostEndpoint host, double now) => host.Name == _blockedHost && now < _blockedUntil;

        private void StopVideo()
        {
            if (_videoStarted) _video.Stop(_options.StreamId);
            _videoStarted = false;
            _streamRequested = false;
        }

        private void EnterNegotiating()
        {
            _welcomed = false;
            _awaitingPairing = false;
            _streamRequested = false;
            Enter(ConnectionState.Negotiating, new ConnectionStatus(ConnectionStatusKind.Connecting, _host.Name));
            _channel.Send(new HelloMessage(_pairing.DeviceId, _options.DeviceName, _pairing.GetToken(_host.Name)));
        }

        private void EnterReconnecting()
        {
            _attemptInFlight = false;
            _lastAttemptAt = double.NegativeInfinity; // primeira tentativa no próximo Tick
            _streamRequested = false;
            Enter(ConnectionState.Reconnecting, new ConnectionStatus(ConnectionStatusKind.Reconnecting, _host.Name));
        }

        private void EnterDiscovering(ConnectionStatus status)
        {
            _welcomed = false;
            _awaitingPairing = false;
            _streamRequested = false;
            _attemptInFlight = false;
            Enter(ConnectionState.Discovering, status);
        }

        private void Enter(ConnectionState state, ConnectionStatus status)
        {
            State = state;
            _stateSince = _clock.NowSeconds;
            SetStatus(status);
        }

        private void SetStatus(ConnectionStatus status)
        {
            if (status.Equals(Status)) return;
            Status = status;
            StatusChanged?.Invoke(status);
        }
    }
}
```

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: `Com falha: 0`.

- [ ] **Step 6: Commit**

```bash
git add Assets/_Project/Application/Connection/ConnectionStateMachine.cs Assets/_Project/Tests/EditMode/Connection
git commit -m "phase1b: connection state machine (discover, pair, stream, degrade, reconnect) with tests"
```

---

### Task 4: Cliente TCP, relógio, descoberta estática e pareamento em memória (puros)

**Files:**
- Create: `Assets/_Project/Infrastructure/Shared/Net/TcpControlChannel.cs`, `MonotonicClock.cs`, `StaticHostDiscovery.cs`, `InMemoryPairingStore.cs`
- Test: `Assets/_Project/Tests/Shared/TcpControlChannelTests.cs`

**Interfaces:**
- Consumes: `IControlChannel`, `IClock`, `IHostDiscovery`, `IPairingStore`, `HostEndpoint` (Task 2); `FrameCodec`, `FrameReader`, `ControlMessageCodec` (Task 1).
- Produces (namespace `ScreenPortal.Infrastructure.Net`): `TcpControlChannel(int connectTimeoutMs) : IControlChannel, IDisposable`; `MonotonicClock : IClock`; `StaticHostDiscovery(HostEndpoint host) : IHostDiscovery`; `InMemoryPairingStore(string deviceId) : IPairingStore`.

- [ ] **Step 1: Escrever o teste que falha**

`Assets/_Project/Tests/Shared/TcpControlChannelTests.cs`:
```csharp
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NUnit.Framework;
using ScreenPortal.App.Connection;
using ScreenPortal.Infrastructure.Net;
using ScreenPortal.Infrastructure.Protocol;

namespace ScreenPortal.Tests
{
    public class TcpControlChannelTests
    {
        private static ChannelEventKind WaitEvent(TcpControlChannel channel, out ControlMessage message, int timeoutMs = 3000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                var kind = channel.Poll(out message);
                if (kind != ChannelEventKind.None) return kind;
                Thread.Sleep(5);
            }
            message = null;
            return ChannelEventKind.None;
        }

        [Test]
        public void ConnectsReceivesFramedMessagesAndSeesDisconnect()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using (var channel = new TcpControlChannel(2000))
            {
                channel.Connect(new HostEndpoint("test", "127.0.0.1", port));
                using (var server = listener.AcceptTcpClient())
                {
                    Assert.That(WaitEvent(channel, out _), Is.EqualTo(ChannelEventKind.Connected));

                    var frame = FrameCodec.Frame(ControlMessageCodec.Encode(new PairingRequiredMessage("123456")));
                    server.GetStream().Write(frame, 0, frame.Length);
                    Assert.That(WaitEvent(channel, out var message), Is.EqualTo(ChannelEventKind.Message));
                    Assert.That(((PairingRequiredMessage)message).Code, Is.EqualTo("123456"));

                    channel.Send(new PingMessage(5));
                    var reader = new FrameReader();
                    var buffer = new byte[256];
                    byte[] payload;
                    do
                    {
                        int n = server.GetStream().Read(buffer, 0, buffer.Length);
                        reader.Append(buffer, 0, n);
                    } while (!reader.TryRead(out payload));
                    Assert.That(((PingMessage)ControlMessageCodec.Decode(payload).Message).SentUs, Is.EqualTo(5UL));
                }
                Assert.That(WaitEvent(channel, out _), Is.EqualTo(ChannelEventKind.Disconnected));
            }
            listener.Stop();
        }

        [Test]
        public void ConnectToClosedPort_ReportsConnectFailed()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            using (var channel = new TcpControlChannel(1000))
            {
                channel.Connect(new HostEndpoint("test", "127.0.0.1", port));
                Assert.That(WaitEvent(channel, out _), Is.EqualTo(ChannelEventKind.ConnectFailed));
            }
        }

        [Test]
        public void Close_DiscardsEventsFromOldConnection()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using (var channel = new TcpControlChannel(2000))
            {
                channel.Connect(new HostEndpoint("test", "127.0.0.1", port));
                using (listener.AcceptTcpClient())
                {
                    Assert.That(WaitEvent(channel, out _), Is.EqualTo(ChannelEventKind.Connected));
                    channel.Close();
                }
                Assert.That(WaitEvent(channel, out _, 300), Is.EqualTo(ChannelEventKind.None), "Disconnected da conexão fechada por nós é descartado");
            }
            listener.Stop();
        }

        [Test]
        public void InMemoryPairingStore_KeepsTokensAndLastHost()
        {
            var store = new InMemoryPairingStore("dev");
            store.SaveToken("PC", "t");
            store.SaveLastHost(new HostEndpoint("PC", "1.2.3.4", 9200));
            Assert.That(store.GetToken("PC"), Is.EqualTo("t"));
            Assert.That(store.GetToken("outro"), Is.EqualTo(""));
            Assert.That(store.TryGetLastHost(out var host), Is.True);
            Assert.That(host.Address, Is.EqualTo("1.2.3.4"));
        }
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: FAIL de compilação — `The type or namespace name 'Net' does not exist in the namespace 'ScreenPortal.Infrastructure'`.

- [ ] **Step 3: Implementar**

`Assets/_Project/Infrastructure/Shared/Net/TcpControlChannel.cs`:
```csharp
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using ScreenPortal.App.Connection;
using ScreenPortal.Infrastructure.Protocol;

namespace ScreenPortal.Infrastructure.Net
{
    /// <summary>
    /// Cliente TCP do canal de controle. Uma thread por conexão lê frames e enfileira eventos; <see cref="Poll"/>
    /// os entrega na thread principal. Cada Connect/Close troca a "geração": eventos de conexões antigas são descartados.
    /// Não é caminho quente (poucas mensagens por segundo): alocação por mensagem é aceitável.
    /// </summary>
    public sealed class TcpControlChannel : IControlChannel, IDisposable
    {
        private readonly ConcurrentQueue<ChannelEvent> _events = new ConcurrentQueue<ChannelEvent>();
        private readonly object _lock = new object();
        private readonly int _connectTimeoutMs;
        private TcpClient _client;
        private NetworkStream _stream;
        private int _generation;

        private struct ChannelEvent
        {
            public int Generation;
            public ChannelEventKind Kind;
            public ControlMessage Message;
        }

        public TcpControlChannel(int connectTimeoutMs) => _connectTimeoutMs = connectTimeoutMs;

        public void Connect(HostEndpoint host)
        {
            int generation;
            lock (_lock)
            {
                CloseLocked();
                generation = _generation;
            }
            var thread = new Thread(() => Run(host, generation)) { IsBackground = true, Name = "sp-control" };
            thread.Start();
        }

        private void Run(HostEndpoint host, int generation)
        {
            var client = new TcpClient { NoDelay = true };
            try
            {
                var connect = client.ConnectAsync(host.Address, host.Port);
                if (!connect.Wait(_connectTimeoutMs) || !client.Connected) throw new TimeoutException();
            }
            catch (Exception)
            {
                client.Dispose();
                Post(generation, ChannelEventKind.ConnectFailed, null);
                return;
            }

            NetworkStream stream;
            lock (_lock)
            {
                if (generation != _generation)
                {
                    client.Dispose();
                    return;
                }
                _client = client;
                _stream = stream = client.GetStream();
            }
            Post(generation, ChannelEventKind.Connected, null);

            var reader = new FrameReader();
            var buffer = new byte[8192];
            try
            {
                while (true)
                {
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;
                    reader.Append(buffer, 0, read);
                    while (reader.TryRead(out var payload))
                    {
                        var decoded = ControlMessageCodec.Decode(payload);
                        if (decoded.Status == DecodeStatus.Ok)
                            Post(generation, ChannelEventKind.Message, decoded.Message);
                        else if (decoded.Status == DecodeStatus.IncompatibleVersion)
                            Post(generation, ChannelEventKind.Message, new ByeMessage(ByeReason.IncompatibleVersion));
                    }
                    if (reader.IsCorrupt) break;
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }

            Post(generation, ChannelEventKind.Disconnected, null);
        }

        private void Post(int generation, ChannelEventKind kind, ControlMessage message) =>
            _events.Enqueue(new ChannelEvent { Generation = generation, Kind = kind, Message = message });

        public ChannelEventKind Poll(out ControlMessage message)
        {
            while (_events.TryDequeue(out var e))
            {
                if (e.Generation != Volatile.Read(ref _generation)) continue;
                message = e.Message;
                return e.Kind;
            }
            message = null;
            return ChannelEventKind.None;
        }

        public void Send(ControlMessage message)
        {
            var frame = FrameCodec.Frame(ControlMessageCodec.Encode(message));
            lock (_lock)
            {
                if (_stream == null) return;
                try { _stream.Write(frame, 0, frame.Length); }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            }
        }

        public void Close()
        {
            lock (_lock) CloseLocked();
        }

        private void CloseLocked()
        {
            Interlocked.Increment(ref _generation);
            _stream = null;
            var client = _client;
            _client = null;
            client?.Dispose();
        }

        public void Dispose() => Close();
    }
}
```

`Assets/_Project/Infrastructure/Shared/Net/MonotonicClock.cs`:
```csharp
using System.Diagnostics;
using ScreenPortal.App.Connection;

namespace ScreenPortal.Infrastructure.Net
{
    public sealed class MonotonicClock : IClock
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public double NowSeconds => _stopwatch.Elapsed.TotalSeconds;
    }
}
```

`Assets/_Project/Infrastructure/Shared/Net/StaticHostDiscovery.cs`:
```csharp
using ScreenPortal.App.Connection;

namespace ScreenPortal.Infrastructure.Net
{
    /// <summary>"Descoberta" com endereço fixo: Editor da Unity e testes de integração no PC.</summary>
    public sealed class StaticHostDiscovery : IHostDiscovery
    {
        private readonly HostEndpoint _host;
        private bool _running;

        public StaticHostDiscovery(HostEndpoint host) => _host = host;

        public void Start() => _running = true;
        public void Stop() => _running = false;

        public bool TryGetHost(out HostEndpoint host)
        {
            host = _host;
            return _running && _host.IsValid;
        }
    }
}
```

`Assets/_Project/Infrastructure/Shared/Net/InMemoryPairingStore.cs`:
```csharp
using System.Collections.Generic;
using ScreenPortal.App.Connection;

namespace ScreenPortal.Infrastructure.Net
{
    /// <summary>Pareamento sem persistência (testes e Editor).</summary>
    public sealed class InMemoryPairingStore : IPairingStore
    {
        private readonly Dictionary<string, string> _tokens = new Dictionary<string, string>();
        private HostEndpoint? _lastHost;

        public InMemoryPairingStore(string deviceId) => DeviceId = deviceId;

        public string DeviceId { get; }
        public string GetToken(string hostName) => _tokens.TryGetValue(hostName, out var token) ? token : string.Empty;
        public void SaveToken(string hostName, string token) => _tokens[hostName] = token ?? string.Empty;
        public void SaveLastHost(HostEndpoint host) => _lastHost = host;

        public bool TryGetLastHost(out HostEndpoint host)
        {
            host = _lastHost ?? default;
            return _lastHost.HasValue;
        }
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: `Com falha: 0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Infrastructure/Shared/Net Assets/_Project/Tests/Shared/TcpControlChannelTests.cs
git commit -m "phase1b: TCP control channel (generation-safe events), monotonic clock, static discovery, in-memory pairing"
```

---

### Task 5: Host — código compartilhado, pareamento e sessão do headset

**Files:**
- Create: `host/Host.Shared/Host.Shared.csproj`
- Create: `host/Host.Domain/Pairing/PairingStore.cs`
- Create: `host/Host.Transport/Control/IControlConnection.cs`, `HostPorts.cs`, `HeadsetSession.cs`
- Modify: `host/Host.Transport/Host.Transport.csproj` (referenciar Host.Shared), `host/Host.Tests/Host.Tests.csproj` (WindowsTfm + referências), `host/Host.sln`
- Test: `host/Host.Tests/Control/PairingStoreTests.cs`, `HeadsetSessionTests.cs`

**Interfaces:**
- Consumes: mensagens (Task 1) via `Host.Shared`.
- Produces:
  - `ScreenPortal.Host.Domain.Pairing.PairingStore(IEnumerable<PairedDevice>? devices = null, Func<byte[]>? randomBytes = null)` com `bool IsPaired(string deviceId, string token)`, `string Pair(string deviceId, string deviceName, DateTimeOffset when)`, `string Serialize()`, `static PairingStore Deserialize(string json)`, `static string NewPairingCode(Func<int, int> randomBelow)`, `IReadOnlyCollection<PairedDevice> Devices`; `record PairedDevice(string DeviceId, string DeviceName, string Token, DateTimeOffset PairedAt)`.
  - `ScreenPortal.Host.Transport.Control`: `interface IControlConnection { string RemoteAddress { get; } void Send(ControlMessage m); void Close(); }`, `interface IPairingApprover { Task<bool> ApproveAsync(string deviceName, string code); }`, `interface IMonitorProvider { IReadOnlyList<MonitorDescriptor> GetMonitors(); }`, `interface IStreamRegistry { bool TryStart(string deviceId, string address, ushort udpPort, ushort streamId, MonitorDescriptor monitor, out int width, out int height); void Release(string deviceId); }`, `sealed class HeadsetSession(IControlConnection connection, PairingStore pairing, Action savePairing, IPairingApprover approver, IMonitorProvider monitors, IStreamRegistry streams, string hostName, Func<string> newCode, Action<string> log)` com `void OnMessage(ControlMessage)`, `void OnClosed()`.

- [ ] **Step 1: Projeto compartilhado**

`host/Host.Shared/Host.Shared.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- Compila o código PURO do headset (Assets/_Project) para o host: um só codec, mensagens e máquina de estados.
       Mesmas regras da Unity: C# 9, sem ImplicitUsings/Nullable. -->
  <PropertyGroup>
    <TargetFramework>$(PortableTfm)</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <RootNamespace>ScreenPortal</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../Assets/_Project/Domain/**/*.cs" Link="Unity/Domain/%(RecursiveDir)%(Filename)%(Extension)" />
    <Compile Include="../../Assets/_Project/Application/**/*.cs" Link="Unity/Application/%(RecursiveDir)%(Filename)%(Extension)" />
    <Compile Include="../../Assets/_Project/Infrastructure/Shared/**/*.cs" Link="Unity/Infrastructure/%(RecursiveDir)%(Filename)%(Extension)" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Google.FlatBuffers" Version="25.2.10" />
  </ItemGroup>
</Project>
```

```powershell
dotnet sln host/Host.sln add host/Host.Shared
dotnet add host/Host.Transport reference host/Host.Shared
```

Em `host/Host.Tests/Host.Tests.csproj`, trocar `<TargetFramework>$(PortableTfm)</TargetFramework>` por `<TargetFramework>$(WindowsTfm)</TargetFramework>` e a referência por:
```xml
    <ProjectReference Include="..\Host.Domain\Host.Domain.csproj" />
    <ProjectReference Include="..\Host.Shared\Host.Shared.csproj" />
    <ProjectReference Include="..\Host.Transport\Host.Transport.csproj" />
```

Run: `dotnet build host/Host.sln -v q` → Expected: `Compilação com êxito` (Host.Shared compila o código da Unity).

- [ ] **Step 2: Escrever os testes que falham**

`host/Host.Tests/Control/PairingStoreTests.cs`:
```csharp
using ScreenPortal.Host.Domain.Pairing;

namespace ScreenPortal.Host.Tests.Control;

public class PairingStoreTests
{
    private static byte[] FixedBytes() => Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void Pair_IssuesToken_ThatIsRecognized()
    {
        var store = new PairingStore(randomBytes: FixedBytes);
        string token = store.Pair("dev-1", "Quest 3", DateTimeOffset.UnixEpoch);

        Assert.Equal(Convert.ToBase64String(FixedBytes()), token);
        Assert.True(store.IsPaired("dev-1", token));
        Assert.False(store.IsPaired("dev-1", "outro"));
        Assert.False(store.IsPaired("dev-2", token));
        Assert.False(store.IsPaired("dev-1", ""));
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var store = new PairingStore(randomBytes: FixedBytes);
        string token = store.Pair("dev-1", "Quest 3", DateTimeOffset.UnixEpoch);
        var restored = PairingStore.Deserialize(store.Serialize());

        Assert.True(restored.IsPaired("dev-1", token));
        Assert.Equal("Quest 3", restored.Devices.Single().DeviceName);
    }

    [Fact]
    public void Deserialize_CorruptFile_StartsEmpty() =>
        Assert.Empty(PairingStore.Deserialize("{lixo").Devices);

    [Fact]
    public void PairingCode_HasSixDigits() =>
        Assert.Equal("000042", PairingStore.NewPairingCode(_ => 42));
}
```

`host/Host.Tests/Control/HeadsetSessionTests.cs`:
```csharp
using ScreenPortal.App.Connection;
using ScreenPortal.Host.Domain.Pairing;
using ScreenPortal.Host.Transport.Control;

namespace ScreenPortal.Host.Tests.Control;

public class HeadsetSessionTests
{
    internal sealed class FakeConnection : IControlConnection
    {
        public readonly List<ControlMessage> Sent = new();
        public bool Closed;
        public string RemoteAddress => "192.168.0.171";
        public void Send(ControlMessage message) { lock (Sent) Sent.Add(message); }
        public void Close() => Closed = true;
        public T? Last<T>() where T : ControlMessage { lock (Sent) return Sent.OfType<T>().LastOrDefault(); }
    }

    internal sealed class FakeApprover(bool answer) : IPairingApprover
    {
        public string? LastCode;
        public Task<bool> ApproveAsync(string deviceName, string code) { LastCode = code; return Task.FromResult(answer); }
    }

    internal sealed class FakeMonitors : IMonitorProvider
    {
        public IReadOnlyList<MonitorDescriptor> GetMonitors() => new[] { new MonitorDescriptor(1, 2560, 1080, "DISPLAY1", true) };
    }

    internal sealed class FakeStreams : IStreamRegistry
    {
        public int Starts, Releases;
        public string? LastAddress;
        public bool TryStart(string deviceId, string address, ushort udpPort, ushort streamId, MonitorDescriptor monitor, out int width, out int height)
        {
            Starts++;
            LastAddress = address;
            width = 2560;
            height = 1080;
            return true;
        }
        public void Release(string deviceId) => Releases++;
    }

    private static (HeadsetSession session, FakeConnection conn, PairingStore pairing, FakeStreams streams, FakeApprover approver) Create(bool approve)
    {
        var conn = new FakeConnection();
        var pairing = new PairingStore();
        var streams = new FakeStreams();
        var approver = new FakeApprover(approve);
        var session = new HeadsetSession(conn, pairing, () => { }, approver, new FakeMonitors(), streams, "CAYO-PC", () => "482913", _ => { });
        return (session, conn, pairing, streams, approver);
    }

    [Fact]
    public void UnknownDevice_IsAskedToPair_ThenWelcomedWithToken()
    {
        var (session, conn, pairing, _, approver) = Create(approve: true);
        session.OnMessage(new HelloMessage("dev-1", "Quest 3", ""));

        Assert.Equal("482913", conn.Last<PairingRequiredMessage>()!.Code);
        Assert.Equal("482913", approver.LastCode);
        var result = conn.Last<PairingResultMessage>()!;
        Assert.True(result.Accepted);
        Assert.True(pairing.IsPaired("dev-1", result.PairingToken));
        Assert.Equal("CAYO-PC", conn.Last<WelcomeMessage>()!.HostName);
    }

    [Fact]
    public void Rejected_SendsByeAndCloses()
    {
        var (session, conn, _, _, _) = Create(approve: false);
        session.OnMessage(new HelloMessage("dev-1", "Quest 3", ""));

        Assert.False(conn.Last<PairingResultMessage>()!.Accepted);
        Assert.Equal(ByeReason.PairingRejected, conn.Last<ByeMessage>()!.Reason);
        Assert.Null(conn.Last<WelcomeMessage>());
        Assert.True(conn.Closed);
    }

    [Fact]
    public void KnownDevice_IsWelcomedDirectly_AndCanStream()
    {
        var (session, conn, pairing, streams, _) = Create(approve: false);
        string token = pairing.Pair("dev-1", "Quest 3", DateTimeOffset.UnixEpoch);
        session.OnMessage(new HelloMessage("dev-1", "Quest 3", token));

        Assert.Null(conn.Last<PairingRequiredMessage>());
        Assert.NotNull(conn.Last<WelcomeMessage>());

        session.OnMessage(new StartStreamMessage(1, 9000, 0));
        Assert.Equal(1, streams.Starts);
        Assert.Equal("192.168.0.171", streams.LastAddress);
        Assert.Equal(1080u, conn.Last<StreamStartedMessage>()!.Height);

        session.OnMessage(new PingMessage(9));
        Assert.Equal(9UL, conn.Last<PongMessage>()!.SentUs);

        session.OnClosed();
        Assert.Equal(1, streams.Releases);
    }

    [Fact]
    public void StartStream_BeforeWelcome_IsIgnored()
    {
        var (session, conn, _, streams, _) = Create(approve: false);
        session.OnMessage(new StartStreamMessage(1, 9000, 0));
        Assert.Equal(0, streams.Starts);
        Assert.Null(conn.Last<StreamStartedMessage>());
    }

    [Fact]
    public void UnknownMonitor_ReportsStreamStopped()
    {
        var (session, conn, pairing, _, _) = Create(approve: false);
        session.OnMessage(new HelloMessage("dev-1", "Quest", pairing.Pair("dev-1", "Quest", DateTimeOffset.UnixEpoch)));
        session.OnMessage(new StartStreamMessage(99, 9000, 0));
        Assert.NotNull(conn.Last<StreamStoppedMessage>());
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test host/Host.Tests -v q`
Expected: FAIL de compilação — `The type or namespace name 'Pairing' does not exist in the namespace 'ScreenPortal.Host.Domain'`.

- [ ] **Step 4: Implementar**

`host/Host.Domain/Pairing/PairingStore.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ScreenPortal.Host.Domain.Pairing;

public sealed record PairedDevice(string DeviceId, string DeviceName, string Token, DateTimeOffset PairedAt);

/// <summary>Headsets pareados com este PC (spec 1B §2). Thread-safe: sessões rodam em threads diferentes.</summary>
public sealed class PairingStore
{
    private readonly Dictionary<string, PairedDevice> _devices = new();
    private readonly Func<byte[]> _randomBytes;
    private readonly object _lock = new();

    public PairingStore(IEnumerable<PairedDevice>? devices = null, Func<byte[]>? randomBytes = null)
    {
        _randomBytes = randomBytes ?? (() => RandomNumberGenerator.GetBytes(32));
        if (devices != null)
            foreach (var d in devices) _devices[d.DeviceId] = d;
    }

    public IReadOnlyCollection<PairedDevice> Devices
    {
        get { lock (_lock) return _devices.Values.ToArray(); }
    }

    public bool IsPaired(string deviceId, string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        lock (_lock)
        {
            if (!_devices.TryGetValue(deviceId, out var device)) return false;
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(device.Token), Encoding.UTF8.GetBytes(token));
        }
    }

    public string Pair(string deviceId, string deviceName, DateTimeOffset when)
    {
        string token = Convert.ToBase64String(_randomBytes());
        lock (_lock) _devices[deviceId] = new PairedDevice(deviceId, deviceName, token, when);
        return token;
    }

    public string Serialize()
    {
        lock (_lock) return JsonSerializer.Serialize(_devices.Values.ToArray(), new JsonSerializerOptions { WriteIndented = true });
    }

    public static PairingStore Deserialize(string json)
    {
        try { return new PairingStore(JsonSerializer.Deserialize<PairedDevice[]>(json)); }
        catch (JsonException) { return new PairingStore(); }
    }

    public static string NewPairingCode(Func<int, int> randomBelow) => randomBelow(1_000_000).ToString("D6");
}
```

`host/Host.Transport/Control/IControlConnection.cs`:
```csharp
using ScreenPortal.App.Connection;

namespace ScreenPortal.Host.Transport.Control;

/// <summary>Conexão de controle com um headset (lado do host). Send é thread-safe.</summary>
public interface IControlConnection
{
    string RemoteAddress { get; }
    void Send(ControlMessage message);
    void Close();
}
```

`host/Host.Transport/Control/HostPorts.cs`:
```csharp
using ScreenPortal.App.Connection;

namespace ScreenPortal.Host.Transport.Control;

/// <summary>Pergunta ao usuário do PC se o headset pode ver a tela (spec 1B §2).</summary>
public interface IPairingApprover
{
    Task<bool> ApproveAsync(string deviceName, string code);
}

public interface IMonitorProvider
{
    IReadOnlyList<MonitorDescriptor> GetMonitors();
}

/// <summary>Pipelines de vídeo por headset. TryStart reaproveita o pipeline do mesmo device (reconexão).</summary>
public interface IStreamRegistry
{
    bool TryStart(string deviceId, string address, ushort udpPort, ushort streamId, MonitorDescriptor monitor, out int width, out int height);
    void Release(string deviceId);
}
```

`host/Host.Transport/Control/HeadsetSession.cs`:
```csharp
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
        var monitor = monitors.GetMonitors().FirstOrDefault(m => m.Id == start.MonitorId);
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
```

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test host/Host.Tests -v q`
Expected: `Com falha: 0` (17 anteriores + 9 novos).

- [ ] **Step 6: Commit**

```bash
git add host
git commit -m "phase1b(host): Host.Shared links the Unity pure code; pairing store; headset session handshake"
```

---

### Task 6: Host — ControlServer e teste de integração com a máquina de estados real

**Files:**
- Create: `host/Host.Transport/Control/ControlServer.cs`
- Test: `host/Host.Tests/Control/ControlServerIntegrationTests.cs`

**Interfaces:**
- Consumes: `HeadsetSession`, `IControlConnection` (Task 5); `FrameReader`, `FrameCodec`, `ControlMessageCodec`, `ControlProtocol` (Task 1); `ConnectionStateMachine`, `TcpControlChannel`, `StaticHostDiscovery`, `InMemoryPairingStore`, `MonotonicClock` (Tasks 3–4).
- Produces: `ControlServer(int port, Func<IControlConnection, HeadsetSession> sessionFactory, Action<string> log) : IDisposable` com `void Start()`, `int Port` (porta real; 0 = aleatória), `int ConnectionCount`, `void DropAllConnections()`, `void BroadcastBye(ByeReason reason)`.

- [ ] **Step 1: Escrever o teste que falha**

`host/Host.Tests/Control/ControlServerIntegrationTests.cs`:
```csharp
using System.Diagnostics;
using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Host.Domain.Pairing;
using ScreenPortal.Host.Transport.Control;
using ScreenPortal.Infrastructure.Net;
using Xunit.Abstractions;

namespace ScreenPortal.Host.Tests.Control;

/// <summary>
/// Máquina de estados REAL do headset + TcpControlChannel real contra o ControlServer real (localhost).
/// Mede a reconexão (critério do brief §4.2: &lt; 3 s).
/// </summary>
public class ControlServerIntegrationTests(ITestOutputHelper output)
{
    private sealed class OkVideo : IVideoStreamSink
    {
        public int Starts;
        public bool TryStart(ScreenId screen, ushort streamId, int udpPort, Resolution resolution) { Starts++; return true; }
        public void Stop(ushort streamId) { }
    }

    private static bool RunUntil(ConnectionStateMachine machine, Func<bool> condition, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            machine.Tick();
            if (condition()) return true;
            Thread.Sleep(5);
        }
        return false;
    }

    [Fact]
    public void PairsStreamsAndReconnectsUnderThreeSeconds()
    {
        var pairing = new PairingStore();
        var streams = new HeadsetSessionTests.FakeStreams();
        using var server = new ControlServer(0, conn => new HeadsetSession(conn, pairing, () => { },
            new HeadsetSessionTests.FakeApprover(true), new HeadsetSessionTests.FakeMonitors(), streams,
            "TEST-PC", () => "123456", _ => { }), _ => { });
        server.Start();

        var store = new WorkspaceStore();
        var video = new OkVideo();
        var clientPairing = new InMemoryPairingStore("dev-it");
        using var channel = new TcpControlChannel(2000);
        var machine = new ConnectionStateMachine(
            new StaticHostDiscovery(new HostEndpoint("TEST-PC", "127.0.0.1", server.Port)),
            channel, clientPairing, new MonotonicClock(), new CommandBus(store, Array.Empty<ICommandObserver>()),
            store, video, new ConnectionOptions { DeviceName = "Quest-IT" });
        machine.Start();

        Assert.True(RunUntil(machine, () => machine.State == ConnectionState.Streaming, 5000), $"estado: {machine.State}");
        Assert.NotEqual("", clientPairing.GetToken("TEST-PC"));
        Assert.Single(store.State.Screens);
        Assert.Equal(2560, store.State.Screens[0].Resolution.Width);
        Assert.Equal(1, streams.Starts);

        var sw = Stopwatch.StartNew();
        server.DropAllConnections();
        Assert.True(RunUntil(machine, () => machine.State == ConnectionState.Reconnecting, 2000), $"estado: {machine.State}");
        Assert.True(RunUntil(machine, () => machine.State == ConnectionState.Streaming, 5000), $"estado: {machine.State}");
        sw.Stop();
        output.WriteLine($"reconexão: {sw.ElapsedMilliseconds} ms");

        Assert.True(sw.ElapsedMilliseconds < 3000, $"reconexão levou {sw.ElapsedMilliseconds} ms");
        Assert.Single(store.State.Screens);
        Assert.Equal(1, video.Starts);
        Assert.Equal(2, streams.Starts);
    }

    [Fact]
    public void ShutdownBye_IsShownToTheHeadset()
    {
        var pairing = new PairingStore();
        using var server = new ControlServer(0, conn => new HeadsetSession(conn, pairing, () => { },
            new HeadsetSessionTests.FakeApprover(true), new HeadsetSessionTests.FakeMonitors(),
            new HeadsetSessionTests.FakeStreams(), "TEST-PC", () => "123456", _ => { }), _ => { });
        server.Start();

        var store = new WorkspaceStore();
        using var channel = new TcpControlChannel(2000);
        var machine = new ConnectionStateMachine(
            new StaticHostDiscovery(new HostEndpoint("TEST-PC", "127.0.0.1", server.Port)),
            channel, new InMemoryPairingStore("dev-it"), new MonotonicClock(),
            new CommandBus(store, Array.Empty<ICommandObserver>()), store, new OkVideo(), new ConnectionOptions());
        machine.Start();
        Assert.True(RunUntil(machine, () => machine.State == ConnectionState.Streaming, 5000));

        server.BroadcastBye(ByeReason.Shutdown);
        Assert.True(RunUntil(machine, () => machine.Status.Kind == ConnectionStatusKind.HostShutdown, 2000));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test host/Host.Tests -v q`
Expected: FAIL de compilação — `The type or namespace name 'ControlServer' could not be found`.

- [ ] **Step 3: Implementar**

`host/Host.Transport/Control/ControlServer.cs`:
```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using ScreenPortal.App.Connection;
using ScreenPortal.Infrastructure.Protocol;

namespace ScreenPortal.Host.Transport.Control;

/// <summary>Servidor TCP do canal de controle (porta 9200): uma thread e uma HeadsetSession por headset.</summary>
public sealed class ControlServer(int port, Func<IControlConnection, HeadsetSession> sessionFactory, Action<string> log) : IDisposable
{
    private readonly ConcurrentDictionary<TcpConnection, byte> _connections = new();
    private TcpListener? _listener;
    private volatile bool _stopping;

    public int Port { get; private set; } = port;
    public int ConnectionCount => _connections.Count;

    public void Start()
    {
        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        new Thread(AcceptLoop) { IsBackground = true, Name = "control-accept" }.Start();
        log($"controle: ouvindo tcp/{Port}");
    }

    private void AcceptLoop()
    {
        while (!_stopping)
        {
            TcpClient client;
            try { client = _listener!.AcceptTcpClient(); }
            catch (SocketException) { break; }
            catch (ObjectDisposedException) { break; }
            new Thread(() => Serve(client)) { IsBackground = true, Name = "control-session" }.Start();
        }
    }

    private void Serve(TcpClient client)
    {
        client.NoDelay = true;
        var connection = new TcpConnection(client);
        _connections[connection] = 0;
        var session = sessionFactory(connection);
        log($"controle: headset conectado ({connection.RemoteAddress})");
        var reader = new FrameReader();
        var buffer = new byte[8192];
        try
        {
            var stream = client.GetStream();
            while (!_stopping)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                reader.Append(buffer, 0, read);
                while (reader.TryRead(out var payload))
                {
                    var decoded = ControlMessageCodec.Decode(payload);
                    if (decoded.Status == DecodeStatus.Ok) session.OnMessage(decoded.Message);
                    else if (decoded.Status == DecodeStatus.IncompatibleVersion)
                    {
                        log($"controle: versão incompatível do headset ({decoded.Version})");
                        connection.Send(new ByeMessage(ByeReason.IncompatibleVersion));
                        connection.Close();
                    }
                }
                if (reader.IsCorrupt) break;
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
        finally
        {
            _connections.TryRemove(connection, out _);
            connection.Close();
            session.OnClosed();
            log($"controle: headset desconectado ({connection.RemoteAddress})");
        }
    }

    /// <summary>Derruba todas as conexões (testes de reconexão).</summary>
    public void DropAllConnections()
    {
        foreach (var c in _connections.Keys) c.Close();
    }

    public void BroadcastBye(ByeReason reason)
    {
        foreach (var c in _connections.Keys) c.Send(new ByeMessage(reason));
    }

    public void Dispose()
    {
        _stopping = true;
        BroadcastBye(ByeReason.Shutdown);
        DropAllConnections();
        _listener?.Stop();
    }

    private sealed class TcpConnection(TcpClient client) : IControlConnection
    {
        private readonly object _lock = new();
        public string RemoteAddress { get; } = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.MapToIPv4().ToString();

        public void Send(ControlMessage message)
        {
            var frame = FrameCodec.Frame(ControlMessageCodec.Encode(message));
            lock (_lock)
            {
                try { client.GetStream().Write(frame, 0, frame.Length); }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
        }

        public void Close()
        {
            lock (_lock) client.Dispose();
        }
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test host/Host.Tests -v q --logger "console;verbosity=normal" 2>&1 | Select-String "reconexão|Aprovado|Com falha"`
Expected: linha `reconexão: <N> ms` com N < 3000 e `Com falha: 0`.

- [ ] **Step 5: Commit**

```bash
git add host
git commit -m "phase1b(host): control server; integration test with the real headset state machine (reconnect < 3 s)"
```

---

### Task 7: Host — vídeo por sessão, monitores, diálogo de pareamento, mDNS e comando `serve`

**Files:**
- Modify: `host/Host.App/StreamPipeline.cs` (stream id inicial + `RequestKeyframe`), `host/Host.App/Program.cs` (comando `serve`, padrão), `host/Host.App/Host.App.csproj` (referenciar Host.Shared)
- Create: `host/Host.App/Control/StreamRegistry.cs`, `WindowsMonitorProvider.cs`, `MessageBoxPairingApprover.cs`, `MdnsAdvertiser.cs`

**Interfaces:**
- Consumes: `IStreamRegistry`, `IMonitorProvider`, `IPairingApprover`, `HeadsetSession`, `ControlServer`, `PairingStore` (Tasks 5–6); `ResolutionFit` (Task 2); `StreamPipeline`, `GpuDevice`, `WgcMonitorCapture`, `ClockSyncServer` (Fase 0).
- Produces: `StreamPipeline(..., string? dumpPath, ushort firstStreamId = 0)` + `void RequestKeyframe()`; `StreamRegistry(GpuDevice gpu, int bitrateKbps, Action<string> log) : IStreamRegistry, IDisposable` com `string TakeStats(double seconds)`; `WindowsMonitorProvider : IMonitorProvider`; `MessageBoxPairingApprover : IPairingApprover`; `MdnsAdvertiser(Action<string> log) : IDisposable` com `void Start(string instanceName, int port, string txtVersion)`; comando `ScreenPortalHost serve [--bitrate N]` (e sem argumentos).

- [ ] **Step 1: StreamPipeline com stream id e keyframe**

Em `host/Host.App/StreamPipeline.cs`, trocar o construtor e o laço de senders por:
```csharp
    public StreamPipeline(GpuDevice gpu, ICaptureSource capture, EncoderSettings settings,
        System.Net.IPEndPoint target, int streamCount, string? dumpPath, ushort firstStreamId = 0)
    {
        _capture = capture;
        _converter = new Nv12Converter(gpu.Device, gpu.Context, capture.Width, capture.Height, settings.Width, settings.Height);
        _encoder = new MfH264Encoder(gpu.Device, settings, this);
        _minFrameIntervalUs = 1_000_000L / settings.Fps - 2_000; // tolerância para jitter do compositor

        _packetizers = new VideoPacketizer[streamCount];
        _senders = new UdpVideoSender[streamCount];
        for (int i = 0; i < streamCount; i++)
        {
            _packetizers[i] = new VideoPacketizer((ushort)(firstStreamId + i));
            _senders[i] = new UdpVideoSender(new System.Net.IPEndPoint(target.Address, target.Port + i));
        }
        if (dumpPath is not null) _dump = File.Create(dumpPath);

        Description = $"{capture.Name} {capture.Width}x{capture.Height} → {_encoder.Name} " +
                      $"{settings.Width}x{settings.Height}@{settings.Fps} {settings.BitrateKbps} kbps → " +
                      $"udp://{target.Address}:{target.Port}..{target.Port + streamCount - 1}";
    }

    /// <summary>Reconexão: o decoder do headset precisa de um IDR para voltar a mostrar imagem.</summary>
    public void RequestKeyframe() => _encoder.RequestKeyframe();
```

Em `host/Host.App/Host.App.csproj`, adicionar `<ProjectReference Include="..\Host.Shared\Host.Shared.csproj" />`.

- [ ] **Step 2: Registro de pipelines por headset**

`host/Host.App/Control/StreamRegistry.cs`:
```csharp
using System.Net;
using ScreenPortal.App.Connection;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Host.Capture;
using ScreenPortal.Host.Encode;
using ScreenPortal.Host.Transport.Control;

namespace ScreenPortal.Host.App.Control;

/// <summary>
/// Um StreamPipeline por headset. Na reconexão do mesmo device (mesmo destino) o pipeline é reaproveitado
/// com um IDR forçado; sem reconexão em 10 s, é encerrado (spec 1B §3).
/// Fase 1B: só o monitor primário (a captura WGC da Fase 0).
/// </summary>
public sealed class StreamRegistry(GpuDevice gpu, int bitrateKbps, Action<string> log) : IStreamRegistry, IDisposable
{
    private static readonly TimeSpan ReleaseGrace = TimeSpan.FromSeconds(10);
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly object _lock = new();

    private sealed class Entry
    {
        public required StreamPipeline Pipeline;
        public required ICaptureSource Capture;
        public required string Address;
        public required ushort Port;
        public required ushort StreamId;
        public required int Width;
        public required int Height;
        public CancellationTokenSource? ReleaseTimer;
    }

    public bool TryStart(string deviceId, string address, ushort udpPort, ushort streamId, MonitorDescriptor monitor, out int width, out int height)
    {
        width = height = 0;
        if (!monitor.IsPrimary) return false;
        lock (_lock)
        {
            if (_entries.TryGetValue(deviceId, out var existing))
            {
                if (existing.Address == address && existing.Port == udpPort && existing.StreamId == streamId)
                {
                    existing.ReleaseTimer?.Cancel();
                    existing.ReleaseTimer = null;
                    existing.Pipeline.RequestKeyframe();
                    width = existing.Width;
                    height = existing.Height;
                    log($"vídeo: reaproveitando o stream de {deviceId} (IDR forçado)");
                    return true;
                }
                Stop(existing);
                _entries.Remove(deviceId);
            }

            try
            {
                var capture = WgcMonitorCapture.ForPrimaryMonitor(gpu, captureCursor: true);
                var fit = ResolutionFit.FromCapture(capture.Width, capture.Height);
                var settings = new EncoderSettings(fit.Width, fit.Height, 60, bitrateKbps);
                var pipeline = new StreamPipeline(gpu, capture, settings, new IPEndPoint(IPAddress.Parse(address), udpPort), 1, null, streamId);
                pipeline.Start();
                _entries[deviceId] = new Entry
                {
                    Pipeline = pipeline, Capture = capture, Address = address, Port = udpPort,
                    StreamId = streamId, Width = fit.Width, Height = fit.Height,
                };
                width = fit.Width;
                height = fit.Height;
                log($"vídeo: {pipeline.Description}");
                return true;
            }
            catch (Exception e)
            {
                log($"vídeo: falha ao iniciar ({e.GetType().Name}: {e.Message})");
                return false;
            }
        }
    }

    public void Release(string deviceId)
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            if (!_entries.TryGetValue(deviceId, out var entry)) return;
            entry.ReleaseTimer?.Cancel();
            cts = entry.ReleaseTimer = new CancellationTokenSource();
        }
        Task.Delay(ReleaseGrace, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            lock (_lock)
            {
                if (_entries.TryGetValue(deviceId, out var entry) && entry.ReleaseTimer == cts)
                {
                    Stop(entry);
                    _entries.Remove(deviceId);
                    log($"vídeo: stream de {deviceId} encerrado (sem reconexão em {ReleaseGrace.TotalSeconds:F0} s)");
                }
            }
        });
    }

    public string TakeStats(double seconds)
    {
        lock (_lock)
        {
            if (_entries.Count == 0) return "sem stream";
            return string.Join(" || ", _entries.Select(e => $"{e.Key}: {e.Value.Pipeline.TakeStats(seconds)}"));
        }
    }

    private static void Stop(Entry entry)
    {
        entry.ReleaseTimer?.Cancel();
        entry.Pipeline.Dispose(); // descarta também a captura
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var entry in _entries.Values) Stop(entry);
            _entries.Clear();
        }
    }
}
```

- [ ] **Step 3: Monitores, diálogo de pareamento e mDNS**

`host/Host.App/Control/WindowsMonitorProvider.cs`:
```csharp
using System.Runtime.InteropServices;
using ScreenPortal.App.Connection;
using ScreenPortal.Host.Transport.Control;

namespace ScreenPortal.Host.App.Control;

/// <summary>Monitores do Windows em pixels físicos (o processo é Per-Monitor DPI Aware v2, app.manifest).</summary>
public sealed class WindowsMonitorProvider : IMonitorProvider
{
    public IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        var result = new List<MonitorDescriptor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var r = info.Monitor;
                result.Add(new MonitorDescriptor((uint)(result.Count + 1), (uint)(r.Right - r.Left), (uint)(r.Bottom - r.Top),
                    info.DeviceName, (info.Flags & MonitorInfoFPrimary) != 0));
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private const uint MonitorInfoFPrimary = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
}
```

`host/Host.App/Control/MessageBoxPairingApprover.cs`:
```csharp
using System.Runtime.InteropServices;
using ScreenPortal.Host.Transport.Control;

namespace ScreenPortal.Host.App.Control;

/// <summary>Diálogo "Permitir / Negar" no topo da tela (o ícone na bandeja entra na Fase 5).</summary>
public sealed class MessageBoxPairingApprover : IPairingApprover
{
    public Task<bool> ApproveAsync(string deviceName, string code) => Task.Run(() =>
    {
        string grouped = code.Length == 6 ? code[..3] + " " + code[3..] : code;
        string text = $"'{deviceName}' quer ver sua tela.\n\nConfira se o headset mostra o código:\n\n        {grouped}\n\nPermitir?";
        return MessageBoxW(IntPtr.Zero, text, "Screen Portal — novo headset", MbYesNo | MbIconQuestion | MbTopMost | MbSetForeground) == IdYes;
    });

    private const uint MbYesNo = 0x4, MbIconQuestion = 0x20, MbTopMost = 0x40000, MbSetForeground = 0x10000;
    private const int IdYes = 6;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);
}
```

`host/Host.App/Control/MdnsAdvertiser.cs`:
```csharp
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using ScreenPortal.Infrastructure.Protocol;

namespace ScreenPortal.Host.App.Control;

/// <summary>
/// Anuncia "&lt;PC&gt;._screenportal._tcp.local" pela API nativa DNS-SD do Windows (dnsapi, Win10 1809+),
/// sem biblioteca de terceiros (spec 1B, decisão 4).
/// </summary>
public sealed class MdnsAdvertiser(Action<string> log) : IDisposable
{
    private const uint DnsQueryRequestVersion1 = 1;
    private const uint DnsRequestPending = 9506;

    private IntPtr _request;
    private IntPtr _instance;
    private IntPtr _ip4;
    private RegisterComplete? _callback; // mantém o delegate vivo enquanto o Windows pode chamá-lo

    public void Start(string instanceName, int port, string txtVersion)
    {
        var address = GetPrimaryIPv4();
        _ip4 = Marshal.AllocHGlobal(4);
        Marshal.Copy(address.GetAddressBytes(), 0, _ip4, 4); // IP4_ADDRESS em ordem de rede

        string service = $"{instanceName}.{ControlProtocol.ServiceType}.local";
        string host = $"{Environment.MachineName}.local";
        _instance = DnsServiceConstructInstance(service, host, _ip4, IntPtr.Zero, (ushort)port, 0, 0, 1,
            new[] { "v" }, new[] { txtVersion });
        if (_instance == IntPtr.Zero) throw new InvalidOperationException("DnsServiceConstructInstance falhou.");

        _callback = (status, _, _) => log(status == 0 ? $"mDNS: anunciado {service} ({address}:{port})" : $"mDNS: erro {status}");
        var request = new DnsServiceRegisterRequest
        {
            Version = DnsQueryRequestVersion1,
            InterfaceIndex = 0,
            ServiceInstance = _instance,
            RegisterCompletionCallback = Marshal.GetFunctionPointerForDelegate(_callback),
        };
        _request = Marshal.AllocHGlobal(Marshal.SizeOf<DnsServiceRegisterRequest>());
        Marshal.StructureToPtr(request, _request, false);

        uint result = DnsServiceRegister(_request, IntPtr.Zero);
        if (result != DnsRequestPending) throw new InvalidOperationException($"DnsServiceRegister retornou {result}.");
    }

    /// <summary>
    /// IP da interface que o Windows usa na rota padrão. "Conectar" um socket UDP não envia pacote algum, só
    /// escolhe a rota. Evita anunciar o IP de adaptadores de VPN (ex.: Radmin 26.x) que também têm gateway.
    /// </summary>
    private static IPAddress GetPrimaryIPv4()
    {
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect(new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53));
            if (probe.LocalEndPoint is IPEndPoint local && !IPAddress.IsLoopback(local.Address)) return local.Address;
        }
        catch (SocketException) { /* sem rota padrão (rede isolada): cai na busca por interface */ }

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            var props = nic.GetIPProperties();
            if (!props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork)) continue;
            var ip = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            if (ip != null) return ip.Address;
        }
        throw new InvalidOperationException("Nenhuma interface IPv4 com gateway encontrada.");
    }

    public void Dispose()
    {
        if (_request != IntPtr.Zero)
        {
            DnsServiceDeRegister(_request, IntPtr.Zero);
            // _request/_instance/_ip4 ficam alocados até o fim do processo: o DeRegister é assíncrono e
            // ainda pode ler a estrutura (vazamento único, na saída do host).
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceRegisterRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public IntPtr ServiceInstance;
        public IntPtr RegisterCompletionCallback;
        public IntPtr QueryContext;
        public IntPtr Credentials;
        public int UnicastEnabled;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void RegisterComplete(uint status, IntPtr context, IntPtr instance);

    [DllImport("dnsapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DnsServiceConstructInstance(string serviceName, string hostName, IntPtr ip4, IntPtr ip6,
        ushort port, ushort priority, ushort weight, uint propertiesCount,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] keys,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] values);

    [DllImport("dnsapi.dll")]
    private static extern uint DnsServiceRegister(IntPtr request, IntPtr cancel);

    [DllImport("dnsapi.dll")]
    private static extern uint DnsServiceDeRegister(IntPtr request, IntPtr cancel);
}
```

- [ ] **Step 4: Comando `serve`**

Em `host/Host.App/Program.cs`:

1. Trocar o bloco de `switch (opts.Command)` por:
```csharp
switch (opts.Command)
{
    case "":
    case "serve": return RunServe(opts, cts.Token);
    case "stream": return RunStream(opts, cts.Token);
    case "receive": return RunReceive(opts, cts.Token);
    default:
        Console.Error.WriteLine("Uso: Host.App [serve] | stream --target <ip> [...] | receive [...]");
        return 2;
}
```

2. Adicionar, antes de `static int RunStream`:
```csharp
static int RunServe(Args o, CancellationToken ct)
{
    static void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");

    string pairingPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenPortal", "paired.json");
    var pairing = File.Exists(pairingPath)
        ? ScreenPortal.Host.Domain.Pairing.PairingStore.Deserialize(File.ReadAllText(pairingPath))
        : new ScreenPortal.Host.Domain.Pairing.PairingStore();
    var saveLock = new object();
    void SavePairing()
    {
        lock (saveLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pairingPath)!);
            File.WriteAllText(pairingPath, pairing.Serialize());
        }
    }

    using var gpu = new GpuDevice();
    using var clock = new ClockSyncServer();
    using var streams = new ScreenPortal.Host.App.Control.StreamRegistry(gpu, o.Int("bitrate", 20_000), Log);
    var monitors = new ScreenPortal.Host.App.Control.WindowsMonitorProvider();
    var approver = new ScreenPortal.Host.App.Control.MessageBoxPairingApprover();
    string hostName = Environment.MachineName;

    using var server = new ScreenPortal.Host.Transport.Control.ControlServer(
        ScreenPortal.Infrastructure.Protocol.ControlProtocol.Port,
        connection => new ScreenPortal.Host.Transport.Control.HeadsetSession(connection, pairing, SavePairing, approver, monitors, streams,
            hostName, () => ScreenPortal.Host.Domain.Pairing.PairingStore.NewPairingCode(System.Security.Cryptography.RandomNumberGenerator.GetInt32), Log),
        Log);
    server.Start();

    using var mdns = new ScreenPortal.Host.App.Control.MdnsAdvertiser(Log);
    mdns.Start(hostName, ScreenPortal.Infrastructure.Protocol.ControlProtocol.Port, "1");

    Log($"Screen Portal host em {hostName} (GPU: {gpu.AdapterName}). {pairing.Devices.Count} headset(s) pareado(s). Ctrl+C para sair.");
    var sw = Stopwatch.StartNew();
    while (!ct.WaitHandle.WaitOne(1000))
    {
        double s = sw.Elapsed.TotalSeconds;
        sw.Restart();
        if (server.ConnectionCount > 0) Log(streams.TakeStats(s));
    }
    return 0;
}
```

- [ ] **Step 5: Build, testes e verificação do mDNS no PC**

Run:
```powershell
dotnet build host/Host.sln -c Release -v q
dotnet test host/Host.Tests -v q
Start-Process host/Host.App/bin/Release/net10.0-windows10.0.22621.0/ScreenPortalHost.exe -ArgumentList "serve" -NoNewWindow -RedirectStandardOutput tools/out/serve.log
Start-Sleep 3; Get-Content tools/out/serve.log
```
Expected: `controle: ouvindo tcp/9200`, `mDNS: anunciado <PC>._screenportal._tcp.local (<ip>:9200)`. Encerrar com `Stop-Process -Name ScreenPortalHost`.

- [ ] **Step 6: Commit**

```bash
git add host
git commit -m "phase1b(host): serve command — per-headset stream registry, monitors, pairing dialog, native mDNS"
```

---

### Task 8: Headset `.aar` — descoberta NSD

**Files:**
- Create: `headset-native/screenportal/src/main/kotlin/com/screenportal/video/HostDiscovery.kt`
- Modify: `headset-native/screenportal/src/main/AndroidManifest.xml`, `headset-native/screenportal/consumer-rules.pro`

**Interfaces:**
- Produces (JNI, estático): `com.screenportal.video.HostDiscovery.start(Context)`, `stop()`, `poll(): String?` (formato `"nome|ip|porta"`, ou null).

- [ ] **Step 1: Implementar**

`headset-native/screenportal/src/main/kotlin/com/screenportal/video/HostDiscovery.kt`:
```kotlin
package com.screenportal.video

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.net.wifi.WifiManager

/**
 * Descobre o host "_screenportal._tcp" (mDNS/DNS-SD) com o NsdManager do Android.
 * A Unity chama poll() periodicamente; o resultado é "nome|ip|porta" do último host resolvido.
 * Sem kotlin-stdlib: só APIs Java/Android e listeners como `object :` (nada de lambdas).
 */
object HostDiscovery {
    private const val SERVICE_TYPE = "_screenportal._tcp"

    private var nsd: NsdManager? = null
    private var multicastLock: WifiManager.MulticastLock? = null
    private var resolving = false

    @Volatile private var found: String? = null

    private val discoveryListener = object : NsdManager.DiscoveryListener {
        override fun onDiscoveryStarted(serviceType: String) {}
        override fun onDiscoveryStopped(serviceType: String) {}
        override fun onStartDiscoveryFailed(serviceType: String, errorCode: Int) {}
        override fun onStopDiscoveryFailed(serviceType: String, errorCode: Int) {}

        override fun onServiceFound(info: NsdServiceInfo) {
            resolve(info)
        }

        override fun onServiceLost(info: NsdServiceInfo) {
            val current = found
            if (current != null && current.startsWith(info.serviceName + "|")) found = null
        }
    }

    @Suppress("DEPRECATION")
    private fun resolve(info: NsdServiceInfo) {
        val manager = nsd ?: return
        synchronized(this) {
            if (resolving) return
            resolving = true
        }
        manager.resolveService(info, object : NsdManager.ResolveListener {
            override fun onResolveFailed(serviceInfo: NsdServiceInfo, errorCode: Int) {
                synchronized(this@HostDiscovery) { resolving = false }
            }

            override fun onServiceResolved(serviceInfo: NsdServiceInfo) {
                val host = serviceInfo.host
                if (host != null) found = serviceInfo.serviceName + "|" + host.hostAddress + "|" + serviceInfo.port
                synchronized(this@HostDiscovery) { resolving = false }
            }
        })
    }

    @JvmStatic
    fun start(context: Context) {
        if (nsd != null) return
        val wifi = context.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
        val lock = wifi.createMulticastLock("screenportal-mdns")
        lock.setReferenceCounted(false)
        lock.acquire()
        multicastLock = lock
        val manager = context.applicationContext.getSystemService(Context.NSD_SERVICE) as NsdManager
        nsd = manager
        manager.discoverServices(SERVICE_TYPE, NsdManager.PROTOCOL_DNS_SD, discoveryListener)
    }

    @JvmStatic
    fun stop() {
        val manager = nsd ?: return
        try { manager.stopServiceDiscovery(discoveryListener) } catch (e: IllegalArgumentException) {}
        nsd = null
        multicastLock?.release()
        multicastLock = null
        found = null
    }

    @JvmStatic
    fun poll(): String? = found
}
```

No `AndroidManifest.xml` do módulo, adicionar dentro de `<manifest>`:
```xml
    <uses-permission android:name="android.permission.CHANGE_WIFI_MULTICAST_STATE" />
```

Em `consumer-rules.pro`, acrescentar:
```text
-keep class com.screenportal.video.HostDiscovery { *; }
```

- [ ] **Step 2: Compilar e checar que não há dependência da kotlin-stdlib**

Run:
```powershell
powershell -ExecutionPolicy Bypass -File tools/build-native.ps1
$tmp="$env:TEMP\sp-aar2"; Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Add-Type -A System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory("$PWD\Assets\Plugins\Android\screenportal-native.aar", $tmp)
[IO.Compression.ZipFile]::ExtractToDirectory("$tmp\classes.jar", "$tmp\classes")
Get-ChildItem "$tmp\classes" -Recurse -File | ForEach-Object { $s=[Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($_.FullName)); "$($_.Name): " + (([regex]::Matches($s,'kotlin/[A-Za-z/]+') | ForEach-Object Value | Sort-Object -Unique) -join ' ') }
```
Expected: `OK: ...screenportal-native.aar`; cada classe cita no máximo `kotlin/Metadata`, `kotlin/jvm/JvmStatic`, `kotlin/jvm/internal/SourceDebugExtension` (anotações). Qualquer `kotlin/jvm/internal/Intrinsics` ou `kotlin/collections` = reescrever o trecho sem stdlib.

- [ ] **Step 3: Commit**

```bash
git add headset-native Assets/Plugins/Android/screenportal-native.aar
git commit -m "phase1b(native): NSD host discovery with multicast lock, JNI-friendly and stdlib-free"
```

---

### Task 9: Unity — adapters, painel de status, composição e cena Main

**Files:**
- Create: `Assets/_Project/Infrastructure/Unity/ScreenPortal.Infrastructure.asmdef`, `AndroidHostDiscovery.cs`, `PlayerPrefsPairingStore.cs`, `NativeVideoJni.cs`, `NativeVideoSink.cs`
- Create: `Assets/_Project/Presentation/Connection/ConnectionStatusView.cs`
- Modify: `Assets/_Project/Presentation/Screens/OverlayScreenView.cs`, `OverlayScreenViewFactory.cs`
- Create: `Assets/_Project/Composition/AppEnvironment.cs`; rewrite `AppInstaller.cs`, `AppEntryPoint.cs`, `AppLifetimeScope.cs`; delete `AppOptions.cs`
- Modify: `Assets/_Project/Composition/ScreenPortal.Composition.asmdef`, `Assets/_Project/Editor/MainSceneBuilder.cs`, `Assets/_Project/Editor/TestRunnerCommand.cs`
- Modify: `Assets/_Project/Tests/Composition/CompositionTests.cs`, `ScreenPortal.Tests.Composition.asmdef`

**Interfaces:**
- Consumes: portas e máquina de estados (Tasks 2–3), `TcpControlChannel`, `MonotonicClock`, `StaticHostDiscovery`, `InMemoryPairingStore` (Task 4), `HostDiscovery` JNI (Task 8), JNI `NativeVideo` (Fase 0).
- Produces: `AppEnvironment(Transform screensRoot, ConnectionStatusView statusView, IHostDiscovery discovery, IPairingStore pairing, ConnectionOptions options)`; `AppInstaller.Install(IContainerBuilder, AppEnvironment)`; `ConnectionStatusView.Bind(ConnectionStateMachine)`; `OverlayScreenViewFactory : IScreenViewFactory, IScreenSurfaceProvider`.

- [ ] **Step 1: Adapters Unity**

`Assets/_Project/Infrastructure/Unity/ScreenPortal.Infrastructure.asmdef`:
```json
{
    "name": "ScreenPortal.Infrastructure",
    "rootNamespace": "ScreenPortal.Infrastructure",
    "references": ["ScreenPortal.Domain", "ScreenPortal.Application", "ScreenPortal.Infrastructure.Shared"],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": false,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`Assets/_Project/Infrastructure/Unity/AndroidHostDiscovery.cs`:
```csharp
using System;
using ScreenPortal.App.Connection;
using UnityEngine;

namespace ScreenPortal.Infrastructure.Unity
{
    /// <summary>mDNS via NsdManager (com.screenportal.video.HostDiscovery no .aar). Consulta a cada 250 ms.</summary>
    public sealed class AndroidHostDiscovery : IHostDiscovery
    {
        private const float PollIntervalSeconds = 0.25f;
        private AndroidJavaClass _discovery;
        private float _nextPoll;
        private HostEndpoint _last;
        private bool _hasLast;

        public void Start()
        {
            if (_discovery != null) return;
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                _discovery = new AndroidJavaClass("com.screenportal.video.HostDiscovery");
                _discovery.CallStatic("start", activity);
            }
        }

        public void Stop()
        {
            if (_discovery == null) return;
            _discovery.CallStatic("stop");
            _discovery.Dispose();
            _discovery = null;
            _hasLast = false;
        }

        public bool TryGetHost(out HostEndpoint host)
        {
            if (_discovery != null && Time.realtimeSinceStartup >= _nextPoll)
            {
                _nextPoll = Time.realtimeSinceStartup + PollIntervalSeconds;
                string text = _discovery.CallStatic<string>("poll");
                _hasLast = HostEndpoint.TryParse(text, out _last);
            }
            host = _last;
            return _hasLast;
        }
    }
}
```

`Assets/_Project/Infrastructure/Unity/PlayerPrefsPairingStore.cs`:
```csharp
using System;
using ScreenPortal.App.Connection;
using UnityEngine;

namespace ScreenPortal.Infrastructure.Unity
{
    /// <summary>Identidade do headset e tokens de pareamento no armazenamento do app (spec 1B §2).</summary>
    public sealed class PlayerPrefsPairingStore : IPairingStore
    {
        private const string DeviceIdKey = "sp.device_id";
        private const string TokenPrefix = "sp.token.";
        private const string LastHostKey = "sp.last_host";

        public string DeviceId
        {
            get
            {
                string id = PlayerPrefs.GetString(DeviceIdKey, string.Empty);
                if (id.Length > 0) return id;
                id = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(DeviceIdKey, id);
                PlayerPrefs.Save();
                return id;
            }
        }

        public string GetToken(string hostName) => PlayerPrefs.GetString(TokenPrefix + hostName, string.Empty);

        public void SaveToken(string hostName, string token)
        {
            PlayerPrefs.SetString(TokenPrefix + hostName, token ?? string.Empty);
            PlayerPrefs.Save();
        }

        public bool TryGetLastHost(out HostEndpoint host) =>
            HostEndpoint.TryParse(PlayerPrefs.GetString(LastHostKey, string.Empty), out host);

        public void SaveLastHost(HostEndpoint host)
        {
            PlayerPrefs.SetString(LastHostKey, host.ToString());
            PlayerPrefs.Save();
        }
    }
}
```

`Assets/_Project/Infrastructure/Unity/NativeVideoJni.cs`:
```csharp
using System;
using UnityEngine;

namespace ScreenPortal.Infrastructure.Unity
{
    /// <summary>JNI para com.screenportal.video.NativeVideo (.aar). O frame nunca passa pelo C#.</summary>
    internal static class NativeVideoJni
    {
        private static IntPtr s_class;
        private static IntPtr s_start;
        private static IntPtr s_stop;
        private static readonly jvalue[] s_args5 = new jvalue[5];
        private static readonly jvalue[] s_args1 = new jvalue[1];

        public static bool IsAvailable => Application.platform == RuntimePlatform.Android;

        private static void EnsureInitialized()
        {
            if (s_class != IntPtr.Zero) return;
            IntPtr local = AndroidJNI.FindClass("com/screenportal/video/NativeVideo");
            s_class = AndroidJNI.NewGlobalRef(local);
            AndroidJNI.DeleteLocalRef(local);
            s_start = AndroidJNI.GetStaticMethodID(s_class, "start", "(IILandroid/view/Surface;II)J");
            s_stop = AndroidJNI.GetStaticMethodID(s_class, "stop", "(J)V");
        }

        public static long Start(int streamId, int port, IntPtr surface, int width, int height)
        {
            EnsureInitialized();
            s_args5[0].i = streamId;
            s_args5[1].i = port;
            s_args5[2].l = surface;
            s_args5[3].i = width;
            s_args5[4].i = height;
            return AndroidJNI.CallStaticLongMethod(s_class, s_start, s_args5);
        }

        public static void Stop(long handle)
        {
            if (handle == 0) return;
            EnsureInitialized();
            s_args1[0].j = handle;
            AndroidJNI.CallStaticVoidMethod(s_class, s_stop, s_args1);
        }
    }
}
```

`Assets/_Project/Infrastructure/Unity/NativeVideoSink.cs`:
```csharp
using System;
using System.Collections.Generic;
using ScreenPortal.App.Connection;
using ScreenPortal.Domain.Screens;
using Resolution = ScreenPortal.Domain.Screens.Resolution;

namespace ScreenPortal.Infrastructure.Unity
{
    /// <summary>Liga o decoder nativo à Surface da compositor layer da tela (adapter de IVideoStreamSink).</summary>
    public sealed class NativeVideoSink : IVideoStreamSink
    {
        private readonly IScreenSurfaceProvider _surfaces;
        private readonly Dictionary<ushort, long> _handles = new Dictionary<ushort, long>();

        public NativeVideoSink(IScreenSurfaceProvider surfaces) => _surfaces = surfaces;

        public bool TryStart(ScreenId screen, ushort streamId, int udpPort, Resolution resolution)
        {
            if (_handles.ContainsKey(streamId)) return true;
            if (!NativeVideoJni.IsAvailable)
            {
                _handles[streamId] = 0; // Editor: sem decoder, mas o protocolo segue
                return true;
            }
            if (!_surfaces.TryGetSurface(screen, out IntPtr surface) || surface == IntPtr.Zero) return false;
            long handle = NativeVideoJni.Start(streamId, udpPort, surface, resolution.Width, resolution.Height);
            if (handle == 0) return false;
            _handles[streamId] = handle;
            return true;
        }

        public void Stop(ushort streamId)
        {
            if (!_handles.TryGetValue(streamId, out long handle)) return;
            if (NativeVideoJni.IsAvailable) NativeVideoJni.Stop(handle);
            _handles.Remove(streamId);
        }
    }
}
```

- [ ] **Step 2: Presentation — painel de status e Surface das telas**

`Assets/_Project/Presentation/Connection/ConnectionStatusView.cs`:
```csharp
using ScreenPortal.App.Connection;
using UnityEngine;

namespace ScreenPortal.Presentation.Connection
{
    /// <summary>Painel de texto com o status da conexão (acaba a tela preta). "Conectado" some após 2 s.</summary>
    public sealed class ConnectionStatusView : MonoBehaviour
    {
        [SerializeField] private TextMesh label;
        private float _hideAt = -1f;

        public void Bind(ConnectionStateMachine machine)
        {
            machine.StatusChanged += Show;
            Show(machine.Status);
        }

        private void Show(ConnectionStatus status)
        {
            label.text = ConnectionStatusText.Format(status);
            label.gameObject.SetActive(label.text.Length > 0);
            _hideAt = status.Kind == ConnectionStatusKind.Connected ? Time.realtimeSinceStartup + 2f : -1f;
        }

        private void Update()
        {
            if (_hideAt > 0f && Time.realtimeSinceStartup >= _hideAt)
            {
                _hideAt = -1f;
                label.gameObject.SetActive(false);
            }
        }
    }
}
```

Em `Assets/_Project/Presentation/Screens/OverlayScreenView.cs`, adicionar o id da tela (necessário para o registro de Surfaces): trocar o construtor e o `Dispose` por:
```csharp
        public ScreenId Id { get; }
        private readonly System.Action<OverlayScreenView> _onDisposed;

        public OverlayScreenView(Transform parent, VirtualScreen screen, System.Action<OverlayScreenView> onDisposed = null)
        {
            Id = screen.Id;
            _onDisposed = onDisposed;
            _root = new GameObject("Screen " + screen.Id.Value);
            _root.transform.SetParent(parent, false);
            BuildOverlay(screen.Resolution);
        }
```
```csharp
        public void Dispose()
        {
            _onDisposed?.Invoke(this);
            if (_root != null) Object.Destroy(_root);
        }
```

`Assets/_Project/Presentation/Screens/OverlayScreenViewFactory.cs` (substituir o arquivo):
```csharp
using System;
using System.Collections.Generic;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Screens;
using UnityEngine;

namespace ScreenPortal.Presentation.Screens
{
    /// <summary>Cria as views OVROverlay e informa a Surface (external surface) de cada tela ao decoder.</summary>
    public sealed class OverlayScreenViewFactory : IScreenViewFactory, IScreenSurfaceProvider
    {
        private readonly Transform _root;
        private readonly Dictionary<ScreenId, OverlayScreenView> _views = new Dictionary<ScreenId, OverlayScreenView>();

        public OverlayScreenViewFactory(Transform root) => _root = root;

        public IScreenView Create(VirtualScreen screen)
        {
            var view = new OverlayScreenView(_root, screen, v => _views.Remove(v.Id));
            _views[screen.Id] = view;
            return view;
        }

        public bool TryGetSurface(ScreenId screen, out IntPtr surface)
        {
            surface = IntPtr.Zero;
            if (!_views.TryGetValue(screen, out var view) || view.Overlay == null) return false;
            surface = view.Overlay.externalSurfaceObject;
            return surface != IntPtr.Zero;
        }
    }
}
```

- [ ] **Step 3: Composição**

`Assets/_Project/Composition/ScreenPortal.Composition.asmdef` — trocar `references` por:
```json
    "references": [
        "ScreenPortal.Domain",
        "ScreenPortal.Application",
        "ScreenPortal.Presentation",
        "ScreenPortal.Infrastructure",
        "ScreenPortal.Infrastructure.Shared",
        "VContainer"
    ],
```

Deletar `Assets/_Project/Composition/AppOptions.cs` (e o `.meta`).

`Assets/_Project/Composition/AppEnvironment.cs`:
```csharp
using ScreenPortal.App.Connection;
using ScreenPortal.Presentation.Connection;
using UnityEngine;

namespace ScreenPortal.Composition
{
    /// <summary>O que varia por plataforma/teste: descoberta, pareamento, raiz das telas, painel de status.</summary>
    public sealed class AppEnvironment
    {
        public Transform ScreensRoot { get; }
        public ConnectionStatusView StatusView { get; }
        public IHostDiscovery Discovery { get; }
        public IPairingStore Pairing { get; }
        public ConnectionOptions Options { get; }

        public AppEnvironment(Transform screensRoot, ConnectionStatusView statusView, IHostDiscovery discovery,
            IPairingStore pairing, ConnectionOptions options)
        {
            ScreensRoot = screensRoot;
            StatusView = statusView;
            Discovery = discovery;
            Pairing = pairing;
            Options = options;
        }
    }
}
```

`Assets/_Project/Composition/AppInstaller.cs` (substituir):
```csharp
using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Screens;
using ScreenPortal.Infrastructure.Net;
using ScreenPortal.Infrastructure.Unity;
using ScreenPortal.Presentation.Screens;
using VContainer;
using VContainer.Unity;

namespace ScreenPortal.Composition
{
    /// <summary>
    /// Registrações do app (testáveis em CompositionTests). Regra: classes com parâmetro primitivo no construtor
    /// entram por RegisterInstance (o VContainer tentaria injetar o primitivo — lição da Fase 1A).
    /// </summary>
    public static class AppInstaller
    {
        public static void Install(IContainerBuilder builder, AppEnvironment env)
        {
            builder.RegisterInstance(env);
            builder.RegisterInstance(env.Options);
            builder.RegisterInstance(new WorkspaceStore());
            builder.RegisterInstance(new CommandJournal()).As<ICommandObserver>().AsSelf();
            builder.Register<CommandBus>(Lifetime.Singleton).As<ICommandBus>();

            var views = new OverlayScreenViewFactory(env.ScreensRoot);
            builder.RegisterInstance(views).As<IScreenViewFactory>().As<IScreenSurfaceProvider>();
            builder.Register<ScreenViewSynchronizer>(Lifetime.Singleton);

            builder.RegisterInstance(new MonotonicClock()).As<IClock>();
            builder.RegisterInstance(new TcpControlChannel(2000)).As<IControlChannel>();
            builder.RegisterInstance(env.Discovery).As<IHostDiscovery>();
            builder.RegisterInstance(env.Pairing).As<IPairingStore>();
            builder.Register<NativeVideoSink>(Lifetime.Singleton).As<IVideoStreamSink>();
            builder.Register<ConnectionStateMachine>(Lifetime.Singleton);

            builder.RegisterEntryPoint<AppEntryPoint>().AsSelf();
        }
    }
}
```

`Assets/_Project/Composition/AppEntryPoint.cs` (substituir):
```csharp
using System;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Screens;
using VContainer.Unity;

namespace ScreenPortal.Composition
{
    /// <summary>Liga views, painel de status e a conexão; a tela do PC é criada pela conexão (Welcome).</summary>
    public sealed class AppEntryPoint : IStartable, ITickable, IDisposable
    {
        private readonly ScreenViewSynchronizer _synchronizer;
        private readonly ConnectionStateMachine _connection;
        private readonly IControlChannel _channel;
        private readonly IHostDiscovery _discovery;
        private readonly AppEnvironment _env;

        public AppEntryPoint(ScreenViewSynchronizer synchronizer, ConnectionStateMachine connection,
            IControlChannel channel, IHostDiscovery discovery, AppEnvironment env)
        {
            _synchronizer = synchronizer;
            _connection = connection;
            _channel = channel;
            _discovery = discovery;
            _env = env;
        }

        public void Start()
        {
            _synchronizer.Start();
            if (_env.StatusView != null) _env.StatusView.Bind(_connection);
            _connection.Start();
        }

        public void Tick() => _connection.Tick();

        public void Dispose()
        {
            _channel.Close();
            _discovery.Stop();
            _synchronizer.Dispose();
        }
    }
}
```

`Assets/_Project/Composition/AppLifetimeScope.cs` (substituir):
```csharp
using ScreenPortal.App.Connection;
using ScreenPortal.Infrastructure.Net;
using ScreenPortal.Infrastructure.Protocol;
using ScreenPortal.Infrastructure.Unity;
using ScreenPortal.Presentation.Connection;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace ScreenPortal.Composition
{
    /// <summary>Raiz de composição (brief §8.4). No Quest: mDNS + PlayerPrefs; no Editor: host fixo + memória.</summary>
    public sealed class AppLifetimeScope : LifetimeScope
    {
        [SerializeField] private Transform screensRoot;
        [SerializeField] private ConnectionStatusView statusView;
        [Tooltip("Editor/PC: endereço do host (sem mDNS).")]
        [SerializeField] private string editorHostAddress = "127.0.0.1";

        protected override void Configure(IContainerBuilder builder)
        {
            bool android = Application.platform == RuntimePlatform.Android;
            IHostDiscovery discovery = android
                ? new AndroidHostDiscovery()
                : (IHostDiscovery)new StaticHostDiscovery(new HostEndpoint("Editor", editorHostAddress, ControlProtocol.Port));
            IPairingStore pairing = android ? new PlayerPrefsPairingStore() : (IPairingStore)new InMemoryPairingStore("editor");
            var options = new ConnectionOptions { DeviceName = SystemInfo.deviceName };

            AppInstaller.Install(builder, new AppEnvironment(
                screensRoot != null ? screensRoot : transform, statusView, discovery, pairing, options));
        }
    }
}
```

- [ ] **Step 4: Teste de composição (atualizar)**

Em `Assets/_Project/Tests/Composition/ScreenPortal.Tests.Composition.asmdef`, trocar `references` por:
```json
    "references": [
        "ScreenPortal.Domain",
        "ScreenPortal.Application",
        "ScreenPortal.Presentation",
        "ScreenPortal.Infrastructure",
        "ScreenPortal.Infrastructure.Shared",
        "ScreenPortal.Composition",
        "VContainer",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
```
e `precompiledReferences` por `["nunit.framework.dll", "Google.FlatBuffers.dll"]`.

`Assets/_Project/Tests/Composition/CompositionTests.cs` (substituir):
```csharp
using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.Composition;
using ScreenPortal.Infrastructure.Net;
using UnityEngine;
using VContainer;

namespace ScreenPortal.Tests
{
    /// <summary>Regressão da Fase 1A (VContainer e primitivos) + o grafo da 1B resolve sem headset.</summary>
    public class CompositionTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("ScreensRootForTest");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void AppGraph_ResolvesEntryPointBusAndConnection()
        {
            var builder = new ContainerBuilder();
            AppInstaller.Install(builder, new AppEnvironment(_root.transform, null,
                new StaticHostDiscovery(new HostEndpoint("PC", "127.0.0.1", 9200)), new InMemoryPairingStore("t"), new ConnectionOptions()));

            using (var container = builder.Build())
            {
                Assert.That(container.Resolve<AppEntryPoint>(), Is.Not.Null);
                Assert.That(container.Resolve<ICommandBus>(), Is.Not.Null);
                Assert.That(container.Resolve<ConnectionStateMachine>(), Is.Not.Null);
            }
        }
    }
}
```

Em `Assets/_Project/Editor/TestRunnerCommand.cs`, trocar a lista de assemblies por:
```csharp
                assemblyNames = new[] { "ScreenPortal.Tests.EditMode", "ScreenPortal.Tests.Shared", "ScreenPortal.Tests.Composition" },
```

- [ ] **Step 5: Cena Main com painel de status**

Em `Assets/_Project/Editor/ScreenPortal.Editor.asmdef`, acrescentar `"ScreenPortal.Presentation"` em `references`.

Em `Assets/_Project/Editor/MainSceneBuilder.cs`, trocar o trecho a partir de `var screensRoot = new GameObject("Screens");` até `so.ApplyModifiedPropertiesWithoutUndo();` por:
```csharp
            var screensRoot = new GameObject("Screens");

            var statusGo = new GameObject("ConnectionStatus");
            statusGo.transform.position = new Vector3(0f, 1.25f, 1.2f);
            var text = statusGo.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            statusGo.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.fontSize = 64;
            text.characterSize = 0.012f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            var statusView = statusGo.AddComponent<ScreenPortal.Presentation.Connection.ConnectionStatusView>();
            var statusSo = new SerializedObject(statusView);
            statusSo.FindProperty("label").objectReferenceValue = text;
            statusSo.ApplyModifiedPropertiesWithoutUndo();

            var scopeGo = new GameObject("AppLifetimeScope");
            var scope = scopeGo.AddComponent<AppLifetimeScope>();
            var so = new SerializedObject(scope);
            so.FindProperty("screensRoot").objectReferenceValue = screensRoot.transform;
            so.FindProperty("statusView").objectReferenceValue = statusView;
            so.ApplyModifiedPropertiesWithoutUndo();
```

- [ ] **Step 6: Compilar na Unity, gerar a cena e rodar os testes**

Run:
```powershell
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command refresh -TimeoutMinutes 3
# aguardar "Reloading assemblies" sem "): error" no Editor.log
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command build-main-scene -TimeoutMinutes 2
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command test-editmode -TimeoutMinutes 5
dotnet test tests-dotnet/UnityPureCode.Tests -v q
```
Expected: `OK Assets/_Project/Scenes/Main.unity`; `<N> passed, 0 failed` na Unity; `Com falha: 0` no dotnet.
Se a Unity acusar erro de `__vector_as_arraysegment`/`Span` no código gerado, aplicar o mesmo `ENABLE_SPAN_T` do Task 1 Step 9 via `Assets/csc.rsp`.

- [ ] **Step 7: Commit**

```bash
git add Assets ProjectSettings
git commit -m "phase1b: Unity adapters (mDNS, PlayerPrefs, native video sink), status panel, composition wiring, Main scene"
```

---

### Task 10: Verificação no Quest (roteiro com o Cayo)

**Files:** `docs/measurements/fase1b-conexao.md` (criar)

- [ ] **Step 1: Build e instalação**

```powershell
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command boot-main
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command build-apk -TimeoutMinutes 30
$adb="D:\Unity Editor\6000.3.19f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
& $adb -s 2G0YC1ZG410B1H shell am broadcast -a com.oculus.vrpowermanager.prox_close
& $adb -s 2G0YC1ZG410B1H install -r -g Builds/Phase0.apk
& $adb -s 2G0YC1ZG410B1H shell pm clear com.screenportal.app   # começa sem pareamento
```

- [ ] **Step 2: Roteiro (Cayo no headset; Claude no PC e no logcat)**

1. Abrir o app **sem** o host → esperado "Procurando seu PC…"; após 10 s, "Instale e abra o Screen Portal no Windows".
2. `ScreenPortalHost.exe serve` → headset mostra "Confirme no PC: código XXX XXX"; o PC mostra o diálogo com o mesmo código → **Permitir** → a tela do PC aparece na tela Demo.
3. Fechar e reabrir o app → conecta sozinho, sem diálogo.
4. Matar o host (`Stop-Process -Name ScreenPortalHost`) e subir de novo em < 5 s; depois, desligar o Wi-Fi do Quest por 2 s → "Reconectando…" → vídeo volta. Medir pelo logcat (tempo entre `Reconectando` e `Conectado`).
5. `pm clear` + abrir → **Negar** no diálogo → "Conexão recusada no PC." e nenhuma tela.

- [ ] **Step 3: Registrar**

`docs/measurements/fase1b-conexao.md` com uma tabela: passo, esperado, observado, tempo medido (reconexões), commit.

- [ ] **Step 4: Commit**

```bash
git add docs/measurements/fase1b-conexao.md
git commit -m "docs: Phase 1B on-device verification"
```

---

### Task 11: Documentação

**Files:**
- Create: `docs/adr/0006-control-protocol-pairing-mdns.md`
- Modify: `docs/brief.md` (§8.5, §12), `docs/STATUS.md`, `CLAUDE.md`

- [ ] **Step 1: ADR 0006**

`docs/adr/0006-control-protocol-pairing-mdns.md`:
```markdown
# ADR 0006 — Canal de controle: FlatBuffers com código gerado commitado, mDNS nativo, pareamento sem criptografia

- **Status:** Aceito (Fase 1B)
- **Spec:** docs/specs/2026-09-28-fase1b-conexao-design.md · **Protocolo:** protocol/control-v1.md
- **Decisões:**
  - FlatBuffers 25.2.10: `flatc` fixo em `tools/flatc/`, C# gerado **commitado** (build sem flatc); runtime via NuGet
    no host e `Google.FlatBuffers.dll` em `Assets/_Project/ThirdParty` na Unity. Versões do flatc e do runtime mudam juntas.
  - **Código puro compartilhado:** mensagens, codec, framing, `TcpControlChannel` e `ConnectionStateMachine` vivem em
    `Assets/_Project`; o host os compila via `host/Host.Shared` (arquivos vinculados). Um só codec; o teste de integração
    do host usa a máquina de estados real do headset.
  - mDNS nativo: `DnsServiceRegister` (Windows) e `NsdManager` + multicast lock (Android, no `.aar`).
  - Pareamento: código de 6 dígitos confirmado no PC; token de 32 bytes lembrado dos dois lados.
- **Consequências:**
  - ✅ Reconexão medida no PC (integração) e no Quest (docs/measurements/fase1b-conexao.md).
  - ⚠️ Sem criptografia até a v1.1: alguém na mesma rede pode capturar o token e o vídeo. Risco registrado no brief §12.
  - ⚠️ O código compartilhado precisa continuar C# 9 e sem UnityEngine (garantido pelo harness e por Host.Shared com LangVersion 9).
```

- [ ] **Step 2: Brief, STATUS e CLAUDE.md**

- Brief §8.5: após a linha da state machine, acrescentar: `Tempos (Fase 1B): connect 2 s · negociação 5 s (sem timeout no pareamento) · ping 500 ms · Degraded após 1,5 s sem Pong · Reconnecting após +3 s · reconexão ao último IP a cada 250 ms, desiste em 10 s. Detalhes: docs/specs/2026-09-28-fase1b-conexao-design.md.`
- Brief §12 (tabela de riscos), nova linha: `| Pareamento sem criptografia (MVP) | Token e vídeo podem ser capturados por alguém na mesma rede. Pareamento impede conexões casuais; TLS/DTLS na v1.1 (ADR 0006) |`
- STATUS: marcar 1B ✅ com link para o plano, o spec, o ADR 0006 e a medição; próximo = Fase 2.
- CLAUDE.md, seção Comandos: acrescentar `- Host completo: ScreenPortalHost serve (controle tcp/9200 + mDNS + vídeo); protocolo: tools/gen-protocol.ps1 (regenera o C# do control.fbs)`.

- [ ] **Step 3: Commit**

```bash
git add docs CLAUDE.md
git commit -m "docs: ADR 0006, brief §8.5/§12, STATUS and CLAUDE.md for Phase 1B"
```
