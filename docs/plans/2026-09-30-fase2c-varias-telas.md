# Fase 2C — Várias telas: monitores virtuais sob comando do headset, N streams — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O usuário cria, pelo menu de pulso, monitores virtuais (proporção → resolução) e espelhos de monitores físicos; o host cria/remove os monitores SudoVDA sob demanda (aluguel com prazo de 10 s ao remover e 5 min ao desconectar) e transmite um stream por tela; o headset decodifica cada um direto na Surface da sua compositor layer.

**Architecture:** Parte A é código puro (Domain + Application + codec, testado no `dotnet test` e no Test Runner): origem da tela (`ScreenSource`), catálogo de resoluções, menu com páginas, protocolo v2, `ConnectionStateMachine` só de conexão e o `HostSync` — reconciliador declarativo entre a workspace e os aluguéis do host. Parte B é o host: `ScreenLeaseTable` (puro), `HeadsetSession` v2 com fila de trabalho e o `ScreenHost` (SudoVDA + WGC + encoder por tela). Parte C é Unity: vídeo nativo por tela, view com estados, painel do menu com frames, composição. Parte D verifica no Simulator (com o host real) e no Quest.

**Tech Stack:** Unity 6.3 (C# 9), Meta XR Core SDK 207 (`OVROverlay` external surface), VContainer 1.19, FlatBuffers 25.2.10, NUnit; host .NET 10 (WGC, Media Foundation/AMF, SudoVDA IOCTL), xUnit; `.aar` nativo existente (`NativeVideo.start/stop/takeStats`).

## Global Constraints

- Spec: `docs/specs/2026-09-30-fase2c-varias-telas-design.md`. ADRs 0007 (SudoVDA) e 0008 (decoder).
- **C# 9 no código da Unity** (Domain, Application, Infrastructure, Presentation, testes EditMode/Shared): sem `record`, `init`, namespaces de arquivo. O host (`host/`) usa C# moderno, exceto o que ele compila de `Assets/_Project` via `Host.Shared`.
- Domain e Application **nunca** referenciam UnityEngine. Toda mudança de estado é Command via `ICommandBus`; views nunca mudam estado.
- **Zero alocação por quadro** em `HostSync.Tick`, `InteractionRouter.Tick` e no `Render` do painel (sem LINQ, closures, `new`, strings montadas). Mensagens só são criadas em transições.
- **O quadro de vídeo nunca atravessa o C#**: o decoder nativo desenha na Surface da `OVROverlay`.
- Mensagens de rede só via `protocol/control.fbs`. Regra do protocolo: campos novos **no fim** das tabelas e membros novos **no fim** do union; nunca reordenar nem remover. `protocol_version` = **2**.
- Catálogo (paisagem; retrato inverte): 16:9 → 1920×1080, 2560×1440, 3840×2160 · 16:10 → 1920×1200, 2560×1600, 3840×2400 · 21:9 → 2560×1080, 3440×1440 · 32:9 → 3840×1080 · 1:1 → 1440×1440, 2160×2160 · 4:3 → 1600×1200, 2048×1536.
- Largura inicial: `resolução.Width / 2560` m (2560 px ≈ 1,0 m), limitada por `ScreenLimits` (0,2–6 m).
- Prazos: remover → `ReleaseScreen` com **10 000 ms**; desconexão → **5 min** no host (`--grace-minutes`, padrão 5).
- Aviso ao criar a **5ª tela com vídeo** em diante: `"Mais de 4 telas:\npode haver quedas e travamentos"`. Sem limite rígido.
- Portas de vídeo no headset: **9000 + n** (n = `stream_id`, menor livre; máx. 16). A 9101 é do KFRQ (pedido de keyframe) e a 9200 do controle.
- Menu: no máximo **12 itens** por página (`WristMenu.MaxItems`).
- Testes: `dotnet test tests-dotnet/UnityPureCode.Tests` em toda tarefa da Parte A; `dotnet test host/Host.Tests` quando tocar Domain/Application/Infrastructure.Shared (o host compila via `Host.Shared`) ou o host.
- Unity pela CLI (Editor aberto): `unity --proxy-disable --no-banner command recompile` / `run_tests --mode EditMode`. Nunca `unity command eval` na `main`.
- Branch `phase2c/multi-screen`. Commits pequenos, um propósito cada. Context-mode para saídas grandes.

## Desvios da spec (avisar o Cayo; registrar na spec na Task 15)

1. **Portas 9000 + n**, não 9100 + n: a porta de vídeo da 1B já é 9000 e a 9101 é do KFRQ.
2. **Sem código de limpeza de órfãos:** o watchdog do SudoVDA remove os monitores quando o host para de pingar (processo morto). Verificado na Task 14.
3. **"PC desconectado" depois de já ter tido vídeo:** a layer fica visível e escurecida (sem texto; o status global da conexão já avisa). Antes do primeiro quadro, texto no quad.
4. **Pipeline para na desconexão:** o stream é encerrado na hora (não fica 10 s como na 1B); só o **monitor** fica 5 min. Na reconexão o `HostSync` pede de novo e o host recria o pipeline (~0,1 s, o monitor já existe).
5. **Fila de trabalho na `HeadsetSession`:** Acquire/StartStream/Release rodam fora da thread de leitura do socket (criar monitor + esperar a captura leva até ~3 s e não pode atrasar o Pong).
6. **Ids de monitor físico estáveis** (= número do `\\.\DISPLAYn`), e os monitores virtuais do app ficam fora da lista do `Welcome`.
7. **Editor pareia uma vez só:** o Editor passa a usar `PlayerPrefsPairingStore` (antes `InMemoryPairingStore`), para a verificação com o host real não pedir código a cada Play.

---

## File Structure

```
Assets/_Project/Domain/Screens/
  ScreenSource.cs            NOVO  origem: Virtual | Mirror(id) | Test
  ScreenSizing.cs            NOVO  largura inicial (2560 px ≈ 1 m)
  VirtualScreen.cs           + Source
Assets/_Project/Domain/Commands/CreateScreen.cs      + source
Assets/_Project/Application/Menu/
  ResolutionCatalog.cs       NOVO  proporções e resoluções (rótulos em cache)
  WristMenu.cs               REESCRITO  páginas: Principal, Proporções, Resoluções, Espelhos
  Ports.cs                   + IHostCatalog, FixedHostCatalog
  TestScreens.cs             usa ScreenSource.Test
Assets/_Project/Application/Manipulation/InteractionRouter.cs   itens por índice; OnHidden
Assets/_Project/Application/Connection/
  ControlMessages.cs         + AcquireScreen, ScreenReady, ScreenFailed, ReleaseScreen, ScreenLeaseInfo; StartStream.ScreenKey; Welcome.Leases
  ConnectionStateMachine.cs  REESCRITO  só conexão: Welcomed/Lost/ScreenMessage, Send
  ConnectionOptions.cs       − StreamId, − DefaultScreenWidthMeters
  Ports.cs                   IVideoStreamSink → IScreenVideo
Assets/_Project/Application/Screens/
  ScreenLinks.cs             NOVO  ScreenLinkState + IScreenLinks
  HostSync.cs                NOVO  reconciliador workspace ⇄ host
Assets/_Project/Infrastructure/Shared/Protocol/ (codec v2, versão 2, Generated/ regenerado)
Assets/_Project/Infrastructure/Shared/Video/SpStatLine.cs   NOVO  parser da linha SPSTAT
Assets/_Project/Infrastructure/Unity/
  NativeScreenVideo.cs       NOVO  IScreenVideo (JNI)
  NativeVideoJni.cs          + TakeStats
  NativeVideoSink.cs         REMOVIDO
Assets/_Project/Presentation/Screens/
  VideoScreenView.cs         (era OverlayScreenView.cs) layer + estados
  OverlayScreenViewFactory.cs  escolhe por origem; Bind(IScreenLinks)
Assets/_Project/Presentation/Menu/WristMenuPanel.cs   REESCRITO  páginas, frames de proporção
Assets/_Project/Composition/  AppEnvironment (+ShowTestScreens), AppInstaller, AppEntryPoint, AppLifetimeScope
host/Host.Domain/Screens/ScreenLeaseTable.cs          NOVO
host/Host.Transport/Control/HostPorts.cs              IStreamRegistry → IScreenHost
host/Host.Transport/Control/HeadsetSession.cs         v2 + fila
host/Host.App/Control/ScreenHost.cs                   NOVO (substitui StreamRegistry.cs)
host/Host.App/Control/WindowsMonitorProvider.cs       ids estáveis, sem virtuais
host/Host.App/Program.cs                              serve usa ScreenHost
protocol/control.fbs, protocol/control-v2.md (era -v1), docs/adr/0009-*.md
```

---

## Parte A — Código puro (headset + protocolo)

### Task 1: Origem da tela e largura inicial

**Files:**
- Create: `Assets/_Project/Domain/Screens/ScreenSource.cs`, `Assets/_Project/Domain/Screens/ScreenSizing.cs`
- Modify: `Assets/_Project/Domain/Screens/VirtualScreen.cs`, `Assets/_Project/Domain/Commands/CreateScreen.cs`, `Assets/_Project/Application/Menu/TestScreens.cs`
- Test: `Assets/_Project/Tests/EditMode/ScreenSourceTests.cs`

**Interfaces:**
- Produces: `ScreenSourceKind { Virtual = 0, Mirror = 1, Test = 2 }`; `struct ScreenSource { Kind; uint PhysicalMonitorId; bool HasVideo; static Virtual; static Test; static Mirror(uint) }`; `VirtualScreen.Source`; `CreateScreen(resolution, placement, widthMeters, initialCurvature = 0f, label = "", source = default)`; `ScreenSizing.InitialWidthMeters(Resolution) : float`; `TestScreens.IsTestScreen` passa a olhar `Source`.

- [ ] **Step 0: Branch**

```bash
git checkout main
git checkout -b phase2c/multi-screen
```

- [ ] **Step 1: Write the failing test** — `Assets/_Project/Tests/EditMode/ScreenSourceTests.cs`

```csharp
using NUnit.Framework;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Tests
{
    public class ScreenSourceTests
    {
        [Test]
        public void Default_IsVirtual_WithVideo()
        {
            var source = default(ScreenSource);
            Assert.That(source.Kind, Is.EqualTo(ScreenSourceKind.Virtual));
            Assert.That(source.HasVideo, Is.True);
            Assert.That(source, Is.EqualTo(ScreenSource.Virtual));
        }

        [Test]
        public void Mirror_KeepsThePhysicalId_TestHasNoVideo()
        {
            var mirror = ScreenSource.Mirror(3);
            Assert.That(mirror.Kind, Is.EqualTo(ScreenSourceKind.Mirror));
            Assert.That(mirror.PhysicalMonitorId, Is.EqualTo(3u));
            Assert.That(mirror.HasVideo, Is.True);
            Assert.That(mirror, Is.Not.EqualTo(ScreenSource.Mirror(4)));
            Assert.That(ScreenSource.Test.HasVideo, Is.False);
        }

        [Test]
        public void CreateScreen_CarriesTheSource_AndWithMethodsKeepIt()
        {
            var result = new CreateScreen(Resolution.Create(2560, 1080), null, 1f, 0f, "DISPLAY1", ScreenSource.Mirror(1))
                .Apply(Workspace.Empty, out var ws);
            Assert.That(result.Accepted, Is.True);
            var screen = ws.Screens[0];
            Assert.That(screen.Source, Is.EqualTo(ScreenSource.Mirror(1)));
            Assert.That(screen.WithWidth(1.5f).Source, Is.EqualTo(ScreenSource.Mirror(1)));
            Assert.That(screen.WithResolution(Resolution.Create(1920, 1080)).Source, Is.EqualTo(ScreenSource.Mirror(1)));
        }

        [Test]
        public void InitialWidth_Is2560PixelsPerMeter_Clamped()
        {
            Assert.That(ScreenSizing.InitialWidthMeters(Resolution.Create(2560, 1440)), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(ScreenSizing.InitialWidthMeters(Resolution.Create(3840, 2160)), Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(ScreenSizing.InitialWidthMeters(Resolution.Create(1440, 2560)), Is.EqualTo(0.5625f).Within(1e-4f));
        }

        [Test]
        public void TestScreens_AreTestSource()
        {
            TestScreens.Next(0).Apply(Workspace.Empty, out var ws);
            Assert.That(ws.Screens[0].Source, Is.EqualTo(ScreenSource.Test));
            Assert.That(TestScreens.IsTestScreen(ws.Screens[0]), Is.True);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests --filter ScreenSourceTests`
Expected: FAIL (erro de compilação: `ScreenSource`/`ScreenSizing` não existem).

- [ ] **Step 3: Implement** — `Assets/_Project/Domain/Screens/ScreenSource.cs`

```csharp
using System;

namespace ScreenPortal.Domain.Screens
{
    public enum ScreenSourceKind
    {
        /// <summary>Monitor virtual (SudoVDA) criado pelo host com a resolução exata da tela.</summary>
        Virtual = 0,
        /// <summary>Espelho de um monitor físico do PC.</summary>
        Mirror = 1,
        /// <summary>Tela de teste, sem vídeo (2B).</summary>
        Test = 2,
    }

    /// <summary>De onde vem a imagem da tela (spec 2C §3). <c>default</c> = Virtual.</summary>
    public readonly struct ScreenSource : IEquatable<ScreenSource>
    {
        public static readonly ScreenSource Virtual = new ScreenSource(ScreenSourceKind.Virtual, 0);
        public static readonly ScreenSource Test = new ScreenSource(ScreenSourceKind.Test, 0);

        public readonly ScreenSourceKind Kind;
        /// <summary>Só em <see cref="ScreenSourceKind.Mirror"/>: id do monitor físico (lista do Welcome).</summary>
        public readonly uint PhysicalMonitorId;

        private ScreenSource(ScreenSourceKind kind, uint physicalMonitorId)
        {
            Kind = kind;
            PhysicalMonitorId = physicalMonitorId;
        }

        public static ScreenSource Mirror(uint physicalMonitorId) => new ScreenSource(ScreenSourceKind.Mirror, physicalMonitorId);

        /// <summary>Virtual e espelho recebem vídeo do PC; a tela de teste não.</summary>
        public bool HasVideo => Kind != ScreenSourceKind.Test;

        public bool Equals(ScreenSource other) => Kind == other.Kind && PhysicalMonitorId == other.PhysicalMonitorId;
        public override bool Equals(object obj) => obj is ScreenSource other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ (int)PhysicalMonitorId;
        public override string ToString() => Kind == ScreenSourceKind.Mirror ? "Mirror(" + PhysicalMonitorId + ")" : Kind.ToString();
    }
}
```

`Assets/_Project/Domain/Screens/ScreenSizing.cs`

```csharp
namespace ScreenPortal.Domain.Screens
{
    /// <summary>Tamanho inicial de uma tela nova: mesma densidade para todas as resoluções (spec 2C §3). É só zoom.</summary>
    public static class ScreenSizing
    {
        /// <summary>2560 px ≈ 1,0 m (a 1,3 m do usuário).</summary>
        public const float PixelsPerMeter = 2560f;

        public static float InitialWidthMeters(Resolution resolution) =>
            ScreenLimits.ClampWidth(resolution.Width / PixelsPerMeter);
    }
}
```

`VirtualScreen.cs`: acrescentar a propriedade, o parâmetro (último, opcional) e repassar em **todos** os `With*`:

```csharp
        public string Label { get; }
        /// <summary>De onde vem a imagem (spec 2C). Nunca muda depois de criada.</summary>
        public ScreenSource Source { get; }
```

```csharp
            FrameRateMode frameRateMode = default,
            string label = "",
            ScreenSource source = default)
        {
            // ... (validações e atribuições existentes)
            Label = label ?? string.Empty;
            Source = source;
        }

        public VirtualScreen WithPlacement(Placement placement) =>
            new VirtualScreen(Id, Resolution, placement, WidthMeters, Curvature, LastNonZeroCurvature, FrameRateMode, Label, Source);

        public VirtualScreen WithWidth(float widthMeters) =>
            new VirtualScreen(Id, Resolution, Placement, widthMeters, Curvature, LastNonZeroCurvature, FrameRateMode, Label, Source);

        public VirtualScreen WithCurvature(float curvature) =>
            new VirtualScreen(Id, Resolution, Placement, WidthMeters, curvature, LastNonZeroCurvature, FrameRateMode, Label, Source);

        public VirtualScreen WithFrameRateMode(FrameRateMode mode) =>
            new VirtualScreen(Id, Resolution, Placement, WidthMeters, Curvature, LastNonZeroCurvature, mode, Label, Source);

        public VirtualScreen WithResolution(Resolution resolution) =>
            new VirtualScreen(Id, resolution, Placement, WidthMeters, Curvature, LastNonZeroCurvature, FrameRateMode, Label, Source);

        public override string ToString() => $"{Id} {Source} {Resolution} {WidthMeters:0.00} m curv {Curvature:0.00} {FrameRateMode}";
```

`CreateScreen.cs`: propriedade + parâmetro + repasse:

```csharp
        public string Label { get; }
        public ScreenSource Source { get; }
        public bool IsUndoable => true;

        public CreateScreen(Resolution resolution, Placement? placement, float widthMeters, float initialCurvature = 0f,
            string label = "", ScreenSource source = default)
        {
            Resolution = resolution;
            Placement = placement;
            WidthMeters = widthMeters;
            InitialCurvature = initialCurvature;
            Label = label;
            Source = source;
        }
```

e em `Apply`: `var screen = new VirtualScreen(id, Resolution, placement, width, InitialCurvature, label: Label, source: Source);`
e `ToString()`: `$"CreateScreen({Source}, {Resolution}, {WidthMeters:0.00} m)"`.

`TestScreens.cs`:

```csharp
        public static bool IsTestScreen(VirtualScreen screen) => screen.Source.Kind == ScreenSourceKind.Test;

        public static CreateScreen Next(int index)
        {
            var resolution = Cycle[index % Cycle.Length];
            float width = resolution.Orientation == Orientation.Portrait ? 0.6f : 1f;
            return new CreateScreen(resolution, null, width, 0f, LabelPrefix + (index + 1), ScreenSource.Test);
        }
```

(remover o `using System;` se ficar sem uso.)

- [ ] **Step 4: Run tests**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests` e `dotnet test host/Host.Tests`
Expected: PASS (todos, incluindo os 5 novos).

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Domain Assets/_Project/Application/Menu/TestScreens.cs Assets/_Project/Tests/EditMode/ScreenSourceTests.cs
git commit -m "domain: screen source (virtual, mirror, test) and initial width by pixel density"
```

---

### Task 2: Catálogo de resoluções

**Files:**
- Create: `Assets/_Project/Application/Menu/ResolutionCatalog.cs`
- Test: `Assets/_Project/Tests/EditMode/Manipulation/ResolutionCatalogTests.cs`

**Interfaces:**
- Consumes: `Resolution.Create`, `Resolution.Rotated()`, `Orientation` (Domain).
- Produces: `ResolutionCatalog.AspectCount : int`; `Name(int aspect, Orientation) : string`; `FrameAspect(int aspect, Orientation) : float`; `Count(int aspect) : int`; `Get(int aspect, int index, Orientation) : Resolution`; `Label(int aspect, int index, Orientation) : string` (strings em cache, sem alocar).

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Tests
{
    public class ResolutionCatalogTests
    {
        [Test]
        public void SixAspects_InTheBriefOrder()
        {
            Assert.That(ResolutionCatalog.AspectCount, Is.EqualTo(6));
            string[] names = { "16:9", "16:10", "21:9", "32:9", "1:1", "4:3" };
            for (int i = 0; i < names.Length; i++)
                Assert.That(ResolutionCatalog.Name(i, Orientation.Landscape), Is.EqualTo(names[i]));
            Assert.That(ResolutionCatalog.Name(0, Orientation.Portrait), Is.EqualTo("9:16"));
            Assert.That(ResolutionCatalog.Name(4, Orientation.Portrait), Is.EqualTo("1:1"));
        }

        [Test]
        public void EveryResolution_IsValid_MultipleOf8_AtMost4096_AndCloseToItsAspect()
        {
            for (int a = 0; a < ResolutionCatalog.AspectCount; a++)
            {
                float nominal = ResolutionCatalog.FrameAspect(a, Orientation.Landscape);
                Assert.That(ResolutionCatalog.Count(a), Is.GreaterThan(0));
                for (int i = 0; i < ResolutionCatalog.Count(a); i++)
                {
                    var r = ResolutionCatalog.Get(a, i, Orientation.Landscape);
                    Assert.That(Resolution.TryCreate(r.Width, r.Height, out _, out var error), Is.True, error);
                    Assert.That(r.Width, Is.LessThanOrEqualTo(4096));
                    Assert.That(r.AspectRatio, Is.EqualTo(nominal).Within(nominal * 0.03f), r.ToString());
                }
            }
        }

        [Test]
        public void Portrait_RotatesResolutionFrameAndLabel()
        {
            var landscape = ResolutionCatalog.Get(0, 1, Orientation.Landscape);
            var portrait = ResolutionCatalog.Get(0, 1, Orientation.Portrait);
            Assert.That(landscape, Is.EqualTo(Resolution.Create(2560, 1440)));
            Assert.That(portrait, Is.EqualTo(Resolution.Create(1440, 2560)));
            Assert.That(ResolutionCatalog.Label(0, 1, Orientation.Portrait), Is.EqualTo("1440×2560"));
            Assert.That(ResolutionCatalog.FrameAspect(0, Orientation.Portrait), Is.EqualTo(9f / 16f).Within(1e-5f));
        }

        [Test]
        public void Labels_AreCached()
        {
            Assert.That(ResolutionCatalog.Label(1, 0, Orientation.Landscape),
                Is.SameAs(ResolutionCatalog.Label(1, 0, Orientation.Landscape)));
        }
    }
}
```

- [ ] **Step 2: Run to fail**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests --filter ResolutionCatalogTests`
Expected: FAIL (`ResolutionCatalog` não existe).

- [ ] **Step 3: Implement** — `Assets/_Project/Application/Menu/ResolutionCatalog.cs`

```csharp
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Menu
{
    /// <summary>
    /// Proporções e resoluções do seletor "+ Monitor virtual" (spec 2C §3; brief §10.3). Todas múltiplas de 8 e
    /// ≤ 4096 (H.264). Retrato inverte. Rótulos em cache: o painel lê a cada quadro sem alocar. Custom fica na 2D.
    /// </summary>
    public static class ResolutionCatalog
    {
        private sealed class Aspect
        {
            public readonly int W;
            public readonly int H;
            public readonly string Landscape;
            public readonly string Portrait;
            public readonly Resolution[] Resolutions;
            public readonly string[] LandscapeLabels;
            public readonly string[] PortraitLabels;

            public Aspect(int w, int h, params Resolution[] resolutions)
            {
                W = w;
                H = h;
                Landscape = w + ":" + h;
                Portrait = h + ":" + w;
                Resolutions = resolutions;
                LandscapeLabels = new string[resolutions.Length];
                PortraitLabels = new string[resolutions.Length];
                for (int i = 0; i < resolutions.Length; i++)
                {
                    LandscapeLabels[i] = resolutions[i].ToString();
                    PortraitLabels[i] = resolutions[i].Rotated().ToString();
                }
            }
        }

        private static readonly Aspect[] Aspects =
        {
            new Aspect(16, 9, R(1920, 1080), R(2560, 1440), R(3840, 2160)),
            new Aspect(16, 10, R(1920, 1200), R(2560, 1600), R(3840, 2400)),
            new Aspect(21, 9, R(2560, 1080), R(3440, 1440)),
            new Aspect(32, 9, R(3840, 1080)),
            new Aspect(1, 1, R(1440, 1440), R(2160, 2160)),
            new Aspect(4, 3, R(1600, 1200), R(2048, 1536)),
        };

        private static Resolution R(int w, int h) => Resolution.Create(w, h);

        public static int AspectCount => Aspects.Length;

        public static string Name(int aspect, Orientation orientation) =>
            orientation == Orientation.Portrait ? Aspects[aspect].Portrait : Aspects[aspect].Landscape;

        /// <summary>Largura/altura do frame de exemplo no menu (gira em retrato).</summary>
        public static float FrameAspect(int aspect, Orientation orientation)
        {
            var a = Aspects[aspect];
            return orientation == Orientation.Portrait ? (float)a.H / a.W : (float)a.W / a.H;
        }

        public static int Count(int aspect) => Aspects[aspect].Resolutions.Length;

        public static Resolution Get(int aspect, int index, Orientation orientation)
        {
            var r = Aspects[aspect].Resolutions[index];
            return orientation == Orientation.Portrait ? r.Rotated() : r;
        }

        public static string Label(int aspect, int index, Orientation orientation) =>
            orientation == Orientation.Portrait ? Aspects[aspect].PortraitLabels[index] : Aspects[aspect].LandscapeLabels[index];
    }
}
```

- [ ] **Step 4: Run tests** — `dotnet test tests-dotnet/UnityPureCode.Tests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Application/Menu/ResolutionCatalog.cs Assets/_Project/Tests/EditMode/Manipulation/ResolutionCatalogTests.cs
git commit -m "app: resolution catalog (6 aspects, landscape/portrait, cached labels)"
```

---

### Task 3: Menu de pulso com páginas (modelo, roteador e painel)

**Files:**
- Modify: `Assets/_Project/Application/Menu/Ports.cs` (+ `IHostCatalog`, `FixedHostCatalog`)
- Modify: `Assets/_Project/Application/Connection/ControlMessages.cs` (só `MonitorDescriptor.FriendlyName`)
- Rewrite: `Assets/_Project/Application/Menu/WristMenu.cs`
- Modify: `Assets/_Project/Application/Manipulation/InteractionRouter.cs`
- Rewrite: `Assets/_Project/Presentation/Menu/WristMenuPanel.cs`
- Test: `Assets/_Project/Tests/EditMode/Manipulation/WristMenuTests.cs` (reescrito)

**Interfaces:**
- Consumes: `ResolutionCatalog` (Task 2), `ScreenSource`, `ScreenSizing` (Task 1), `ResolutionFit.FromCapture` (Domain), `MonitorDescriptor` (App.Connection).
- Produces:
  - `interface IHostCatalog { bool IsConnected { get; } IReadOnlyList<MonitorDescriptor> PhysicalMonitors { get; } }` e `FixedHostCatalog(bool connected, IReadOnlyList<MonitorDescriptor> monitors)` com setters públicos.
  - `enum MenuPage { Main, Aspects, Resolutions, Mirrors }`; `enum MenuItemId { Undo, Redo, ToggleMode, Recenter, AlignPosture, ToggleGrid, AddVirtual, AddMirror, ReturnToSurface, RemoveScreen, AddTestScreen }`.
  - `WristMenu(WorkspaceStore, ICommandBus, IHeadPoseSource, IHostCatalog host = null, bool showTestScreens = false)` com `const int MaxItems = 12`, `const int ComfortableVideoScreens = 4`, `const string TooManyNotice`, `MenuPage Page`, `Orientation Orientation`, `int ItemCount`, `string Label(int)`, `bool IsEnabled(int)`, `float FrameAspect(int)` (0 = botão comum), `void Invoke(int)`, `void Invoke(MenuItemId)`, `int IndexOf(MenuItemId)` (página principal; −1 se ausente), `void OnHidden()`, `string StatusLine`.
  - `MonitorDescriptor.FriendlyName` (sem o prefixo `\\.\`).

- [ ] **Step 1: Write the failing test** — substituir **todo** o conteúdo de `WristMenuTests.cs`:

```csharp
using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class WristMenuTests
    {
        private static readonly Pose Head = new Pose(new Vec3(0f, 1.3f, 0f), Quat.AngleAxis(30f, Vec3.Up));

        private static readonly MonitorDescriptor[] Physical =
        {
            new MonitorDescriptor(1, 2560, 1080, @"\\.\DISPLAY1", true),
            new MonitorDescriptor(2, 1920, 1080, @"\\.\DISPLAY2", false),
        };

        private sealed class Rig
        {
            public readonly WorkspaceStore Store = new WorkspaceStore();
            public readonly CommandBus Bus;
            public readonly FixedHostCatalog Host = new FixedHostCatalog(true, Physical);
            public readonly WristMenu Menu;

            public Rig(bool connected = true, bool showTestScreens = true)
            {
                Host.IsConnected = connected;
                Bus = new CommandBus(Store, null);
                Menu = new WristMenu(Store, Bus, new FixedHeadPose(Head), Host, showTestScreens);
            }
        }

        private sealed class FakeInput : IManipulationInput
        {
            public ManipulationInput Next;

            public bool Read(out ManipulationInput input)
            {
                input = Next;
                return true;
            }
        }

        private sealed class FakePanel : IWristMenuPanel
        {
            public bool Visible { get; set; } = true;
            public Pose Pose => Pose.At(new Vec3(0f, 1f, 0.4f));
            public int LastHover = -2;
            public void Render(WristMenu menu, int hoverIndex) => LastHover = hoverIndex;
        }

        [Test]
        public void AddTestScreen_CreatesNumberedTestScreens_AndUndoBecomesAvailable()
        {
            var r = new Rig();
            Assert.That(r.Menu.IsEnabled(r.Menu.IndexOf(MenuItemId.Undo)), Is.False);
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(2));
            Assert.That(r.Store.State.Screens[0].Label, Is.EqualTo("Teste 1"));
            Assert.That(r.Menu.IsEnabled(r.Menu.IndexOf(MenuItemId.Undo)), Is.True);
            Assert.That(r.Menu.StatusLine, Is.EqualTo("Cilindro · 2 telas"));
        }

        [Test]
        public void TestScreenItem_OnlyInDevBuilds()
        {
            Assert.That(new Rig(showTestScreens: false).Menu.IndexOf(MenuItemId.AddTestScreen), Is.EqualTo(-1));
            Assert.That(new Rig(showTestScreens: true).Menu.IndexOf(MenuItemId.AddTestScreen), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void ToggleModeRecenterAndGrid_SendTheSurfaceCommands()
        {
            var r = new Rig();
            r.Menu.Invoke(MenuItemId.ToggleMode);
            Assert.That(r.Store.State.Surface.Mode, Is.EqualTo(LayoutMode.Sphere));
            r.Menu.Invoke(MenuItemId.Recenter);
            Assert.That(r.Store.State.Surface.Center, Is.EqualTo(Head.Position));
            Assert.That(r.Store.State.Surface.HeadingDeg, Is.EqualTo(30f).Within(0.01f));
            r.Menu.Invoke(MenuItemId.ToggleGrid);
            Assert.That(r.Store.State.Surface.Snap.AngularGrid, Is.True);
            r.Menu.Invoke(MenuItemId.Undo);
            Assert.That(r.Store.State.Surface.Snap.AngularGrid, Is.False);
        }

        [Test]
        public void ReturnAndRemove_NeedAFocusedScreen()
        {
            var r = new Rig();
            Assert.That(r.Menu.IsEnabled(r.Menu.IndexOf(MenuItemId.RemoveScreen)), Is.False);
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            Assert.That(r.Menu.IsEnabled(r.Menu.IndexOf(MenuItemId.RemoveScreen)), Is.True);
            Assert.That(r.Menu.IsEnabled(r.Menu.IndexOf(MenuItemId.ReturnToSurface)), Is.False, "tela na superfície");
            r.Menu.Invoke(MenuItemId.RemoveScreen);
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(0));
        }

        [Test]
        public void Offline_CreateItemsAreDisabled_AndStatusAsksToConnect()
        {
            var r = new Rig(connected: false);
            Assert.That(r.Menu.IsEnabled(r.Menu.IndexOf(MenuItemId.AddVirtual)), Is.False);
            Assert.That(r.Menu.IsEnabled(r.Menu.IndexOf(MenuItemId.AddMirror)), Is.False);
            Assert.That(r.Menu.StatusLine, Is.EqualTo("Cilindro · 0 telas · Conecte ao PC"));
        }

        [Test]
        public void AddVirtual_AspectPage_HasOrientationSixFramesAndBack()
        {
            var r = new Rig();
            r.Menu.Invoke(MenuItemId.AddVirtual);
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Aspects));
            Assert.That(r.Menu.ItemCount, Is.EqualTo(8));
            Assert.That(r.Menu.Label(0), Is.EqualTo("Orientação: Paisagem"));
            Assert.That(r.Menu.FrameAspect(0), Is.EqualTo(0f));
            Assert.That(r.Menu.Label(1), Is.EqualTo("16:9"));
            Assert.That(r.Menu.FrameAspect(1), Is.EqualTo(16f / 9f).Within(1e-5f));
            Assert.That(r.Menu.Label(7), Is.EqualTo(WristMenu.BackLabel));

            r.Menu.Invoke(0); // Paisagem → Retrato
            Assert.That(r.Menu.Orientation, Is.EqualTo(Orientation.Portrait));
            Assert.That(r.Menu.Label(1), Is.EqualTo("9:16"));
            Assert.That(r.Menu.FrameAspect(1), Is.EqualTo(9f / 16f).Within(1e-5f));

            r.Menu.Invoke(7); // voltar
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Main));
        }

        [Test]
        public void PickingAspectThenResolution_CreatesAVirtualScreen_AndReturnsToMain()
        {
            var r = new Rig();
            r.Menu.Invoke(MenuItemId.AddVirtual);
            r.Menu.Invoke(1); // 16:9
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Resolutions));
            Assert.That(r.Menu.ItemCount, Is.EqualTo(4));
            Assert.That(r.Menu.Label(1), Is.EqualTo("2560×1440"));

            r.Menu.Invoke(3); // voltar → proporções
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Aspects));
            r.Menu.Invoke(1);
            r.Menu.Invoke(1); // 2560×1440

            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Main));
            var screen = r.Store.State.Screens[0];
            Assert.That(screen.Source, Is.EqualTo(ScreenSource.Virtual));
            Assert.That(screen.Resolution, Is.EqualTo(Resolution.Create(2560, 1440)));
            Assert.That(screen.WidthMeters, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(screen.Label, Is.EqualTo("2560×1440"));
        }

        [Test]
        public void MirrorPage_ListsPhysicalMonitors_AndCreatesAMirror()
        {
            var r = new Rig();
            r.Menu.Invoke(MenuItemId.AddMirror);
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Mirrors));
            Assert.That(r.Menu.ItemCount, Is.EqualTo(3));
            Assert.That(r.Menu.Label(1), Is.EqualTo("DISPLAY2 · 1920×1080"));

            r.Menu.Invoke(1);
            var screen = r.Store.State.Screens[0];
            Assert.That(screen.Source, Is.EqualTo(ScreenSource.Mirror(2)));
            Assert.That(screen.Resolution, Is.EqualTo(Resolution.Create(1920, 1080)));
            Assert.That(screen.Label, Is.EqualTo("DISPLAY2"));
        }

        [Test]
        public void FifthVideoScreen_ShowsTheWarning_TestScreensDoNotCount_HidingClearsIt()
        {
            var r = new Rig();
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            for (int i = 0; i < 4; i++)
            {
                r.Menu.Invoke(MenuItemId.AddVirtual);
                r.Menu.Invoke(1);
                r.Menu.Invoke(0);
            }
            Assert.That(r.Menu.StatusLine, Does.Not.Contain("Mais de 4"), "4 telas com vídeo + 1 de teste");

            r.Menu.Invoke(MenuItemId.AddVirtual);
            r.Menu.Invoke(1);
            r.Menu.Invoke(0);
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(6), "cria mesmo assim");
            Assert.That(r.Menu.StatusLine, Is.EqualTo(WristMenu.TooManyNotice));

            r.Menu.OnHidden();
            Assert.That(r.Menu.StatusLine, Is.EqualTo("Cilindro · 6 telas"));
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Main));
        }

        [Test]
        public void MenuLayout_ButtonAtFindsEachButtonCenter()
        {
            int n = new Rig().Menu.ItemCount;
            for (int i = 0; i < n; i++)
            {
                MenuLayout.ButtonCenter(i, n, out float x, out float y);
                Assert.That(MenuLayout.ButtonAt(x, y, n), Is.EqualTo(i));
            }
            Assert.That(MenuLayout.ButtonAt(0f, MenuLayout.PanelHeight(n) * 0.5f - 0.002f, n), Is.EqualTo(-1), "linha de status");
        }

        [Test]
        public void Router_RaySelectOnAButton_InvokesItAndDoesNotGrabScreens()
        {
            var r = new Rig();
            var right = new FakeInput();
            var left = new FakeInput();
            var panel = new FakePanel();
            var controller = new ScreenManipulationController(r.Store, r.Bus);
            var router = new InteractionRouter(right, left, controller, r.Menu, panel, r.Bus);

            int index = r.Menu.IndexOf(MenuItemId.AddTestScreen);
            MenuLayout.ButtonCenter(index, r.Menu.ItemCount, out float x, out float y);
            var eye = new Vec3(0f, 1f, 0f);
            var dir = (panel.Pose.Position + new Vec3(x, y, 0f) - eye).Normalized;
            right.Next = new ManipulationInput { HasRay = true, Origin = eye, Direction = dir };
            router.Tick();
            Assert.That(router.MenuHover, Is.EqualTo(index));
            Assert.That(panel.LastHover, Is.EqualTo(index));

            right.Next.SelectHeld = true;
            router.Tick();
            router.Tick(); // segurar não repete
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(1));
            Assert.That(controller.IsDragging, Is.False);
        }

        [Test]
        public void Router_PanelHidden_ResetsTheMenuPage()
        {
            var r = new Rig();
            var panel = new FakePanel();
            var router = new InteractionRouter(NullManipulationInput.Instance, NullManipulationInput.Instance,
                new ScreenManipulationController(r.Store, r.Bus), r.Menu, panel, r.Bus);
            r.Menu.Invoke(MenuItemId.AddVirtual);
            router.Tick();
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Aspects));
            panel.Visible = false;
            router.Tick();
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Main));
        }

        [Test]
        public void Router_LeftX_Undoes_LeftY_Redoes()
        {
            var r = new Rig();
            var left = new FakeInput();
            var router = new InteractionRouter(NullManipulationInput.Instance, left,
                new ScreenManipulationController(r.Store, r.Bus), r.Menu, null, r.Bus);
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            left.Next = new ManipulationInput { UndoPressed = true };
            router.Tick();
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(0));
            left.Next = new ManipulationInput { RedoPressed = true };
            router.Tick();
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(1));
        }
    }
}
```

- [ ] **Step 2: Run to fail**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests --filter WristMenuTests`
Expected: FAIL (compilação: `FixedHostCatalog`, `MenuPage`, `Invoke(int)` etc. não existem).

- [ ] **Step 3: Implement**

`Application/Connection/ControlMessages.cs` — em `MonitorDescriptor`, acrescentar:

```csharp
        /// <summary>Nome sem o prefixo do Windows (<c>\\.\DISPLAY2</c> → <c>DISPLAY2</c>) para mostrar no headset.</summary>
        public string FriendlyName { get; }
```

e no construtor, depois de `Name = ...`:

```csharp
            FriendlyName = Name.StartsWith(@"\\.\", StringComparison.Ordinal) ? Name.Substring(4) : Name;
```

`Application/Menu/Ports.cs` — acrescentar (com `using System.Collections.Generic;` e `using ScreenPortal.App.Connection;`):

```csharp
    /// <summary>O que o menu precisa saber do PC (implementado pelo HostSync).</summary>
    public interface IHostCatalog
    {
        bool IsConnected { get; }
        /// <summary>Monitores físicos do último Welcome (espelhar).</summary>
        IReadOnlyList<MonitorDescriptor> PhysicalMonitors { get; }
    }

    public sealed class FixedHostCatalog : IHostCatalog
    {
        public FixedHostCatalog(bool connected, IReadOnlyList<MonitorDescriptor> monitors)
        {
            IsConnected = connected;
            PhysicalMonitors = monitors ?? Array.Empty<MonitorDescriptor>();
        }

        public bool IsConnected { get; set; }
        public IReadOnlyList<MonitorDescriptor> PhysicalMonitors { get; set; }
    }
```

`Application/Menu/WristMenu.cs` — conteúdo completo:

```csharp
using System;
using System.Collections.Generic;
using ScreenPortal.App.Connection;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Menu
{
    public enum MenuItemId
    {
        Undo,
        Redo,
        ToggleMode,
        Recenter,
        AlignPosture,
        ToggleGrid,
        AddVirtual,
        AddMirror,
        ReturnToSurface,
        RemoveScreen,
        AddTestScreen,
    }

    public enum MenuPage
    {
        Main,
        Aspects,
        Resolutions,
        Mirrors,
    }

    /// <summary>
    /// Menu de pulso (spec 2B §5, 2C §3) com páginas: principal → proporções (frames) → resoluções, e espelhos.
    /// Tudo vira Command (ou undo/redo) no bus. Itens por índice; sem alocação por quadro (rótulos em cache).
    /// </summary>
    public sealed class WristMenu
    {
        public const int MaxItems = 12;
        public const int ComfortableVideoScreens = 4;
        public const string TooManyNotice = "Mais de 4 telas:\npode haver quedas e travamentos";
        public const string BackLabel = "← Voltar";

        private static readonly MenuItemId[] MainItems =
        {
            MenuItemId.Undo, MenuItemId.Redo, MenuItemId.ToggleMode, MenuItemId.Recenter, MenuItemId.AlignPosture,
            MenuItemId.ToggleGrid, MenuItemId.AddVirtual, MenuItemId.AddMirror, MenuItemId.ReturnToSurface, MenuItemId.RemoveScreen,
        };

        private static readonly MenuItemId[] MainItemsWithTest =
        {
            MenuItemId.Undo, MenuItemId.Redo, MenuItemId.ToggleMode, MenuItemId.Recenter, MenuItemId.AlignPosture,
            MenuItemId.ToggleGrid, MenuItemId.AddVirtual, MenuItemId.AddMirror, MenuItemId.ReturnToSurface, MenuItemId.RemoveScreen,
            MenuItemId.AddTestScreen,
        };

        private readonly WorkspaceStore _store;
        private readonly ICommandBus _bus;
        private readonly IHeadPoseSource _head;
        private readonly IHostCatalog _host;
        private readonly MenuItemId[] _main;
        private int _testScreens;
        private int _aspect;
        private string _notice = string.Empty;

        private Workspace _statusFor;
        private bool _statusConnected;
        private string _statusNotice;
        private string _status = string.Empty;

        private IReadOnlyList<MonitorDescriptor> _mirrorLabelsFor;
        private string[] _mirrorLabels = Array.Empty<string>();

        public MenuPage Page { get; private set; } = MenuPage.Main;
        public Orientation Orientation { get; private set; } = Orientation.Landscape;

        public WristMenu(WorkspaceStore store, ICommandBus bus, IHeadPoseSource head, IHostCatalog host = null, bool showTestScreens = false)
        {
            _store = store;
            _bus = bus;
            _head = head;
            _host = host ?? new FixedHostCatalog(false, Array.Empty<MonitorDescriptor>());
            _main = showTestScreens ? MainItemsWithTest : MainItems;
        }

        public int ItemCount
        {
            get
            {
                switch (Page)
                {
                    case MenuPage.Aspects: return ResolutionCatalog.AspectCount + 2; // orientação + proporções + voltar
                    case MenuPage.Resolutions: return ResolutionCatalog.Count(_aspect) + 1;
                    case MenuPage.Mirrors: return MirrorCount + 1;
                    default: return _main.Length;
                }
            }
        }

        private int MirrorCount => Math.Min(_host.PhysicalMonitors.Count, MaxItems - 1);

        private bool IsBack(int index) => Page != MenuPage.Main && index == ItemCount - 1;

        public int IndexOf(MenuItemId id) => Array.IndexOf(_main, id);

        /// <summary>Recalculada só quando o estado, a conexão ou o aviso mudam (sem alocar por quadro).</summary>
        public string StatusLine
        {
            get
            {
                var ws = _store.State;
                bool connected = _host.IsConnected;
                if (ReferenceEquals(ws, _statusFor) && connected == _statusConnected && ReferenceEquals(_notice, _statusNotice))
                    return _status;
                _statusFor = ws;
                _statusConnected = connected;
                _statusNotice = _notice;
                if (_notice.Length > 0)
                {
                    _status = _notice;
                    return _status;
                }
                int n = ws.Screens.Count;
                _status = (ws.Surface.Mode == LayoutMode.Cylinder ? "Cilindro" : "Esfera") + " · " + n + (n == 1 ? " tela" : " telas")
                          + (connected ? string.Empty : " · Conecte ao PC");
                return _status;
            }
        }

        public string Label(int index)
        {
            if (IsBack(index)) return BackLabel;
            switch (Page)
            {
                case MenuPage.Aspects:
                    if (index == 0) return Orientation == Orientation.Landscape ? "Orientação: Paisagem" : "Orientação: Retrato";
                    return ResolutionCatalog.Name(index - 1, Orientation);
                case MenuPage.Resolutions:
                    return ResolutionCatalog.Label(_aspect, index, Orientation);
                case MenuPage.Mirrors:
                    return MirrorLabels()[index];
                default:
                    return Label(_main[index]);
            }
        }

        private string Label(MenuItemId id)
        {
            var surface = _store.State.Surface;
            switch (id)
            {
                case MenuItemId.Undo: return "Desfazer";
                case MenuItemId.Redo: return "Refazer";
                case MenuItemId.ToggleMode: return surface.Mode == LayoutMode.Cylinder ? "Cilindro → Esfera" : "Esfera → Cilindro";
                case MenuItemId.Recenter: return "Recentrar";
                case MenuItemId.AlignPosture: return "Alinhar à postura";
                case MenuItemId.ToggleGrid: return surface.Snap.AngularGrid ? "Grade: ligada" : "Grade: desligada";
                case MenuItemId.AddVirtual: return "+ Monitor virtual";
                case MenuItemId.AddMirror: return "+ Espelhar monitor";
                case MenuItemId.ReturnToSurface: return "Voltar à superfície";
                case MenuItemId.RemoveScreen: return "Remover tela";
                case MenuItemId.AddTestScreen: return "+ Tela de teste";
                default: return id.ToString();
            }
        }

        private string[] MirrorLabels()
        {
            var monitors = _host.PhysicalMonitors;
            if (ReferenceEquals(monitors, _mirrorLabelsFor)) return _mirrorLabels;
            _mirrorLabelsFor = monitors;
            _mirrorLabels = new string[Math.Min(monitors.Count, MaxItems - 1)];
            for (int i = 0; i < _mirrorLabels.Length; i++)
                _mirrorLabels[i] = monitors[i].FriendlyName + " · " + monitors[i].Width + "×" + monitors[i].Height;
            return _mirrorLabels;
        }

        public bool IsEnabled(int index)
        {
            if (IsBack(index)) return true;
            switch (Page)
            {
                case MenuPage.Aspects: return index == 0 || _host.IsConnected;
                case MenuPage.Resolutions:
                case MenuPage.Mirrors: return _host.IsConnected;
                default: return IsEnabled(_main[index]);
            }
        }

        private bool IsEnabled(MenuItemId id)
        {
            var ws = _store.State;
            switch (id)
            {
                case MenuItemId.Undo: return _store.CanUndo;
                case MenuItemId.Redo: return _store.CanRedo;
                case MenuItemId.RemoveScreen: return ws.Contains(ws.FocusedScreenId);
                case MenuItemId.ReturnToSurface:
                    return ws.TryGetScreen(ws.FocusedScreenId, out var focused) && focused.Placement.IsLoose;
                case MenuItemId.AddVirtual:
                case MenuItemId.AddMirror:
                    return _host.IsConnected;
                default: return true;
            }
        }

        /// <summary>Largura/altura do frame de exemplo desenhado no botão; 0 = botão comum.</summary>
        public float FrameAspect(int index) =>
            Page == MenuPage.Aspects && index >= 1 && index <= ResolutionCatalog.AspectCount
                ? ResolutionCatalog.FrameAspect(index - 1, Orientation)
                : 0f;

        public void Invoke(int index)
        {
            if (index < 0 || index >= ItemCount || !IsEnabled(index)) return;
            if (IsBack(index))
            {
                Page = Page == MenuPage.Resolutions ? MenuPage.Aspects : MenuPage.Main;
                return;
            }
            switch (Page)
            {
                case MenuPage.Aspects:
                    if (index == 0)
                    {
                        Orientation = Orientation == Orientation.Landscape ? Orientation.Portrait : Orientation.Landscape;
                        return;
                    }
                    _aspect = index - 1;
                    Page = MenuPage.Resolutions;
                    return;
                case MenuPage.Resolutions:
                {
                    var resolution = ResolutionCatalog.Get(_aspect, index, Orientation);
                    Create(resolution, ScreenSource.Virtual, ResolutionCatalog.Label(_aspect, index, Orientation));
                    return;
                }
                case MenuPage.Mirrors:
                {
                    var monitor = _host.PhysicalMonitors[index];
                    var resolution = ResolutionFit.FromCapture((int)monitor.Width, (int)monitor.Height);
                    if (resolution.IsValid) Create(resolution, ScreenSource.Mirror(monitor.Id), monitor.FriendlyName);
                    return;
                }
                default:
                    Invoke(_main[index]);
                    return;
            }
        }

        public void Invoke(MenuItemId id)
        {
            if (!IsEnabled(id)) return;
            var ws = _store.State;
            switch (id)
            {
                case MenuItemId.Undo:
                    _bus.Undo();
                    break;
                case MenuItemId.Redo:
                    _bus.Redo();
                    break;
                case MenuItemId.ToggleMode:
                    _bus.Send(new SetLayoutMode(ws.Surface.Mode == LayoutMode.Cylinder ? LayoutMode.Sphere : LayoutMode.Cylinder));
                    break;
                case MenuItemId.Recenter:
                    var head = _head.HeadPose;
                    _bus.Send(new RecenterLayout(head.Position, LayoutSurface.HeadingOf(head.Rotation)));
                    break;
                case MenuItemId.AlignPosture:
                    _bus.Send(new AlignSurfaceToPosture(_head.HeadPose));
                    break;
                case MenuItemId.ToggleGrid:
                    _bus.Send(new SetSnapOptions(ws.Surface.Snap.WithAngularGrid(!ws.Surface.Snap.AngularGrid)));
                    break;
                case MenuItemId.AddVirtual:
                    Page = MenuPage.Aspects;
                    break;
                case MenuItemId.AddMirror:
                    Page = MenuPage.Mirrors;
                    break;
                case MenuItemId.AddTestScreen:
                    _bus.Send(TestScreens.Next(_testScreens++));
                    break;
                case MenuItemId.ReturnToSurface:
                    _bus.Send(new ReturnScreenToSurface(ws.FocusedScreenId));
                    break;
                case MenuItemId.RemoveScreen:
                    _bus.Send(new RemoveScreen(ws.FocusedScreenId));
                    break;
            }
        }

        private void Create(Resolution resolution, ScreenSource source, string label)
        {
            var result = _bus.Send(new CreateScreen(resolution, null, ScreenSizing.InitialWidthMeters(resolution), 0f, label, source));
            Page = MenuPage.Main;
            if (result.Accepted && VideoScreens(_store.State) > ComfortableVideoScreens) _notice = TooManyNotice;
        }

        private static int VideoScreens(Workspace ws)
        {
            int n = 0;
            for (int i = 0; i < ws.Screens.Count; i++)
                if (ws.Screens[i].Source.HasVideo) n++;
            return n;
        }

        /// <summary>O painel sumiu: volta à página principal e apaga o aviso.</summary>
        public void OnHidden()
        {
            Page = MenuPage.Main;
            _notice = string.Empty;
        }
    }
}
```

`Application/Manipulation/InteractionRouter.cs` — campo novo `private bool _prevPanelVisible;` e o trecho do menu em `Tick()` passa a ser:

```csharp
            MenuHover = -1;
            bool rightSelect = right.SelectHeld;
            bool panelVisible = _panel != null && _panel.Visible;
            if (!panelVisible && _prevPanelVisible) _menu.OnHidden();
            _prevPanelVisible = panelVisible;
            int count = _menu.ItemCount;
            if (panelVisible && !_manipulation.IsDragging && right.HasRay
                && ScreenHitTest.Raycast(right.Origin, right.Direction, _panel.Pose, MenuLayout.PanelWidth, MenuLayout.PanelHeight(count),
                    out _, out float lx, out float ly))
            {
                MenuHover = MenuLayout.ButtonAt(lx, ly, count);
                if (MenuHover >= 0 && rightSelect && !_prevRightSelect) _menu.Invoke(MenuHover);
                right.HasRay = false; // SelectHeld continua: o controlador só pega a tela num gatilho novo
            }
```

`Presentation/Menu/WristMenuPanel.cs` — conteúdo completo (mantém os campos serializados, o `Update` e o `palmNormalLocal` calibrado da 2B):

```csharp
using ScreenPortal.App.Menu;
using ScreenPortal.Domain.Screens;
using UnityEngine;
using Pose = ScreenPortal.Domain.Spatial.Pose;

namespace ScreenPortal.Presentation.Menu
{
    /// <summary>
    /// Menu de pulso (spec 2B §5, 2C §3): aparece com a palma esquerda virada para o rosto ou segurando Menu (≡) no
    /// controle esquerdo; fica acima da mão, virado para a cabeça. Páginas de tamanho variável; na página de
    /// proporções cada botão mostra um frame na proporção. Acionado pelo raio da mão/controle direito (InteractionRouter).
    /// </summary>
    public sealed class WristMenuPanel : MonoBehaviour, IWristMenuPanel
    {
        private const float FrameFill = 0.8f;      // o frame ocupa até 80% do botão
        private const float FrameBorderM = 0.0015f;

        [SerializeField] private Transform anchor;
        [SerializeField] private Transform head;
        [SerializeField] private OVRHand leftHand;
        [SerializeField] private Material unlit;
        [Tooltip("Normal da palma no espaço local da âncora da mão esquerda (calibrado no Simulator/Quest).")]
        [SerializeField] private Vector3 palmNormalLocal = Vector3.up;
        [SerializeField] private float palmFacingThreshold = 0.5f;
        [SerializeField] private float heightAboveHandM = 0.12f;

        private GameObject _content;
        private Transform _background;
        private TextMesh _status;
        private readonly TextMesh[] _labels = new TextMesh[WristMenu.MaxItems];
        private readonly MeshRenderer[] _buttons = new MeshRenderer[WristMenu.MaxItems];
        private readonly Transform[] _frames = new Transform[WristMenu.MaxItems];
        private readonly Transform[] _frameInner = new Transform[WristMenu.MaxItems];
        private Material _normal;
        private Material _hover;
        private Material _disabled;
        private Material _text;
        private MenuPage _layoutPage = (MenuPage)(-1);
        private int _layoutCount = -1;
        private Orientation _layoutOrientation;

        public bool Visible { get; private set; }

        public Pose Pose => new Pose(transform.position.ToDomain(), transform.rotation.ToDomain());

        private void Awake() => Build();

        private void Update()
        {
            bool controllerMenu = OVRInput.Get(OVRInput.Button.Start, OVRInput.Controller.LTouch);
            bool palm = false;
            if (leftHand != null && leftHand.IsTracked && anchor != null && head != null)
            {
                var normal = (anchor.rotation * palmNormalLocal).normalized;
                palm = Vector3.Dot(normal, (head.position - anchor.position).normalized) > palmFacingThreshold;
            }
            Visible = anchor != null && head != null && (controllerMenu || palm);
            _content.SetActive(Visible);
            if (!Visible) return;
            var position = anchor.position + Vector3.up * heightAboveHandM;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
        }

        public void Render(WristMenu menu, int hoverIndex)
        {
            if (!Visible) return;
            int count = menu.ItemCount;
            if (menu.Page != _layoutPage || count != _layoutCount || menu.Orientation != _layoutOrientation) Layout(menu, count);
            for (int i = 0; i < count; i++)
            {
                _labels[i].text = menu.Label(i);
                _buttons[i].sharedMaterial = !menu.IsEnabled(i) ? _disabled : i == hoverIndex ? _hover : _normal;
            }
            _status.text = menu.StatusLine;
        }

        private void Layout(WristMenu menu, int count)
        {
            _layoutPage = menu.Page;
            _layoutCount = count;
            _layoutOrientation = menu.Orientation;
            float height = MenuLayout.PanelHeight(count);
            _background.localScale = new Vector3(MenuLayout.PanelWidth, height, 1f);
            _status.transform.localPosition = new Vector3(0f, height * 0.5f - MenuLayout.StatusHeightM * 0.5f, -0.002f);
            for (int i = 0; i < WristMenu.MaxItems; i++)
            {
                bool used = i < count;
                _buttons[i].gameObject.SetActive(used);
                _labels[i].gameObject.SetActive(used);
                float aspect = used ? menu.FrameAspect(i) : 0f;
                _frames[i].gameObject.SetActive(aspect > 0f);
                if (!used) continue;
                MenuLayout.ButtonCenter(i, count, out float x, out float y);
                _buttons[i].transform.localPosition = new Vector3(x, y, 0f);
                _labels[i].transform.localPosition = new Vector3(x, y, -0.003f);
                if (aspect > 0f) PlaceFrame(i, x, y, aspect);
            }
        }

        private void PlaceFrame(int i, float x, float y, float aspect)
        {
            float maxW = MenuLayout.ButtonWidthM * FrameFill;
            float h = MenuLayout.ButtonHeightM * FrameFill;
            float w = h * aspect;
            if (w > maxW)
            {
                w = maxW;
                h = w / aspect;
            }
            _frames[i].localPosition = new Vector3(x, y, -0.001f);
            _frames[i].localScale = new Vector3(w, h, 1f);
            _frameInner[i].localScale = new Vector3(Mathf.Max(0f, 1f - 2f * FrameBorderM / w), Mathf.Max(0f, 1f - 2f * FrameBorderM / h), 1f);
        }

        private void Build()
        {
            _content = new GameObject("Content");
            _content.transform.SetParent(transform, false);
            _normal = Tinted(new Color(0.18f, 0.2f, 0.24f));
            _hover = Tinted(new Color(0.25f, 0.55f, 0.9f));
            _disabled = Tinted(new Color(0.1f, 0.1f, 0.12f));
            var frameLine = Tinted(new Color(0.85f, 0.87f, 0.9f));
            var frameFill = Tinted(new Color(0.12f, 0.13f, 0.16f));

            _background = Quad("Background", Tinted(new Color(0.06f, 0.06f, 0.08f)), _content.transform);
            _background.localPosition = new Vector3(0f, 0f, 0.002f);
            _status = Text("Status", Vector3.zero);
            for (int i = 0; i < WristMenu.MaxItems; i++)
            {
                var button = Quad("Button" + i, _normal, _content.transform);
                button.localScale = new Vector3(MenuLayout.ButtonWidthM, MenuLayout.ButtonHeightM, 1f);
                _buttons[i] = button.GetComponent<MeshRenderer>();
                _frames[i] = Quad("Frame" + i, frameLine, _content.transform);
                _frameInner[i] = Quad("FrameInner" + i, frameFill, _frames[i]);
                _frameInner[i].localPosition = new Vector3(0f, 0f, -0.0005f);
                _labels[i] = Text("Label" + i, Vector3.zero);
            }
            _content.SetActive(false);
        }

        private Material Tinted(Color color) => unlit != null ? new Material(unlit) { color = color } : null;

        private static Transform Quad(string name, Material material, Transform parent)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.GetComponent<Collider>().enabled = false;
            if (material != null) quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            quad.transform.SetParent(parent, false);
            return quad.transform;
        }

        private TextMesh Text(string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_content.transform, false);
            go.transform.localPosition = localPosition;
            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_text == null) _text = StereoText.MaterialFor(text.font);
            go.GetComponent<MeshRenderer>().sharedMaterial = _text;
            text.fontSize = 48;
            text.characterSize = 0.0012f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            return text;
        }
    }
}
```

`Composition/AppEntryPoint.cs`: `OnSystemRecentered` continua `_menu.Invoke(MenuItemId.Recenter);` (compila sem mudança).

- [ ] **Step 4: Run tests**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests` e `dotnet test host/Host.Tests`
Expected: PASS. (O painel é Unity: compila na Task 13; o visual é conferido no Simulator.)

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Application Assets/_Project/Presentation/Menu/WristMenuPanel.cs Assets/_Project/Tests/EditMode/Manipulation/WristMenuTests.cs
git commit -m "app+presentation: paged wrist menu (aspect frames, resolutions, mirrors, 5th-screen warning)"
```

---

### Task 4: Protocolo de controle v2 (+ ADR 0009)

**Files:**
- Modify: `protocol/control.fbs`; Move: `protocol/control-v1.md` → `protocol/control-v2.md`
- Regenerate: `Assets/_Project/Infrastructure/Shared/Protocol/Generated/ControlProtocol.g.cs` (`tools/gen-protocol.ps1`)
- Modify: `Assets/_Project/Application/Connection/ControlMessages.cs`, `Assets/_Project/Infrastructure/Shared/Protocol/ControlMessageCodec.cs`, `Assets/_Project/Infrastructure/Shared/Protocol/ControlProtocol.cs`
- Modify (compilação): `host/Host.Transport/Control/HeadsetSession.cs` (`start.MonitorId` → `start.ScreenKey`)
- Create: `docs/adr/0009-screen-leases-declarative-sync.md`
- Test: `Assets/_Project/Tests/Shared/ProtocolTests.cs`

**Interfaces:**
- Produces (App.Connection):
  - `enum ScreenKind { Virtual = 0, Mirror = 1 }`
  - `AcquireScreenMessage(uint screenKey, ScreenKind kind, uint width, uint height, uint physicalMonitorId)`
  - `ScreenReadyMessage(uint screenKey, uint width, uint height)`
  - `ScreenFailedMessage(uint screenKey, string reason)`
  - `ReleaseScreenMessage(uint screenKey, uint graceMs)`
  - `ScreenLeaseInfo(uint screenKey, ScreenKind kind, uint width, uint height)`
  - `WelcomeMessage(string hostName, IReadOnlyList<MonitorDescriptor> monitors, IReadOnlyList<ScreenLeaseInfo> leases = null)` com `Leases` (nunca null)
  - `StartStreamMessage(uint screenKey, ushort udpPort, ushort streamId)` — a propriedade `MonitorId` vira `ScreenKey`
  - `ControlMessageKind` + `AcquireScreen, ScreenReady, ScreenFailed, ReleaseScreen` (no fim)
  - `ControlProtocol.Version = 2`

- [ ] **Step 1: Write the failing test** — em `ProtocolTests.cs`, trocar em `AllOtherMessages_RoundTrip` as duas linhas do `StartStream` por:

```csharp
            var ss = (StartStreamMessage)RoundTrip(new StartStreamMessage(7, 9000, 0));
            Assert.That(ss.UdpPort, Is.EqualTo((ushort)9000));
            Assert.That(ss.ScreenKey, Is.EqualTo(7u));
```

e acrescentar os testes:

```csharp
        [Test]
        public void ScreenMessages_RoundTrip()
        {
            var a = (AcquireScreenMessage)RoundTrip(new AcquireScreenMessage(3, ScreenKind.Mirror, 2560, 1080, 2));
            Assert.That(a.ScreenKey, Is.EqualTo(3u));
            Assert.That(a.ScreenKind, Is.EqualTo(ScreenKind.Mirror));
            Assert.That(a.Width, Is.EqualTo(2560u));
            Assert.That(a.Height, Is.EqualTo(1080u));
            Assert.That(a.PhysicalMonitorId, Is.EqualTo(2u));

            var ready = (ScreenReadyMessage)RoundTrip(new ScreenReadyMessage(3, 3840, 2160));
            Assert.That(ready.ScreenKey, Is.EqualTo(3u));
            Assert.That(ready.Height, Is.EqualTo(2160u));

            var failed = (ScreenFailedMessage)RoundTrip(new ScreenFailedMessage(4, "SudoVDA não disponível"));
            Assert.That(failed.ScreenKey, Is.EqualTo(4u));
            Assert.That(failed.Reason, Is.EqualTo("SudoVDA não disponível"));

            var release = (ReleaseScreenMessage)RoundTrip(new ReleaseScreenMessage(5, 10000));
            Assert.That(release.ScreenKey, Is.EqualTo(5u));
            Assert.That(release.GraceMs, Is.EqualTo(10000u));
        }

        [Test]
        public void Welcome_RoundTripsLeases_AndDefaultsToEmpty()
        {
            var monitors = new[] { new MonitorDescriptor(1, 2560, 1080, "DISPLAY1", true) };
            var m = (WelcomeMessage)RoundTrip(new WelcomeMessage("CAYO-PC", monitors, new[]
            {
                new ScreenLeaseInfo(2, ScreenKind.Virtual, 3840, 2160),
                new ScreenLeaseInfo(5, ScreenKind.Mirror, 2560, 1080),
            }));
            Assert.That(m.Leases.Count, Is.EqualTo(2));
            Assert.That(m.Leases[0].ScreenKey, Is.EqualTo(2u));
            Assert.That(m.Leases[0].Width, Is.EqualTo(3840u));
            Assert.That(m.Leases[1].Kind, Is.EqualTo(ScreenKind.Mirror));

            var empty = (WelcomeMessage)RoundTrip(new WelcomeMessage("CAYO-PC", monitors));
            Assert.That(empty.Leases.Count, Is.EqualTo(0));
        }

        [Test]
        public void ProtocolVersion_Is2()
        {
            Assert.That(ControlProtocol.Version, Is.EqualTo((ushort)2));
        }
```

(`ProtocolTests.cs` já tem `using ScreenPortal.Infrastructure.Protocol;` e o helper `RoundTrip`; se o teste de versão incompatível usar o literal `1` como versão válida, trocar para `ControlProtocol.Version`.)

- [ ] **Step 2: Run to fail**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests --filter ProtocolTests`
Expected: FAIL (compilação).

- [ ] **Step 3: Implement**

`protocol/control.fbs` (campos novos no fim; membros novos no fim do union):

```
// Canal de controle PC <-> headset, v2. Ver protocol/control-v2.md.
// Regra (brief §8.9): campos novos sempre no FIM das tabelas e opcionais. Nunca reordenar nem remover.
namespace ScreenPortal.Protocol.Fb;

enum ByeReason : ubyte { Shutdown = 0, IncompatibleVersion = 1, PairingRejected = 2, Replaced = 3 }
enum ScreenKind : ubyte { Virtual = 0, Mirror = 1 }

table Hello { device_id:string; device_name:string; pairing_token:string; }
table PairingRequired { code:string; }
table PairingResult { accepted:bool; pairing_token:string; }
table MonitorInfo { id:uint; width:uint; height:uint; name:string; is_primary:bool; }
table ScreenLease { screen_key:uint; kind:ScreenKind; width:uint; height:uint; }
table Welcome { host_name:string; monitors:[MonitorInfo]; leases:[ScreenLease]; }
// v2: monitor_id não é mais usado (0); a tela é screen_key.
table StartStream { monitor_id:uint; udp_port:ushort; stream_id:ushort; screen_key:uint; }
table StreamStarted { stream_id:ushort; width:uint; height:uint; }
table StreamStopped { stream_id:ushort; reason:string; }
table Ping { sent_us:ulong; }
table Pong { sent_us:ulong; }
table Bye { reason:ByeReason; }
table AcquireScreen { screen_key:uint; kind:ScreenKind; width:uint; height:uint; physical_monitor_id:uint; }
table ScreenReady { screen_key:uint; width:uint; height:uint; }
table ScreenFailed { screen_key:uint; reason:string; }
table ReleaseScreen { screen_key:uint; grace_ms:uint; }

union Message { Hello, PairingRequired, PairingResult, Welcome, StartStream, StreamStarted, StreamStopped, Ping, Pong, Bye,
                AcquireScreen, ScreenReady, ScreenFailed, ReleaseScreen }

table Envelope { protocol_version:ushort; body:Message; }

root_type Envelope;
file_identifier "SPCT";   // 4 bytes no início do buffer: rejeita lixo cedo (e evita o bug do Verify sem identificador no C# 25.2.10)
```

Run: `powershell -ExecutionPolicy Bypass -File tools/gen-protocol.ps1` → `OK: ...ControlProtocol.g.cs`.

`ControlProtocol.cs`: `public const ushort Version = 2;` e o comentário "Constantes do canal de controle v2 (protocol/control-v2.md)."

`ControlMessages.cs`:
- `ControlMessageKind`: acrescentar no fim `AcquireScreen, ScreenReady, ScreenFailed, ReleaseScreen,`.
- Novo enum logo após `ByeReason`:

```csharp
    /// <summary>Tipo de monitor pedido ao host (spec 2C §4).</summary>
    public enum ScreenKind
    {
        Virtual = 0,
        Mirror = 1,
    }
```

- `WelcomeMessage`:

```csharp
    public sealed class WelcomeMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.Welcome;
        public string HostName { get; }
        public IReadOnlyList<MonitorDescriptor> Monitors { get; }
        /// <summary>Monitores que o host ainda mantém para este headset (v2).</summary>
        public IReadOnlyList<ScreenLeaseInfo> Leases { get; }

        public WelcomeMessage(string hostName, IReadOnlyList<MonitorDescriptor> monitors, IReadOnlyList<ScreenLeaseInfo> leases = null)
        {
            HostName = hostName ?? string.Empty;
            Monitors = monitors ?? Array.Empty<MonitorDescriptor>();
            Leases = leases ?? Array.Empty<ScreenLeaseInfo>();
        }
    }
```

- `StartStreamMessage`:

```csharp
    public sealed class StartStreamMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.StartStream;
        /// <summary>Tela (= ScreenId) cujo monitor o host deve transmitir (v2).</summary>
        public uint ScreenKey { get; }
        public ushort UdpPort { get; }
        public ushort StreamId { get; }

        public StartStreamMessage(uint screenKey, ushort udpPort, ushort streamId)
        {
            ScreenKey = screenKey;
            UdpPort = udpPort;
            StreamId = streamId;
        }
    }
```

- Classes novas (no fim do arquivo, antes do `}` do namespace):

```csharp
    public sealed class ScreenLeaseInfo
    {
        public uint ScreenKey { get; }
        public ScreenKind Kind { get; }
        public uint Width { get; }
        public uint Height { get; }

        public ScreenLeaseInfo(uint screenKey, ScreenKind kind, uint width, uint height)
        {
            ScreenKey = screenKey;
            Kind = kind;
            Width = width;
            Height = height;
        }
    }

    /// <summary>Headset → host: crie (ou reate) o monitor desta tela.</summary>
    public sealed class AcquireScreenMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.AcquireScreen;
        public uint ScreenKey { get; }
        public ScreenKind ScreenKind { get; }
        public uint Width { get; }
        public uint Height { get; }
        public uint PhysicalMonitorId { get; }

        public AcquireScreenMessage(uint screenKey, ScreenKind kind, uint width, uint height, uint physicalMonitorId)
        {
            ScreenKey = screenKey;
            ScreenKind = kind;
            Width = width;
            Height = height;
            PhysicalMonitorId = physicalMonitorId;
        }
    }

    public sealed class ScreenReadyMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.ScreenReady;
        public uint ScreenKey { get; }
        public uint Width { get; }
        public uint Height { get; }

        public ScreenReadyMessage(uint screenKey, uint width, uint height)
        {
            ScreenKey = screenKey;
            Width = width;
            Height = height;
        }
    }

    public sealed class ScreenFailedMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.ScreenFailed;
        public uint ScreenKey { get; }
        public string Reason { get; }

        public ScreenFailedMessage(uint screenKey, string reason)
        {
            ScreenKey = screenKey;
            Reason = reason ?? string.Empty;
        }
    }

    /// <summary>Headset → host: pare o stream e remova o monitor depois de <see cref="GraceMs"/> (undo reata).</summary>
    public sealed class ReleaseScreenMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.ReleaseScreen;
        public uint ScreenKey { get; }
        public uint GraceMs { get; }

        public ReleaseScreenMessage(uint screenKey, uint graceMs)
        {
            ScreenKey = screenKey;
            GraceMs = graceMs;
        }
    }
```

> `AcquireScreenMessage` expõe o tipo do monitor como `ScreenKind`, porque `Kind` já é o `ControlMessageKind` da classe base. `ScreenLeaseInfo` não herda de `ControlMessage`, então lá o nome é `Kind`.

`ControlMessageCodec.cs` — `Encode`:

```csharp
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
```

e, antes do `default:`:

```csharp
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
```

`Read`:

```csharp
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
```

e, antes do `default:`:

```csharp
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
```

`host/Host.Transport/Control/HeadsetSession.cs` (só para compilar; a sessão é reescrita na Task 9): em `OnStartStream`, `m.Id == start.MonitorId` → `m.Id == start.ScreenKey`.

`protocol/control-v2.md`:

```bash
git mv protocol/control-v1.md protocol/control-v2.md
```

No topo, trocar o título para "Canal de controle — v2 (Fase 2C)" e acrescentar ao fim:

```markdown
## v2 (Fase 2C) — telas sob comando do headset

`protocol_version` = 2 (v1 e v2 não conversam: `Bye(IncompatibleVersion)`). A chave de uma tela é o `ScreenId` da
workspace (`screen_key`), nunca reutilizado; undo restaura o mesmo id e, portanto, o mesmo monitor.

| Mensagem | Sentido | Campos | Resposta |
|---|---|---|---|
| `AcquireScreen` | headset → host | `screen_key, kind (Virtual/Mirror), width, height, physical_monitor_id` | `ScreenReady{screen_key,width,height}` ou `ScreenFailed{screen_key,reason}` |
| `StartStream` | headset → host | `screen_key, udp_port, stream_id` (`monitor_id` = 0, legado) | `StreamStarted` / `StreamStopped` |
| `ReleaseScreen` | headset → host | `screen_key, grace_ms` | — (para o stream; remove o monitor após o prazo; `AcquireScreen` da mesma chave antes disso reata) |
| `Welcome.leases` | host → headset | `[ScreenLease{screen_key, kind, width, height}]` | aluguéis ainda vivos deste headset |

Desconexão: o host aplica 5 min (padrão) a todos os aluguéis do headset. Espelho nunca remove nada no Windows: expirar só
encerra o stream. Portas de vídeo do headset: 9000 + `stream_id`. Detalhes: ADR 0009.
```

`docs/adr/0009-screen-leases-declarative-sync.md`:

```markdown
# ADR 0009 — Aluguéis de monitor e sincronização declarativa headset ⇄ host (protocolo v2)

- **Status:** Aceito (Fase 2C, 30/09/2026, aprovado pelo Cayo no brainstorming)
- **Spec:** docs/specs/2026-09-30-fase2c-varias-telas-design.md · **Protocolo:** protocol/control-v2.md

## Contexto
Na 2C o headset cria e remove monitores virtuais (SudoVDA) e espelhos de monitores físicos. Três fluxos mexem nisso:
undo/redo (a workspace volta atrás), remover com prazo (10 s para desfazer sem perder as janelas) e reconexão (Wi-Fi
caiu, óculos dormiu). Mensagens imperativas por Command exigiriam operações inversas no undo e um caminho separado de
ressincronização na reconexão: três caminhos para o mesmo estado.

## Decisão
1. **A workspace do headset é a fonte da verdade.** Um reconciliador puro (`HostSync`, camada Application) compara as
   telas com vídeo da workspace com o estado de cada chave no host e envia só as diferenças (`AcquireScreen`,
   `StartStream`, `ReleaseScreen`). Nenhum Command fala com o host.
2. **Chave estável = `ScreenId`.** Undo restaura o mesmo id, então o mesmo pedido reata o mesmo monitor.
3. **Aluguéis no host** (`ScreenLeaseTable`, `(device, screen_key)` → monitor, tipo, expiração): remover = expira em
   10 s; desconectar = expira em 5 min; `AcquireScreen` da mesma chave antes do prazo cancela a expiração. Aluguéis só em
   memória; se o host morrer, o watchdog do SudoVDA remove os monitores.
4. **Welcome lista os aluguéis vivos**: o headset libera os que a workspace não tem mais.

## Consequências
- Undo, remoção adiada e reconexão usam o mesmo caminho e são testáveis sem Unity nem Windows.
- O host precisa de uma fila de trabalho por sessão (criar monitor + esperar a captura leva até ~3 s).
- Um só headset por PC (mais de um fica fora do escopo; a chave já inclui o device).
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests` e `dotnet test host/Host.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add protocol Assets/_Project/Application/Connection/ControlMessages.cs Assets/_Project/Infrastructure/Shared/Protocol Assets/_Project/Tests/Shared/ProtocolTests.cs host/Host.Transport/Control/HeadsetSession.cs docs/adr/0009-screen-leases-declarative-sync.md
git commit -m "protocol: control v2 (screen leases: acquire/ready/failed/release, welcome leases, start stream by screen key) + ADR 0009"
```

---

### Task 5: `ConnectionStateMachine` só de conexão

**Files:**
- Rewrite: `Assets/_Project/Application/Connection/ConnectionStateMachine.cs`
- Modify: `Assets/_Project/Application/Connection/ConnectionOptions.cs` (remover `StreamId`, `DefaultScreenWidthMeters`; `VideoPort` = porta base)
- Modify: `Assets/_Project/Application/Connection/Ports.cs` (`IVideoStreamSink` → `IScreenVideo`)
- Delete: `Assets/_Project/Infrastructure/Unity/NativeVideoSink.cs` (+ `.meta`); Modify: `Assets/_Project/Composition/AppInstaller.cs` (remover a linha do `NativeVideoSink`)
- Modify: `Assets/_Project/Tests/EditMode/Connection/ConnectionFakes.cs`, `ConnectionStateMachineTests.cs`
- Modify: `host/Host.Tests/Control/ControlServerIntegrationTests.cs`

**Interfaces:**
- Produces:
  - `ConnectionStateMachine(IHostDiscovery, IControlChannel, IPairingStore, IClock, ConnectionOptions)`
  - `bool IsLinked`, `event Action<WelcomeMessage> Welcomed`, `event Action Lost`, `event Action<ControlMessage> ScreenMessage` (ScreenReady, ScreenFailed, StreamStarted, StreamStopped), `void Send(ControlMessage)` (só envia com `IsLinked`).
  - Welcome ⇒ estado `Streaming` + status `Connected` + `IsLinked = true` + `Welcomed` (nesta ordem). Saída de Streaming/Degraded ⇒ `IsLinked = false` e `Lost` antes do novo status.
  - `interface IScreenVideo { bool TryOpen(ScreenId screen, ushort streamId, int udpPort, Resolution resolution); void Close(ScreenId screen); bool HasFirstFrame(ScreenId screen); }`
- `ConnectionRig`: `Bus` público, sem `Video`; `GoToStreaming(WelcomeMessage welcome = null)` só empurra Connected + Welcome.

- [ ] **Step 1: Write the failing tests** — em `ConnectionStateMachineTests.cs`:
  1. Acrescentar `using System.Collections.Generic;`.
  2. Em `SlowPairingApproval_DoesNotTimeOutBeforeWelcome`, apagar a linha `r.Channel.Push(ChannelEventKind.Message, new StreamStartedMessage(0, 2560, 1080));`.
  3. **Apagar** `Welcome_CreatesScreenForPrimaryMonitor_AndRequestsStream`, `SurfaceNotReady_RetriesStartStreamOnLaterTick` e `Reconnect_GoesStraightToLastHost_AndReusesScreenAndDecoder`.
  4. Em `Reconnect_RetriesEvery250ms_AndGivesUpAfter10s`: depois de `var r = new ConnectionRig();` inserir `int lost = 0; r.Machine.Lost += () => lost++;` e trocar a última asserção por `Assert.That(lost, Is.EqualTo(1), "Lost só na queda, não a cada tentativa");`.
  5. Acrescentar:

```csharp
        [Test]
        public void Welcome_LinksImmediately_AndRaisesWelcomed()
        {
            var r = new ConnectionRig();
            WelcomeMessage got = null;
            r.Machine.Welcomed += w => got = w;
            r.GoToStreaming();

            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Streaming));
            Assert.That(r.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.Connected));
            Assert.That(r.Machine.IsLinked, Is.True);
            Assert.That(got, Is.SameAs(ConnectionRig.Welcome));
            Assert.That(r.Pairing.LastHost, Is.EqualTo(ConnectionRig.Pc));
            Assert.That(r.Channel.Count<StartStreamMessage>(), Is.EqualTo(0), "streams são do HostSync");
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(0), "telas são do HostSync");
        }

        [Test]
        public void Reconnect_GoesStraightToLastHost_RaisesLostThenWelcomedAgain()
        {
            var r = new ConnectionRig();
            int welcomed = 0, lost = 0;
            r.Machine.Welcomed += _ => welcomed++;
            r.Machine.Lost += () => lost++;
            r.GoToStreaming();
            r.Discovery.Host = null; // prova que não depende do mDNS

            r.Channel.Push(ChannelEventKind.Disconnected);
            r.Machine.Tick();
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Reconnecting));
            Assert.That(r.Machine.IsLinked, Is.False);
            Assert.That(lost, Is.EqualTo(1));
            r.Machine.Tick();
            Assert.That(r.Channel.Connects[r.Channel.Connects.Count - 1], Is.EqualTo(ConnectionRig.Pc));

            r.Channel.Push(ChannelEventKind.Connected);
            r.Channel.Push(ChannelEventKind.Message, ConnectionRig.Welcome);
            r.Machine.Tick();
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Streaming));
            Assert.That(welcomed, Is.EqualTo(2));
            Assert.That(r.Machine.IsLinked, Is.True);
        }

        [Test]
        public void ScreenMessages_AreForwarded_AndSendOnlyWhileLinked()
        {
            var r = new ConnectionRig();
            var got = new List<ControlMessage>();
            r.Machine.ScreenMessage += m => got.Add(m);
            r.Machine.Send(new ReleaseScreenMessage(1, 0));
            Assert.That(r.Channel.Count<ReleaseScreenMessage>(), Is.EqualTo(0), "sem conexão não envia");

            r.GoToStreaming();
            r.Machine.Send(new ReleaseScreenMessage(1, 0));
            Assert.That(r.Channel.Count<ReleaseScreenMessage>(), Is.EqualTo(1));

            r.Channel.Push(ChannelEventKind.Message, new ScreenReadyMessage(1, 2560, 1440));
            r.Channel.Push(ChannelEventKind.Message, new ScreenFailedMessage(2, "x"));
            r.Channel.Push(ChannelEventKind.Message, new StreamStartedMessage(0, 2560, 1440));
            r.Channel.Push(ChannelEventKind.Message, new StreamStoppedMessage(0, "y"));
            r.Machine.Tick();
            Assert.That(got.Count, Is.EqualTo(4));
            Assert.That(got[0], Is.TypeOf<ScreenReadyMessage>());
        }
```

  6. `ConnectionFakes.cs`: apagar a classe `FakeVideo`; no `ConnectionRig`, apagar o campo `Video`, criar `public readonly CommandBus Bus;` e trocar o construtor e o `GoToStreaming`:

```csharp
        public ConnectionRig()
        {
            Bus = new CommandBus(Store, new ICommandObserver[0]);
            Machine = new ConnectionStateMachine(Discovery, Channel, Pairing, Clock, new ConnectionOptions());
        }
```

```csharp
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
```

- [ ] **Step 2: Run to fail** — `dotnet test tests-dotnet/UnityPureCode.Tests --filter ConnectionStateMachineTests` → FAIL (compilação: construtor, `IsLinked`, eventos).

- [ ] **Step 3: Implement**

`Ports.cs` — trocar `IVideoStreamSink` por:

```csharp
    /// <summary>
    /// Decoder de vídeo de uma tela (nativo no Quest; no-op no Editor). TryOpen devolve false enquanto a Surface da
    /// layer não existe. HasFirstFrame é consultado ~1×/s pelo HostSync até o primeiro quadro decodificado.
    /// </summary>
    public interface IScreenVideo
    {
        bool TryOpen(ScreenId screen, ushort streamId, int udpPort, Resolution resolution);
        void Close(ScreenId screen);
        bool HasFirstFrame(ScreenId screen);
    }
```

`ConnectionOptions.cs`: apagar `StreamId` e `DefaultScreenWidthMeters`; documentar `VideoPort`:

```csharp
        /// <summary>Porta base do vídeo no headset: a tela com stream_id n escuta em VideoPort + n (spec 2C §4).</summary>
        public int VideoPort { get; set; } = 9000;
```

`ConnectionStateMachine.cs` — conteúdo completo:

```csharp
using System;

namespace ScreenPortal.App.Connection
{
    /// <summary>
    /// Ciclo de conexão do headset (brief §8.5, spec 1B §3, 2C §5):
    /// Idle → Discovering → Connecting → Negotiating → Streaming ⇄ Degraded → Reconnecting.
    /// Só conexão: telas e streams são do <c>HostSync</c>, que ouve <see cref="Welcomed"/>, <see cref="Lost"/> e
    /// <see cref="ScreenMessage"/> e envia por <see cref="Send"/>. Puro; <see cref="Tick"/> 1× por quadro.
    /// </summary>
    public sealed class ConnectionStateMachine
    {
        private readonly IHostDiscovery _discovery;
        private readonly IControlChannel _channel;
        private readonly IPairingStore _pairing;
        private readonly IClock _clock;
        private readonly ConnectionOptions _options;

        private HostEndpoint _host;
        private double _stateSince;
        private double _lastPingAt;
        private double _lastPongAt;
        private double _lastAttemptAt;
        private bool _attemptInFlight;
        private bool _awaitingPairing;
        private bool _welcomed;
        private bool _hasConnectedBefore;
        private bool _linked;
        private string _blockedHost;
        private double _blockedUntil;

        public ConnectionState State { get; private set; } = ConnectionState.Idle;
        public ConnectionStatus Status { get; private set; } = new ConnectionStatus(ConnectionStatusKind.Idle);
        /// <summary>true entre o Welcome e a queda: dá para falar com o host.</summary>
        public bool IsLinked => _linked;

        public event Action<ConnectionStatus> StatusChanged;
        /// <summary>Welcome recebido (conexão nova ou reconexão). IsLinked já é true.</summary>
        public event Action<WelcomeMessage> Welcomed;
        /// <summary>A conexão caiu (IsLinked já é false).</summary>
        public event Action Lost;
        /// <summary>ScreenReady, ScreenFailed, StreamStarted e StreamStopped.</summary>
        public event Action<ControlMessage> ScreenMessage;

        public ConnectionStateMachine(IHostDiscovery discovery, IControlChannel channel, IPairingStore pairing, IClock clock,
            ConnectionOptions options)
        {
            _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _pairing = pairing ?? throw new ArgumentNullException(nameof(pairing));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public void Start()
        {
            if (State != ConnectionState.Idle) return;
            _hasConnectedBefore = _pairing.TryGetLastHost(out _);
            _discovery.Start();
            EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
        }

        /// <summary>Envia ao host se a conexão estiver de pé; senão descarta (o HostSync reenvia após o Welcome).</summary>
        public void Send(ControlMessage message)
        {
            if (_linked) _channel.Send(message);
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
                    if (!_welcomed && !_awaitingPairing && now - _stateSince > _options.NegotiateTimeoutSeconds)
                    {
                        _channel.Close();
                        EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
                    }
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
                        _stateSince = now; // o timeout da negociação recomeça: o tempo do usuário decidindo não conta
                        SetStatus(new ConnectionStatus(ConnectionStatusKind.Connecting, _host.Name));
                    }
                    else
                    {
                        Block(ConnectionStatusKind.Rejected, _options.RejectCooldownSeconds);
                    }
                    break;
                case WelcomeMessage welcome:
                    OnWelcome(welcome, now);
                    break;
                case ScreenReadyMessage _:
                case ScreenFailedMessage _:
                case StreamStartedMessage _:
                case StreamStoppedMessage _:
                    ScreenMessage?.Invoke(message);
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

        private void OnWelcome(WelcomeMessage welcome, double now)
        {
            if (State != ConnectionState.Negotiating) return;
            _welcomed = true;
            _awaitingPairing = false;
            _hasConnectedBefore = true;
            _pairing.SaveLastHost(_host);
            _lastPongAt = now;
            _lastPingAt = now;
            Enter(ConnectionState.Streaming, new ConnectionStatus(ConnectionStatusKind.Connected, _host.Name));
            _linked = true;
            Welcomed?.Invoke(welcome);
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
                    EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
                    break;
            }
        }

        private void Block(ConnectionStatusKind statusKind, double cooldownSeconds)
        {
            _channel.Close();
            _blockedHost = _host.Name;
            _blockedUntil = _clock.NowSeconds + cooldownSeconds;
            EnterDiscovering(new ConnectionStatus(statusKind, _host.Name));
        }

        private bool IsBlocked(HostEndpoint host, double now) => host.Name == _blockedHost && now < _blockedUntil;

        private void Unlink()
        {
            if (!_linked) return;
            _linked = false;
            Lost?.Invoke();
        }

        private void EnterNegotiating()
        {
            _welcomed = false;
            _awaitingPairing = false;
            Enter(ConnectionState.Negotiating, new ConnectionStatus(ConnectionStatusKind.Connecting, _host.Name));
            _channel.Send(new HelloMessage(_pairing.DeviceId, _options.DeviceName, _pairing.GetToken(_host.Name)));
        }

        private void EnterReconnecting()
        {
            Unlink();
            _attemptInFlight = false;
            _lastAttemptAt = double.NegativeInfinity; // primeira tentativa no próximo Tick
            Enter(ConnectionState.Reconnecting, new ConnectionStatus(ConnectionStatusKind.Reconnecting, _host.Name));
        }

        private void EnterDiscovering(ConnectionStatus status)
        {
            Unlink();
            _welcomed = false;
            _awaitingPairing = false;
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

`AppInstaller.cs`: apagar a linha `builder.Register<NativeVideoSink>(Lifetime.Singleton).As<IVideoStreamSink>();`.

```bash
git rm Assets/_Project/Infrastructure/Unity/NativeVideoSink.cs Assets/_Project/Infrastructure/Unity/NativeVideoSink.cs.meta
```

`Spikes/Phase0/NativeVideoBridge.cs`: só o comentário cita `IVideoStreamSink` — trocar a frase por "Na Fase 2C isto virou o adapter `NativeScreenVideo : IScreenVideo`".

`host/Host.Tests/Control/ControlServerIntegrationTests.cs`: apagar a classe `OkVideo`; nos dois testes, construir a máquina com
`new ConnectionStateMachine(new StaticHostDiscovery(new HostEndpoint("TEST-PC", "127.0.0.1", server.Port)), channel, clientPairing, new MonotonicClock(), new ConnectionOptions { DeviceName = "Quest-IT" })` (e `new InMemoryPairingStore("dev-it")` no segundo); no primeiro teste apagar as asserções sobre `store.State.Screens`, `streams.Starts` e `video.Starts` (e as variáveis `store`/`video` que sobrarem). A asserção de reconexão < 3 s continua.

- [ ] **Step 4: Run tests** — `dotnet test tests-dotnet/UnityPureCode.Tests` e `dotnet test host/Host.Tests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add -A Assets/_Project/Application/Connection Assets/_Project/Infrastructure/Unity Assets/_Project/Composition/AppInstaller.cs Assets/_Project/Spikes Assets/_Project/Tests/EditMode/Connection host/Host.Tests/Control/ControlServerIntegrationTests.cs
git commit -m "app: connection state machine only connects (welcomed/lost/screen messages); screens and streams move to HostSync"
```

---

### Task 6: `HostSync` — reconciliação workspace ⇄ host

**Files:**
- Create: `Assets/_Project/Application/Screens/ScreenLinks.cs`, `Assets/_Project/Application/Screens/HostSync.cs`
- Modify: `Assets/_Project/Tests/EditMode/Connection/ConnectionFakes.cs` (+ `FakeScreenVideo`)
- Test: `Assets/_Project/Tests/EditMode/Screens/HostSyncTests.cs`

**Interfaces:**
- Consumes: `ConnectionStateMachine` (Task 5), mensagens v2 (Task 4), `IScreenVideo`, `ScreenSource`, `ScreenSizing`, `IHostCatalog` (Task 3).
- Produces:
  - `enum ScreenLinkState { Offline, Creating, Connecting, Live, Failed }`
  - `interface IScreenLinks { event Action<ScreenId> LinkChanged; bool TryGetLink(ScreenId screen, out ScreenLinkState state, out string detail); }`
  - `HostSync(WorkspaceStore, ConnectionStateMachine, IScreenVideo, ICommandBus, IClock, ConnectionOptions) : IHostCatalog, IScreenLinks, IDisposable` com `Start()`, `Tick()`, `const uint RemoveGraceMs = 10000`, `const int MaxStreams = 16`.

- [ ] **Step 1: Write the failing test**

`ConnectionFakes.cs` — acrescentar:

```csharp
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
    }
```

`Assets/_Project/Tests/EditMode/Screens/HostSyncTests.cs`:

```csharp
using NUnit.Framework;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Menu;
using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Tests
{
    public class HostSyncTests
    {
        private static readonly WelcomeMessage NoMonitors = new WelcomeMessage("CAYO-PC", new MonitorDescriptor[0]);

        private sealed class Rig
        {
            public readonly ConnectionRig C = new ConnectionRig();
            public readonly FakeScreenVideo Video = new FakeScreenVideo();
            public readonly HostSync Sync;

            public Rig()
            {
                Sync = new HostSync(C.Store, C.Machine, Video, C.Bus, C.Clock, new ConnectionOptions());
                Sync.Start();
            }

            public void Tick()
            {
                C.Machine.Tick();
                Sync.Tick();
            }

            public void Link(WelcomeMessage welcome = null)
            {
                C.GoToStreaming(welcome ?? NoMonitors);
                Sync.Tick();
            }

            public void Advance(double seconds)
            {
                double end = C.Clock.Now + seconds;
                while (C.Clock.Now < end - 1e-9)
                {
                    C.Clock.Now += 0.05;
                    C.Channel.Push(ChannelEventKind.Message, new PongMessage(0)); // mantém a conexão viva
                    Tick();
                }
            }

            public ScreenId AddVirtual(int w = 2560, int h = 1440)
            {
                var r = Resolution.Create(w, h);
                C.Bus.Send(new CreateScreen(r, null, ScreenSizing.InitialWidthMeters(r), 0f, r.ToString(), ScreenSource.Virtual));
                return C.Store.State.FocusedScreenId;
            }

            public void Push(ControlMessage m)
            {
                C.Channel.Push(ChannelEventKind.Message, m);
                Tick();
            }

            public ScreenLinkState State(ScreenId id)
            {
                Assert.That(Sync.TryGetLink(id, out var state, out _), Is.True);
                return state;
            }
        }

        [Test]
        public void FirstConnection_EmptyWorkspace_CreatesPrimaryMirror_AndAcquiresIt()
        {
            var r = new Rig();
            r.Link(ConnectionRig.Welcome); // DISPLAY1 2560×1080 primário

            Assert.That(r.C.Store.State.Screens.Count, Is.EqualTo(1));
            var screen = r.C.Store.State.Screens[0];
            Assert.That(screen.Source, Is.EqualTo(ScreenSource.Mirror(1)));
            Assert.That(screen.Label, Is.EqualTo("DISPLAY1"));
            var acquire = r.C.Channel.Last<AcquireScreenMessage>();
            Assert.That(acquire.ScreenKey, Is.EqualTo((uint)screen.Id.Value));
            Assert.That(acquire.ScreenKind, Is.EqualTo(ScreenKind.Mirror));
            Assert.That(acquire.PhysicalMonitorId, Is.EqualTo(1u));
            Assert.That(r.State(screen.Id), Is.EqualTo(ScreenLinkState.Creating));
        }

        [Test]
        public void AutoMirror_OnlyOnTheFirstConnection_AndNotWithScreens()
        {
            var r = new Rig();
            r.AddVirtual();
            r.Link(ConnectionRig.Welcome);
            Assert.That(r.C.Store.State.Screens.Count, Is.EqualTo(1), "já havia tela: sem espelho automático");
        }

        [Test]
        public void Ready_OpensVideo_ThenStartsStream_ThenLiveOnFirstFrame()
        {
            var r = new Rig();
            var id = r.AddVirtual();
            r.Link();
            var acquire = r.C.Channel.Last<AcquireScreenMessage>();
            Assert.That(acquire.ScreenKind, Is.EqualTo(ScreenKind.Virtual));
            Assert.That(acquire.Width, Is.EqualTo(2560u));

            r.Push(new ScreenReadyMessage((uint)id.Value, 2560, 1440));
            Assert.That(r.Video.Opens, Is.EqualTo(1));
            Assert.That(r.Video.LastPort, Is.EqualTo(9000));
            Assert.That(r.Video.LastStreamId, Is.EqualTo((ushort)0));
            var start = r.C.Channel.Last<StartStreamMessage>();
            Assert.That(start.ScreenKey, Is.EqualTo((uint)id.Value));
            Assert.That(start.UdpPort, Is.EqualTo((ushort)9000));
            Assert.That(r.State(id), Is.EqualTo(ScreenLinkState.Connecting));

            r.Push(new StreamStartedMessage(0, 2560, 1440));
            r.Video.FirstFrame = true;
            r.Advance(1.1);
            Assert.That(r.State(id), Is.EqualTo(ScreenLinkState.Live));
        }

        [Test]
        public void SurfaceNotReady_RetriesOpen_BeforeStartStream()
        {
            var r = new Rig();
            var id = r.AddVirtual();
            r.Link();
            r.Video.Ready = false;
            r.Push(new ScreenReadyMessage((uint)id.Value, 2560, 1440));
            Assert.That(r.C.Channel.Count<StartStreamMessage>(), Is.EqualTo(0));
            r.Video.Ready = true;
            r.Tick();
            Assert.That(r.C.Channel.Count<StartStreamMessage>(), Is.EqualTo(1));
        }

        [Test]
        public void Remove_ReleasesWith10sGrace_ClosesVideo_AndUndoAcquiresTheSameKey()
        {
            var r = new Rig();
            var id = r.AddVirtual();
            r.Link();
            r.Push(new ScreenReadyMessage((uint)id.Value, 2560, 1440));

            r.C.Bus.Send(new RemoveScreen(id));
            var release = r.C.Channel.Last<ReleaseScreenMessage>();
            Assert.That(release.ScreenKey, Is.EqualTo((uint)id.Value));
            Assert.That(release.GraceMs, Is.EqualTo(HostSync.RemoveGraceMs));
            Assert.That(r.Video.Open.Contains(id), Is.False);

            r.C.Bus.Undo();
            r.Tick();
            Assert.That(r.C.Channel.Count<AcquireScreenMessage>(), Is.EqualTo(2));
            Assert.That(r.C.Channel.Last<AcquireScreenMessage>().ScreenKey, Is.EqualTo((uint)id.Value));
        }

        [Test]
        public void Lost_ShowsOffline_KeepsTheDecoder_ReconnectAcquiresAgain()
        {
            var r = new Rig();
            var id = r.AddVirtual();
            r.Link();
            r.Push(new ScreenReadyMessage((uint)id.Value, 2560, 1440));
            r.Push(new StreamStartedMessage(0, 2560, 1440));

            r.C.Channel.Push(ChannelEventKind.Disconnected);
            r.Tick();
            Assert.That(r.State(id), Is.EqualTo(ScreenLinkState.Offline));
            Assert.That(r.Video.Closes, Is.EqualTo(0), "o decoder fica aberto para a reconexão");

            r.Tick(); // tentativa de reconexão
            r.C.Channel.Push(ChannelEventKind.Connected);
            r.C.Channel.Push(ChannelEventKind.Message, NoMonitors);
            r.Tick();
            Assert.That(r.C.Channel.Count<AcquireScreenMessage>(), Is.EqualTo(2));
            r.Push(new ScreenReadyMessage((uint)id.Value, 2560, 1440));
            Assert.That(r.C.Channel.Count<StartStreamMessage>(), Is.EqualTo(2));
            Assert.That(r.Video.Opens, Is.EqualTo(1), "mesma sessão de decoder");
        }

        [Test]
        public void Welcome_ReleasesLeasesTheWorkspaceNoLongerHas()
        {
            var r = new Rig();
            var id = r.AddVirtual();
            r.Link(new WelcomeMessage("CAYO-PC", new MonitorDescriptor[0], new[]
            {
                new ScreenLeaseInfo((uint)id.Value, ScreenKind.Virtual, 2560, 1440),
                new ScreenLeaseInfo(42, ScreenKind.Virtual, 1920, 1080),
            }));
            var release = r.C.Channel.Last<ReleaseScreenMessage>();
            Assert.That(release.ScreenKey, Is.EqualTo(42u));
            Assert.That(r.C.Channel.Count<ReleaseScreenMessage>(), Is.EqualTo(1));
        }

        [Test]
        public void ScreenFailed_ShowsTheReason_WithoutRemovingTheScreen()
        {
            var r = new Rig();
            var id = r.AddVirtual();
            r.Link();
            r.Push(new ScreenFailedMessage((uint)id.Value, "SudoVDA não disponível"));
            Assert.That(r.Sync.TryGetLink(id, out var state, out var detail), Is.True);
            Assert.That(state, Is.EqualTo(ScreenLinkState.Failed));
            Assert.That(detail, Is.EqualTo("SudoVDA não disponível"));
            Assert.That(r.C.Store.State.Screens.Count, Is.EqualTo(1));
        }

        [Test]
        public void StreamStopped_RetriesStartStreamAfterOneSecond()
        {
            var r = new Rig();
            var id = r.AddVirtual();
            r.Link();
            r.Push(new ScreenReadyMessage((uint)id.Value, 2560, 1440));
            r.Push(new StreamStoppedMessage(0, "falha ao iniciar o stream"));
            r.Advance(0.5);
            Assert.That(r.C.Channel.Count<StartStreamMessage>(), Is.EqualTo(1));
            r.Advance(0.6);
            Assert.That(r.C.Channel.Count<StartStreamMessage>(), Is.EqualTo(2));
        }

        [Test]
        public void TestScreens_AreIgnored()
        {
            var r = new Rig();
            r.C.Bus.Send(TestScreens.Next(0));
            r.Link();
            Assert.That(r.C.Channel.Count<AcquireScreenMessage>(), Is.EqualTo(0));
        }

        [Test]
        public void Ports_ArePooled_LowestFreeFirst()
        {
            var r = new Rig();
            var a = r.AddVirtual();
            var b = r.AddVirtual(1920, 1080);
            r.Link();
            r.Push(new ScreenReadyMessage((uint)a.Value, 2560, 1440));
            r.Push(new ScreenReadyMessage((uint)b.Value, 1920, 1080));
            Assert.That(r.Video.LastPort, Is.EqualTo(9001));

            r.C.Bus.Send(new RemoveScreen(a));
            var c = r.AddVirtual(3840, 2160);
            r.Tick();
            r.Push(new ScreenReadyMessage((uint)c.Value, 3840, 2160));
            Assert.That(r.Video.LastPort, Is.EqualTo(9000), "reusa a porta liberada");
        }

        [Test]
        public void Catalog_ExposesConnectionAndPhysicalMonitors()
        {
            var r = new Rig();
            Assert.That(r.Sync.IsConnected, Is.False);
            r.AddVirtual();
            r.Link(ConnectionRig.Welcome);
            Assert.That(r.Sync.IsConnected, Is.True);
            Assert.That(r.Sync.PhysicalMonitors.Count, Is.EqualTo(1));
        }
    }
}
```

- [ ] **Step 2: Run to fail** — `dotnet test tests-dotnet/UnityPureCode.Tests --filter HostSyncTests` → FAIL (compilação).

- [ ] **Step 3: Implement**

`Assets/_Project/Application/Screens/ScreenLinks.cs`:

```csharp
using System;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Screens
{
    /// <summary>Estado visível do vídeo de uma tela (spec 2C §5).</summary>
    public enum ScreenLinkState
    {
        /// <summary>Sem conexão com o PC.</summary>
        Offline,
        /// <summary>Pedido enviado; o host está criando/reatando o monitor.</summary>
        Creating,
        /// <summary>Monitor pronto; abrindo o decoder e o stream, aguardando o primeiro quadro.</summary>
        Connecting,
        /// <summary>Vídeo chegando.</summary>
        Live,
        /// <summary>O host recusou (ver o detalhe).</summary>
        Failed,
    }

    /// <summary>Consulta do estado do vídeo por tela (Presentation). LinkChanged só em mudança real.</summary>
    public interface IScreenLinks
    {
        event Action<ScreenId> LinkChanged;
        bool TryGetLink(ScreenId screen, out ScreenLinkState state, out string detail);
    }
}
```

`Assets/_Project/Application/Screens/HostSync.cs`:

```csharp
using System;
using System.Collections.Generic;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Screens
{
    /// <summary>
    /// Reconcilia as telas com vídeo da workspace com os aluguéis de monitor do host (spec 2C §4, ADR 0009).
    /// A workspace é a fonte da verdade: undo, remoção adiada e reconexão caem no mesmo caminho.
    /// <see cref="Tick"/> 1× por quadro, depois de <see cref="ConnectionStateMachine.Tick"/>; sem alocação por quadro
    /// (mensagens só nas transições).
    /// </summary>
    public sealed class HostSync : IHostCatalog, IScreenLinks, IDisposable
    {
        public const uint RemoveGraceMs = 10000;
        public const int MaxStreams = 16;
        public const double StreamRetrySeconds = 1.0;
        public const double FirstFramePollSeconds = 1.0;

        private enum Phase
        {
            Unlinked,
            Requested,
            Ready,
            Starting,
            Streaming,
            Live,
            Failed,
        }

        private sealed class Entry
        {
            public ScreenId Id;
            public ScreenSource Source;
            public Resolution Resolution;
            public Phase Phase;
            public int Slot = -1;
            public bool VideoOpen;
            public double NotBefore;
            public string Detail = string.Empty;
            public int Reported = -1;
            public bool Seen;
        }

        private readonly WorkspaceStore _store;
        private readonly ConnectionStateMachine _connection;
        private readonly IScreenVideo _video;
        private readonly ICommandBus _bus;
        private readonly IClock _clock;
        private readonly ConnectionOptions _options;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly bool[] _slots = new bool[MaxStreams];
        private IReadOnlyList<MonitorDescriptor> _monitors = Array.Empty<MonitorDescriptor>();
        private bool _started;
        private bool _autoMirrorDone;

        public event Action<ScreenId> LinkChanged;

        public HostSync(WorkspaceStore store, ConnectionStateMachine connection, IScreenVideo video, ICommandBus bus, IClock clock,
            ConnectionOptions options)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _video = video ?? throw new ArgumentNullException(nameof(video));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public bool IsConnected => _connection.IsLinked;
        public IReadOnlyList<MonitorDescriptor> PhysicalMonitors => _monitors;

        public void Start()
        {
            if (_started) return;
            _started = true;
            _store.StateChanged += OnStateChanged;
            _connection.Welcomed += OnWelcomed;
            _connection.Lost += OnLost;
            _connection.ScreenMessage += OnScreenMessage;
            Reconcile(_store.State);
        }

        public void Tick()
        {
            if (!_connection.IsLinked) return;
            double now = _clock.NowSeconds;
            for (int i = 0; i < _entries.Count; i++) Advance(_entries[i], now);
        }

        public bool TryGetLink(ScreenId screen, out ScreenLinkState state, out string detail)
        {
            var e = Find(screen);
            if (e == null)
            {
                state = ScreenLinkState.Offline;
                detail = string.Empty;
                return false;
            }
            state = Map(e);
            detail = e.Detail;
            return true;
        }

        private void Advance(Entry e, double now)
        {
            switch (e.Phase)
            {
                case Phase.Unlinked:
                    _connection.Send(new AcquireScreenMessage(Key(e.Id), KindOf(e.Source), (uint)e.Resolution.Width,
                        (uint)e.Resolution.Height, e.Source.PhysicalMonitorId));
                    SetPhase(e, Phase.Requested);
                    break;
                case Phase.Ready:
                    if (now < e.NotBefore) return;
                    if (e.Slot < 0) e.Slot = TakeSlot();
                    if (e.Slot < 0)
                    {
                        Fail(e, "Sem stream livre.");
                        return;
                    }
                    if (!e.VideoOpen) e.VideoOpen = _video.TryOpen(e.Id, (ushort)e.Slot, Port(e.Slot), e.Resolution);
                    if (!e.VideoOpen) return; // Surface da layer ainda não existe: tenta no próximo quadro
                    _connection.Send(new StartStreamMessage(Key(e.Id), (ushort)Port(e.Slot), (ushort)e.Slot));
                    SetPhase(e, Phase.Starting);
                    break;
                case Phase.Streaming:
                    if (now < e.NotBefore) return;
                    e.NotBefore = now + FirstFramePollSeconds;
                    if (_video.HasFirstFrame(e.Id)) SetPhase(e, Phase.Live);
                    break;
            }
        }

        private void OnStateChanged(Workspace previous, Workspace current) => Reconcile(current);

        private void Reconcile(Workspace ws)
        {
            for (int i = 0; i < _entries.Count; i++) _entries[i].Seen = false;
            var screens = ws.Screens;
            for (int i = 0; i < screens.Count; i++)
            {
                var s = screens[i];
                if (!s.Source.HasVideo) continue;
                var e = Find(s.Id);
                if (e == null)
                {
                    e = new Entry { Id = s.Id, Source = s.Source, Resolution = s.Resolution, Phase = Phase.Unlinked };
                    _entries.Add(e);
                    Report(e);
                }
                else if (!e.Resolution.Equals(s.Resolution) || !e.Source.Equals(s.Source))
                {
                    // RecreateScreen: o AcquireScreen com o tamanho novo faz o host trocar o monitor
                    CloseVideo(e);
                    e.Source = s.Source;
                    e.Resolution = s.Resolution;
                    SetPhase(e, Phase.Unlinked);
                }
                e.Seen = true;
            }
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.Seen) continue;
                _connection.Send(new ReleaseScreenMessage(Key(e.Id), RemoveGraceMs));
                CloseVideo(e);
                _entries.RemoveAt(i);
            }
        }

        private void OnWelcomed(WelcomeMessage welcome)
        {
            _monitors = welcome.Monitors;
            var leases = welcome.Leases;
            for (int i = 0; i < leases.Count; i++)
                if (Find(new ScreenId((int)leases[i].ScreenKey)) == null)
                    _connection.Send(new ReleaseScreenMessage(leases[i].ScreenKey, RemoveGraceMs));
            for (int i = 0; i < _entries.Count; i++) SetPhase(_entries[i], Phase.Unlinked); // pede de novo: o host reata
            if (!_autoMirrorDone)
            {
                _autoMirrorDone = true;
                if (_store.State.Screens.Count == 0) CreatePrimaryMirror();
            }
        }

        private void CreatePrimaryMirror()
        {
            MonitorDescriptor primary = null;
            for (int i = 0; i < _monitors.Count; i++)
                if (_monitors[i].IsPrimary) primary = _monitors[i];
            if (primary == null && _monitors.Count > 0) primary = _monitors[0];
            if (primary == null) return;
            var resolution = ResolutionFit.FromCapture((int)primary.Width, (int)primary.Height);
            if (!resolution.IsValid) return;
            _bus.Send(new CreateScreen(resolution, null, ScreenSizing.InitialWidthMeters(resolution), 0f, primary.FriendlyName,
                ScreenSource.Mirror(primary.Id)));
        }

        private void OnLost()
        {
            // o decoder e a porta ficam: na reconexão o host volta a transmitir para o mesmo lugar
            for (int i = 0; i < _entries.Count; i++) SetPhase(_entries[i], Phase.Unlinked);
        }

        private void OnScreenMessage(ControlMessage message)
        {
            Entry e;
            switch (message)
            {
                case ScreenReadyMessage ready:
                    e = Find(new ScreenId((int)ready.ScreenKey));
                    if (e != null && e.Phase == Phase.Requested)
                    {
                        e.NotBefore = 0;
                        SetPhase(e, Phase.Ready);
                    }
                    break;
                case ScreenFailedMessage failed:
                    e = Find(new ScreenId((int)failed.ScreenKey));
                    if (e != null) Fail(e, failed.Reason);
                    break;
                case StreamStartedMessage started:
                    e = FindBySlot(started.StreamId);
                    if (e != null && e.Phase == Phase.Starting)
                    {
                        e.NotBefore = _clock.NowSeconds;
                        SetPhase(e, Phase.Streaming);
                    }
                    break;
                case StreamStoppedMessage stopped:
                    e = FindBySlot(stopped.StreamId);
                    if (e != null && (e.Phase == Phase.Starting || e.Phase == Phase.Streaming || e.Phase == Phase.Live))
                    {
                        e.NotBefore = _clock.NowSeconds + StreamRetrySeconds;
                        SetPhase(e, Phase.Ready);
                    }
                    break;
            }
        }

        private ScreenLinkState Map(Entry e)
        {
            if (!_connection.IsLinked) return ScreenLinkState.Offline;
            switch (e.Phase)
            {
                case Phase.Unlinked:
                case Phase.Requested:
                    return ScreenLinkState.Creating;
                case Phase.Ready:
                case Phase.Starting:
                case Phase.Streaming:
                    return ScreenLinkState.Connecting;
                case Phase.Live:
                    return ScreenLinkState.Live;
                default:
                    return ScreenLinkState.Failed;
            }
        }

        private void SetPhase(Entry e, Phase phase)
        {
            e.Phase = phase;
            Report(e);
        }

        private void Fail(Entry e, string detail)
        {
            e.Detail = detail ?? string.Empty;
            SetPhase(e, Phase.Failed);
        }

        private void Report(Entry e)
        {
            int state = (int)Map(e);
            if (state == e.Reported) return;
            e.Reported = state;
            LinkChanged?.Invoke(e.Id);
        }

        private void CloseVideo(Entry e)
        {
            if (e.VideoOpen) _video.Close(e.Id);
            e.VideoOpen = false;
            if (e.Slot >= 0) _slots[e.Slot] = false;
            e.Slot = -1;
        }

        private int TakeSlot()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i]) continue;
                _slots[i] = true;
                return i;
            }
            return -1;
        }

        private Entry Find(ScreenId id)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Id == id) return _entries[i];
            return null;
        }

        private Entry FindBySlot(ushort streamId)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Slot == streamId) return _entries[i];
            return null;
        }

        private int Port(int slot) => _options.VideoPort + slot;

        private static uint Key(ScreenId id) => (uint)id.Value;

        private static ScreenKind KindOf(ScreenSource source) =>
            source.Kind == ScreenSourceKind.Mirror ? ScreenKind.Mirror : ScreenKind.Virtual;

        public void Dispose()
        {
            if (!_started) return;
            _started = false;
            _store.StateChanged -= OnStateChanged;
            _connection.Welcomed -= OnWelcomed;
            _connection.Lost -= OnLost;
            _connection.ScreenMessage -= OnScreenMessage;
            for (int i = 0; i < _entries.Count; i++) CloseVideo(_entries[i]);
            _entries.Clear();
        }
    }
}
```

- [ ] **Step 4: Run tests** — `dotnet test tests-dotnet/UnityPureCode.Tests` e `dotnet test host/Host.Tests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Application/Screens Assets/_Project/Tests/EditMode/Screens Assets/_Project/Tests/EditMode/Connection/ConnectionFakes.cs
git commit -m "app: HostSync reconciles video screens with host leases (acquire, stream pool, remove grace, undo, reconnect, auto mirror)"
```

---

### Task 7: Parser da linha de estatística do decoder

**Files:**
- Create: `Assets/_Project/Infrastructure/Shared/Video/SpStatLine.cs`
- Test: `Assets/_Project/Tests/Shared/SpStatLineTests.cs`

**Interfaces:**
- Produces: `ScreenPortal.Infrastructure.Video.SpStatLine.TryParseDecodedFps(string line, out float decodedFps) : bool`.

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using ScreenPortal.Infrastructure.Video;

namespace ScreenPortal.Tests
{
    public class SpStatLineTests
    {
        [Test]
        public void ParsesDecodedFps_FromTheNativeLine()
        {
            // headset-native stream_session.cpp: SPSTAT,id,recvFps,decFps,Mbps,dropNet,dropDec,avg,min,max,rtt
            Assert.That(SpStatLine.TryParseDecodedFps("SPSTAT,0,60.0,59.5,18.20,0,0,16.1,12.0,22.3,3.2", out float fps), Is.True);
            Assert.That(fps, Is.EqualTo(59.5f).Within(1e-4f));
        }

        [Test]
        public void RejectsNullGarbageAndShortLines()
        {
            Assert.That(SpStatLine.TryParseDecodedFps(null, out _), Is.False);
            Assert.That(SpStatLine.TryParseDecodedFps("hello", out _), Is.False);
            Assert.That(SpStatLine.TryParseDecodedFps("SPSTAT,0,60.0", out _), Is.False);
        }
    }
}
```

- [ ] **Step 2: Run to fail** — `dotnet test tests-dotnet/UnityPureCode.Tests --filter SpStatLineTests` → FAIL.

- [ ] **Step 3: Implement**

```csharp
using System;
using System.Globalization;

namespace ScreenPortal.Infrastructure.Video
{
    /// <summary>
    /// Linha CSV do decoder nativo (<c>NativeVideo.takeStats</c>, headset-native stream_session.cpp):
    /// <c>SPSTAT,streamId,recvFps,decodedFps,Mbps,dropNet,dropDec,avgMs,minMs,maxMs,rttMs</c>. Lida ~1×/s, fora do
    /// caminho de vídeo.
    /// </summary>
    public static class SpStatLine
    {
        public static bool TryParseDecodedFps(string line, out float decodedFps)
        {
            decodedFps = 0f;
            if (string.IsNullOrEmpty(line) || !line.StartsWith("SPSTAT,", StringComparison.Ordinal)) return false;
            var parts = line.Split(',');
            return parts.Length > 3 && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out decodedFps);
        }
    }
}
```

- [ ] **Step 4: Run tests** — `dotnet test tests-dotnet/UnityPureCode.Tests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Infrastructure/Shared/Video Assets/_Project/Tests/Shared/SpStatLineTests.cs
git commit -m "infra: parse the native SPSTAT line (decoded fps)"
```

---

## Parte B — Host

### Task 8: `ScreenLeaseTable`

**Files:**
- Create: `host/Host.Domain/Screens/ScreenLeaseTable.cs`
- Test: `host/Host.Tests/Screens/ScreenLeaseTableTests.cs`

**Interfaces:**
- Produces (`ScreenPortal.Host.Domain.Screens`): `enum ScreenLeaseKind { Virtual, Mirror }`; `ScreenLease(string deviceId, uint screenKey, ScreenLeaseKind kind, int width, int height, uint physicalMonitorId, string displayName)` com `DateTimeOffset? ExpiresAt`; `ScreenLeaseTable` com `TryGet`, `Add`, `Remove`, `Renew`, `Expire(device, key, at)` (mantém o prazo mais cedo), `ExpireAll(device, at)`, `ForDevice(device)`, `TakeExpired(now)`, `Count`.

- [ ] **Step 1: Write the failing test**

```csharp
using ScreenPortal.Host.Domain.Screens;

namespace ScreenPortal.Host.Tests.Screens;

public class ScreenLeaseTableTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static ScreenLease Virtual(string device, uint key) =>
        new(device, key, ScreenLeaseKind.Virtual, 2560, 1440, 0, @"\\.\DISPLAY5");

    [Fact]
    public void NewLease_IsLive_AndListedForItsDevice()
    {
        var table = new ScreenLeaseTable();
        table.Add(Virtual("dev-1", 2));
        table.Add(Virtual("dev-1", 1));
        Assert.True(table.TryGet("dev-1", 2, out var lease));
        Assert.Null(lease.ExpiresAt);
        Assert.Equal(new uint[] { 1, 2 }, table.ForDevice("dev-1").Select(l => l.ScreenKey));
    }

    [Fact]
    public void Expire_OnlyTakenAfterTheDeadline()
    {
        var table = new ScreenLeaseTable();
        table.Add(Virtual("dev-1", 1));
        table.Expire("dev-1", 1, T0.AddSeconds(10));
        Assert.Empty(table.TakeExpired(T0.AddSeconds(9)));
        var expired = table.TakeExpired(T0.AddSeconds(10));
        Assert.Single(expired);
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void Renew_CancelsTheExpiry()
    {
        var table = new ScreenLeaseTable();
        table.Add(Virtual("dev-1", 1));
        table.Expire("dev-1", 1, T0.AddSeconds(10));
        table.Renew("dev-1", 1);
        Assert.Empty(table.TakeExpired(T0.AddMinutes(10)));
    }

    [Fact]
    public void ExpireAll_KeepsTheEarlierDeadline()
    {
        var table = new ScreenLeaseTable();
        table.Add(Virtual("dev-1", 1));
        table.Add(Virtual("dev-1", 2));
        table.Expire("dev-1", 1, T0.AddSeconds(10));       // removida
        table.ExpireAll("dev-1", T0.AddMinutes(5));         // depois o headset caiu
        Assert.Equal(T0.AddSeconds(10), table.ForDevice("dev-1")[0].ExpiresAt);
        Assert.Equal(T0.AddMinutes(5), table.ForDevice("dev-1")[1].ExpiresAt);
    }

    [Fact]
    public void Devices_AreIsolated()
    {
        var table = new ScreenLeaseTable();
        table.Add(Virtual("dev-1", 1));
        table.Add(Virtual("dev-2", 1));
        table.ExpireAll("dev-1", T0);
        Assert.Single(table.TakeExpired(T0));
        Assert.True(table.TryGet("dev-2", 1, out _));
    }
}
```

- [ ] **Step 2: Run to fail** — `dotnet test host/Host.Tests --filter ScreenLeaseTableTests` → FAIL (compilação).

- [ ] **Step 3: Implement** — `host/Host.Domain/Screens/ScreenLeaseTable.cs`

```csharp
namespace ScreenPortal.Host.Domain.Screens;

public enum ScreenLeaseKind { Virtual, Mirror }

/// <summary>Um monitor (virtual ou espelho) mantido para uma tela de um headset. ExpiresAt null = em uso.</summary>
public sealed class ScreenLease(string deviceId, uint screenKey, ScreenLeaseKind kind, int width, int height,
    uint physicalMonitorId, string displayName)
{
    public string DeviceId { get; } = deviceId;
    public uint ScreenKey { get; } = screenKey;
    public ScreenLeaseKind Kind { get; } = kind;
    /// <summary>Resolução do monitor (virtual: a pedida; espelho: a do monitor físico ajustada).</summary>
    public int Width { get; } = width;
    public int Height { get; } = height;
    public uint PhysicalMonitorId { get; } = physicalMonitorId;
    /// <summary><c>\\.\DISPLAYn</c> capturado.</summary>
    public string DisplayName { get; } = displayName;
    public DateTimeOffset? ExpiresAt { get; internal set; }
}

/// <summary>
/// Aluguéis de monitor por (headset, tela) (spec 2C §4, ADR 0009). Puro: o relógio vem de fora. Não é thread-safe
/// (o ScreenHost serializa o acesso).
/// </summary>
public sealed class ScreenLeaseTable
{
    private readonly Dictionary<(string, uint), ScreenLease> _leases = new();

    public int Count => _leases.Count;

    public bool TryGet(string deviceId, uint screenKey, out ScreenLease lease) =>
        _leases.TryGetValue((deviceId, screenKey), out lease!);

    public void Add(ScreenLease lease) => _leases[(lease.DeviceId, lease.ScreenKey)] = lease;

    public bool Remove(string deviceId, uint screenKey) => _leases.Remove((deviceId, screenKey));

    /// <summary>A tela voltou (undo ou reconexão): cancela a expiração.</summary>
    public void Renew(string deviceId, uint screenKey)
    {
        if (TryGet(deviceId, screenKey, out var lease)) lease.ExpiresAt = null;
    }

    /// <summary>Agenda a remoção; se já havia um prazo mais cedo, ele fica.</summary>
    public void Expire(string deviceId, uint screenKey, DateTimeOffset at)
    {
        if (TryGet(deviceId, screenKey, out var lease)) Schedule(lease, at);
    }

    public void ExpireAll(string deviceId, DateTimeOffset at)
    {
        foreach (var lease in _leases.Values)
            if (lease.DeviceId == deviceId) Schedule(lease, at);
    }

    public IReadOnlyList<ScreenLease> ForDevice(string deviceId) =>
        _leases.Values.Where(l => l.DeviceId == deviceId).OrderBy(l => l.ScreenKey).ToList();

    /// <summary>Remove e devolve os aluguéis vencidos.</summary>
    public IReadOnlyList<ScreenLease> TakeExpired(DateTimeOffset now)
    {
        var expired = _leases.Values.Where(l => l.ExpiresAt is { } at && at <= now).ToList();
        foreach (var lease in expired) _leases.Remove((lease.DeviceId, lease.ScreenKey));
        return expired;
    }

    private static void Schedule(ScreenLease lease, DateTimeOffset at)
    {
        if (lease.ExpiresAt is { } current && current <= at) return;
        lease.ExpiresAt = at;
    }
}
```

- [ ] **Step 4: Run tests** — `dotnet test host/Host.Tests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add host/Host.Domain/Screens host/Host.Tests/Screens
git commit -m "host: screen lease table (per headset and screen; renew, earliest expiry, take expired)"
```

---

### Task 9: `HeadsetSession` v2 com fila de trabalho

**Files:**
- Modify: `host/Host.Transport/Control/HostPorts.cs` (`IStreamRegistry` → `IScreenHost` + `ScreenAcquireResult`)
- Rewrite: `host/Host.Transport/Control/HeadsetSession.cs`
- Modify: `host/Host.Tests/Control/HeadsetSessionTests.cs`, `host/Host.Tests/Control/ControlServerIntegrationTests.cs`

**Interfaces:**
- Consumes: mensagens v2 (Task 4).
- Produces:
  - `readonly record struct ScreenAcquireResult(bool Ok, int Width, int Height, string Error)`
  - `interface IScreenHost { IReadOnlyList<ScreenLeaseInfo> Leases(string deviceId); ScreenAcquireResult Acquire(string deviceId, AcquireScreenMessage request); bool StartStream(string deviceId, uint screenKey, string address, ushort udpPort, ushort streamId, out int width, out int height); void Release(string deviceId, uint screenKey, TimeSpan grace); void Disconnected(string deviceId); }`
  - `HeadsetSession(IControlConnection, PairingStore, Action savePairing, IPairingApprover, IMonitorProvider, IScreenHost, string hostName, Func<string> newCode, Action<string> log)` com `Task Idle` (fim da fila, para testes).

- [ ] **Step 1: Write the failing test** — em `HeadsetSessionTests.cs`: trocar a classe `FakeStreams` por `FakeScreens`, o helper `Create` e os três testes de stream:

```csharp
    internal sealed class FakeScreens : IScreenHost
    {
        public readonly List<string> Calls = new();
        public bool FailAcquire;
        public string? LastAddress;
        public ManualResetEventSlim? BlockAcquire;
        public List<ScreenLeaseInfo> LeasesToReport = new();

        public IReadOnlyList<ScreenLeaseInfo> Leases(string deviceId) => LeasesToReport;

        public ScreenAcquireResult Acquire(string deviceId, AcquireScreenMessage request)
        {
            BlockAcquire?.Wait(5000);
            lock (Calls) Calls.Add($"acquire {request.ScreenKey}");
            return FailAcquire ? new ScreenAcquireResult(false, 0, 0, "sem driver")
                               : new ScreenAcquireResult(true, (int)request.Width, (int)request.Height, "");
        }

        public bool StartStream(string deviceId, uint screenKey, string address, ushort udpPort, ushort streamId, out int width, out int height)
        {
            lock (Calls) Calls.Add($"start {screenKey}:{udpPort}");
            LastAddress = address;
            width = 2560;
            height = 1440;
            return true;
        }

        public void Release(string deviceId, uint screenKey, TimeSpan grace)
        {
            lock (Calls) Calls.Add($"release {screenKey} {grace.TotalMilliseconds}");
        }

        public void Disconnected(string deviceId)
        {
            lock (Calls) Calls.Add($"disconnected {deviceId}");
        }
    }

    private static (HeadsetSession session, FakeConnection conn, PairingStore pairing, FakeScreens screens, FakeApprover approver) Create(bool approve)
    {
        var conn = new FakeConnection();
        var pairing = new PairingStore();
        var screens = new FakeScreens();
        var approver = new FakeApprover(approve);
        var session = new HeadsetSession(conn, pairing, () => { }, approver, new FakeMonitors(), screens, "CAYO-PC", () => "482913", _ => { });
        return (session, conn, pairing, screens, approver);
    }

    private static HeadsetSession Welcomed(out FakeConnection conn, out FakeScreens screens)
    {
        var (session, c, pairing, s, _) = Create(approve: false);
        session.OnMessage(new HelloMessage("dev-1", "Quest 3", pairing.Pair("dev-1", "Quest 3", DateTimeOffset.UnixEpoch)));
        conn = c;
        screens = s;
        return session;
    }

    [Fact]
    public void KnownDevice_IsWelcomedWithLeases_ThenAcquiresStreamsReleasesAndDisconnects()
    {
        var (session, conn, pairing, screens, _) = Create(approve: false);
        screens.LeasesToReport.Add(new ScreenLeaseInfo(7, ScreenKind.Virtual, 3840, 2160));
        session.OnMessage(new HelloMessage("dev-1", "Quest 3", pairing.Pair("dev-1", "Quest 3", DateTimeOffset.UnixEpoch)));
        Assert.Null(conn.Last<PairingRequiredMessage>());
        Assert.Equal(7u, conn.Last<WelcomeMessage>()!.Leases[0].ScreenKey);

        session.OnMessage(new AcquireScreenMessage(7, ScreenKind.Virtual, 3840, 2160, 0));
        session.OnMessage(new StartStreamMessage(7, 9000, 0));
        session.OnMessage(new ReleaseScreenMessage(7, 10000));
        session.Idle.Wait();
        Assert.Equal(2160u, conn.Last<ScreenReadyMessage>()!.Height);
        Assert.Equal(1440u, conn.Last<StreamStartedMessage>()!.Height);
        Assert.Equal("192.168.0.171", screens.LastAddress);
        Assert.Equal(new[] { "acquire 7", "start 7:9000", "release 7 10000" }, screens.Calls);

        session.OnMessage(new PingMessage(9));
        Assert.Equal(9UL, conn.Last<PongMessage>()!.SentUs);

        session.OnClosed();
        session.Idle.Wait();
        Assert.Equal("disconnected dev-1", screens.Calls[^1]);
    }

    [Fact]
    public void AcquireFailure_ReportsScreenFailedWithTheReason()
    {
        var session = Welcomed(out var conn, out var screens);
        screens.FailAcquire = true;
        session.OnMessage(new AcquireScreenMessage(3, ScreenKind.Virtual, 2560, 1440, 0));
        session.Idle.Wait();
        var failed = conn.Last<ScreenFailedMessage>()!;
        Assert.Equal(3u, failed.ScreenKey);
        Assert.Equal("sem driver", failed.Reason);
    }

    [Fact]
    public void ScreenMessages_BeforeWelcome_AreIgnored()
    {
        var (session, conn, _, screens, _) = Create(approve: false);
        session.OnMessage(new AcquireScreenMessage(1, ScreenKind.Virtual, 2560, 1440, 0));
        session.OnMessage(new StartStreamMessage(1, 9000, 0));
        session.Idle.Wait();
        Assert.Empty(screens.Calls);
        Assert.Null(conn.Last<ScreenReadyMessage>());
    }

    [Fact]
    public void SlowAcquire_DoesNotBlockPingOnTheReadThread()
    {
        var session = Welcomed(out var conn, out var screens);
        screens.BlockAcquire = new ManualResetEventSlim(false);
        session.OnMessage(new AcquireScreenMessage(1, ScreenKind.Virtual, 2560, 1440, 0)); // volta na hora
        session.OnMessage(new PingMessage(5));
        Assert.Equal(5UL, conn.Last<PongMessage>()!.SentUs);
        Assert.Null(conn.Last<ScreenReadyMessage>());
        screens.BlockAcquire.Set();
        session.Idle.Wait();
        Assert.NotNull(conn.Last<ScreenReadyMessage>());
    }
```

(`UnknownDevice_IsAskedToPair_ThenWelcomedWithToken` e `Rejected_SendsByeAndCloses` continuam; só descartam o 4º item da tupla com `_`. Apagar `KnownDevice_IsWelcomedDirectly_AndCanStream`, `StartStream_BeforeWelcome_IsIgnored` e `UnknownMonitor_ReportsStreamStopped`.)

`ControlServerIntegrationTests.cs`: trocar `new HeadsetSessionTests.FakeStreams()`/`streams` por `new HeadsetSessionTests.FakeScreens()`.

- [ ] **Step 2: Run to fail** — `dotnet test host/Host.Tests --filter HeadsetSessionTests` → FAIL (compilação).

- [ ] **Step 3: Implement**

`HostPorts.cs` — trocar `IStreamRegistry` por:

```csharp
public readonly record struct ScreenAcquireResult(bool Ok, int Width, int Height, string Error);

/// <summary>
/// Monitores e streams por headset (spec 2C §4, ADR 0009). Acquire/StartStream podem levar até ~3 s (criar monitor,
/// esperar a captura): a HeadsetSession chama fora da thread de leitura.
/// </summary>
public interface IScreenHost
{
    IReadOnlyList<ScreenLeaseInfo> Leases(string deviceId);
    ScreenAcquireResult Acquire(string deviceId, AcquireScreenMessage request);
    bool StartStream(string deviceId, uint screenKey, string address, ushort udpPort, ushort streamId, out int width, out int height);
    void Release(string deviceId, uint screenKey, TimeSpan grace);
    void Disconnected(string deviceId);
}
```

`HeadsetSession.cs` — conteúdo completo:

```csharp
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
    private volatile bool _welcomed;
    private readonly object _queueLock = new();
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
                _deviceId = hello.DeviceId;
                if (pairing.IsPaired(hello.DeviceId, hello.PairingToken)) SendWelcome();
                else BeginPairing(hello);
                break;
            case AcquireScreenMessage acquire:
                Enqueue(() => OnAcquire(acquire));
                break;
            case StartStreamMessage start:
                Enqueue(() => OnStartStream(start));
                break;
            case ReleaseScreenMessage release:
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
        _welcomed = true;
        connection.Send(new WelcomeMessage(hostName, monitors.GetMonitors(), screens.Leases(_deviceId!)));
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
        var result = screens.Acquire(_deviceId, request);
        connection.Send(result.Ok
            ? new ScreenReadyMessage(request.ScreenKey, (uint)result.Width, (uint)result.Height)
            : new ScreenFailedMessage(request.ScreenKey, result.Error));
    }

    private void OnStartStream(StartStreamMessage start)
    {
        if (!_welcomed || _deviceId == null) return;
        if (screens.StartStream(_deviceId, start.ScreenKey, connection.RemoteAddress, start.UdpPort, start.StreamId, out int w, out int h))
            connection.Send(new StreamStartedMessage(start.StreamId, (uint)w, (uint)h));
        else
            connection.Send(new StreamStoppedMessage(start.StreamId, "falha ao iniciar o stream"));
    }

    private void OnRelease(ReleaseScreenMessage release)
    {
        if (!_welcomed || _deviceId == null) return;
        screens.Release(_deviceId, release.ScreenKey, TimeSpan.FromMilliseconds(release.GraceMs));
    }

    public void OnClosed()
    {
        string? deviceId = _deviceId;
        if (deviceId != null) Enqueue(() => screens.Disconnected(deviceId));
    }
}
```

> O `Host.App` (Program.cs/StreamRegistry) deixa de compilar aqui e volta na Task 10; rode só `dotnet test host/Host.Tests` (que não referencia o Host.App).

- [ ] **Step 4: Run tests** — `dotnet test host/Host.Tests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add host/Host.Transport/Control host/Host.Tests/Control
git commit -m "host: headset session v2 (acquire/start/release on an ordered work queue off the read thread; welcome lists leases)"
```

---

### Task 10: `ScreenHost` (SudoVDA + WGC + encoder por tela) e `serve`

**Files:**
- Create: `host/Host.App/Control/ScreenHost.cs`
- Delete: `host/Host.App/Control/StreamRegistry.cs`
- Modify: `host/Host.App/Control/WindowsMonitorProvider.cs`, `host/Host.App/Program.cs`

**Interfaces:**
- Consumes: `IScreenHost`/`ScreenAcquireResult` (Task 9), `ScreenLeaseTable` (Task 8), `SudoVdaDriver`, `Win32Displays`, `WgcMonitorCapture.ForMonitor`, `StreamPipeline`, `EncoderSettings`, `ResolutionFit`.
- Produces: `ScreenHost(GpuDevice gpu, int bitrateKbps, TimeSpan disconnectGrace, Action<string> log) : IScreenHost, IDisposable` com `IMonitorProvider PhysicalMonitors`, `void RequestKeyframe(IPAddress address, ushort streamId)`, `string TakeStats(double seconds)`, `bool IsVirtualDisplay(string deviceName)`; `WindowsMonitorProvider(Func<string, bool>? isVirtual = null)`.

- [ ] **Step 1: `WindowsMonitorProvider`** — ids estáveis e sem os virtuais do app:

```csharp
/// <summary>
/// Monitores FÍSICOS do Windows em pixels físicos (o processo é Per-Monitor DPI Aware v2, app.manifest).
/// Id = número do \\.\DISPLAYn (estável entre conexões). Monitores virtuais do app ficam de fora (espelhar é só físico).
/// </summary>
public sealed class WindowsMonitorProvider(Func<string, bool>? isVirtual = null) : IMonitorProvider
{
    public IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        var result = new List<MonitorDescriptor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(monitor, ref info) && isVirtual?.Invoke(info.DeviceName) != true)
            {
                var r = info.Monitor;
                result.Add(new MonitorDescriptor(StableId(info.DeviceName, result.Count), (uint)(r.Right - r.Left), (uint)(r.Bottom - r.Top),
                    info.DeviceName, (info.Flags & MonitorInfoFPrimary) != 0));
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static uint StableId(string deviceName, int index)
    {
        int i = deviceName.Length;
        while (i > 0 && char.IsDigit(deviceName[i - 1])) i--;
        return i < deviceName.Length && uint.TryParse(deviceName.AsSpan(i), out uint n) ? n : (uint)(1000 + index);
    }

    // ... (structs e DllImports existentes, sem mudança)
}
```

- [ ] **Step 2: `ScreenHost.cs`**

```csharp
using System.Diagnostics;
using System.Net;
using ScreenPortal.App.Connection;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Host.App.Display;
using ScreenPortal.Host.Capture;
using ScreenPortal.Host.Domain.Screens;
using ScreenPortal.Host.Encode;
using ScreenPortal.Host.Transport.Control;

namespace ScreenPortal.Host.App.Control;

/// <summary>
/// Monitores e streams por headset (spec 2C §4, ADR 0009): aluguéis na ScreenLeaseTable, monitores SudoVDA com a
/// resolução exata, um StreamPipeline por tela. Prazos: o que o headset mandar ao remover (10 s) e
/// <c>disconnectGrace</c> ao cair. Se o host morrer, o watchdog do SudoVDA remove os monitores sozinho (sem ping).
/// </summary>
public sealed class ScreenHost : IScreenHost, IDisposable
{
    private static readonly TimeSpan CaptureSettle = TimeSpan.FromSeconds(3); // ADR 0007: o WGC recusa o HMONITOR por ~1 s

    private sealed class Live
    {
        public VirtualMonitor? Monitor;   // só Virtual
        public required string DisplayName;
        public StreamPipeline? Pipeline;
        public string Address = "";
        public ushort Port;
        public ushort StreamId;
        public int Width;
        public int Height;
    }

    private readonly GpuDevice _gpu;
    private readonly int _bitrateKbps;
    private readonly TimeSpan _disconnectGrace;
    private readonly Action<string> _log;
    private readonly ScreenLeaseTable _leases = new();
    private readonly Dictionary<(string, uint), Live> _live = new();
    private readonly object _lock = new();
    private readonly Timer _sweeper;
    private SudoVdaDriver? _driver;

    public IMonitorProvider PhysicalMonitors { get; }

    public ScreenHost(GpuDevice gpu, int bitrateKbps, TimeSpan disconnectGrace, Action<string> log)
    {
        _gpu = gpu;
        _bitrateKbps = bitrateKbps;
        _disconnectGrace = disconnectGrace;
        _log = log;
        PhysicalMonitors = new WindowsMonitorProvider(IsVirtualDisplay);
        _sweeper = new Timer(_ => Sweep(), null, 1000, 1000);
    }

    public IReadOnlyList<ScreenLeaseInfo> Leases(string deviceId)
    {
        lock (_lock)
            return _leases.ForDevice(deviceId)
                .Select(l => new ScreenLeaseInfo(l.ScreenKey, l.Kind == ScreenLeaseKind.Mirror ? ScreenKind.Mirror : ScreenKind.Virtual,
                    (uint)l.Width, (uint)l.Height))
                .ToList();
    }

    public ScreenAcquireResult Acquire(string deviceId, AcquireScreenMessage request)
    {
        lock (_lock)
        {
            if (_leases.TryGet(deviceId, request.ScreenKey, out var lease))
            {
                bool same = request.ScreenKind == ScreenKind.Mirror
                    ? lease.Kind == ScreenLeaseKind.Mirror && lease.PhysicalMonitorId == request.PhysicalMonitorId
                    : lease.Kind == ScreenLeaseKind.Virtual && lease.Width == request.Width && lease.Height == request.Height;
                if (same)
                {
                    _leases.Renew(deviceId, request.ScreenKey);
                    _log($"telas: {deviceId}/{request.ScreenKey} reatado ({lease.DisplayName})");
                    return new ScreenAcquireResult(true, lease.Width, lease.Height, "");
                }
                Destroy(deviceId, request.ScreenKey); // RecreateScreen: troca o monitor
            }
            return request.ScreenKind == ScreenKind.Mirror ? CreateMirror(deviceId, request) : CreateVirtual(deviceId, request);
        }
    }

    private ScreenAcquireResult CreateVirtual(string deviceId, AcquireScreenMessage request)
    {
        try
        {
            _driver ??= SudoVdaDriver.Open(_log);
        }
        catch (Exception e)
        {
            return new ScreenAcquireResult(false, 0, 0, "SudoVDA não disponível: " + e.Message);
        }
        try
        {
            var monitor = _driver.Create((int)request.Width, (int)request.Height, 60);
            string name = monitor.Display.DeviceName;
            _leases.Add(new ScreenLease(deviceId, request.ScreenKey, ScreenLeaseKind.Virtual, (int)request.Width, (int)request.Height, 0, name));
            _live[(deviceId, request.ScreenKey)] = new Live { Monitor = monitor, DisplayName = name };
            _log($"telas: monitor virtual {request.Width}×{request.Height} criado ({name}) para {deviceId}/{request.ScreenKey}");
            return new ScreenAcquireResult(true, (int)request.Width, (int)request.Height, "");
        }
        catch (Exception e)
        {
            return new ScreenAcquireResult(false, 0, 0, "Falha ao criar monitor: " + e.Message);
        }
    }

    private ScreenAcquireResult CreateMirror(string deviceId, AcquireScreenMessage request)
    {
        var monitor = PhysicalMonitors.GetMonitors().FirstOrDefault(m => m.Id == request.PhysicalMonitorId);
        if (monitor == null) return new ScreenAcquireResult(false, 0, 0, "Monitor físico não encontrado.");
        var fit = ResolutionFit.FromCapture((int)monitor.Width, (int)monitor.Height);
        _leases.Add(new ScreenLease(deviceId, request.ScreenKey, ScreenLeaseKind.Mirror, fit.Width, fit.Height, monitor.Id, monitor.Name));
        _live[(deviceId, request.ScreenKey)] = new Live { DisplayName = monitor.Name };
        _log($"telas: espelho de {monitor.Name} para {deviceId}/{request.ScreenKey}");
        return new ScreenAcquireResult(true, fit.Width, fit.Height, "");
    }

    public bool StartStream(string deviceId, uint screenKey, string address, ushort udpPort, ushort streamId, out int width, out int height)
    {
        width = height = 0;
        lock (_lock)
        {
            if (!_live.TryGetValue((deviceId, screenKey), out var live)) return false;
            if (live.Pipeline != null && live.Address == address && live.Port == udpPort && live.StreamId == streamId)
            {
                live.Pipeline.RequestKeyframe();
                width = live.Width;
                height = live.Height;
                return true;
            }
            live.Pipeline?.Dispose();
            live.Pipeline = null;
            try
            {
                var capture = CaptureWithRetry(live.DisplayName);
                var fit = ResolutionFit.FromCapture(capture.Width, capture.Height);
                var settings = new EncoderSettings(fit.Width, fit.Height, 60, _bitrateKbps);
                var pipeline = new StreamPipeline(_gpu, capture, settings, new IPEndPoint(IPAddress.Parse(address), udpPort), 1, null, streamId);
                pipeline.Start();
                live.Pipeline = pipeline;
                live.Address = address;
                live.Port = udpPort;
                live.StreamId = streamId;
                live.Width = width = fit.Width;
                live.Height = height = fit.Height;
                _log($"vídeo: {pipeline.Description} → {address}:{udpPort} (tela {screenKey})");
                return true;
            }
            catch (Exception e)
            {
                _log($"vídeo: falha ao iniciar a tela {screenKey} ({e.GetType().Name}: {e.Message})");
                return false;
            }
        }
    }

    /// <summary>Logo após criar um monitor, o WGC rejeita o HMONITOR por ~1 s (ADR 0007): resolve e tenta de novo.</summary>
    private WgcMonitorCapture CaptureWithRetry(string displayName)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return WgcMonitorCapture.ForMonitor(_gpu, Win32Displays.Current(displayName).HMonitor, captureCursor: true);
            }
            catch (Exception e) when ((e is ArgumentException || e is InvalidOperationException) && sw.Elapsed < CaptureSettle)
            {
                Thread.Sleep(100);
            }
        }
    }

    public void Release(string deviceId, uint screenKey, TimeSpan grace)
    {
        lock (_lock)
        {
            StopStream(deviceId, screenKey);
            _leases.Expire(deviceId, screenKey, DateTimeOffset.Now + grace);
        }
    }

    public void Disconnected(string deviceId)
    {
        lock (_lock)
        {
            foreach (var lease in _leases.ForDevice(deviceId)) StopStream(deviceId, lease.ScreenKey);
            _leases.ExpireAll(deviceId, DateTimeOffset.Now + _disconnectGrace);
            _log($"telas: {deviceId} desconectou; monitores mantidos por {_disconnectGrace.TotalMinutes:0.#} min");
        }
    }

    private void StopStream(string deviceId, uint screenKey)
    {
        if (!_live.TryGetValue((deviceId, screenKey), out var live) || live.Pipeline == null) return;
        live.Pipeline.Dispose(); // descarta também a captura
        live.Pipeline = null;
    }

    private void Sweep()
    {
        lock (_lock)
        {
            foreach (var lease in _leases.TakeExpired(DateTimeOffset.Now))
            {
                DisposeLive(lease.DeviceId, lease.ScreenKey);
                _log($"telas: {lease.DeviceId}/{lease.ScreenKey} expirou ({lease.DisplayName} {(lease.Kind == ScreenLeaseKind.Virtual ? "removido" : "stream encerrado")})");
            }
        }
    }

    private void Destroy(string deviceId, uint screenKey)
    {
        _leases.Remove(deviceId, screenKey);
        DisposeLive(deviceId, screenKey);
    }

    private void DisposeLive(string deviceId, uint screenKey)
    {
        if (!_live.Remove((deviceId, screenKey), out var live)) return;
        live.Pipeline?.Dispose();
        if (live.Monitor == null || _driver == null) return; // espelho: nada a remover no Windows
        try { _driver.Remove(live.Monitor); }
        catch (Exception e) { _log($"telas: falha ao remover {live.DisplayName}: {e.Message}"); }
    }

    /// <summary>Pedido de keyframe (KFRQ, UDP 9101) de um headset: força IDR no stream dele.</summary>
    public void RequestKeyframe(IPAddress address, ushort streamId)
    {
        lock (_lock)
        {
            foreach (var live in _live.Values)
            {
                if (live.Pipeline != null && live.StreamId == streamId && IPAddress.TryParse(live.Address, out var ip)
                    && ip.MapToIPv4().Equals(address))
                    live.Pipeline.RequestKeyframe();
            }
        }
    }

    public bool IsVirtualDisplay(string deviceName)
    {
        lock (_lock)
            return _live.Values.Any(l => l.Monitor != null && l.DisplayName.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
    }

    public string TakeStats(double seconds)
    {
        lock (_lock)
        {
            var streams = _live.Where(p => p.Value.Pipeline != null).ToList();
            if (streams.Count == 0) return $"sem stream ({_leases.Count} monitor(es) alugado(s))";
            return string.Join(" || ", streams.Select(p => $"{p.Key.Item2}: {p.Value.Pipeline!.TakeStats(seconds)}"));
        }
    }

    public void Dispose()
    {
        _sweeper.Dispose();
        lock (_lock)
        {
            foreach (var key in _live.Keys.ToList()) DisposeLive(key.Item1, key.Item2);
            _driver?.Dispose();
            _driver = null;
        }
    }
}
```

- [ ] **Step 3: `Program.cs` (`RunServe`)** — trocar `StreamRegistry`/`WindowsMonitorProvider`:

```csharp
    using var gpu = new GpuDevice();
    using var clock = new ClockSyncServer();
    using var screens = new ScreenPortal.Host.App.Control.ScreenHost(gpu, o.Int("bitrate", 20_000),
        TimeSpan.FromMinutes(o.Int("grace-minutes", 5)), Log);
    var approver = new ScreenPortal.Host.App.Control.MessageBoxPairingApprover();
    string hostName = Environment.MachineName;

    using var server = new ScreenPortal.Host.Transport.Control.ControlServer(
        ScreenPortal.Infrastructure.Protocol.ControlProtocol.Port,
        connection => new ScreenPortal.Host.Transport.Control.HeadsetSession(connection, pairing, SavePairing, approver,
            screens.PhysicalMonitors, screens, hostName,
            () => ScreenPortal.Host.Domain.Pairing.PairingStore.NewPairingCode(System.Security.Cryptography.RandomNumberGenerator.GetInt32), Log),
        Log);
    server.Start();
    using var keyframes = new KeyframeRequestListener(KeyframeRequestListener.DefaultPort, screens.RequestKeyframe);
```

e no loop: `if (server.ConnectionCount > 0) Log(screens.TakeStats(s));`. Atualizar o comentário de uso no topo:
`//   ScreenPortalHost [serve] [--bitrate 20000] [--grace-minutes 5]   (padrão) controle tcp/9200 + mDNS + pareamento + monitores/vídeo por tela`.

```bash
git rm host/Host.App/Control/StreamRegistry.cs
```

- [ ] **Step 4: Build and test**

Run: `dotnet build host/Host.sln` → 0 erros; `dotnet test host/Host.Tests` → PASS.

- [ ] **Step 5: Smoke (PC, sem headset)** — confere o driver fora do fluxo do app (o spike da 2A continua válido):

Run (PowerShell **como administrador**): `dotnet run --project host/Host.App -- display-spike --only 1 --out $env:TEMP\sp-smoke.md`
Expected: linha "1. Resolução exata" ✅ (o SudoVDA responde nesta máquina).

- [ ] **Step 6: Commit**

```bash
git add -A host/Host.App
git commit -m "host: ScreenHost (SudoVDA monitors on demand, mirrors, one pipeline per screen, lease expiry sweep); serve uses it"
```

---

## Parte C — Unity

### Task 11: Vídeo nativo por tela (`NativeScreenVideo`)

**Files:**
- Modify: `Assets/_Project/Infrastructure/Unity/NativeVideoJni.cs` (+ `TakeStats`)
- Create: `Assets/_Project/Infrastructure/Unity/NativeScreenVideo.cs`

**Interfaces:**
- Consumes: `IScreenVideo` (Task 5), `IScreenSurfaceProvider` (existente), `SpStatLine` (Task 7).
- Produces: `NativeScreenVideo(IScreenSurfaceProvider surfaces) : IScreenVideo`.

- [ ] **Step 1: `NativeVideoJni.cs`** — acrescentar:

```csharp
        private static IntPtr s_takeStats;
```

em `EnsureInitialized()`, depois de `s_stop`:

```csharp
            s_takeStats = AndroidJNI.GetStaticMethodID(s_class, "takeStats", "(J)Ljava/lang/String;");
```

e o método:

```csharp
        /// <summary>Linha "SPSTAT,..." do intervalo desde a última chamada (chamar ~1×/s; aloca uma string).</summary>
        public static string TakeStats(long handle)
        {
            if (handle == 0) return null;
            EnsureInitialized();
            s_args1[0].j = handle;
            return AndroidJNI.CallStaticStringMethod(s_class, s_takeStats, s_args1);
        }
```

- [ ] **Step 2: `NativeScreenVideo.cs`**

```csharp
using System;
using System.Collections.Generic;
using ScreenPortal.App.Connection;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Infrastructure.Video;
using UnityEngine;
using Resolution = ScreenPortal.Domain.Screens.Resolution;

namespace ScreenPortal.Infrastructure.Unity
{
    /// <summary>
    /// Um decoder nativo por tela, desenhando direto na Surface da compositor layer (o quadro nunca passa pelo C#).
    /// No Editor/Simulator não há decoder: TryOpen aceita e HasFirstFrame fica false (a tela mostra "Conectando vídeo…").
    /// </summary>
    public sealed class NativeScreenVideo : IScreenVideo
    {
        private sealed class Session
        {
            public long Handle;
            public bool FirstFrame;
        }

        private readonly IScreenSurfaceProvider _surfaces;
        private readonly Dictionary<ScreenId, Session> _sessions = new Dictionary<ScreenId, Session>();

        public NativeScreenVideo(IScreenSurfaceProvider surfaces) => _surfaces = surfaces;

        public bool TryOpen(ScreenId screen, ushort streamId, int udpPort, Resolution resolution)
        {
            if (_sessions.ContainsKey(screen)) return true;
            if (!NativeVideoJni.IsAvailable)
            {
                _sessions[screen] = new Session();
                return true;
            }
            if (!_surfaces.TryGetSurface(screen, out IntPtr surface) || surface == IntPtr.Zero) return false;
            long handle = NativeVideoJni.Start(streamId, udpPort, surface, resolution.Width, resolution.Height);
            if (handle == 0) return false;
            _sessions[screen] = new Session { Handle = handle };
            return true;
        }

        public void Close(ScreenId screen)
        {
            if (!_sessions.TryGetValue(screen, out var session)) return;
            if (session.Handle != 0) NativeVideoJni.Stop(session.Handle);
            _sessions.Remove(screen);
        }

        public bool HasFirstFrame(ScreenId screen)
        {
            if (!_sessions.TryGetValue(screen, out var session) || session.Handle == 0) return false;
            if (session.FirstFrame) return true;
            string line = NativeVideoJni.TakeStats(session.Handle); // ~1×/s: o HostSync controla a cadência
            if (line != null) Debug.Log(line);
            session.FirstFrame = SpStatLine.TryParseDecodedFps(line, out float fps) && fps > 0f;
            return session.FirstFrame;
        }
    }
}
```

- [ ] **Step 3: Compile in Unity**

Run: `unity --proxy-disable --no-banner command recompile` e depois `recompile_status` até `completed`; `console_status` sem `compilationFailed`.
Expected: compila (a composição ainda não usa a classe; ela entra na Task 13). Se o Editor estiver fechado: `powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command refresh`.

- [ ] **Step 4: Commit**

```bash
git add Assets/_Project/Infrastructure/Unity
git commit -m "infra: native video per screen (IScreenVideo over JNI; first frame from SPSTAT)"
```

---

### Task 12: `VideoScreenView` com estados e fábrica por origem

**Files:**
- Move+rewrite: `Assets/_Project/Presentation/Screens/OverlayScreenView.cs` → `VideoScreenView.cs` (`git mv` do `.cs` e do `.meta`)
- Modify: `Assets/_Project/Presentation/Screens/OverlayScreenViewFactory.cs`

**Interfaces:**
- Consumes: `IScreenLinks`, `ScreenLinkState` (Task 6), `ScreenSource` (Task 1), `StereoText` (2B).
- Produces: `VideoScreenView(Transform parent, VirtualScreen screen, Material unlit, Action<VideoScreenView> onDisposed)` com `OVROverlay Overlay`, `void SetLink(ScreenLinkState state, string detail)`; `OverlayScreenViewFactory.Bind(IScreenLinks links)`.

- [ ] **Step 1: Move**

```bash
git mv Assets/_Project/Presentation/Screens/OverlayScreenView.cs Assets/_Project/Presentation/Screens/VideoScreenView.cs
git mv Assets/_Project/Presentation/Screens/OverlayScreenView.cs.meta Assets/_Project/Presentation/Screens/VideoScreenView.cs.meta
```

- [ ] **Step 2: `VideoScreenView.cs`** — conteúdo completo:

```csharp
using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Screens;
using UnityEngine;
using Pose = ScreenPortal.Domain.Spatial.Pose; // UnityEngine.Pose também existe
using Resolution = ScreenPortal.Domain.Screens.Resolution; // UnityEngine.Resolution também existe

namespace ScreenPortal.Presentation.Screens
{
    /// <summary>
    /// Tela com vídeo do PC (monitor virtual ou espelho): compositor layer (OVROverlay quad, Surface externa na
    /// resolução exata, brief §6.3) + estados (spec 2C §5). Antes do primeiro quadro a layer fica escondida (hidden só
    /// pula a submissão: a Surface existe e o decoder já pode abrir) e um quad no eye buffer mostra o estado. Depois de ter
    /// tido vídeo, perder a conexão só escurece a layer.
    /// </summary>
    public sealed class VideoScreenView : IScreenView
    {
        private static readonly Vector4 Dimmed = new Vector4(0.35f, 0.35f, 0.35f, 1f);
        private static readonly Vector4 Normal = Vector4.one;

        private readonly GameObject _root;       // pose (sem escala)
        private readonly Transform _layer;       // escala = largura × altura
        private readonly Transform _statusQuad;  // escala = largura × altura
        private readonly TextMesh _statusText;
        private readonly Material _statusMaterial;
        private readonly Material _textMaterial;
        private readonly System.Action<VideoScreenView> _onDisposed;
        private Resolution _resolution;
        private bool _everLive;

        public OVROverlay Overlay { get; private set; }
        public ScreenId Id { get; }

        public VideoScreenView(Transform parent, VirtualScreen screen, Material unlit, System.Action<VideoScreenView> onDisposed = null)
        {
            Id = screen.Id;
            _onDisposed = onDisposed;
            _root = new GameObject("Screen " + screen.Id.Value);
            _root.transform.SetParent(parent, false);

            _layer = new GameObject("Layer").transform;
            _layer.SetParent(_root.transform, false);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Status";
            quad.GetComponent<Collider>().enabled = false;
            quad.transform.SetParent(_root.transform, false);
            _statusQuad = quad.transform;
            if (unlit != null)
            {
                _statusMaterial = new Material(unlit) { color = new Color(0.08f, 0.09f, 0.11f) };
                quad.GetComponent<MeshRenderer>().sharedMaterial = _statusMaterial;
            }

            var textGo = new GameObject("StatusText");
            textGo.transform.SetParent(_root.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, -0.003f);
            _statusText = textGo.AddComponent<TextMesh>();
            _statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _textMaterial = StereoText.MaterialFor(_statusText.font);
            textGo.GetComponent<MeshRenderer>().sharedMaterial = _textMaterial;
            _statusText.fontSize = 96;
            _statusText.anchor = TextAnchor.MiddleCenter;
            _statusText.alignment = TextAlignment.Center;
            _statusText.color = Color.white;

            BuildOverlay(screen.Resolution);
            SetLink(ScreenLinkState.Offline, string.Empty);
        }

        public void Apply(VirtualScreen screen, Pose pose)
        {
            // Resolução só muda via RecreateScreen (raro): recria a layer com a nova external surface.
            if (!screen.Resolution.Equals(_resolution)) BuildOverlay(screen.Resolution);
            ApplyTransform(pose, screen.WidthMeters, screen.HeightMeters);
            _root.name = string.IsNullOrEmpty(screen.Label) ? "Screen " + screen.Id.Value : screen.Label;
        }

        public void ApplyTransform(Pose pose, float widthMeters, float heightMeters)
        {
            _root.transform.SetLocalPositionAndRotation(pose.Position.ToUnity(), pose.Rotation.ToUnity());
            var size = new Vector3(widthMeters, heightMeters, 1f);
            _layer.localScale = size;
            _statusQuad.localScale = size;
            _statusText.characterSize = 0.006f * widthMeters;
        }

        public void SetLink(ScreenLinkState state, string detail)
        {
            if (state == ScreenLinkState.Live) _everLive = true;
            string text;
            switch (state)
            {
                case ScreenLinkState.Live: text = null; break;
                case ScreenLinkState.Creating: text = "Criando monitor…"; break;
                case ScreenLinkState.Connecting: text = "Conectando vídeo…"; break;
                case ScreenLinkState.Failed: text = "Falha:\n" + detail; break;
                default: text = "PC desconectado"; break;
            }
            // Depois de já ter tido vídeo, só a falha troca a imagem por texto; o resto escurece a última imagem.
            bool showText = text != null && (!_everLive || state == ScreenLinkState.Failed);
            _statusQuad.gameObject.SetActive(showText);
            _statusText.gameObject.SetActive(showText);
            if (showText) _statusText.text = text;
            if (Overlay == null) return;
            Overlay.hidden = showText;
            Overlay.SetPerLayerColorScaleAndOffset(state == ScreenLinkState.Live ? Normal : Dimmed, Vector4.zero);
        }

        private void BuildOverlay(Resolution resolution)
        {
            if (Overlay != null) Object.Destroy(Overlay);
            _resolution = resolution;

            var go = _layer.gameObject;
            go.SetActive(false); // configurar antes do OnEnable criar a layer
            var overlay = go.AddComponent<OVROverlay>();
            overlay.currentOverlayType = OVROverlay.OverlayType.Overlay;
            overlay.currentOverlayShape = OVROverlay.OverlayShape.Quad;
            overlay.isExternalSurface = true;
            overlay.externalSurfaceWidth = resolution.Width;
            overlay.externalSurfaceHeight = resolution.Height;
            overlay.useBicubicFiltering = true; // padrão validado no teste de texto (fase0-texto.md)
            overlay.overridePerLayerColorScaleAndOffset = true;
            overlay.hidden = true;
            Overlay = overlay;
            go.SetActive(true);
        }

        public void Dispose()
        {
            _onDisposed?.Invoke(this);
            if (_root != null) Object.Destroy(_root);
            if (_statusMaterial != null) Object.Destroy(_statusMaterial);
            if (_textMaterial != null) Object.Destroy(_textMaterial);
        }
    }
}
```

- [ ] **Step 3: `OverlayScreenViewFactory.cs`** — conteúdo completo:

```csharp
using System;
using System.Collections.Generic;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Menu;
using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Screens;
using UnityEngine;

namespace ScreenPortal.Presentation.Screens
{
    /// <summary>
    /// Cria as views pela origem da tela: vídeo do PC (virtual/espelho, OVROverlay) ou padrão de teste. Informa a Surface
    /// de cada layer ao decoder, repassa o estado do vídeo (IScreenLinks) e dá acesso às views para a pré-visualização.
    /// </summary>
    public sealed class OverlayScreenViewFactory : IScreenViewFactory, IScreenSurfaceProvider
    {
        private readonly Transform _root;
        private readonly Material _unlit;
        private readonly Dictionary<ScreenId, IScreenView> _views = new Dictionary<ScreenId, IScreenView>();
        private readonly Dictionary<ScreenId, VideoScreenView> _videos = new Dictionary<ScreenId, VideoScreenView>();
        private IScreenLinks _links;

        public OverlayScreenViewFactory(Transform root, Material unlit = null)
        {
            _root = root;
            _unlit = unlit;
        }

        /// <summary>Liga o estado do vídeo (HostSync). Chamado uma vez na partida (evita ciclo no container).</summary>
        public void Bind(IScreenLinks links)
        {
            if (_links != null) _links.LinkChanged -= OnLinkChanged;
            _links = links;
            if (_links == null) return;
            _links.LinkChanged += OnLinkChanged;
            foreach (var id in _videos.Keys) OnLinkChanged(id);
        }

        public IScreenView Create(VirtualScreen screen)
        {
            IScreenView view;
            if (TestScreens.IsTestScreen(screen))
            {
                view = new TestPatternScreenView(_root, screen, _unlit, v => _views.Remove(v.Id));
            }
            else
            {
                var video = new VideoScreenView(_root, screen, _unlit, v =>
                {
                    _views.Remove(v.Id);
                    _videos.Remove(v.Id);
                });
                _videos[screen.Id] = video;
                view = video;
                OnLinkChanged(screen.Id);
            }
            _views[screen.Id] = view;
            return view;
        }

        private void OnLinkChanged(ScreenId id)
        {
            if (_links == null || !_videos.TryGetValue(id, out var view)) return;
            if (_links.TryGetLink(id, out var state, out var detail)) view.SetLink(state, detail);
        }

        public bool TryGetView(ScreenId screen, out IScreenView view) => _views.TryGetValue(screen, out view);

        public bool TryGetSurface(ScreenId screen, out IntPtr surface)
        {
            surface = IntPtr.Zero;
            if (!_videos.TryGetValue(screen, out var view) || view.Overlay == null) return false;
            surface = view.Overlay.externalSurfaceObject;
            return surface != IntPtr.Zero;
        }
    }
}
```

> Observação: `Create` é chamado pelo `ScreenViewSynchronizer` dentro do `StateChanged`; o `HostSync` também reage ao mesmo evento. A ordem entre os dois não importa: `OnLinkChanged` só aplica se já houver link (`TryGetLink`), e o `HostSync` levanta `LinkChanged` na criação da entrada.

- [ ] **Step 4: Compile in Unity** — `recompile` + `recompile_status` → sem erros. (Uso de `OverlayScreenView` em outros arquivos: `grep -rn OverlayScreenView Assets/_Project --include=*.cs` deve listar só a fábrica.)

- [ ] **Step 5: Commit**

```bash
git add -A Assets/_Project/Presentation/Screens
git commit -m "presentation: video screen view with link states (creating, connecting, live, failed, offline dim); factory by source"
```

---

### Task 13: Composição, pareamento persistente no Editor e testes da Unity

**Files:**
- Modify: `Assets/_Project/Composition/AppEnvironment.cs`, `AppInstaller.cs`, `AppEntryPoint.cs`, `AppLifetimeScope.cs`
- Modify: `Assets/_Project/Tests/Composition/CompositionTests.cs`

**Interfaces:**
- Consumes: tudo das Tasks 3, 5, 6, 11, 12.
- Produces: `AppEnvironment(..., Material unlitMaterial = null, bool showTestScreens = false)` com `ShowTestScreens`.

- [ ] **Step 1: `AppEnvironment.cs`** — propriedade e parâmetro (último, opcional):

```csharp
        /// <summary>Build de desenvolvimento: o menu mostra "+ Tela de teste".</summary>
        public bool ShowTestScreens { get; }
```

```csharp
            IRecenterEvents recenter = null, IWristMenuPanel menuPanel = null, Material unlitMaterial = null,
            bool showTestScreens = false)
        {
            // ... atribuições existentes
            ShowTestScreens = showTestScreens;
        }
```

- [ ] **Step 2: `AppInstaller.cs`** — trocar a fábrica, a máquina de conexão e o menu; acrescentar vídeo e `HostSync`:

```csharp
            var views = new OverlayScreenViewFactory(env.ScreensRoot, env.UnlitMaterial);
            builder.RegisterInstance(views).AsSelf().As<IScreenViewFactory>().As<IScreenSurfaceProvider>();
            builder.Register<ScreenViewSynchronizer>(Lifetime.Singleton);

            builder.RegisterInstance(new MonotonicClock()).As<IClock>();
            builder.RegisterInstance(new TcpControlChannel(2000)).As<IControlChannel>();
            builder.RegisterInstance(env.Discovery).As<IHostDiscovery>();
            builder.RegisterInstance(env.Pairing).As<IPairingStore>();
            builder.Register<ConnectionStateMachine>(Lifetime.Singleton);

            // 2C: telas com vídeo sob comando do headset (spec 2026-09-30-fase2c-varias-telas-design.md)
            builder.Register<NativeScreenVideo>(Lifetime.Singleton).As<IScreenVideo>();
            builder.Register<HostSync>(Lifetime.Singleton).AsSelf().As<IHostCatalog>().As<IScreenLinks>();
```

e o menu:

```csharp
            builder.Register(r => new WristMenu(r.Resolve<WorkspaceStore>(), r.Resolve<ICommandBus>(), r.Resolve<IHeadPoseSource>(),
                r.Resolve<IHostCatalog>(), env.ShowTestScreens), Lifetime.Singleton);
```

(no lugar de `builder.Register<WristMenu>(Lifetime.Singleton);`). Acrescentar `using ScreenPortal.App.Screens;` se faltar.

- [ ] **Step 3: `AppEntryPoint.cs`** — injetar `HostSync` e a fábrica; ordem de partida e Tick:

```csharp
        private readonly HostSync _hostSync;
        private readonly OverlayScreenViewFactory _views;

        public AppEntryPoint(ScreenViewSynchronizer synchronizer, ConnectionStateMachine connection,
            IControlChannel channel, IHostDiscovery discovery, AppEnvironment env,
            InteractionRouter router, ManipulationPresenter presenter, WristMenu menu,
            HostSync hostSync, OverlayScreenViewFactory views)
        {
            // ... atribuições existentes
            _hostSync = hostSync;
            _views = views;
        }

        public void Start()
        {
            _views.Bind(_hostSync);
            _synchronizer.Start();
            _hostSync.Start();
            if (_env.StatusView != null) _env.StatusView.Bind(_connection);
            if (_env.Recenter != null) _env.Recenter.Recentered += OnSystemRecentered;
            _connection.Start();
        }

        public void Tick()
        {
            _connection.Tick();
            _hostSync.Tick();
            _router.Tick();
            _presenter.Tick();
        }
```

e em `Dispose()`, antes de `_channel.Close();`: `_hostSync.Dispose();` e `_views.Bind(null);`. Atualizar o `<summary>` da classe: "Liga views, status, conexão, telas com vídeo (HostSync) e entrada/menu/manipulação."

- [ ] **Step 4: `AppLifetimeScope.cs`** — pareamento persistente também no Editor (desvio 7) e flag das telas de teste:

```csharp
            IPairingStore pairing = new PlayerPrefsPairingStore(); // Editor também: pareia uma vez (verificação com o host real)
```

(no lugar do ternário com `InMemoryPairingStore`) e no `AppInstaller.Install(...)` acrescentar o último argumento `Debug.isDebugBuild`:

```csharp
            AppInstaller.Install(builder, new AppEnvironment(
                screensRoot != null ? screensRoot : transform, statusView, discovery, pairing, options,
                right, left, head, new OvrRecenterEvents(), menuPanel, unlitMaterial, Debug.isDebugBuild));
```

- [ ] **Step 5: `CompositionTests.cs`** — acrescentar no `using` do container:

```csharp
                Assert.That(container.Resolve<ScreenPortal.App.Screens.HostSync>(), Is.Not.Null);
                Assert.That(container.Resolve<ScreenPortal.App.Menu.WristMenu>(), Is.Not.Null);
```

- [ ] **Step 6: Unity: compilar, gerar `.meta` e rodar os testes**

Run: `unity --proxy-disable --no-banner command recompile` → `recompile_status` = `completed`;
`unity --proxy-disable --no-banner --format json command run_tests --mode EditMode --timeout 600`
Expected: `"Failed":0` (≈ 169 + os novos). Harness: `dotnet test tests-dotnet/UnityPureCode.Tests` e `dotnet test host/Host.Tests` → PASS.
`git status` deve listar os `.meta` novos (ScreenSource, ScreenSizing, ResolutionCatalog, ScreenLinks, HostSync, SpStatLine + pasta `Video`, NativeScreenVideo, testes novos + pasta `Screens`).

- [ ] **Step 7: Commit**

```bash
git add -A Assets/_Project
git commit -m "composition: wire HostSync, native video per screen, paged menu; editor pairs once; Unity .meta files"
```

---

## Parte D — Verificação

### Task 14: Meta XR Simulator + host real (automatizada)

**Files:**
- Create: `docs/measurements/fase2c-simulator.md`

- [ ] **Step 1: Preparar**
  - PC: `dotnet run --project host/Host.App -- serve` num PowerShell **como administrador** (SudoVDA). Deixar o console visível (log das telas).
  - Carregar as skills `meta-xr-operator-unity-cli-workflow` e `meta-xr-operator-hand-tracking`; Play Mode na cena `Main` com o Simulator (setup e armadilhas em `docs/measurements/fase2b-simulator.md` §3 "Preparação": loader OpenXR no Standalone, `adb forward --remove-all` se a porta 8720 estiver presa, `editor_focus` + `set_autotick`, raio = pose de **grip** com +60°).
  - Primeira vez: o PC mostra o pedido de pareamento — **pedir ao Cayo para clicar em Permitir** (depois fica salvo, desvio 7).

- [ ] **Step 2: Roteiro** (um item por linha no relatório: ✅/❌ + observação; dados pelo `eval` no `WorkspaceStore`/`HostSync` e pelo log do host; visual pelas capturas)

| # | Ação | Esperado |
|---|---|---|
| 1 | Conectar com a workspace vazia | espelho do monitor principal criado sozinho ("DISPLAY1"), estado Creating → Connecting (sem vídeo no Editor); log do host "espelho de \\.\DISPLAY1" |
| 2 | Menu (≡) → "+ Monitor virtual" | página de proporções: "Orientação: Paisagem" + 6 frames com o nome no meio + "← Voltar" |
| 3 | "Orientação" | frames giram (16:9 vira 9:16 em pé) |
| 4 | Paisagem → 16:9 → 2560×1440 | tela nova (largura 1,0 m); no PC um monitor 2560×1440 aparece em Configurações de vídeo; log "monitor virtual 2560×1440 criado" |
| 5 | Retrato → 16:9 → 1440×2560 | monitor em pé no Windows (sem rotação) |
| 6 | "+ Espelhar monitor" | lista só monitores físicos (os virtuais do app não aparecem) |
| 7 | Criar até a 5ª tela com vídeo | aviso "Mais de 4 telas…" na linha de status; a tela é criada |
| 8 | Abrir o Bloco de Notas e movê-lo para o monitor virtual da tela 4 (snippet abaixo); "Remover tela" nela e **X em < 10 s** | a tela volta; a janela continua no mesmo monitor (log "reatado") |
| 9 | Remover de novo e esperar 12 s | log "expirou … removido"; o Bloco de Notas volta para um monitor físico |
| 10 | Parar o Play Mode, esperar 10 s, Play de novo | Welcome traz os aluguéis; as telas não existem mais na workspace (Editor não guarda layout) → o headset libera todos (log de expiração 10 s depois) |
| 11 | Com telas criadas, fechar a conexão (parar o host com Ctrl+C) | as telas mostram "PC desconectado"; o watchdog do SudoVDA remove os monitores em poucos segundos (Configurações de vídeo) — **confirma o desvio 2** |
| 12 | Religar o host com telas na workspace | reconexão < 3 s; cada tela pede de novo e o host recria os monitores |

Snippet para mover uma janela (PowerShell no PC):

```powershell
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W { [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool r); }
"@
$p = Start-Process notepad -PassThru; Start-Sleep 1
Add-Type -AssemblyName System.Windows.Forms
$s = [System.Windows.Forms.Screen]::AllScreens | Where-Object { $_.DeviceName -eq '\\.\DISPLAY5' }   # o do log do host
[W]::MoveWindow($p.MainWindowHandle, $s.Bounds.X + 100, $s.Bounds.Y + 100, 800, 600, $true)
```

- [ ] **Step 3: Relatório e correções**

Escrever `docs/measurements/fase2c-simulator.md` (data, commit, tabela, capturas relevantes em `docs/measurements/fase2c-simulator/`). Falha com lógica pura vira teste antes da correção. Máximo de 3 tentativas por falha (skill `meta-xr-operator-unity-test-mechanics`); se persistir, parar e reportar ao Cayo.

- [ ] **Step 4: Commit**

```bash
git add docs/measurements/fase2c-simulator.md docs/measurements/fase2c-simulator Assets/_Project
git commit -m "docs: Phase 2C Simulator verification with the real host"
```

---

### Task 15: Quest e fechamento

**Files:**
- Modify: `docs/measurements/fase2c-simulator.md` (seção "Quest"), `docs/specs/2026-09-30-fase2c-varias-telas-design.md` (seção "Desvios na implementação"), `docs/STATUS.md`

- [ ] **Step 1: Build e instalação**

Por que o Simulator não basta: decoder/MediaCodec, 4K, latência real e compositor layers só são conclusivos no Quest.

```powershell
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command boot-main
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command build-apk -TimeoutMinutes 40
powershell -ExecutionPolicy Bypass -File tools/quest.ps1 -Install Builds/Phase0.apk -Launch
```

Host: `dotnet run --project host/Host.App -- serve` (admin).

- [ ] **Step 2: Roteiro no Quest (Cayo, ~15 min)**

| # | Ação | Anotar |
|---|---|---|
| 1 | Conectar | espelho automático com vídeo; texto de estado nos dois olhos |
| 2 | 4 telas com vídeo, **uma 3840×2160** | fps e quedas no log do host (`TakeStats`) e no `SPSTAT` (adb logcat -s Unity); nitidez do texto por resolução |
| 3 | 5ª tela | aviso; a partir de quando perde quadros |
| 4 | Espelho de um monitor físico | imagem correta |
| 5 | Remover + X em < 10 s | janela no lugar |
| 6 | Tirar o óculos até dormir, voltar < 5 min | tudo reaparece com as janelas no lugar |
| 7 | Alças dos cantos sob a layer | incomodam? (senão, fica como está) |

Latência por stream: `adb logcat -s Unity | Select-String SPSTAT` (campo 8 = média captura→decoder em ms).

- [ ] **Step 3: Spec, STATUS e desvios**

Na spec 2C, acrescentar a seção "7. Desvios na implementação" com os 7 itens de "Desvios da spec" deste plano (e o que o Quest mudar). No STATUS: linha 2C → ✅ com links para a spec, este plano, o relatório e o ADR 0009; linha 2D → "Contorno fantasma + recriar + resolução custom (o seletor veio na 2C)"; "Fase atual" → próximas 2D/2E.

- [ ] **Step 4: Commit**

```bash
git add docs
git commit -m "docs: Phase 2C done (Quest check, spec deviations, STATUS)"
```

Depois: skill `superpowers:finishing-a-development-branch` (merge na `main` com os testes verdes).
