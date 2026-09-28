# Fase 1A — Esqueleto Unity + Domínio + CommandBus/WorkspaceStore — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Criar a base definitiva do app no headset: camadas (asmdefs) com dependência unidirecional, domínio puro da workspace (telas, comandos, invariantes), `WorkspaceStore` com undo/redo, `CommandBus`, sincronização estado→views e composição com VContainer numa cena `Main`.

**Architecture:** Clean/Hexagonal (brief §8.1). `Domain` e `Application` são C# puro (`noEngineReferences: true`) e os testes NUnit deles compilam **também fora da Unity** via `dotnet test` (loop de TDD rápido, sem Editor). Estado imutável: cada Command é uma função pura `Workspace → Workspace`; o store guarda snapshots para undo/redo (Memento). `Presentation` observa o store por um sincronizador (Observer) e cria views `OVROverlay`; `Composition` monta tudo com VContainer.

**Tech Stack:** Unity 6000.3.19f1 (C# 9, .NET Standard 2.1), Meta XR Core SDK 207 (`OVROverlay`), VContainer 1.19.0 (UPM via git), Unity Test Framework 1.6.0 (NUnit 3.5), .NET 10 SDK + NUnit 3.14 para o harness `dotnet test`.

## Global Constraints

- Projeto Unity na **raiz** do repositório (ADR 0000): o código vai em `Assets/_Project/...`.
- **Domain não referencia UnityEngine**, nem APIs Windows/Android (CLAUDE.md). Application também é pura nesta fase.
- Asmdefs unidirecionais: `Presentation/Infrastructure → Application → Domain`; `Composition` referencia todos.
- Toda mudança de estado da workspace é um **Command** via `CommandBus`. Views nunca mutam estado.
- **`ResizeScreen` nunca altera a resolução**; curvatura nunca altera `Resolution`, `Orientation` nem `WidthMeters` (brief §10.1, §10.2).
- Resolução: múltiplos de 8, mínimo 640×480 (ou 480×640), máximo **4096 px por dimensão** no MVP H.264 (brief §10.3, D5).
- `FrameRateMode.Fixed` aceita **15 / 30 / 60 / 90** fps (brief §8.8).
- Curvatura 0..1; preset "Média" = **0,6** é o valor inicial do toggle (brief §10.2).
- Sem singletons/`FindObjectOfType`; DI via VContainer.
- Código de domínio sempre com teste. Código dos testes de Domain/Application usa **só NUnit** (sem UnityEngine), para rodar nos dois runners.
- C# **9** no código Unity (`LangVersion 9.0` no harness dotnet garante isso): nada de `record`, `init`, file-scoped namespace.
- Nunca usar `unity command eval`/MCP `RunCommand` na `main`. Automação do Editor pelo gatilho por arquivo (`tools/unity-cmd.ps1`).
- Commits pequenos, um propósito cada, na branch `phase1/skeleton`.
- A camada Application usa o asmdef `ScreenPortal.Application` mas o **namespace `ScreenPortal.App`**: um namespace `ScreenPortal.Application` sombrearia `UnityEngine.Application` em todo código `ScreenPortal.*`.

---

## File Structure

```
Assets/_Project/
├── Domain/                                   (asmdef ScreenPortal.Domain, noEngineReferences)
│   ├── ScreenPortal.Domain.asmdef
│   ├── Spatial/Vec3.cs · Quat.cs · Pose.cs           tipos espaciais sem UnityEngine
│   ├── Screens/ScreenId.cs · Orientation.cs · Resolution.cs · FrameRateMode.cs · ScreenLimits.cs · VirtualScreen.cs
│   ├── Workspace.cs                                   estado imutável da workspace
│   └── Commands/ICommand.cs · CommandResult.cs · ScreenCommand.cs · CreateScreen.cs · RemoveScreen.cs ·
│       MoveScreen.cs · ResizeScreen.cs · SetScreenCurvature.cs · ToggleScreenCurvature.cs ·
│       SetFrameRateMode.cs · RecreateScreen.cs · SetFocus.cs
├── Application/                              (asmdef ScreenPortal.Application, noEngineReferences)
│   ├── ScreenPortal.Application.asmdef
│   ├── WorkspaceStore.cs                              fonte única da verdade + undo/redo
│   ├── CommandBus.cs · ICommandBus.cs · ICommandObserver.cs · CommandJournal.cs
│   └── Screens/IScreenView.cs · IScreenViewFactory.cs · ScreenViewSynchronizer.cs
├── Presentation/                             (asmdef ScreenPortal.Presentation → Domain, Application, Oculus.VR)
│   ├── ScreenPortal.Presentation.asmdef
│   ├── DomainConversions.cs                           Vec3/Quat ↔ Vector3/Quaternion
│   └── Screens/OverlayScreenView.cs · OverlayScreenViewFactory.cs
├── Composition/                              (asmdef ScreenPortal.Composition → todos + VContainer)
│   ├── ScreenPortal.Composition.asmdef
│   ├── AppOptions.cs · AppLifetimeScope.cs · AppEntryPoint.cs
├── Editor/                                   (asmdef ScreenPortal.Editor, Editor only)
│   ├── ScreenPortal.Editor.asmdef
│   ├── EditorCommands.cs · FileCommandWatcher.cs (movido do spike) · TestRunnerCommand.cs
│   ├── BuildScenes.cs · MainSceneBuilder.cs
├── Scenes/Main.unity                                  (gerada pelo MainSceneBuilder)
└── Tests/EditMode/                           (asmdef ScreenPortal.Tests.EditMode)
    ├── ScreenPortal.Tests.EditMode.asmdef
    ├── SpatialAndIdTests.cs · ResolutionTests.cs · FrameRateModeTests.cs · VirtualScreenTests.cs ·
    ├── WorkspaceTests.cs · CommandTests.cs · WorkspaceStoreTests.cs · CommandBusTests.cs ·
    └── ScreenViewSynchronizerTests.cs
tests-dotnet/UnityPureCode.Tests/UnityPureCode.Tests.csproj   compila Domain + Application + Tests/EditMode fora da Unity
Assets/_Project/Spikes/Phase0/Editor/…                        passa a registrar comandos no EditorCommands
Packages/manifest.json                                         + jp.hadashikick.vcontainer
docs/adr/0004-…md · CLAUDE.md · docs/STATUS.md
```

---

### Task 1: Harness de testes puro + asmdefs Domain/Application + tipos espaciais

**Files:**
- Create: `tests-dotnet/UnityPureCode.Tests/UnityPureCode.Tests.csproj`
- Create: `Assets/_Project/Domain/ScreenPortal.Domain.asmdef`
- Create: `Assets/_Project/Application/ScreenPortal.Application.asmdef`
- Create: `Assets/_Project/Tests/EditMode/ScreenPortal.Tests.EditMode.asmdef`
- Create: `Assets/_Project/Domain/Spatial/Vec3.cs`, `Quat.cs`, `Pose.cs`
- Create: `Assets/_Project/Domain/Screens/ScreenId.cs`
- Test: `Assets/_Project/Tests/EditMode/SpatialAndIdTests.cs`

**Interfaces:**
- Produces: `ScreenPortal.Domain.Spatial.Vec3(float x, float y, float z)` com `X/Y/Z`, `Vec3.Zero`; `Quat(float x, float y, float z, float w)` com `Quat.Identity`; `Pose(Vec3 position, Quat rotation)` com `Position`, `Rotation`, `Pose.Identity`, `Pose.At(Vec3)`; `ScreenPortal.Domain.Screens.ScreenId(int value)` com `Value`, `ScreenId.None`, `IsNone`, `==`/`!=`.

- [ ] **Step 1: Criar o harness dotnet**

`tests-dotnet/UnityPureCode.Tests/UnityPureCode.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- Compila o código PURO da Unity (Domain, Application) e os testes EditMode fora do Editor.
       LangVersion 9.0 = a mesma da Unity 6: impede usar recursos que a Unity não compila. -->
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <RootNamespace>ScreenPortal.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../Assets/_Project/Domain/**/*.cs" />
    <Compile Include="../../Assets/_Project/Application/**/*.cs" />
    <Compile Include="../../Assets/_Project/Tests/EditMode/**/*.cs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="NUnit" Version="3.14.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 1b: Ignorar os artefatos do harness**

Acrescentar ao final de `.gitignore`:
```gitignore
# ---------- Harness dotnet dos testes puros da Unity ----------
tests-dotnet/**/bin/
tests-dotnet/**/obj/
```

- [ ] **Step 2: Criar os asmdefs**

`Assets/_Project/Domain/ScreenPortal.Domain.asmdef`:
```json
{
    "name": "ScreenPortal.Domain",
    "rootNamespace": "ScreenPortal.Domain",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": false,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}
```

`Assets/_Project/Application/ScreenPortal.Application.asmdef`:
```json
{
    "name": "ScreenPortal.Application",
    "rootNamespace": "ScreenPortal.App",
    "references": ["ScreenPortal.Domain"],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": false,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}
```

`Assets/_Project/Tests/EditMode/ScreenPortal.Tests.EditMode.asmdef`:
```json
{
    "name": "ScreenPortal.Tests.EditMode",
    "rootNamespace": "ScreenPortal.Tests",
    "references": [
        "ScreenPortal.Domain",
        "ScreenPortal.Application",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 3: Escrever o teste que falha**

`Assets/_Project/Tests/EditMode/SpatialAndIdTests.cs`:
```csharp
using NUnit.Framework;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class SpatialAndIdTests
    {
        [Test]
        public void Vec3_EqualityIsByValue()
        {
            Assert.That(new Vec3(1f, 2f, 3f), Is.EqualTo(new Vec3(1f, 2f, 3f)));
            Assert.That(new Vec3(1f, 2f, 3f), Is.Not.EqualTo(Vec3.Zero));
        }

        [Test]
        public void Pose_At_UsesIdentityRotation()
        {
            var pose = Pose.At(new Vec3(0f, 1.2f, 1.3f));
            Assert.That(pose.Position, Is.EqualTo(new Vec3(0f, 1.2f, 1.3f)));
            Assert.That(pose.Rotation, Is.EqualTo(Quat.Identity));
        }

        [Test]
        public void ScreenId_DefaultIsNone()
        {
            Assert.That(default(ScreenId).IsNone, Is.True);
            Assert.That(ScreenId.None.IsNone, Is.True);
            Assert.That(new ScreenId(3).IsNone, Is.False);
            Assert.That(new ScreenId(3) == new ScreenId(3), Is.True);
            Assert.That(new ScreenId(3) != new ScreenId(4), Is.True);
        }
    }
}
```

- [ ] **Step 4: Rodar e ver falhar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: FAIL de compilação — `The type or namespace name 'Spatial' does not exist in the namespace 'ScreenPortal.Domain'`.

- [ ] **Step 5: Implementar os tipos**

`Assets/_Project/Domain/Spatial/Vec3.cs`:
```csharp
using System;

namespace ScreenPortal.Domain.Spatial
{
    /// <summary>Vetor 3D do domínio (metros, convenção da Unity: +Y para cima, +Z para frente). Sem UnityEngine.</summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);

        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is Vec3 other && Equals(other);
        public override int GetHashCode() => (X, Y, Z).GetHashCode();
        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }
}
```

`Assets/_Project/Domain/Spatial/Quat.cs`:
```csharp
using System;

namespace ScreenPortal.Domain.Spatial
{
    /// <summary>Rotação (quaternion unitário, mesma convenção da Unity). Sem UnityEngine.</summary>
    public readonly struct Quat : IEquatable<Quat>
    {
        public static readonly Quat Identity = new Quat(0f, 0f, 0f, 1f);

        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public readonly float W;

        public Quat(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public bool Equals(Quat other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
        public override bool Equals(object obj) => obj is Quat other && Equals(other);
        public override int GetHashCode() => (X, Y, Z, W).GetHashCode();
        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###}, {W:0.###})";
    }
}
```

`Assets/_Project/Domain/Spatial/Pose.cs`:
```csharp
using System;

namespace ScreenPortal.Domain.Spatial
{
    /// <summary>Posição + rotação no espaço de tracking (origem no chão, brief: FloorLevel).</summary>
    public readonly struct Pose : IEquatable<Pose>
    {
        public static readonly Pose Identity = new Pose(Vec3.Zero, Quat.Identity);

        public readonly Vec3 Position;
        public readonly Quat Rotation;

        public Pose(Vec3 position, Quat rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public static Pose At(Vec3 position) => new Pose(position, Quat.Identity);

        public bool Equals(Pose other) => Position.Equals(other.Position) && Rotation.Equals(other.Rotation);
        public override bool Equals(object obj) => obj is Pose other && Equals(other);
        public override int GetHashCode() => (Position, Rotation).GetHashCode();
        public override string ToString() => $"Pose{Position}{Rotation}";
    }
}
```

`Assets/_Project/Domain/Screens/ScreenId.cs`:
```csharp
using System;

namespace ScreenPortal.Domain.Screens
{
    /// <summary>Identidade de uma tela na workspace. 0 = nenhuma.</summary>
    public readonly struct ScreenId : IEquatable<ScreenId>
    {
        public static readonly ScreenId None = default;

        public readonly int Value;

        public ScreenId(int value) => Value = value;

        public bool IsNone => Value == 0;

        public bool Equals(ScreenId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ScreenId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => IsNone ? "screen#none" : "screen#" + Value;

        public static bool operator ==(ScreenId a, ScreenId b) => a.Value == b.Value;
        public static bool operator !=(ScreenId a, ScreenId b) => a.Value != b.Value;
    }
}
```

- [ ] **Step 6: Rodar e ver passar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: `Aprovado! – Com falha: 0, Aprovado: 3` (ou `Passed! - Failed: 0, Passed: 3`).

- [ ] **Step 7: Commit**

```bash
git add .gitignore tests-dotnet Assets/_Project/Domain Assets/_Project/Application Assets/_Project/Tests
git commit -m "phase1: pure-code test harness, Domain/Application asmdefs, spatial types and ScreenId"
```

---

### Task 2: Resolution, Orientation, FrameRateMode, ScreenLimits

**Files:**
- Create: `Assets/_Project/Domain/Screens/Orientation.cs`, `Resolution.cs`, `FrameRateMode.cs`, `ScreenLimits.cs`
- Test: `Assets/_Project/Tests/EditMode/ResolutionTests.cs`, `FrameRateModeTests.cs`

**Interfaces:**
- Consumes: nada além de `System`.
- Produces: `enum Orientation { Landscape, Portrait }`; `Resolution` com `Width`, `Height`, `Orientation`, `AspectRatio`, `IsValid`, `static bool TryCreate(int width, int height, out Resolution resolution, out string error)`, `static Resolution Create(int width, int height)`, `Resolution Rotated()`, constantes `Alignment=8`, `MinLongSide=640`, `MinShortSide=480`, `MaxDimension=4096`; `enum FrameRateKind { Auto, Fixed, Video, Eco }`; `FrameRateMode` com `Kind`, `FixedFps`, `Auto`, `Video`, `Eco`, `static FrameRateMode Fixed(int fps)`; `static class ScreenLimits` com `MinWidthMeters=0.2f`, `MaxWidthMeters=6f`, `DefaultCurvature=0.6f`, `float ClampWidth(float)`, `float Clamp01(float)`.

- [ ] **Step 1: Escrever os testes que falham**

`Assets/_Project/Tests/EditMode/ResolutionTests.cs`:
```csharp
using NUnit.Framework;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Tests
{
    public class ResolutionTests
    {
        [TestCase(2560, 1080, Orientation.Landscape)]
        [TestCase(1440, 2560, Orientation.Portrait)]
        [TestCase(1440, 1440, Orientation.Landscape)]
        [TestCase(3840, 2160, Orientation.Landscape)]
        [TestCase(640, 480, Orientation.Landscape)]
        [TestCase(480, 640, Orientation.Portrait)]
        public void ValidResolutions_AreAccepted(int w, int h, Orientation expected)
        {
            Assert.That(Resolution.TryCreate(w, h, out var r, out var error), Is.True, error);
            Assert.That(r.Width, Is.EqualTo(w));
            Assert.That(r.Height, Is.EqualTo(h));
            Assert.That(r.Orientation, Is.EqualTo(expected));
            Assert.That(r.IsValid, Is.True);
        }

        [TestCase(1366, 768, "múltiplos de 8")]
        [TestCase(320, 240, "mínimo")]
        [TestCase(480, 480, "mínimo")]
        [TestCase(5120, 1440, "4096")]
        [TestCase(1440, 5120, "4096")]
        [TestCase(0, 1080, "positivas")]
        public void InvalidResolutions_AreRejectedWithReason(int w, int h, string reasonFragment)
        {
            Assert.That(Resolution.TryCreate(w, h, out _, out var error), Is.False);
            Assert.That(error, Does.Contain(reasonFragment));
        }

        [Test]
        public void Create_ThrowsOnInvalid() =>
            Assert.Throws<System.ArgumentException>(() => Resolution.Create(1366, 768));

        [Test]
        public void Rotated_SwapsWidthAndHeight()
        {
            var r = Resolution.Create(2560, 1440).Rotated();
            Assert.That(r.Width, Is.EqualTo(1440));
            Assert.That(r.Height, Is.EqualTo(2560));
            Assert.That(r.Orientation, Is.EqualTo(Orientation.Portrait));
        }

        [Test]
        public void AspectRatio_IsWidthOverHeight() =>
            Assert.That(Resolution.Create(2560, 1080).AspectRatio, Is.EqualTo(2560f / 1080f).Within(1e-5f));

        [Test]
        public void Default_IsInvalid() => Assert.That(default(Resolution).IsValid, Is.False);
    }
}
```

`Assets/_Project/Tests/EditMode/FrameRateModeTests.cs`:
```csharp
using NUnit.Framework;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Tests
{
    public class FrameRateModeTests
    {
        [Test]
        public void Default_IsAuto() => Assert.That(default(FrameRateMode), Is.EqualTo(FrameRateMode.Auto));

        [TestCase(15)]
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(90)]
        public void Fixed_AcceptsSupportedRates(int fps)
        {
            var mode = FrameRateMode.Fixed(fps);
            Assert.That(mode.Kind, Is.EqualTo(FrameRateKind.Fixed));
            Assert.That(mode.FixedFps, Is.EqualTo(fps));
        }

        [TestCase(0)]
        [TestCase(45)]
        [TestCase(120)]
        public void Fixed_RejectsOtherRates(int fps) =>
            Assert.Throws<System.ArgumentOutOfRangeException>(() => FrameRateMode.Fixed(fps));

        [Test]
        public void Modes_AreDistinct()
        {
            Assert.That(FrameRateMode.Video, Is.Not.EqualTo(FrameRateMode.Eco));
            Assert.That(FrameRateMode.Fixed(60), Is.Not.EqualTo(FrameRateMode.Fixed(30)));
        }
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: FAIL de compilação — `The type or namespace name 'Resolution' could not be found`.

- [ ] **Step 3: Implementar**

`Assets/_Project/Domain/Screens/Orientation.cs`:
```csharp
namespace ScreenPortal.Domain.Screens
{
    /// <summary>Derivada da resolução: o monitor é criado já em pé, sem rotação do Windows (brief §10.3, D4).</summary>
    public enum Orientation
    {
        Landscape,
        Portrait,
    }
}
```

`Assets/_Project/Domain/Screens/Resolution.cs`:
```csharp
using System;

namespace ScreenPortal.Domain.Screens
{
    /// <summary>
    /// Resolução de um monitor virtual, escolhida na criação e nunca trocada ao vivo (brief §10.1).
    /// Regras (brief §10.3, D5): múltiplos de 8, mínimo 640×480 em qualquer orientação,
    /// máximo 4096 px por dimensão enquanto o MVP usa H.264.
    /// </summary>
    public readonly struct Resolution : IEquatable<Resolution>
    {
        public const int Alignment = 8;
        public const int MinLongSide = 640;
        public const int MinShortSide = 480;
        public const int MaxDimension = 4096;

        public readonly int Width;
        public readonly int Height;

        private Resolution(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public bool IsValid => Width > 0 && Height > 0;
        public Orientation Orientation => Height > Width ? Orientation.Portrait : Orientation.Landscape;
        public float AspectRatio => (float)Width / Height;

        public static bool TryCreate(int width, int height, out Resolution resolution, out string error)
        {
            resolution = default;
            if (width <= 0 || height <= 0)
            {
                error = "Largura e altura devem ser positivas.";
                return false;
            }
            if (width % Alignment != 0 || height % Alignment != 0)
            {
                error = "Largura e altura devem ser múltiplos de 8 (exigência do encoder).";
                return false;
            }
            int longSide = Math.Max(width, height);
            int shortSide = Math.Min(width, height);
            if (longSide < MinLongSide || shortSide < MinShortSide)
            {
                error = "Resolução abaixo do mínimo de 640×480.";
                return false;
            }
            if (longSide > MaxDimension)
            {
                error = "Acima de 4096 px em uma dimensão requer HEVC (v1.1).";
                return false;
            }
            resolution = new Resolution(width, height);
            error = null;
            return true;
        }

        public static Resolution Create(int width, int height)
        {
            if (!TryCreate(width, height, out var resolution, out var error))
                throw new ArgumentException(error);
            return resolution;
        }

        /// <summary>Mesma resolução na outra orientação (as regras são simétricas, então continua válida).</summary>
        public Resolution Rotated() => new Resolution(Height, Width);

        public bool Equals(Resolution other) => Width == other.Width && Height == other.Height;
        public override bool Equals(object obj) => obj is Resolution other && Equals(other);
        public override int GetHashCode() => (Width, Height).GetHashCode();
        public override string ToString() => Width + "×" + Height;
    }
}
```

`Assets/_Project/Domain/Screens/FrameRateMode.cs`:
```csharp
using System;

namespace ScreenPortal.Domain.Screens
{
    public enum FrameRateKind
    {
        Auto,
        Fixed,
        Video,
        Eco,
    }

    /// <summary>Modo de fps por tela (brief §8.8). default = Auto.</summary>
    public readonly struct FrameRateMode : IEquatable<FrameRateMode>
    {
        public static readonly FrameRateMode Auto = new FrameRateMode(FrameRateKind.Auto, 0);
        public static readonly FrameRateMode Video = new FrameRateMode(FrameRateKind.Video, 0);
        public static readonly FrameRateMode Eco = new FrameRateMode(FrameRateKind.Eco, 0);

        public readonly FrameRateKind Kind;
        /// <summary>Só para <see cref="FrameRateKind.Fixed"/>; 0 nos demais.</summary>
        public readonly int FixedFps;

        private FrameRateMode(FrameRateKind kind, int fixedFps)
        {
            Kind = kind;
            FixedFps = fixedFps;
        }

        public static FrameRateMode Fixed(int fps)
        {
            if (fps != 15 && fps != 30 && fps != 60 && fps != 90)
                throw new ArgumentOutOfRangeException(nameof(fps), fps, "Fixed aceita 15, 30, 60 ou 90 fps.");
            return new FrameRateMode(FrameRateKind.Fixed, fps);
        }

        public bool Equals(FrameRateMode other) => Kind == other.Kind && FixedFps == other.FixedFps;
        public override bool Equals(object obj) => obj is FrameRateMode other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ FixedFps;
        public override string ToString() => Kind == FrameRateKind.Fixed ? "Fixed(" + FixedFps + ")" : Kind.ToString();
    }
}
```

`Assets/_Project/Domain/Screens/ScreenLimits.cs`:
```csharp
namespace ScreenPortal.Domain.Screens
{
    /// <summary>Limites físicos das telas no espaço. Um só lugar para as regras de tamanho e curvatura.</summary>
    public static class ScreenLimits
    {
        public const float MinWidthMeters = 0.2f;
        public const float MaxWidthMeters = 6f;
        /// <summary>Preset "Média" (brief §10.2): valor do primeiro toggle plana → curva.</summary>
        public const float DefaultCurvature = 0.6f;

        public static float ClampWidth(float widthMeters)
        {
            if (float.IsNaN(widthMeters)) return MinWidthMeters;
            if (widthMeters < MinWidthMeters) return MinWidthMeters;
            if (widthMeters > MaxWidthMeters) return MaxWidthMeters;
            return widthMeters;
        }

        public static float Clamp01(float value)
        {
            if (float.IsNaN(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: `Com falha: 0` (3 + 12 + 4 + 3 + … = todos aprovados).

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Domain/Screens Assets/_Project/Tests/EditMode
git commit -m "phase1: Resolution (rules from brief §10.3/D5), Orientation, FrameRateMode, ScreenLimits"
```

---

### Task 3: VirtualScreen e Workspace

**Files:**
- Create: `Assets/_Project/Domain/Screens/VirtualScreen.cs`, `Assets/_Project/Domain/Workspace.cs`
- Test: `Assets/_Project/Tests/EditMode/VirtualScreenTests.cs`, `WorkspaceTests.cs`

**Interfaces:**
- Consumes: `ScreenId`, `Resolution`, `Orientation`, `FrameRateMode`, `ScreenLimits`, `Pose` (Tasks 1–2).
- Produces:
  - `sealed class VirtualScreen(ScreenId id, Resolution resolution, Pose pose, float widthMeters, float curvature = 0f, float lastNonZeroCurvature = ScreenLimits.DefaultCurvature, FrameRateMode frameRateMode = default, string label = "")` com `Id`, `Resolution`, `Orientation`, `Pose`, `WidthMeters`, `HeightMeters`, `Curvature`, `LastNonZeroCurvature`, `FrameRateMode`, `Label`, e `WithPose(Pose)`, `WithWidth(float)`, `WithCurvature(float)`, `WithFrameRateMode(FrameRateMode)`, `WithResolution(Resolution)`.
  - `sealed class Workspace` (namespace `ScreenPortal.Domain`) com `static Workspace Empty`, `IReadOnlyList<VirtualScreen> Screens`, `ScreenId FocusedScreenId`, `int NextScreenId`, `bool TryGetScreen(ScreenId, out VirtualScreen)`, `bool Contains(ScreenId)`, `ScreenId AllocateId()`, `Workspace WithScreenAdded(VirtualScreen)`, `Workspace WithScreenReplaced(VirtualScreen)`, `Workspace WithScreenRemoved(ScreenId)`, `Workspace WithFocus(ScreenId)`.

- [ ] **Step 1: Escrever os testes que falham**

`Assets/_Project/Tests/EditMode/VirtualScreenTests.cs`:
```csharp
using NUnit.Framework;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class VirtualScreenTests
    {
        private static VirtualScreen Screen(float width = 1.4f, float curvature = 0f) =>
            new VirtualScreen(new ScreenId(1), Resolution.Create(2560, 1080), Pose.Identity, width, curvature);

        [Test]
        public void HeightDerivesFromAspectRatio() =>
            Assert.That(Screen(1.4f).HeightMeters, Is.EqualTo(1.4f * 1080f / 2560f).Within(1e-5f));

        [Test]
        public void OrientationComesFromResolution()
        {
            var portrait = new VirtualScreen(new ScreenId(1), Resolution.Create(1440, 2560), Pose.Identity, 0.6f);
            Assert.That(portrait.Orientation, Is.EqualTo(Orientation.Portrait));
            Assert.That(portrait.HeightMeters, Is.GreaterThan(portrait.WidthMeters));
        }

        [TestCase(0.01f, ScreenLimits.MinWidthMeters)]
        [TestCase(50f, ScreenLimits.MaxWidthMeters)]
        [TestCase(1.5f, 1.5f)]
        public void WidthIsClamped(float requested, float expected) =>
            Assert.That(Screen(requested).WidthMeters, Is.EqualTo(expected));

        [Test]
        public void CurvatureIsClampedAndRemembered()
        {
            var s = Screen().WithCurvature(1.7f);
            Assert.That(s.Curvature, Is.EqualTo(1f));
            Assert.That(s.LastNonZeroCurvature, Is.EqualTo(1f));

            var flat = s.WithCurvature(0f);
            Assert.That(flat.Curvature, Is.EqualTo(0f));
            Assert.That(flat.LastNonZeroCurvature, Is.EqualTo(1f));
        }

        [Test]
        public void NewScreen_LastCurvatureDefaultsToMedium() =>
            Assert.That(Screen().LastNonZeroCurvature, Is.EqualTo(ScreenLimits.DefaultCurvature));

        [Test]
        public void WithMethods_ReturnNewInstances_AndKeepOtherFields()
        {
            var s = Screen();
            var moved = s.WithPose(Pose.At(new Vec3(1f, 2f, 3f)));
            Assert.That(moved, Is.Not.SameAs(s));
            Assert.That(moved.Resolution, Is.EqualTo(s.Resolution));
            Assert.That(moved.WidthMeters, Is.EqualTo(s.WidthMeters));
            Assert.That(s.Pose, Is.EqualTo(Pose.Identity));
        }

        [Test]
        public void NoneId_IsRejected() =>
            Assert.Throws<System.ArgumentException>(() =>
                new VirtualScreen(ScreenId.None, Resolution.Create(1920, 1080), Pose.Identity, 1f));

        [Test]
        public void InvalidResolution_IsRejected() =>
            Assert.Throws<System.ArgumentException>(() =>
                new VirtualScreen(new ScreenId(1), default(Resolution), Pose.Identity, 1f));
    }
}
```

`Assets/_Project/Tests/EditMode/WorkspaceTests.cs`:
```csharp
using NUnit.Framework;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class WorkspaceTests
    {
        private static VirtualScreen Screen(int id) =>
            new VirtualScreen(new ScreenId(id), Resolution.Create(1920, 1080), Pose.Identity, 1f);

        [Test]
        public void Empty_HasNoScreensAndNoFocus()
        {
            Assert.That(Workspace.Empty.Screens, Is.Empty);
            Assert.That(Workspace.Empty.FocusedScreenId.IsNone, Is.True);
            Assert.That(Workspace.Empty.AllocateId(), Is.EqualTo(new ScreenId(1)));
        }

        [Test]
        public void AddingScreens_AdvancesIdsAndKeepsOriginalUnchanged()
        {
            var ws = Workspace.Empty.WithScreenAdded(Screen(1));
            Assert.That(ws.Screens.Count, Is.EqualTo(1));
            Assert.That(ws.AllocateId(), Is.EqualTo(new ScreenId(2)));
            Assert.That(Workspace.Empty.Screens, Is.Empty);
        }

        [Test]
        public void AddingDuplicateId_Throws() =>
            Assert.Throws<System.InvalidOperationException>(() =>
                Workspace.Empty.WithScreenAdded(Screen(1)).WithScreenAdded(Screen(1)));

        [Test]
        public void Replace_SwapsTheScreenWithSameId()
        {
            var ws = Workspace.Empty.WithScreenAdded(Screen(1));
            var wider = Screen(1).WithWidth(2f);
            ws = ws.WithScreenReplaced(wider);
            Assert.That(ws.TryGetScreen(new ScreenId(1), out var s), Is.True);
            Assert.That(s, Is.SameAs(wider));
        }

        [Test]
        public void Replace_MissingScreen_Throws() =>
            Assert.Throws<System.InvalidOperationException>(() => Workspace.Empty.WithScreenReplaced(Screen(9)));

        [Test]
        public void RemovingFocusedScreen_ClearsFocus()
        {
            var ws = Workspace.Empty.WithScreenAdded(Screen(1)).WithScreenAdded(Screen(2)).WithFocus(new ScreenId(2));
            ws = ws.WithScreenRemoved(new ScreenId(2));
            Assert.That(ws.Contains(new ScreenId(2)), Is.False);
            Assert.That(ws.FocusedScreenId.IsNone, Is.True);
            Assert.That(ws.AllocateId(), Is.EqualTo(new ScreenId(3)), "ids nunca são reutilizados");
        }

        [Test]
        public void Focus_OnUnknownScreen_Throws() =>
            Assert.Throws<System.InvalidOperationException>(() => Workspace.Empty.WithFocus(new ScreenId(5)));

        [Test]
        public void Focus_None_IsAllowed()
        {
            var ws = Workspace.Empty.WithScreenAdded(Screen(1)).WithFocus(new ScreenId(1)).WithFocus(ScreenId.None);
            Assert.That(ws.FocusedScreenId.IsNone, Is.True);
        }
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: FAIL de compilação — `'VirtualScreen' could not be found`.

- [ ] **Step 3: Implementar**

`Assets/_Project/Domain/Screens/VirtualScreen.cs`:
```csharp
using System;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Screens
{
    /// <summary>
    /// Uma tela no espaço 3D. Imutável: toda alteração devolve uma nova instância (Commands, brief §8.2).
    /// Resolução é fixa desde a criação; redimensionar é só <see cref="WidthMeters"/> (zoom, brief §10.1).
    /// </summary>
    public sealed class VirtualScreen
    {
        public ScreenId Id { get; }
        public Resolution Resolution { get; }
        public Pose Pose { get; }
        public float WidthMeters { get; }
        /// <summary>0 = plana, 1 = envolvente (brief §10.2).</summary>
        public float Curvature { get; }
        /// <summary>Última curvatura &gt; 0, usada pelo toggle plana ↔ curva.</summary>
        public float LastNonZeroCurvature { get; }
        public FrameRateMode FrameRateMode { get; }
        public string Label { get; }

        public Orientation Orientation => Resolution.Orientation;
        public float HeightMeters => WidthMeters / Resolution.AspectRatio;

        public VirtualScreen(
            ScreenId id,
            Resolution resolution,
            Pose pose,
            float widthMeters,
            float curvature = 0f,
            float lastNonZeroCurvature = ScreenLimits.DefaultCurvature,
            FrameRateMode frameRateMode = default,
            string label = "")
        {
            if (id.IsNone) throw new ArgumentException("Tela precisa de um id.", nameof(id));
            if (!resolution.IsValid) throw new ArgumentException("Resolução inválida.", nameof(resolution));

            Id = id;
            Resolution = resolution;
            Pose = pose;
            WidthMeters = ScreenLimits.ClampWidth(widthMeters);
            Curvature = ScreenLimits.Clamp01(curvature);
            float last = ScreenLimits.Clamp01(lastNonZeroCurvature);
            LastNonZeroCurvature = Curvature > 0f ? Curvature : (last > 0f ? last : ScreenLimits.DefaultCurvature);
            FrameRateMode = frameRateMode;
            Label = label ?? string.Empty;
        }

        public VirtualScreen WithPose(Pose pose) =>
            new VirtualScreen(Id, Resolution, pose, WidthMeters, Curvature, LastNonZeroCurvature, FrameRateMode, Label);

        public VirtualScreen WithWidth(float widthMeters) =>
            new VirtualScreen(Id, Resolution, Pose, widthMeters, Curvature, LastNonZeroCurvature, FrameRateMode, Label);

        public VirtualScreen WithCurvature(float curvature) =>
            new VirtualScreen(Id, Resolution, Pose, WidthMeters, curvature, LastNonZeroCurvature, FrameRateMode, Label);

        public VirtualScreen WithFrameRateMode(FrameRateMode mode) =>
            new VirtualScreen(Id, Resolution, Pose, WidthMeters, Curvature, LastNonZeroCurvature, mode, Label);

        /// <summary>Só para <c>RecreateScreen</c> (ação explícita, brief §10.4). Mantém pose e largura física.</summary>
        public VirtualScreen WithResolution(Resolution resolution) =>
            new VirtualScreen(Id, resolution, Pose, WidthMeters, Curvature, LastNonZeroCurvature, FrameRateMode, Label);

        public override string ToString() => $"{Id} {Resolution} {WidthMeters:0.00} m curv {Curvature:0.00} {FrameRateMode}";
    }
}
```

`Assets/_Project/Domain/Workspace.cs`:
```csharp
using System;
using System.Collections.Generic;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain
{
    /// <summary>
    /// Estado completo da workspace. Imutável: o <c>WorkspaceStore</c> guarda snapshots para undo/redo
    /// e perfis de layout (Memento, brief §8.2). Ids nunca são reutilizados.
    /// </summary>
    public sealed class Workspace
    {
        public static readonly Workspace Empty = new Workspace(Array.Empty<VirtualScreen>(), ScreenId.None, 1);

        private readonly VirtualScreen[] _screens;

        public IReadOnlyList<VirtualScreen> Screens => _screens;
        public ScreenId FocusedScreenId { get; }
        public int NextScreenId { get; }

        private Workspace(VirtualScreen[] screens, ScreenId focused, int nextScreenId)
        {
            _screens = screens;
            FocusedScreenId = focused;
            NextScreenId = nextScreenId;
        }

        public ScreenId AllocateId() => new ScreenId(NextScreenId);

        public bool TryGetScreen(ScreenId id, out VirtualScreen screen)
        {
            int index = IndexOf(id);
            screen = index >= 0 ? _screens[index] : null;
            return index >= 0;
        }

        public bool Contains(ScreenId id) => IndexOf(id) >= 0;

        public Workspace WithScreenAdded(VirtualScreen screen)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            if (Contains(screen.Id)) throw new InvalidOperationException(screen.Id + " já existe.");
            var screens = new VirtualScreen[_screens.Length + 1];
            Array.Copy(_screens, screens, _screens.Length);
            screens[_screens.Length] = screen;
            return new Workspace(screens, FocusedScreenId, Math.Max(NextScreenId, screen.Id.Value + 1));
        }

        public Workspace WithScreenReplaced(VirtualScreen screen)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            int index = IndexOf(screen.Id);
            if (index < 0) throw new InvalidOperationException(screen.Id + " não existe.");
            var screens = (VirtualScreen[])_screens.Clone();
            screens[index] = screen;
            return new Workspace(screens, FocusedScreenId, NextScreenId);
        }

        public Workspace WithScreenRemoved(ScreenId id)
        {
            int index = IndexOf(id);
            if (index < 0) throw new InvalidOperationException(id + " não existe.");
            var screens = new VirtualScreen[_screens.Length - 1];
            Array.Copy(_screens, 0, screens, 0, index);
            Array.Copy(_screens, index + 1, screens, index, _screens.Length - index - 1);
            var focus = FocusedScreenId == id ? ScreenId.None : FocusedScreenId;
            return new Workspace(screens, focus, NextScreenId);
        }

        public Workspace WithFocus(ScreenId id)
        {
            if (!id.IsNone && !Contains(id)) throw new InvalidOperationException(id + " não existe.");
            return new Workspace(_screens, id, NextScreenId);
        }

        private int IndexOf(ScreenId id)
        {
            for (int i = 0; i < _screens.Length; i++)
                if (_screens[i].Id == id) return i;
            return -1;
        }
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: `Com falha: 0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Domain Assets/_Project/Tests/EditMode
git commit -m "phase1: immutable VirtualScreen and Workspace with id allocation and focus rules"
```

---

### Task 4: Commands da workspace e invariantes

**Files:**
- Create: `Assets/_Project/Domain/Commands/ICommand.cs`, `CommandResult.cs`, `ScreenCommand.cs`, `CreateScreen.cs`, `RemoveScreen.cs`, `MoveScreen.cs`, `ResizeScreen.cs`, `SetScreenCurvature.cs`, `ToggleScreenCurvature.cs`, `SetFrameRateMode.cs`, `RecreateScreen.cs`, `SetFocus.cs`
- Test: `Assets/_Project/Tests/EditMode/CommandTests.cs`

**Interfaces:**
- Consumes: `Workspace` (`AllocateId`, `WithScreenAdded`, `WithScreenReplaced`, `WithScreenRemoved`, `WithFocus`, `TryGetScreen`, `Contains`), `VirtualScreen` (`With*`), `Resolution`, `FrameRateMode`, `Pose`.
- Produces (namespace `ScreenPortal.Domain.Commands`):
  - `readonly struct CommandResult` com `Accepted`, `Error`, `static CommandResult Ok`, `static CommandResult Rejected(string error)`.
  - `interface ICommand { bool IsUndoable { get; } CommandResult Apply(Workspace state, out Workspace next); }` — contrato: se rejeitado, `next == state`.
  - Construtores: `CreateScreen(Resolution resolution, Pose pose, float widthMeters, float initialCurvature = 0f, string label = "")`, `RemoveScreen(ScreenId)`, `MoveScreen(ScreenId, Pose)`, `ResizeScreen(ScreenId, float widthMeters)`, `SetScreenCurvature(ScreenId, float curvature)`, `ToggleScreenCurvature(ScreenId)`, `SetFrameRateMode(ScreenId, FrameRateMode)`, `RecreateScreen(ScreenId, Resolution)`, `SetFocus(ScreenId)` (`IsUndoable = false`).

- [ ] **Step 1: Escrever os testes que falham**

`Assets/_Project/Tests/EditMode/CommandTests.cs`:
```csharp
using NUnit.Framework;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class CommandTests
    {
        private static readonly Resolution Ultrawide = Resolution.Create(2560, 1080);
        private static readonly Pose Front = Pose.At(new Vec3(0f, 1.2f, 1.3f));

        private static Workspace WithOneScreen(out ScreenId id)
        {
            Assert.That(new CreateScreen(Ultrawide, Front, 1.4f).Apply(Workspace.Empty, out var ws).Accepted, Is.True);
            id = ws.FocusedScreenId;
            return ws;
        }

        private static VirtualScreen Get(Workspace ws, ScreenId id)
        {
            Assert.That(ws.TryGetScreen(id, out var s), Is.True);
            return s;
        }

        [Test]
        public void CreateScreen_AddsAndFocusesNewScreen()
        {
            var ws = WithOneScreen(out var id);
            Assert.That(id, Is.EqualTo(new ScreenId(1)));
            var s = Get(ws, id);
            Assert.That(s.Resolution, Is.EqualTo(Ultrawide));
            Assert.That(s.Pose, Is.EqualTo(Front));
            Assert.That(s.WidthMeters, Is.EqualTo(1.4f));
        }

        [Test]
        public void CreateScreen_WithInvalidResolution_IsRejectedAndStateUnchanged()
        {
            var result = new CreateScreen(default(Resolution), Front, 1f).Apply(Workspace.Empty, out var next);
            Assert.That(result.Accepted, Is.False);
            Assert.That(next, Is.SameAs(Workspace.Empty));
        }

        [TestCase(0.01f)]
        [TestCase(0.5f)]
        [TestCase(3f)]
        [TestCase(100f)]
        public void Invariant_ResizeNeverChangesResolutionOrOrientation(float width)
        {
            var ws = WithOneScreen(out var id);
            new ResizeScreen(id, width).Apply(ws, out var next);
            var s = Get(next, id);
            Assert.That(s.Resolution, Is.EqualTo(Ultrawide));
            Assert.That(s.Orientation, Is.EqualTo(Orientation.Landscape));
            Assert.That(s.WidthMeters, Is.EqualTo(ScreenLimits.ClampWidth(width)));
        }

        [TestCase(0f)]
        [TestCase(0.3f)]
        [TestCase(1f)]
        [TestCase(5f)]
        public void Invariant_CurvatureNeverChangesResolutionOrientationOrWidth(float curvature)
        {
            var ws = WithOneScreen(out var id);
            new SetScreenCurvature(id, curvature).Apply(ws, out var next);
            var s = Get(next, id);
            Assert.That(s.Resolution, Is.EqualTo(Ultrawide));
            Assert.That(s.Orientation, Is.EqualTo(Orientation.Landscape));
            Assert.That(s.WidthMeters, Is.EqualTo(1.4f));
            Assert.That(s.Curvature, Is.EqualTo(ScreenLimits.Clamp01(curvature)));
        }

        [Test]
        public void ToggleCurvature_FlatToLastUsedAndBack()
        {
            var ws = WithOneScreen(out var id);
            new ToggleScreenCurvature(id).Apply(ws, out ws);
            Assert.That(Get(ws, id).Curvature, Is.EqualTo(ScreenLimits.DefaultCurvature), "primeira vez: Média");

            new SetScreenCurvature(id, 0.3f).Apply(ws, out ws);
            new ToggleScreenCurvature(id).Apply(ws, out ws);
            Assert.That(Get(ws, id).Curvature, Is.EqualTo(0f));
            new ToggleScreenCurvature(id).Apply(ws, out ws);
            Assert.That(Get(ws, id).Curvature, Is.EqualTo(0.3f), "volta à última curvatura usada");
        }

        [Test]
        public void MoveScreen_ChangesOnlyPose()
        {
            var ws = WithOneScreen(out var id);
            var target = Pose.At(new Vec3(-1f, 1.5f, 1f));
            new MoveScreen(id, target).Apply(ws, out var next);
            var s = Get(next, id);
            Assert.That(s.Pose, Is.EqualTo(target));
            Assert.That(s.WidthMeters, Is.EqualTo(1.4f));
            Assert.That(s.Resolution, Is.EqualTo(Ultrawide));
        }

        [Test]
        public void CommandsOnUnknownScreen_AreRejected()
        {
            var unknown = new ScreenId(42);
            ICommand[] commands =
            {
                new RemoveScreen(unknown), new MoveScreen(unknown, Front), new ResizeScreen(unknown, 1f),
                new SetScreenCurvature(unknown, 0.5f), new ToggleScreenCurvature(unknown),
                new SetFrameRateMode(unknown, FrameRateMode.Video), new RecreateScreen(unknown, Ultrawide),
                new SetFocus(unknown),
            };
            foreach (var command in commands)
            {
                var result = command.Apply(Workspace.Empty, out var next);
                Assert.That(result.Accepted, Is.False, command.GetType().Name);
                Assert.That(result.Error, Does.Contain("42"), command.GetType().Name);
                Assert.That(next, Is.SameAs(Workspace.Empty), command.GetType().Name);
            }
        }

        [Test]
        public void RecreateScreen_ChangesResolutionKeepsPoseAndPhysicalWidth()
        {
            var ws = WithOneScreen(out var id);
            var portrait = Resolution.Create(1440, 2560);
            new RecreateScreen(id, portrait).Apply(ws, out var next);
            var s = Get(next, id);
            Assert.That(s.Resolution, Is.EqualTo(portrait));
            Assert.That(s.Orientation, Is.EqualTo(Orientation.Portrait));
            Assert.That(s.Pose, Is.EqualTo(Front));
            Assert.That(s.WidthMeters, Is.EqualTo(1.4f));
        }

        [Test]
        public void SetFrameRateMode_IsStoredPerScreen()
        {
            var ws = WithOneScreen(out var id);
            new SetFrameRateMode(id, FrameRateMode.Fixed(30)).Apply(ws, out var next);
            Assert.That(Get(next, id).FrameRateMode, Is.EqualTo(FrameRateMode.Fixed(30)));
        }

        [Test]
        public void RemoveScreen_RemovesAndClearsFocus()
        {
            var ws = WithOneScreen(out var id);
            new RemoveScreen(id).Apply(ws, out var next);
            Assert.That(next.Contains(id), Is.False);
            Assert.That(next.FocusedScreenId.IsNone, Is.True);
        }

        [Test]
        public void SetFocus_IsNotUndoable_OthersAre()
        {
            Assert.That(new SetFocus(ScreenId.None).IsUndoable, Is.False);
            Assert.That(new MoveScreen(new ScreenId(1), Front).IsUndoable, Is.True);
            Assert.That(new CreateScreen(Ultrawide, Front, 1f).IsUndoable, Is.True);
        }
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: FAIL de compilação — `The type or namespace name 'Commands' does not exist`.

- [ ] **Step 3: Implementar a base**

`Assets/_Project/Domain/Commands/CommandResult.cs`:
```csharp
namespace ScreenPortal.Domain.Commands
{
    public readonly struct CommandResult
    {
        public static readonly CommandResult Ok = new CommandResult(true, null);

        public bool Accepted { get; }
        public string Error { get; }

        private CommandResult(bool accepted, string error)
        {
            Accepted = accepted;
            Error = error;
        }

        public static CommandResult Rejected(string error) => new CommandResult(false, error);

        public override string ToString() => Accepted ? "Ok" : "Rejected: " + Error;
    }
}
```

`Assets/_Project/Domain/Commands/ICommand.cs`:
```csharp
namespace ScreenPortal.Domain.Commands
{
    /// <summary>
    /// Toda mudança de estado da workspace (brief §8.2). Função pura: não altera <paramref name="state"/>.
    /// Se rejeitado, <paramref name="next"/> é o próprio <paramref name="state"/>.
    /// </summary>
    public interface ICommand
    {
        /// <summary>false para mudanças que não devem entrar no histórico (ex.: foco).</summary>
        bool IsUndoable { get; }

        CommandResult Apply(Workspace state, out Workspace next);
    }
}
```

`Assets/_Project/Domain/Commands/ScreenCommand.cs`:
```csharp
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Base para comandos que transformam uma tela existente.</summary>
    public abstract class ScreenCommand : ICommand
    {
        public ScreenId ScreenId { get; }
        public virtual bool IsUndoable => true;

        protected ScreenCommand(ScreenId screenId) => ScreenId = screenId;

        protected abstract VirtualScreen Transform(VirtualScreen screen);

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            if (!state.TryGetScreen(ScreenId, out var screen))
            {
                next = state;
                return CommandResult.Rejected(ScreenId + " não existe.");
            }
            next = state.WithScreenReplaced(Transform(screen));
            return CommandResult.Ok;
        }

        public override string ToString() => GetType().Name + "(" + ScreenId + ")";
    }
}
```

- [ ] **Step 4: Implementar os comandos**

`Assets/_Project/Domain/Commands/CreateScreen.cs`:
```csharp
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Cria um monitor com resolução fixa (brief §10.3). A nova tela recebe o foco.</summary>
    public sealed class CreateScreen : ICommand
    {
        public Resolution Resolution { get; }
        public Pose Pose { get; }
        public float WidthMeters { get; }
        public float InitialCurvature { get; }
        public string Label { get; }
        public bool IsUndoable => true;

        public CreateScreen(Resolution resolution, Pose pose, float widthMeters, float initialCurvature = 0f, string label = "")
        {
            Resolution = resolution;
            Pose = pose;
            WidthMeters = widthMeters;
            InitialCurvature = initialCurvature;
            Label = label;
        }

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            if (!Resolution.IsValid)
            {
                next = state;
                return CommandResult.Rejected("Resolução inválida.");
            }
            var id = state.AllocateId();
            var screen = new VirtualScreen(id, Resolution, Pose, WidthMeters, InitialCurvature, label: Label);
            next = state.WithScreenAdded(screen).WithFocus(id);
            return CommandResult.Ok;
        }

        public override string ToString() => $"CreateScreen({Resolution}, {WidthMeters:0.00} m)";
    }
}
```

`Assets/_Project/Domain/Commands/RemoveScreen.cs`:
```csharp
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    public sealed class RemoveScreen : ICommand
    {
        public ScreenId ScreenId { get; }
        public bool IsUndoable => true;

        public RemoveScreen(ScreenId screenId) => ScreenId = screenId;

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            if (!state.Contains(ScreenId))
            {
                next = state;
                return CommandResult.Rejected(ScreenId + " não existe.");
            }
            next = state.WithScreenRemoved(ScreenId);
            return CommandResult.Ok;
        }

        public override string ToString() => "RemoveScreen(" + ScreenId + ")";
    }
}
```

`Assets/_Project/Domain/Commands/MoveScreen.cs`:
```csharp
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Commands
{
    public sealed class MoveScreen : ScreenCommand
    {
        public Pose Pose { get; }

        public MoveScreen(ScreenId screenId, Pose pose) : base(screenId) => Pose = pose;

        protected override VirtualScreen Transform(VirtualScreen screen) => screen.WithPose(Pose);
    }
}
```

`Assets/_Project/Domain/Commands/ResizeScreen.cs`:
```csharp
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Zoom proporcional: só a largura física muda; a resolução NUNCA (brief §10.1).</summary>
    public sealed class ResizeScreen : ScreenCommand
    {
        public float WidthMeters { get; }

        public ResizeScreen(ScreenId screenId, float widthMeters) : base(screenId) => WidthMeters = widthMeters;

        protected override VirtualScreen Transform(VirtualScreen screen) => screen.WithWidth(WidthMeters);
    }
}
```

`Assets/_Project/Domain/Commands/SetScreenCurvature.cs`:
```csharp
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Curvatura 0..1 (limitada). Nunca altera resolução, orientação nem largura (brief §10.2).</summary>
    public sealed class SetScreenCurvature : ScreenCommand
    {
        public float Curvature { get; }

        public SetScreenCurvature(ScreenId screenId, float curvature) : base(screenId) => Curvature = curvature;

        protected override VirtualScreen Transform(VirtualScreen screen) => screen.WithCurvature(Curvature);
    }
}
```

`Assets/_Project/Domain/Commands/ToggleScreenCurvature.cs`:
```csharp
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Plana ↔ última curvatura usada (primeira vez: Média). Atalho Ctrl+Alt+C (brief §10.9).</summary>
    public sealed class ToggleScreenCurvature : ScreenCommand
    {
        public ToggleScreenCurvature(ScreenId screenId) : base(screenId) { }

        protected override VirtualScreen Transform(VirtualScreen screen) =>
            screen.WithCurvature(screen.Curvature > 0f ? 0f : screen.LastNonZeroCurvature);
    }
}
```

`Assets/_Project/Domain/Commands/SetFrameRateMode.cs`:
```csharp
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    public sealed class SetFrameRateMode : ScreenCommand
    {
        public FrameRateMode Mode { get; }

        public SetFrameRateMode(ScreenId screenId, FrameRateMode mode) : base(screenId) => Mode = mode;

        protected override VirtualScreen Transform(VirtualScreen screen) => screen.WithFrameRateMode(Mode);
    }
}
```

`Assets/_Project/Domain/Commands/RecreateScreen.cs`:
```csharp
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>
    /// Única forma de mudar a resolução/orientação de uma tela existente: ação explícita do usuário
    /// (brief §10.4). O host recria o monitor virtual; aqui mantemos pose e largura física.
    /// </summary>
    public sealed class RecreateScreen : ICommand
    {
        public ScreenId ScreenId { get; }
        public Resolution Resolution { get; }
        public bool IsUndoable => true;

        public RecreateScreen(ScreenId screenId, Resolution resolution)
        {
            ScreenId = screenId;
            Resolution = resolution;
        }

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            next = state;
            if (!state.TryGetScreen(ScreenId, out var screen)) return CommandResult.Rejected(ScreenId + " não existe.");
            if (!Resolution.IsValid) return CommandResult.Rejected("Resolução inválida.");
            next = state.WithScreenReplaced(screen.WithResolution(Resolution));
            return CommandResult.Ok;
        }

        public override string ToString() => $"RecreateScreen({ScreenId}, {Resolution})";
    }
}
```

`Assets/_Project/Domain/Commands/SetFocus.cs`:
```csharp
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Foco = tela do cursor (brief §10.7). Não entra no undo: é consequência de navegação, não edição.</summary>
    public sealed class SetFocus : ICommand
    {
        public ScreenId ScreenId { get; }
        public bool IsUndoable => false;

        public SetFocus(ScreenId screenId) => ScreenId = screenId;

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            if (!ScreenId.IsNone && !state.Contains(ScreenId))
            {
                next = state;
                return CommandResult.Rejected(ScreenId + " não existe.");
            }
            next = state.WithFocus(ScreenId);
            return CommandResult.Ok;
        }

        public override string ToString() => "SetFocus(" + ScreenId + ")";
    }
}
```

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: `Com falha: 0`.

- [ ] **Step 6: Commit**

```bash
git add Assets/_Project/Domain/Commands Assets/_Project/Tests/EditMode/CommandTests.cs
git commit -m "phase1: workspace commands (create/remove/move/resize/curvature/fps/recreate/focus) with invariant tests"
```

---

### Task 5: WorkspaceStore, CommandBus e CommandJournal

**Files:**
- Create: `Assets/_Project/Application/WorkspaceStore.cs`, `ICommandBus.cs`, `ICommandObserver.cs`, `CommandBus.cs`, `CommandJournal.cs`
- Test: `Assets/_Project/Tests/EditMode/WorkspaceStoreTests.cs`, `CommandBusTests.cs`

**Interfaces:**
- Consumes: `Workspace`, `ICommand`, `CommandResult` (Tasks 3–4).
- Produces (namespace `ScreenPortal.App`):
  - `sealed class WorkspaceStore(Workspace initial = null)` com `Workspace State`, `event Action<Workspace, Workspace> StateChanged` (anterior, atual), `CommandResult Dispatch(ICommand)`, `bool Undo()`, `bool Redo()`, `bool CanUndo`, `bool CanRedo`, `const int MaxHistory = 100`.
  - `interface ICommandBus { CommandResult Send(ICommand command); bool Undo(); bool Redo(); }`
  - `interface ICommandObserver { void OnCommandProcessed(ICommand command, CommandResult result); }`
  - `sealed class CommandBus(WorkspaceStore store, IEnumerable<ICommandObserver> observers) : ICommandBus`
  - `sealed class CommandJournal(int capacity = 256) : ICommandObserver` com `int Count`, `JournalEntry this[int index]` (0 = mais antigo), `readonly struct JournalEntry { ICommand Command; CommandResult Result; }`.

- [ ] **Step 1: Escrever os testes que falham**

`Assets/_Project/Tests/EditMode/WorkspaceStoreTests.cs`:
```csharp
using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class WorkspaceStoreTests
    {
        private static readonly Resolution Hd = Resolution.Create(1920, 1080);
        private static CreateScreen Create() => new CreateScreen(Hd, Pose.At(new Vec3(0f, 1.2f, 1.3f)), 1f);

        [Test]
        public void Dispatch_Accepted_ChangesStateAndRaisesEvent()
        {
            var store = new WorkspaceStore();
            Workspace previous = null, current = null;
            store.StateChanged += (p, c) => { previous = p; current = c; };

            var result = store.Dispatch(Create());

            Assert.That(result.Accepted, Is.True);
            Assert.That(store.State.Screens.Count, Is.EqualTo(1));
            Assert.That(previous, Is.SameAs(Workspace.Empty));
            Assert.That(current, Is.SameAs(store.State));
        }

        [Test]
        public void Dispatch_Rejected_KeepsStateAndRaisesNothing()
        {
            var store = new WorkspaceStore();
            int events = 0;
            store.StateChanged += (_, __) => events++;

            var result = store.Dispatch(new ResizeScreen(new ScreenId(7), 2f));

            Assert.That(result.Accepted, Is.False);
            Assert.That(store.State, Is.SameAs(Workspace.Empty));
            Assert.That(events, Is.EqualTo(0));
            Assert.That(store.CanUndo, Is.False);
        }

        [Test]
        public void UndoRedo_RestoresSnapshots()
        {
            var store = new WorkspaceStore();
            store.Dispatch(Create());
            var id = store.State.FocusedScreenId;
            store.Dispatch(new ResizeScreen(id, 2.5f));

            Assert.That(store.Undo(), Is.True);
            Assert.That(store.State.TryGetScreen(id, out var s), Is.True);
            Assert.That(s.WidthMeters, Is.EqualTo(1f));

            Assert.That(store.Redo(), Is.True);
            store.State.TryGetScreen(id, out s);
            Assert.That(s.WidthMeters, Is.EqualTo(2.5f));

            store.Undo();
            store.Undo();
            Assert.That(store.State.Screens, Is.Empty);
            Assert.That(store.Undo(), Is.False);
        }

        [Test]
        public void NewCommand_ClearsRedo()
        {
            var store = new WorkspaceStore();
            store.Dispatch(Create());
            store.Undo();
            Assert.That(store.CanRedo, Is.True);
            store.Dispatch(Create());
            Assert.That(store.CanRedo, Is.False);
        }

        [Test]
        public void NonUndoableCommand_DoesNotEnterHistory()
        {
            var store = new WorkspaceStore();
            store.Dispatch(Create());
            var id = store.State.FocusedScreenId;
            store.Dispatch(new SetFocus(ScreenId.None));
            store.Dispatch(new SetFocus(id));

            Assert.That(store.Undo(), Is.True);
            Assert.That(store.State.Screens, Is.Empty, "o único passo no histórico é o CreateScreen");
        }

        [Test]
        public void History_IsCappedAtMaxHistory()
        {
            var store = new WorkspaceStore();
            store.Dispatch(Create());
            var id = store.State.FocusedScreenId;
            for (int i = 0; i < WorkspaceStore.MaxHistory + 20; i++)
                store.Dispatch(new ResizeScreen(id, 1f + i * 0.01f));

            int undos = 0;
            while (store.Undo()) undos++;
            Assert.That(undos, Is.EqualTo(WorkspaceStore.MaxHistory));
        }
    }
}
```

`Assets/_Project/Tests/EditMode/CommandBusTests.cs`:
```csharp
using System.Collections.Generic;
using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class CommandBusTests
    {
        private sealed class RecordingObserver : ICommandObserver
        {
            public readonly List<(ICommand command, CommandResult result)> Seen = new List<(ICommand, CommandResult)>();
            public void OnCommandProcessed(ICommand command, CommandResult result) => Seen.Add((command, result));
        }

        [Test]
        public void Send_DispatchesToStoreAndNotifiesObserversWithResult()
        {
            var store = new WorkspaceStore();
            var observer = new RecordingObserver();
            var bus = new CommandBus(store, new ICommandObserver[] { observer });

            var ok = new CreateScreen(Resolution.Create(1920, 1080), Pose.Identity, 1f);
            var bad = new RemoveScreen(new ScreenId(99));
            bus.Send(ok);
            bus.Send(bad);

            Assert.That(store.State.Screens.Count, Is.EqualTo(1));
            Assert.That(observer.Seen.Count, Is.EqualTo(2));
            Assert.That(observer.Seen[0].command, Is.SameAs(ok));
            Assert.That(observer.Seen[0].result.Accepted, Is.True);
            Assert.That(observer.Seen[1].result.Accepted, Is.False);
        }

        [Test]
        public void UndoRedo_GoThroughStore()
        {
            var store = new WorkspaceStore();
            var bus = new CommandBus(store, new ICommandObserver[0]);
            bus.Send(new CreateScreen(Resolution.Create(1920, 1080), Pose.Identity, 1f));

            Assert.That(bus.Undo(), Is.True);
            Assert.That(store.State.Screens, Is.Empty);
            Assert.That(bus.Redo(), Is.True);
            Assert.That(store.State.Screens.Count, Is.EqualTo(1));
        }

        [Test]
        public void Journal_KeepsMostRecentEntriesUpToCapacity()
        {
            var journal = new CommandJournal(capacity: 3);
            var store = new WorkspaceStore();
            var bus = new CommandBus(store, new ICommandObserver[] { journal });
            for (int i = 0; i < 5; i++) bus.Send(new SetFocus(new ScreenId(100 + i)));

            Assert.That(journal.Count, Is.EqualTo(3));
            Assert.That(((SetFocus)journal[0].Command).ScreenId, Is.EqualTo(new ScreenId(102)), "o mais antigo mantido");
            Assert.That(((SetFocus)journal[2].Command).ScreenId, Is.EqualTo(new ScreenId(104)));
            Assert.That(journal[2].Result.Accepted, Is.False);
        }
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: FAIL de compilação — `The type or namespace name 'App' does not exist in the namespace 'ScreenPortal'`.

- [ ] **Step 3: Implementar**

`Assets/_Project/Application/WorkspaceStore.cs`:
```csharp
using System;
using System.Collections.Generic;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;

namespace ScreenPortal.App
{
    /// <summary>
    /// Fonte única da verdade da workspace (brief §8.2). Aplica Commands e guarda snapshots imutáveis
    /// para undo/redo. Uso só na thread principal da Unity.
    /// </summary>
    public sealed class WorkspaceStore
    {
        public const int MaxHistory = 100;

        private readonly List<Workspace> _undo = new List<Workspace>();
        private readonly List<Workspace> _redo = new List<Workspace>();

        public Workspace State { get; private set; }

        /// <summary>(anterior, atual). Só dispara quando o estado realmente muda.</summary>
        public event Action<Workspace, Workspace> StateChanged;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public WorkspaceStore(Workspace initial = null) => State = initial ?? Workspace.Empty;

        public CommandResult Dispatch(ICommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var result = command.Apply(State, out var next);
            if (!result.Accepted) return result;

            if (command.IsUndoable)
            {
                Push(_undo, State);
                _redo.Clear();
            }
            SetState(next);
            return result;
        }

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            Push(_redo, State);
            SetState(Pop(_undo));
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            Push(_undo, State);
            SetState(Pop(_redo));
            return true;
        }

        private void SetState(Workspace next)
        {
            var previous = State;
            State = next;
            if (!ReferenceEquals(previous, next)) StateChanged?.Invoke(previous, next);
        }

        private static void Push(List<Workspace> stack, Workspace state)
        {
            stack.Add(state);
            if (stack.Count > MaxHistory) stack.RemoveAt(0);
        }

        private static Workspace Pop(List<Workspace> stack)
        {
            var top = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            return top;
        }
    }
}
```

`Assets/_Project/Application/ICommandBus.cs`:
```csharp
using ScreenPortal.Domain.Commands;

namespace ScreenPortal.App
{
    /// <summary>Porta de entrada de toda intenção de mudança (views, atalhos, rede).</summary>
    public interface ICommandBus
    {
        CommandResult Send(ICommand command);
        bool Undo();
        bool Redo();
    }
}
```

`Assets/_Project/Application/ICommandObserver.cs`:
```csharp
using ScreenPortal.Domain.Commands;

namespace ScreenPortal.App
{
    /// <summary>Recebe cada comando processado (log/replay, e na Fase 1B a sincronização com o host).</summary>
    public interface ICommandObserver
    {
        void OnCommandProcessed(ICommand command, CommandResult result);
    }
}
```

`Assets/_Project/Application/CommandBus.cs`:
```csharp
using System;
using System.Collections.Generic;
using ScreenPortal.Domain.Commands;

namespace ScreenPortal.App
{
    public sealed class CommandBus : ICommandBus
    {
        private readonly WorkspaceStore _store;
        private readonly ICommandObserver[] _observers;

        public CommandBus(WorkspaceStore store, IEnumerable<ICommandObserver> observers)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _observers = observers == null ? new ICommandObserver[0] : new List<ICommandObserver>(observers).ToArray();
        }

        public CommandResult Send(ICommand command)
        {
            var result = _store.Dispatch(command);
            for (int i = 0; i < _observers.Length; i++) _observers[i].OnCommandProcessed(command, result);
            return result;
        }

        public bool Undo() => _store.Undo();
        public bool Redo() => _store.Redo();
    }
}
```

`Assets/_Project/Application/CommandJournal.cs`:
```csharp
using System;
using ScreenPortal.Domain.Commands;

namespace ScreenPortal.App
{
    public readonly struct JournalEntry
    {
        public ICommand Command { get; }
        public CommandResult Result { get; }

        public JournalEntry(ICommand command, CommandResult result)
        {
            Command = command;
            Result = result;
        }
    }

    /// <summary>Buffer circular dos últimos comandos (debug e replay, brief §8.2). Sem alocação por comando.</summary>
    public sealed class CommandJournal : ICommandObserver
    {
        private readonly JournalEntry[] _entries;
        private int _start;

        public int Count { get; private set; }

        public CommandJournal(int capacity = 256)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _entries = new JournalEntry[capacity];
        }

        /// <summary>0 = entrada mais antiga mantida.</summary>
        public JournalEntry this[int index]
        {
            get
            {
                if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
                return _entries[(_start + index) % _entries.Length];
            }
        }

        public void OnCommandProcessed(ICommand command, CommandResult result)
        {
            int end = (_start + Count) % _entries.Length;
            _entries[end] = new JournalEntry(command, result);
            if (Count < _entries.Length) Count++;
            else _start = (_start + 1) % _entries.Length;
        }
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: `Com falha: 0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Application Assets/_Project/Tests/EditMode
git commit -m "phase1: WorkspaceStore with snapshot undo/redo, CommandBus with observers, CommandJournal"
```

---

### Task 6: Portas de view e ScreenViewSynchronizer

**Files:**
- Create: `Assets/_Project/Application/Screens/IScreenView.cs`, `IScreenViewFactory.cs`, `ScreenViewSynchronizer.cs`
- Test: `Assets/_Project/Tests/EditMode/ScreenViewSynchronizerTests.cs`

**Interfaces:**
- Consumes: `WorkspaceStore` (`State`, `StateChanged`), `Workspace.Screens`, `VirtualScreen`, `ScreenId`.
- Produces (namespace `ScreenPortal.App.Screens`):
  - `interface IScreenView : IDisposable { void Apply(VirtualScreen screen); }`
  - `interface IScreenViewFactory { IScreenView Create(VirtualScreen screen); }` — a view recebe `Apply` logo após criada.
  - `sealed class ScreenViewSynchronizer(WorkspaceStore store, IScreenViewFactory factory) : IDisposable` com `void Start()`, `int ViewCount`.

- [ ] **Step 1: Escrever o teste que falha**

`Assets/_Project/Tests/EditMode/ScreenViewSynchronizerTests.cs`:
```csharp
using System.Collections.Generic;
using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class ScreenViewSynchronizerTests
    {
        private sealed class FakeView : IScreenView
        {
            public readonly List<VirtualScreen> Applied = new List<VirtualScreen>();
            public bool Disposed;
            public void Apply(VirtualScreen screen) => Applied.Add(screen);
            public void Dispose() => Disposed = true;
        }

        private sealed class FakeFactory : IScreenViewFactory
        {
            public readonly Dictionary<ScreenId, FakeView> Views = new Dictionary<ScreenId, FakeView>();
            public IScreenView Create(VirtualScreen screen)
            {
                var view = new FakeView();
                Views[screen.Id] = view;
                return view;
            }
        }

        private static CreateScreen Create() => new CreateScreen(Resolution.Create(1920, 1080), Pose.Identity, 1f);

        [Test]
        public void Start_CreatesViewsForExistingScreens()
        {
            var store = new WorkspaceStore();
            store.Dispatch(Create());
            var factory = new FakeFactory();

            using (var sync = new ScreenViewSynchronizer(store, factory))
            {
                sync.Start();
                Assert.That(sync.ViewCount, Is.EqualTo(1));
                Assert.That(factory.Views[new ScreenId(1)].Applied.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void StateChanges_CreateApplyAndDisposeViews()
        {
            var store = new WorkspaceStore();
            var factory = new FakeFactory();
            var sync = new ScreenViewSynchronizer(store, factory);
            sync.Start();

            store.Dispatch(Create());
            store.Dispatch(Create());
            var view1 = factory.Views[new ScreenId(1)];
            var view2 = factory.Views[new ScreenId(2)];
            Assert.That(sync.ViewCount, Is.EqualTo(2));

            store.Dispatch(new ResizeScreen(new ScreenId(1), 2f));
            Assert.That(view1.Applied.Count, Is.EqualTo(2), "tela alterada recebe Apply");
            Assert.That(view1.Applied[1].WidthMeters, Is.EqualTo(2f));
            Assert.That(view2.Applied.Count, Is.EqualTo(1), "tela intacta não recebe Apply de novo");

            store.Dispatch(new RemoveScreen(new ScreenId(2)));
            Assert.That(view2.Disposed, Is.True);
            Assert.That(sync.ViewCount, Is.EqualTo(1));

            sync.Dispose();
            Assert.That(view1.Disposed, Is.True);
            store.Dispatch(Create());
            Assert.That(factory.Views.ContainsKey(new ScreenId(3)), Is.False, "após Dispose não observa mais");
        }

        [Test]
        public void Undo_RecreatesRemovedView()
        {
            var store = new WorkspaceStore();
            var factory = new FakeFactory();
            var sync = new ScreenViewSynchronizer(store, factory);
            sync.Start();
            store.Dispatch(Create());
            store.Dispatch(new RemoveScreen(new ScreenId(1)));
            Assert.That(sync.ViewCount, Is.EqualTo(0));

            store.Undo();
            Assert.That(sync.ViewCount, Is.EqualTo(1));
            Assert.That(factory.Views[new ScreenId(1)].Disposed, Is.False);
            sync.Dispose();
        }
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: FAIL de compilação — `The type or namespace name 'Screens' does not exist in the namespace 'ScreenPortal.App'`.

- [ ] **Step 3: Implementar**

`Assets/_Project/Application/Screens/IScreenView.cs`:
```csharp
using System;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Screens
{
    /// <summary>View passiva de uma tela (brief §8.3): só reflete o estado; nunca o altera.</summary>
    public interface IScreenView : IDisposable
    {
        void Apply(VirtualScreen screen);
    }
}
```

`Assets/_Project/Application/Screens/IScreenViewFactory.cs`:
```csharp
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Screens
{
    /// <summary>Porta implementada pela Presentation (ex.: OVROverlay). A view recebe Apply logo após criada.</summary>
    public interface IScreenViewFactory
    {
        IScreenView Create(VirtualScreen screen);
    }
}
```

`Assets/_Project/Application/Screens/ScreenViewSynchronizer.cs`:
```csharp
using System;
using System.Collections.Generic;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Screens
{
    /// <summary>
    /// Observer do <see cref="WorkspaceStore"/>: cria, atualiza e descarta views para que elas espelhem
    /// o estado. Como <see cref="VirtualScreen"/> é imutável, "mudou" = outra instância (comparação por referência).
    /// </summary>
    public sealed class ScreenViewSynchronizer : IDisposable
    {
        private readonly WorkspaceStore _store;
        private readonly IScreenViewFactory _factory;
        private readonly Dictionary<ScreenId, Entry> _entries = new Dictionary<ScreenId, Entry>();
        private readonly List<ScreenId> _stale = new List<ScreenId>();
        private bool _started;

        private struct Entry
        {
            public IScreenView View;
            public VirtualScreen Applied;
            public bool Seen;
        }

        public int ViewCount => _entries.Count;

        public ScreenViewSynchronizer(WorkspaceStore store, IScreenViewFactory factory)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public void Start()
        {
            if (_started) return;
            _started = true;
            _store.StateChanged += OnStateChanged;
            Sync(_store.State);
        }

        private void OnStateChanged(Workspace previous, Workspace current) => Sync(current);

        private void Sync(Workspace workspace)
        {
            var screens = workspace.Screens;
            for (int i = 0; i < screens.Count; i++)
            {
                var screen = screens[i];
                if (_entries.TryGetValue(screen.Id, out var entry))
                {
                    if (!ReferenceEquals(entry.Applied, screen))
                    {
                        entry.View.Apply(screen);
                        entry.Applied = screen;
                    }
                    entry.Seen = true;
                    _entries[screen.Id] = entry;
                }
                else
                {
                    var view = _factory.Create(screen);
                    view.Apply(screen);
                    _entries[screen.Id] = new Entry { View = view, Applied = screen, Seen = true };
                }
            }

            _stale.Clear();
            foreach (var pair in _entries)
                if (!pair.Value.Seen) _stale.Add(pair.Key);
            for (int i = 0; i < _stale.Count; i++)
            {
                _entries[_stale[i]].View.Dispose();
                _entries.Remove(_stale[i]);
            }

            _stale.Clear();
            foreach (var key in _entries.Keys) _stale.Add(key);
            for (int i = 0; i < _stale.Count; i++)
            {
                var entry = _entries[_stale[i]];
                entry.Seen = false;
                _entries[_stale[i]] = entry;
            }
        }

        public void Dispose()
        {
            if (_started) _store.StateChanged -= OnStateChanged;
            _started = false;
            foreach (var pair in _entries) pair.Value.View.Dispose();
            _entries.Clear();
        }
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: `Com falha: 0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Application/Screens Assets/_Project/Tests/EditMode/ScreenViewSynchronizerTests.cs
git commit -m "phase1: view ports and ScreenViewSynchronizer (store → views, observer)"
```

---

### Task 7: Ferramentas do Editor — registro de comandos e testes EditMode pela Unity

**Files:**
- Create: `Assets/_Project/Editor/ScreenPortal.Editor.asmdef`, `EditorCommands.cs`, `FileCommandWatcher.cs`, `TestRunnerCommand.cs`, `BuildScenes.cs`
- Delete: `Assets/_Project/Spikes/Phase0/Editor/FileCommandWatcher.cs` (e o `.meta`)
- Modify: `Assets/_Project/Spikes/Phase0/Editor/ScreenPortal.Spikes.Phase0.Editor.asmdef` (referenciar `ScreenPortal.Editor`)
- Modify: `Assets/_Project/Spikes/Phase0/Editor/Phase0SceneBuilder.cs` (registrar comandos; usar `BuildScenes.SetFirst`)
- Modify: `tools/unity-cmd.ps1` (lista de comandos no cabeçalho)

**Interfaces:**
- Produces (namespace `ScreenPortal.Editor`):
  - `static class EditorCommands` com `void Register(string name, Func<string> run)`, `void RegisterAsync(string name, Action<Action<string>> run)`, `bool TryRun(string name, Action<string> done)`.
  - `static class BuildScenes` com `void SetFirst(string scenePath)` (põe a cena em primeiro, mantém as demais existentes, adiciona se faltar).
  - Comandos por arquivo: `refresh`, `test-editmode`; o spike registra `build-apk`, `build-scenes`, `boot-video`, `boot-passthrough`.

- [ ] **Step 1: Criar o asmdef e o registro**

`Assets/_Project/Editor/ScreenPortal.Editor.asmdef`:
```json
{
    "name": "ScreenPortal.Editor",
    "rootNamespace": "ScreenPortal.Editor",
    "references": [
        "UnityEditor.TestRunner",
        "UnityEngine.TestRunner"
    ],
    "includePlatforms": ["Editor"],
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

`Assets/_Project/Editor/EditorCommands.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace ScreenPortal.Editor
{
    /// <summary>
    /// Registro dos comandos disparáveis por arquivo (tools/unity-cmd.ps1). Cada módulo de Editor registra os
    /// seus em [InitializeOnLoadMethod]; o registro é refeito a cada domain reload.
    /// </summary>
    public static class EditorCommands
    {
        private static readonly Dictionary<string, Action<Action<string>>> s_commands =
            new Dictionary<string, Action<Action<string>>>(StringComparer.OrdinalIgnoreCase);

        public static void Register(string name, Func<string> run) => s_commands[name] = done => done(run());

        /// <summary>Para comandos que terminam depois (ex.: testes): chame <c>done(resultado)</c> ao final.</summary>
        public static void RegisterAsync(string name, Action<Action<string>> run) => s_commands[name] = run;

        public static bool TryRun(string name, Action<string> done)
        {
            if (!s_commands.TryGetValue(name, out var run)) return false;
            run(done);
            return true;
        }

        public static IEnumerable<string> Names => s_commands.Keys;
    }
}
```

`Assets/_Project/Editor/FileCommandWatcher.cs`:
```csharp
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ScreenPortal.Editor
{
    /// <summary>
    /// Gatilho por arquivo para automação sem MCP (o MCP trava com diálogos modais do Editor).
    /// Arquivo vazio em Temp/sp-commands/&lt;comando&gt; → executa → resultado em &lt;comando&gt;.done.
    /// </summary>
    [InitializeOnLoad]
    internal static class FileCommandWatcher
    {
        public const string Dir = "Temp/sp-commands";
        private static double s_nextPoll;

        static FileCommandWatcher()
        {
            Directory.CreateDirectory(Dir);
            EditorApplication.update += Poll;
            EditorCommands.Register("refresh", () =>
            {
                EditorApplication.delayCall += () => AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                return "OK (refresh agendado; veja 'Reloading assemblies' no Editor.log)";
            });
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < s_nextPoll) return;
            s_nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer) return;
            if (!Directory.Exists(Dir)) return;

            foreach (var path in Directory.GetFiles(Dir))
            {
                if (Path.GetExtension(path) != string.Empty) continue; // ignora .done
                string command = Path.GetFileName(path);
                File.Delete(path);
                Run(command);
                return; // um por tick: comandos podem recarregar o domínio
            }
        }

        private static void Run(string command)
        {
            Debug.Log("[ScreenPortal] CMD " + command);
            try
            {
                if (!EditorCommands.TryRun(command, result => Done(command, result)))
                    Done(command, "ERRO: comando desconhecido. Disponíveis: " + string.Join(", ", EditorCommands.Names));
            }
            catch (Exception e)
            {
                Done(command, "ERRO: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void Done(string command, string result)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, command + ".done"), DateTime.Now.ToString("s") + " " + result);
            Debug.Log("[ScreenPortal] CMD " + command + " → " + result);
        }
    }
}
```

`Assets/_Project/Editor/TestRunnerCommand.cs`:
```csharp
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace ScreenPortal.Editor
{
    /// <summary>Comando "test-editmode": roda ScreenPortal.Tests.EditMode pelo Test Runner da Unity.</summary>
    internal static class TestRunnerCommand
    {
        [InitializeOnLoadMethod]
        private static void RegisterCommand() => EditorCommands.RegisterAsync("test-editmode", Run);

        private static void Run(Action<string> done)
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks(done, api));
            api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                assemblyNames = new[] { "ScreenPortal.Tests.EditMode" },
            }));
        }

        private sealed class Callbacks : ICallbacks
        {
            private readonly Action<string> _done;
            private readonly TestRunnerApi _api;
            private readonly List<string> _failures = new List<string>();

            public Callbacks(Action<string> done, TestRunnerApi api)
            {
                _done = done;
                _api = api;
            }

            public void RunStarted(ITestAdaptor testsToRun) { }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren && result.TestStatus == TestStatus.Failed)
                    _failures.Add(result.Test.FullName + ": " + result.Message);
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                _api.UnregisterCallbacks(this);
                string summary = $"{result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped";
                if (_failures.Count > 0) summary += " | " + string.Join(" | ", _failures);
                _done(summary);
            }
        }
    }
}
```

`Assets/_Project/Editor/BuildScenes.cs`:
```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ScreenPortal.Editor
{
    public static class BuildScenes
    {
        /// <summary>Põe <paramref name="scenePath"/> como cena inicial do build, mantendo as demais.</summary>
        public static void SetFirst(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(scenePath, true) };
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.path != scenePath) scenes.Add(scene);
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[ScreenPortal] Cena inicial do build: " + scenePath);
        }
    }
}
```

- [ ] **Step 2: Migrar o spike para o registro**

Deletar `Assets/_Project/Spikes/Phase0/Editor/FileCommandWatcher.cs` e `FileCommandWatcher.cs.meta`.

Em `Assets/_Project/Spikes/Phase0/Editor/ScreenPortal.Spikes.Phase0.Editor.asmdef`, trocar a lista `references` por:
```json
    "references": [
        "ScreenPortal.Spikes.Phase0",
        "ScreenPortal.Editor",
        "Oculus.VR",
        "Oculus.VR.Editor",
        "meta.xr.mrutilitykit"
    ],
```

Em `Assets/_Project/Spikes/Phase0/Editor/Phase0SceneBuilder.cs`, substituir o método `SetBootScene` inteiro por:
```csharp
        private static void SetBootScene(string first)
        {
            // Garante que as duas cenas do spike estejam no build e põe a escolhida em primeiro.
            ScreenPortal.Editor.BuildScenes.SetFirst(first == VideoScenePath ? PassthroughScenePath : VideoScenePath);
            ScreenPortal.Editor.BuildScenes.SetFirst(first);
        }

        [InitializeOnLoadMethod]
        private static void RegisterFileCommands()
        {
            ScreenPortal.Editor.EditorCommands.Register("build-scenes", () => { BuildAll(); return "OK"; });
            ScreenPortal.Editor.EditorCommands.Register("boot-video", () => { BootVideo(); return "OK"; });
            ScreenPortal.Editor.EditorCommands.Register("boot-passthrough", () => { BootPassthrough(); return "OK"; });
            ScreenPortal.Editor.EditorCommands.Register("build-apk", BuildApkNow);
        }
```

Em `tools/unity-cmd.ps1`, trocar a linha de comentário `# Comandos: ...` por:
```powershell
# Comandos: refresh, test-editmode, build-main-scene, boot-main, build-scenes, boot-video, boot-passthrough, build-apk
```

- [ ] **Step 3: Compilar na Unity e rodar os testes EditMode pela Unity**

Run:
```powershell
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command refresh -TimeoutMinutes 3
# aguardar "Reloading assemblies" no Editor.log (a mudança de asmdef recompila tudo)
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command test-editmode -TimeoutMinutes 5
```
Expected: `... <N> passed, 0 failed, 0 skipped`, com N igual ao número de testes do `dotnet test` (os mesmos arquivos). Se o `refresh` não for consumido em 3 min, o watcher antigo foi apagado antes do novo compilar: clicar na janela da Unity para disparar o auto-refresh e repetir.

- [ ] **Step 4: Commit**

```bash
git add Assets/_Project/Editor Assets/_Project/Spikes tools/unity-cmd.ps1
git commit -m "phase1: editor command registry, file watcher moved to ScreenPortal.Editor, test-editmode command"
```

---

### Task 8: VContainer, Presentation (OVROverlay views), Composition e cena Main

**Files:**
- Modify: `Packages/manifest.json` (adicionar VContainer)
- Create: `Assets/_Project/Presentation/ScreenPortal.Presentation.asmdef`, `DomainConversions.cs`, `Screens/OverlayScreenView.cs`, `Screens/OverlayScreenViewFactory.cs`
- Create: `Assets/_Project/Composition/ScreenPortal.Composition.asmdef`, `AppOptions.cs`, `AppLifetimeScope.cs`, `AppEntryPoint.cs`
- Create: `Assets/_Project/Editor/MainSceneBuilder.cs` (e ajustar `ScreenPortal.Editor.asmdef`)
- Generated: `Assets/_Project/Scenes/Main.unity`

**Interfaces:**
- Consumes: `WorkspaceStore`, `CommandBus`, `ICommandBus`, `ICommandObserver`, `CommandJournal`, `ScreenViewSynchronizer`, `IScreenView`, `IScreenViewFactory` (Tasks 5–6); `CreateScreen`, `Resolution`, `Pose`, `Vec3`, `Quat` (Tasks 1–4).
- Produces: `ScreenPortal.Presentation.DomainConversions.ToUnity(this Vec3)` → `Vector3`, `ToUnity(this Quat)` → `Quaternion`; `OverlayScreenViewFactory(Transform root)`; `ScreenPortal.Composition.AppLifetimeScope` (campos serializados `screensRoot`, `seedDemoScreen`); comandos de arquivo `build-main-scene` e `boot-main`.

- [ ] **Step 1: Instalar o VContainer**

Em `Packages/manifest.json`, adicionar dentro de `"dependencies"` (logo após `"com.unity.xr.openxr": "1.18.0",`):
```json
    "jp.hadashikick.vcontainer": "https://github.com/hadashiA/VContainer.git?path=VContainer/Assets/VContainer#1.19.0",
```

- [ ] **Step 2: Presentation**

`Assets/_Project/Presentation/ScreenPortal.Presentation.asmdef`:
```json
{
    "name": "ScreenPortal.Presentation",
    "rootNamespace": "ScreenPortal.Presentation",
    "references": [
        "ScreenPortal.Domain",
        "ScreenPortal.Application",
        "Oculus.VR"
    ],
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

`Assets/_Project/Presentation/DomainConversions.cs`:
```csharp
using ScreenPortal.Domain.Spatial;
using UnityEngine;

namespace ScreenPortal.Presentation
{
    /// <summary>Única fronteira entre os tipos espaciais do domínio e os da Unity (mesma convenção de eixos).</summary>
    public static class DomainConversions
    {
        public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        public static Quaternion ToUnity(this Quat q) => new Quaternion(q.X, q.Y, q.Z, q.W);
        public static Vec3 ToDomain(this Vector3 v) => new Vec3(v.x, v.y, v.z);
        public static Quat ToDomain(this Quaternion q) => new Quat(q.x, q.y, q.z, q.w);
    }
}
```

`Assets/_Project/Presentation/Screens/OverlayScreenView.cs`:
```csharp
using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Screens;
using UnityEngine;

namespace ScreenPortal.Presentation.Screens
{
    /// <summary>
    /// Tela como compositor layer (OVROverlay quad, external surface, brief §6.3). Nesta fase só reflete
    /// pose e tamanho; o vídeo é ligado na Fase 2 (a Surface fica em <see cref="Overlay"/>.externalSurfaceObject).
    /// Curvatura (layer cilíndrica) também é Fase 2.
    /// </summary>
    public sealed class OverlayScreenView : IScreenView
    {
        private readonly GameObject _root;
        private Resolution _resolution;

        public OVROverlay Overlay { get; private set; }

        public OverlayScreenView(Transform parent, VirtualScreen screen)
        {
            _root = new GameObject("Screen " + screen.Id.Value);
            _root.transform.SetParent(parent, false);
            BuildOverlay(screen.Resolution);
        }

        public void Apply(VirtualScreen screen)
        {
            // Resolução só muda via RecreateScreen (raro): recria a layer com a nova external surface.
            if (!screen.Resolution.Equals(_resolution)) BuildOverlay(screen.Resolution);

            var t = _root.transform;
            t.SetLocalPositionAndRotation(screen.Pose.Position.ToUnity(), screen.Pose.Rotation.ToUnity());
            t.localScale = new Vector3(screen.WidthMeters, screen.HeightMeters, 1f);
            _root.name = string.IsNullOrEmpty(screen.Label) ? "Screen " + screen.Id.Value : screen.Label;
        }

        private void BuildOverlay(Resolution resolution)
        {
            if (Overlay != null) Object.Destroy(Overlay);
            _resolution = resolution;

            _root.SetActive(false); // configurar antes do OnEnable criar a layer
            var overlay = _root.AddComponent<OVROverlay>();
            overlay.currentOverlayType = OVROverlay.OverlayType.Overlay;
            overlay.currentOverlayShape = OVROverlay.OverlayShape.Quad;
            overlay.isExternalSurface = true;
            overlay.externalSurfaceWidth = resolution.Width;
            overlay.externalSurfaceHeight = resolution.Height;
            overlay.useBicubicFiltering = true; // padrão validado no teste de texto (fase0-texto.md)
            Overlay = overlay;
            _root.SetActive(true);
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root);
        }
    }
}
```

`Assets/_Project/Presentation/Screens/OverlayScreenViewFactory.cs`:
```csharp
using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Screens;
using UnityEngine;

namespace ScreenPortal.Presentation.Screens
{
    public sealed class OverlayScreenViewFactory : IScreenViewFactory
    {
        private readonly Transform _root;

        public OverlayScreenViewFactory(Transform root) => _root = root;

        public IScreenView Create(VirtualScreen screen) => new OverlayScreenView(_root, screen);
    }
}
```

- [ ] **Step 3: Composition**

`Assets/_Project/Composition/ScreenPortal.Composition.asmdef`:
```json
{
    "name": "ScreenPortal.Composition",
    "rootNamespace": "ScreenPortal.Composition",
    "references": [
        "ScreenPortal.Domain",
        "ScreenPortal.Application",
        "ScreenPortal.Presentation",
        "VContainer"
    ],
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

`Assets/_Project/Composition/AppOptions.cs`:
```csharp
namespace ScreenPortal.Composition
{
    public sealed class AppOptions
    {
        /// <summary>Cria uma tela de demonstração ao iniciar (até o seletor "Novo monitor" da Fase 2 existir).</summary>
        public bool SeedDemoScreen { get; }

        public AppOptions(bool seedDemoScreen) => SeedDemoScreen = seedDemoScreen;
    }
}
```

`Assets/_Project/Composition/AppEntryPoint.cs`:
```csharp
using System;
using ScreenPortal.App;
using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;
using VContainer.Unity;

namespace ScreenPortal.Composition
{
    /// <summary>Liga o sincronizador de views e, em modo demo, cria a tela padrão validada na Fase 0.</summary>
    public sealed class AppEntryPoint : IStartable, IDisposable
    {
        private readonly ScreenViewSynchronizer _synchronizer;
        private readonly ICommandBus _bus;
        private readonly AppOptions _options;

        public AppEntryPoint(ScreenViewSynchronizer synchronizer, ICommandBus bus, AppOptions options)
        {
            _synchronizer = synchronizer;
            _bus = bus;
            _options = options;
        }

        public void Start()
        {
            _synchronizer.Start();
            if (_options.SeedDemoScreen)
            {
                // Tamanho/distância padrão do teste de legibilidade (fase0-texto.md): 140 cm a 1,3 m.
                _bus.Send(new CreateScreen(Resolution.Create(2560, 1080), Pose.At(new Vec3(0f, 1.2f, 1.3f)), 1.4f, label: "Demo"));
            }
        }

        public void Dispose() => _synchronizer.Dispose();
    }
}
```

`Assets/_Project/Composition/AppLifetimeScope.cs`:
```csharp
using ScreenPortal.App;
using ScreenPortal.App.Screens;
using ScreenPortal.Presentation.Screens;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace ScreenPortal.Composition
{
    /// <summary>Raiz de composição (brief §8.4): o único lugar que conhece as implementações concretas.</summary>
    public sealed class AppLifetimeScope : LifetimeScope
    {
        [SerializeField] private Transform screensRoot;
        [SerializeField] private bool seedDemoScreen = true;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterInstance(new AppOptions(seedDemoScreen));
            builder.RegisterInstance(new WorkspaceStore());
            builder.Register<CommandJournal>(Lifetime.Singleton).As<ICommandObserver>().AsSelf();
            builder.Register<CommandBus>(Lifetime.Singleton).As<ICommandBus>();
            builder.RegisterInstance(new OverlayScreenViewFactory(screensRoot != null ? screensRoot : transform))
                .As<IScreenViewFactory>();
            builder.Register<ScreenViewSynchronizer>(Lifetime.Singleton);
            builder.RegisterEntryPoint<AppEntryPoint>();
        }
    }
}
```

- [ ] **Step 4: Gerador da cena Main**

Em `Assets/_Project/Editor/ScreenPortal.Editor.asmdef`, trocar `references` por:
```json
    "references": [
        "UnityEditor.TestRunner",
        "UnityEngine.TestRunner",
        "ScreenPortal.Composition",
        "Oculus.VR"
    ],
```

`Assets/_Project/Editor/MainSceneBuilder.cs`:
```csharp
using ScreenPortal.Composition;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ScreenPortal.Editor
{
    /// <summary>Gera Assets/_Project/Scenes/Main.unity: OVRCameraRig + raiz das telas + AppLifetimeScope.</summary>
    public static class MainSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        private const string CameraRigPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";

        [MenuItem("Screen Portal/Main/Build Main Scene")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Scenes")) AssetDatabase.CreateFolder("Assets/_Project", "Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var rigGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CameraRigPrefab));
            var manager = rigGo.GetComponent<OVRManager>();
            manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            var cam = rigGo.GetComponent<OVRCameraRig>().centerEyeAnchor.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;

            var screensRoot = new GameObject("Screens");

            var scopeGo = new GameObject("AppLifetimeScope");
            var scope = scopeGo.AddComponent<AppLifetimeScope>();
            var so = new SerializedObject(scope);
            so.FindProperty("screensRoot").objectReferenceValue = screensRoot.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[ScreenPortal] Cena Main gerada: " + ScenePath);
        }

        [MenuItem("Screen Portal/Main/Boot scene: Main")]
        public static void BootMain() => BuildScenes.SetFirst(ScenePath);

        [InitializeOnLoadMethod]
        private static void RegisterFileCommands()
        {
            EditorCommands.Register("build-main-scene", () => { Build(); return "OK " + ScenePath; });
            EditorCommands.Register("boot-main", () => { BootMain(); return "OK"; });
        }
    }
}
```

- [ ] **Step 5: Resolver pacote, compilar, gerar a cena e rodar os testes na Unity**

Run:
```powershell
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command refresh -TimeoutMinutes 5
# aguardar o Package Manager resolver o VContainer e "Reloading assemblies" no Editor.log, sem "error CS"
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command build-main-scene -TimeoutMinutes 2
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command test-editmode -TimeoutMinutes 5
```
Expected: `OK Assets/_Project/Scenes/Main.unity` e `<N> passed, 0 failed`. Conferir `Packages/packages-lock.json` com `jp.hadashikick.vcontainer` na versão 1.19.0.

- [ ] **Step 6 (verificação no headset, opcional nesta fase): cena Main no Quest**

Run:
```powershell
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command boot-main
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command build-apk -TimeoutMinutes 30
& "D:\Unity Editor\6000.3.19f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" -s 2G0YC1ZG410B1H install -r -g Builds/Phase0.apk
& "D:\Unity Editor\6000.3.19f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" -s 2G0YC1ZG410B1H logcat -d -s Unity:I | Select-String "Exception|ScreenPortal"
```
Expected: app abre sem exceções no logcat. (A tela Demo não mostra vídeo ainda; a layer existe.) Depois voltar o boot para o spike de vídeo se ainda for usado: `-Command boot-video`.

- [ ] **Step 7: Commit**

```bash
git add Packages/manifest.json Packages/packages-lock.json Assets/_Project ProjectSettings/EditorBuildSettings.asset
git commit -m "phase1: VContainer composition root, OVROverlay screen views, Main scene builder"
```

---

### Task 9: ADR 0004, CLAUDE.md e STATUS

**Files:**
- Create: `docs/adr/0004-immutable-workspace-and-shared-nunit-tests.md`
- Modify: `CLAUDE.md` (seção "Toolchain" / comandos)
- Modify: `docs/STATUS.md`

- [ ] **Step 1: Escrever o ADR**

`docs/adr/0004-immutable-workspace-and-shared-nunit-tests.md`:
```markdown
# ADR 0004 — Workspace imutável com undo por snapshots; testes NUnit compartilhados Unity ↔ dotnet

- **Status:** Aceito (Fase 1A)
- **Contexto:** o brief (§8.2) pede Commands, fonte única da verdade, undo/redo e perfis como Memento.
  O MCP da Unity trava com diálogos modais, então depender só do Test Runner do Editor deixa o TDD lento e frágil.
- **Decisão:**
  - `Workspace` e `VirtualScreen` são imutáveis; cada `ICommand` é uma função pura `Workspace → Workspace`.
    O `WorkspaceStore` guarda snapshots (até 100) para undo/redo; `SetFocus` não entra no histórico.
  - Views observam o store via `ScreenViewSynchronizer` (Application) usando as portas `IScreenView`/`IScreenViewFactory`;
    "mudou" = outra instância de `VirtualScreen` (comparação por referência, sem diff campo a campo).
  - Domain e Application têm `noEngineReferences: true`. Os testes deles usam só NUnit e são compilados
    **também** por `tests-dotnet/UnityPureCode.Tests` (`dotnet test`, `LangVersion 9.0`), além do Test Runner
    da Unity (`tools/unity-cmd.ps1 -Command test-editmode`).
- **Consequências:**
  - ✅ Loop de TDD em segundos, sem Editor; o compilador do harness barra UnityEngine e recursos acima do C# 9.
  - ✅ Undo/redo e perfis triviais (snapshots); fácil serializar o estado para o host (Fase 1B).
  - ⚠️ Cada comando aloca (novos arrays/objetos). Aceitável: comandos vêm de ações do usuário, não do caminho
    por frame. Arrastes contínuos usam preview local e geram UM comando ao soltar (brief §10.10).
  - ⚠️ Os testes precisam ficar restritos a NUnit 3.5 (a versão da Unity): usar `Assert.That` com constraints.
```

- [ ] **Step 2: Atualizar o CLAUDE.md**

Em `CLAUDE.md`, na seção "## Toolchain desta máquina", substituir a linha que começa com `Comandos:` por:
```markdown
Comandos:
- Testes rápidos (Domain/Application, sem Editor): `dotnet test tests-dotnet/UnityPureCode.Tests`
- Unity sem MCP: `tools/unity-cmd.ps1 -Command refresh|test-editmode|build-main-scene|boot-main|build-apk|…`
- Host: `tools/host-loopback-test.ps1` · `.aar`: `tools/build-native.ps1` · vídeo no Quest: `tools/phase0-streams-test.ps1`
```

- [ ] **Step 3: Atualizar o STATUS**

Em `docs/STATUS.md`, substituir a seção "## Próximo: Fase 1" inteira por:
```markdown
## Fase 1
- **1A (esqueleto + domínio):** ✅ asmdefs Domain/Application/Presentation/Composition/Editor, Workspace imutável,
  9 Commands com invariantes testados, WorkspaceStore (undo/redo), CommandBus + CommandJournal,
  ScreenViewSynchronizer, VContainer + cena `Main` com views OVROverlay. Plano: `docs/plans/2026-09-28-fase1a-esqueleto-dominio.md`.
- **1B (próximo):** schemas FlatBuffers do canal de controle, handshake versionado, state machine de conexão, mDNS.
```

- [ ] **Step 4: Rodar tudo uma última vez**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q` e `powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command test-editmode -TimeoutMinutes 5`
Expected: os dois com `0 failed` e o mesmo número de testes.

- [ ] **Step 5: Commit**

```bash
git add docs CLAUDE.md
git commit -m "docs: ADR 0004 (immutable workspace, shared NUnit tests), CLAUDE.md commands, STATUS for Phase 1A"
```
