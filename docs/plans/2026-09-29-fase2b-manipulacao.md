# Fase 2B — Superfície de layout + manipulação por controle/mãos + undo — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Telas vivem numa superfície Cilindro/Esfera e são movidas, afastadas, soltas e redimensionadas por controle ou mãos, com um passo de undo por gesto, menu de pulso e telas de teste — tudo verificável no Meta XR Simulator.

**Architecture:** Parte A é código puro (Domain + Application, testado no `dotnet test` e no Test Runner): modelo da superfície (`LayoutSurface`, `Placement`, `ToPose`), comandos, encaixe angular, teste de acerto do raio, o `ScreenManipulationController` (State Machine) e o menu. Parte B é Unity: views (overlay e padrão de teste), apresentador da pré-visualização/moldura/guias, painel do menu, adaptadores OVR (controle/mãos → `IManipulationInput`), composição e cena. O Interaction SDK não é usado: a lógica de acerto é pura (ver "Desvios").

**Tech Stack:** Unity 6.3 (C# 9), Meta XR Core SDK 207 (`OVRInput`, `OVRHand`, `OVRManager.display.RecenteredPose`), VContainer 1.19, NUnit, .NET 10 harness.

## Global Constraints

- Specs: `docs/specs/2026-09-29-fase2b-manipulacao-design.md` e `docs/specs/2026-09-28-superficie-layout-design.md`.
- **C# 9** (LangVersion da Unity): sem `record`, `init`, namespaces de arquivo. Namespaces em bloco, como o código atual.
- Domain e Application **nunca** referenciam UnityEngine. Toda mudança de estado é Command via `ICommandBus`; views nunca mudam estado.
- **Zero alocação** por quadro no controlador, no roteador e no apresentador (sem LINQ, closures, `new` de classe, strings montadas por quadro).
- Convenção Unity: +Y cima, +Z frente, azimute positivo = direita. Face visível da tela olha para o usuário ⇔ `transform.forward` aponta para longe dele.
- Limites: distância 0,4–5 m; elevação −60°…+75°; tilt 0°–60°; passo da grade 1°–30°; encaixe a ≤ 1,5°.
- Tela nova: azimute 0°, elevação −5°, 1,3 m; se ocupado, primeiro espaço livre à direita (1° de folga).
- Centro padrão da superfície: (0; 1,2; 0) — modo Cilindro, heading 0.
- Moldura: faixa de 3 cm fora da borda; alças: quadrados de 6 cm nos cantos; meio da tela não manipula.
- Controle: gatilho > 0,55 = selecionar; grip > 0,55 = solto; analógico Y (zona morta 0,2) × 1,5 m/s = distância; B (direito)/Y (esquerdo) = cancelar no arraste; X = desfazer, Y = refazer fora do arraste; Menu (≡) esquerdo segura o menu.
- Mãos: pinça do indicador = selecionar; mão fechada (médio e anelar > 0,9) = solto; pinça da outra mão = sem encaixe; puxar/empurrar ×3 = distância.
- Testes: `dotnet test tests-dotnet/UnityPureCode.Tests` a cada tarefa da Parte A; `dotnet test host/Host.Tests` quando tocar Domain/Application (o host compila esse código via `Host.Shared`).
- Branch `phase2b/manipulation`. Commits pequenos. Context-mode para saídas grandes.

## Desvios da spec (avisar o Cayo; atualizar a spec na Task 14)

1. **Acerto do raio puro** (raio × retângulo em `ScreenHitTest`), sem colliders do Interaction SDK: o ISDK só forneceria raio + selecionar, que já vêm de `OVRHand.PointerPose`/pinça e `OVRInput`. Ganho: moldura, alças e menu testáveis sem Unity.
2. **Sem `CompositeCommand`:** `MoveScreenOnSurface(id, az, el, distância)` carrega a distância, então mover + afastar já é **um** comando (um passo de undo), que era o objetivo.
3. **"Voltar à superfície"** fica no menu de pulso (age na tela em foco), não num botão na moldura.
4. **Menu acionado pelo raio** da mão/controle direito (sem poke direto).

---

## File Structure

```
Assets/_Project/Domain/
  Spatial/Vec3.cs, Quat.cs                 + operadores, AngleAxis, LookRotation, Inverse
  Layout/Angles.cs                         conversões grau/radiano
  Layout/LayoutMode.cs, SnapOptions.cs, Placement.cs, LayoutSurface.cs, CylinderMath.cs
  Layout/AngularRect.cs, ScreenAngles.cs, SurfaceSnapper.cs, SurfaceSlots.cs
  Screens/VirtualScreen.cs                 Pose → Placement
  Workspace.cs                             + Surface
  Commands/ScreenCommand.cs                Transform(screen, state) + Validate
  Commands/CreateScreen.cs                 Placement? (null = primeiro espaço livre)
  Commands/MoveScreen.cs                   REMOVIDO
  Commands/SetLayoutMode.cs, RecenterLayout.cs, AlignSurfaceToPosture.cs, SetSurfaceTilt.cs,
           MoveScreenOnSurface.cs, SetScreenDistance.cs, SetScreenLoose.cs, ReturnScreenToSurface.cs, SetSnapOptions.cs
Assets/_Project/Application/
  Screens/IScreenView.cs, ScreenViewSynchronizer.cs   pose derivada da superfície
  Connection/ConnectionOptions.cs, ConnectionStateMachine.cs   sem DefaultScreenPose
  Manipulation/ManipulationInput.cs, ScreenHitTest.cs, SurfaceRay.cs, ManipulationPreview.cs,
               ScreenManipulationController.cs, InteractionRouter.cs
  Menu/MenuLayout.cs, WristMenu.cs, TestScreens.cs, Ports.cs
Assets/_Project/Presentation/
  Screens/OverlayScreenView.cs, OverlayScreenViewFactory.cs, TestPatternScreenView.cs, TestPatternTexture.cs
  Manipulation/ManipulationPresenter.cs
  Menu/WristMenuPanel.cs
Assets/_Project/Infrastructure/Unity/
  OvrPointerInput.cs, OvrHeadPose.cs, OvrRecenterEvents.cs
Assets/_Project/Composition/  AppEnvironment.cs, AppInstaller.cs, AppEntryPoint.cs, AppLifetimeScope.cs
Assets/_Project/Editor/MainSceneBuilder.cs      mãos, material, menu
Assets/_Project/Tests/EditMode/Layout/*.cs, Manipulation/*.cs   testes novos
```

---

# Parte A — código puro

### Task 1: Matemática espacial (Vec3/Quat)

**Files:**
- Modify: `Assets/_Project/Domain/Spatial/Vec3.cs`, `Assets/_Project/Domain/Spatial/Quat.cs`
- Test: `Assets/_Project/Tests/EditMode/Layout/SpatialMathTests.cs`

**Interfaces:**
- Produces: `Vec3.Up/Forward/Right`, operadores `+ - *` (vetor e escalar), `Vec3.Dot`, `Vec3.Cross`, `v.Length`, `v.Normalized`; `Quat * Quat`, `Quat * Vec3`, `q.Inverse`, `Quat.AngleAxis(float degrees, Vec3 axis)`, `Quat.LookRotation(Vec3 forward, Vec3 up)`.

- [ ] **Step 1: Branch e teste que falha**

```powershell
git switch -c phase2b/manipulation
```

`Assets/_Project/Tests/EditMode/Layout/SpatialMathTests.cs`:
```csharp
using System;
using NUnit.Framework;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class SpatialMathTests
    {
        internal static void AssertNear(Vec3 actual, Vec3 expected, float tolerance = 1e-4f, string message = "")
        {
            Assert.That((actual - expected).Length, Is.LessThanOrEqualTo(tolerance), $"{message} esperado {expected}, veio {actual}");
        }

        internal static float AngleDeg(Vec3 a, Vec3 b)
        {
            double cos = Vec3.Dot(a.Normalized, b.Normalized);
            return (float)(Math.Acos(Math.Max(-1.0, Math.Min(1.0, cos))) * 180.0 / Math.PI);
        }

        [Test]
        public void AngleAxis_YawPositive_TurnsForwardToTheRight() =>
            AssertNear(Quat.AngleAxis(90f, Vec3.Up) * Vec3.Forward, Vec3.Right);

        [Test]
        public void AngleAxis_PitchPositive_TurnsForwardDown() =>
            AssertNear(Quat.AngleAxis(90f, Vec3.Right) * Vec3.Forward, new Vec3(0f, -1f, 0f));

        [Test]
        public void Multiply_ComposesRotations()
        {
            var q = Quat.AngleAxis(30f, Vec3.Up) * Quat.AngleAxis(60f, Vec3.Up);
            AssertNear(q * Vec3.Forward, Vec3.Right);
        }

        [Test]
        public void Inverse_UndoesRotation()
        {
            var q = Quat.AngleAxis(37f, new Vec3(1f, 2f, 3f));
            var v = new Vec3(0.3f, -1.2f, 2f);
            AssertNear(q.Inverse * (q * v), v);
        }

        [Test]
        public void LookRotation_PointsForwardAndKeepsUp()
        {
            var dir = new Vec3(1f, 0.5f, 2f).Normalized;
            var q = Quat.LookRotation(dir, Vec3.Up);
            AssertNear(q * Vec3.Forward, dir);
            Assert.That(Vec3.Dot(q * Vec3.Right, Vec3.Up), Is.EqualTo(0f).Within(1e-4f), "sem roll");
            var yaw = Quat.LookRotation(Vec3.Right, Vec3.Up);
            var expected = Quat.AngleAxis(90f, Vec3.Up);
            Assert.That(Math.Abs(yaw.Y), Is.EqualTo(Math.Abs(expected.Y)).Within(1e-4f));
            Assert.That(Math.Abs(yaw.W), Is.EqualTo(Math.Abs(expected.W)).Within(1e-4f));
        }

        [Test]
        public void Vec3_CrossFollowsUnityConvention() =>
            AssertNear(Vec3.Cross(Vec3.Up, Vec3.Forward), Vec3.Right);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: FAIL de compilação — `'Vec3' does not contain a definition for 'Up'`.

- [ ] **Step 3: Implementar**

Em `Assets/_Project/Domain/Spatial/Vec3.cs`, depois de `public static readonly Vec3 Zero = ...;` acrescentar:
```csharp
        public static readonly Vec3 Up = new Vec3(0f, 1f, 0f);
        public static readonly Vec3 Forward = new Vec3(0f, 0f, 1f);
        public static readonly Vec3 Right = new Vec3(1f, 0f, 0f);
```
e antes de `public bool Equals(Vec3 other)`:
```csharp
        public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);

        public Vec3 Normalized
        {
            get
            {
                float length = Length;
                return length > 1e-6f ? new Vec3(X / length, Y / length, Z / length) : Zero;
            }
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(float s, Vec3 a) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        /// <summary>Mesma fórmula do Vector3.Cross da Unity (Cross(Up, Forward) = Right).</summary>
        public static Vec3 Cross(Vec3 a, Vec3 b) =>
            new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
```

Em `Assets/_Project/Domain/Spatial/Quat.cs`, antes de `public bool Equals(Quat other)`:
```csharp
        public Quat Inverse => new Quat(-X, -Y, -Z, W);

        /// <summary>Produto de Hamilton, como o Quaternion da Unity: (a * b) aplica b e depois a.</summary>
        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        public static Vec3 operator *(Quat q, Vec3 v)
        {
            var u = new Vec3(q.X, q.Y, q.Z);
            var t = Vec3.Cross(u, v) * 2f;
            return v + t * q.W + Vec3.Cross(u, t);
        }

        /// <summary>Como Quaternion.AngleAxis: yaw positivo gira +Z para +X; pitch positivo gira +Z para baixo.</summary>
        public static Quat AngleAxis(float degrees, Vec3 axis)
        {
            var n = axis.Normalized;
            double half = degrees * Math.PI / 360.0;
            float s = (float)Math.Sin(half);
            return new Quat(n.X * s, n.Y * s, n.Z * s, (float)Math.Cos(half));
        }

        /// <summary>Como Quaternion.LookRotation: +Z local = forward, +Y local o mais próximo possível de up.</summary>
        public static Quat LookRotation(Vec3 forward, Vec3 up)
        {
            var f = forward.Normalized;
            var r = Vec3.Cross(up, f).Normalized;
            if (r.Length < 0.5f) r = Vec3.Cross(Vec3.Forward, f).Normalized; // forward paralelo a up
            if (r.Length < 0.5f) r = Vec3.Right;
            var u = Vec3.Cross(f, r);
            float m00 = r.X, m01 = u.X, m02 = f.X;
            float m10 = r.Y, m11 = u.Y, m12 = f.Y;
            float m20 = r.Z, m21 = u.Z, m22 = f.Z;
            float trace = m00 + m11 + m22;
            if (trace > 0f)
            {
                float s = (float)Math.Sqrt(trace + 1f) * 2f;
                return new Quat((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s);
            }
            if (m00 > m11 && m00 > m22)
            {
                float s = (float)Math.Sqrt(1f + m00 - m11 - m22) * 2f;
                return new Quat(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
            }
            if (m11 > m22)
            {
                float s = (float)Math.Sqrt(1f + m11 - m00 - m22) * 2f;
                return new Quat((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s);
            }
            float s2 = (float)Math.Sqrt(1f + m22 - m00 - m11) * 2f;
            return new Quat((m02 + m20) / s2, (m12 + m21) / s2, 0.25f * s2, (m10 - m01) / s2);
        }
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: `Com falha: 0`, 118 testes (112 + 6).

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Domain/Spatial Assets/_Project/Tests/EditMode/Layout
git commit -m "domain: vector/quaternion math (Unity conventions) for the layout surface"
```

---

### Task 2: Modelo da superfície

**Files:**
- Create: `Assets/_Project/Domain/Layout/Angles.cs`, `LayoutMode.cs`, `SnapOptions.cs`, `Placement.cs`, `LayoutSurface.cs`, `CylinderMath.cs`
- Test: `Assets/_Project/Tests/EditMode/Layout/LayoutSurfaceTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces: `enum LayoutMode { Cylinder, Sphere }`; `struct SnapOptions(bool neighborSnap, bool angularGrid, float gridStepDeg)` com `Default`, `WithAngularGrid(bool)`; `struct Placement` com `OnSurface(az, el, d)`, `Loose(Pose)`, `Default`, `IsLoose`, `AzimuthDeg`, `ElevationDeg`, `DistanceM`, `LoosePose`, `NormalizeAzimuth(float)`, `ClampDistance(float)`, `ClampElevation(float)`; `class LayoutSurface` com `Default`, `Mode`, `Center`, `HeadingDeg`, `TiltDeg`, `Snap`, `FrameRotation`, `Direction(az, el)`, `ToPose(Placement)`, `FromWorldPosition(Vec3)`, `CurvatureDistance(Placement)`, `HeadingOf(Quat)`, `PitchUpOf(Quat)`, `WithMode`, `WithCenter(Vec3, float)`, `WithTilt(float)`, `WithSnap(SnapOptions)`; `static class CylinderMath` com `HorizontalRadius`, `Height`, `FromAzimuthHeight(az, height, radius)`, `FromAzimuthElevationAtRadius(az, el, radius)`, `MoveVertically(p, height)`, `WithHorizontalRadius(p, radius)`; `static class Angles { DegToRad, RadToDeg }`.

- [ ] **Step 1: Teste que falha**

`Assets/_Project/Tests/EditMode/Layout/LayoutSurfaceTests.cs`:
```csharp
using NUnit.Framework;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Spatial;
using static ScreenPortal.Tests.SpatialMathTests;

namespace ScreenPortal.Tests
{
    public class LayoutSurfaceTests
    {
        private static readonly Vec3 Center = new Vec3(0.2f, 1.1f, -0.3f);

        private static LayoutSurface Surface(LayoutMode mode, float heading = 25f, float tilt = 0f) =>
            new LayoutSurface(mode, Center, heading, tilt, SnapOptions.Default);

        [Test]
        public void Cylinder_ScreensStayUpright_ForAnyPlacement()
        {
            var s = Surface(LayoutMode.Cylinder, tilt: 30f); // tilt é ignorado no Cilindro
            for (float az = -150f; az <= 150f; az += 30f)
            for (float el = -60f; el <= 75f; el += 15f)
            foreach (float d in new[] { 0.5f, 1.3f, 4f })
            {
                var pose = s.ToPose(Placement.OnSurface(az, el, d));
                AssertNear(pose.Rotation * Vec3.Up, Vec3.Up, 1e-4f, "roll/pitch");
                var toScreen = pose.Position - Center;
                var flat = new Vec3(toScreen.X, 0f, toScreen.Z);
                Assert.That(AngleDeg(pose.Rotation * Vec3.Forward, flat), Is.LessThanOrEqualTo(0.01f), "olha para o eixo");
            }
        }

        [Test]
        public void Sphere_ScreenFacesTheCenter_WithAndWithoutTilt()
        {
            foreach (float tilt in new[] { 0f, 35f, 60f })
            {
                var s = Surface(LayoutMode.Sphere, tilt: tilt);
                for (float az = -150f; az <= 150f; az += 50f)
                for (float el = -60f; el <= 75f; el += 45f)
                {
                    var pose = s.ToPose(Placement.OnSurface(az, el, 1.5f));
                    Assert.That(AngleDeg(pose.Rotation * Vec3.Forward, pose.Position - Center), Is.LessThanOrEqualTo(0.01f),
                        $"tilt {tilt} az {az} el {el}");
                }
            }
        }

        [Test]
        public void CylinderAndSphere_PlaceTheScreenAtTheSamePoint_WithoutTilt()
        {
            var p = Placement.OnSurface(40f, 20f, 2f);
            AssertNear(Surface(LayoutMode.Cylinder).ToPose(p).Position, Surface(LayoutMode.Sphere).ToPose(p).Position, 1e-4f);
        }

        [Test]
        public void DefaultPlacement_IsInFrontAndSlightlyBelow()
        {
            var pose = LayoutSurface.Default.ToPose(Placement.Default);
            Assert.That(pose.Position.Z, Is.GreaterThan(1.2f));
            Assert.That(pose.Position.Y, Is.LessThan(1.2f));
            Assert.That(pose.Position.X, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void FromWorldPosition_RoundTrips()
        {
            foreach (var mode in new[] { LayoutMode.Cylinder, LayoutMode.Sphere })
            {
                var s = Surface(mode, tilt: 20f);
                var p = Placement.OnSurface(-35f, 12f, 1.7f);
                var back = s.FromWorldPosition(s.ToPose(p).Position);
                Assert.That(back.AzimuthDeg, Is.EqualTo(-35f).Within(0.01f));
                Assert.That(back.ElevationDeg, Is.EqualTo(12f).Within(0.01f));
                Assert.That(back.DistanceM, Is.EqualTo(1.7f).Within(0.001f));
            }
        }

        [Test]
        public void CurvatureDistance_IsHorizontalRadiusOnCylinder_AndDistanceOnSphere()
        {
            var p = Placement.OnSurface(0f, 60f, 2f);
            Assert.That(Surface(LayoutMode.Cylinder).CurvatureDistance(p), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Surface(LayoutMode.Sphere).CurvatureDistance(p), Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void Limits_AreClamped()
        {
            var high = Placement.OnSurface(190f, 90f, 10f);
            Assert.That(high.ElevationDeg, Is.EqualTo(75f));
            Assert.That(high.DistanceM, Is.EqualTo(5f));
            Assert.That(high.AzimuthDeg, Is.EqualTo(-170f).Within(1e-4f));
            var low = Placement.OnSurface(0f, -80f, 0.1f);
            Assert.That(low.ElevationDeg, Is.EqualTo(-60f));
            Assert.That(low.DistanceM, Is.EqualTo(0.4f));
            Assert.That(Surface(LayoutMode.Sphere, tilt: 80f).TiltDeg, Is.EqualTo(60f));
            Assert.That(Surface(LayoutMode.Sphere, tilt: -5f).TiltDeg, Is.EqualTo(0f));
            Assert.That(new SnapOptions(true, true, 0f).GridStepDeg, Is.EqualTo(1f));
            Assert.That(new SnapOptions(true, true, 50f).GridStepDeg, Is.EqualTo(30f));
        }

        [Test]
        public void CylinderMath_MoveVertically_KeepsHorizontalRadius()
        {
            var p = Placement.OnSurface(10f, -5f, 1.3f);
            float radius = CylinderMath.HorizontalRadius(p);
            var moved = CylinderMath.MoveVertically(p, 0.6f);
            Assert.That(CylinderMath.HorizontalRadius(moved), Is.EqualTo(radius).Within(0.001f));
            Assert.That(CylinderMath.Height(moved), Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(moved.AzimuthDeg, Is.EqualTo(10f).Within(1e-4f));
        }

        [Test]
        public void CylinderMath_WithHorizontalRadius_KeepsHeight()
        {
            var p = Placement.OnSurface(0f, 20f, 1.5f);
            float height = CylinderMath.Height(p);
            var farther = CylinderMath.WithHorizontalRadius(p, 2.5f);
            Assert.That(CylinderMath.Height(farther), Is.EqualTo(height).Within(0.001f));
            Assert.That(CylinderMath.HorizontalRadius(farther), Is.EqualTo(2.5f).Within(0.001f));
        }

        [Test]
        public void HeadingAndPitchOf_ReadTheHeadRotation()
        {
            var head = Quat.AngleAxis(30f, Vec3.Up) * Quat.AngleAxis(-20f, Vec3.Right);
            Assert.That(LayoutSurface.HeadingOf(head), Is.EqualTo(30f).Within(0.01f));
            Assert.That(LayoutSurface.PitchUpOf(head), Is.EqualTo(20f).Within(0.01f));
        }
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: FAIL de compilação — `The type or namespace name 'Layout' does not exist`.

- [ ] **Step 3: Implementar**

`Assets/_Project/Domain/Layout/Angles.cs`:
```csharp
using System;

namespace ScreenPortal.Domain.Layout
{
    public static class Angles
    {
        public const double DegToRad = Math.PI / 180.0;
        public const double RadToDeg = 180.0 / Math.PI;
    }
}
```

`Assets/_Project/Domain/Layout/LayoutMode.cs`:
```csharp
namespace ScreenPortal.Domain.Layout
{
    /// <summary>Superfície de layout (brief §10.12, ADR 0005). Cilindro é o padrão.</summary>
    public enum LayoutMode
    {
        Cylinder,
        Sphere,
    }
}
```

`Assets/_Project/Domain/Layout/SnapOptions.cs`:
```csharp
using System;

namespace ScreenPortal.Domain.Layout
{
    /// <summary>Encaixe (spec superfície §3): nas vizinhas (ligado) e grade angular (desligada, passo 5°).</summary>
    public readonly struct SnapOptions : IEquatable<SnapOptions>
    {
        public const float MinGridStepDeg = 1f;
        public const float MaxGridStepDeg = 30f;
        public static readonly SnapOptions Default = new SnapOptions(true, false, 5f);

        public readonly bool NeighborSnap;
        public readonly bool AngularGrid;
        public readonly float GridStepDeg;

        public SnapOptions(bool neighborSnap, bool angularGrid, float gridStepDeg)
        {
            NeighborSnap = neighborSnap;
            AngularGrid = angularGrid;
            GridStepDeg = Placement.Clamp(gridStepDeg, MinGridStepDeg, MaxGridStepDeg);
        }

        public SnapOptions WithAngularGrid(bool on) => new SnapOptions(NeighborSnap, on, GridStepDeg);

        public bool Equals(SnapOptions other) =>
            NeighborSnap == other.NeighborSnap && AngularGrid == other.AngularGrid && GridStepDeg == other.GridStepDeg;
        public override bool Equals(object obj) => obj is SnapOptions other && Equals(other);
        public override int GetHashCode() => (NeighborSnap, AngularGrid, GridStepDeg).GetHashCode();
    }
}
```

`Assets/_Project/Domain/Layout/Placement.cs`:
```csharp
using System;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Layout
{
    /// <summary>
    /// Onde a tela está (spec superfície §3): sobre a superfície (azimute, elevação, distância a partir do centro)
    /// ou solta (pose livre). Valores fora dos limites são limitados, como a largura das telas.
    /// </summary>
    public readonly struct Placement : IEquatable<Placement>
    {
        public const float MinDistanceM = 0.4f;
        public const float MaxDistanceM = 5f;
        public const float MinElevationDeg = -60f;
        public const float MaxElevationDeg = 75f;

        /// <summary>Onde uma tela nova nasce (spec superfície §6): à frente, 5° abaixo, a 1,3 m.</summary>
        public static readonly Placement Default = OnSurface(0f, -5f, 1.3f);

        public readonly bool IsLoose;
        public readonly float AzimuthDeg;
        public readonly float ElevationDeg;
        public readonly float DistanceM;
        public readonly Pose LoosePose;

        private Placement(bool isLoose, float azimuthDeg, float elevationDeg, float distanceM, Pose loosePose)
        {
            IsLoose = isLoose;
            AzimuthDeg = azimuthDeg;
            ElevationDeg = elevationDeg;
            DistanceM = distanceM;
            LoosePose = loosePose;
        }

        public static Placement OnSurface(float azimuthDeg, float elevationDeg, float distanceM) =>
            new Placement(false, NormalizeAzimuth(azimuthDeg), ClampElevation(elevationDeg), ClampDistance(distanceM), Pose.Identity);

        public static Placement Loose(Pose pose) => new Placement(true, 0f, 0f, 0f, pose);

        /// <summary>Azimute em (−180°, 180°].</summary>
        public static float NormalizeAzimuth(float degrees)
        {
            if (float.IsNaN(degrees)) return 0f;
            float a = degrees % 360f;
            if (a > 180f) a -= 360f;
            else if (a <= -180f) a += 360f;
            return a;
        }

        public static float ClampDistance(float meters) => Clamp(meters, MinDistanceM, MaxDistanceM);
        public static float ClampElevation(float degrees) => Clamp(degrees, MinElevationDeg, MaxElevationDeg);

        internal static float Clamp(float value, float min, float max)
        {
            if (float.IsNaN(value) || value < min) return min;
            return value > max ? max : value;
        }

        public bool Equals(Placement other) =>
            IsLoose == other.IsLoose && (IsLoose
                ? LoosePose.Equals(other.LoosePose)
                : AzimuthDeg == other.AzimuthDeg && ElevationDeg == other.ElevationDeg && DistanceM == other.DistanceM);
        public override bool Equals(object obj) => obj is Placement other && Equals(other);
        public override int GetHashCode() => IsLoose ? LoosePose.GetHashCode() : (AzimuthDeg, ElevationDeg, DistanceM).GetHashCode();
        public override string ToString() =>
            IsLoose ? "Loose" + LoosePose : $"Surface(az {AzimuthDeg:0.#}°, el {ElevationDeg:0.#}°, {DistanceM:0.00} m)";
    }
}
```

`Assets/_Project/Domain/Layout/LayoutSurface.cs`:
```csharp
using System;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Layout
{
    /// <summary>
    /// "Grade invisível" em volta do usuário (brief §10.12, spec superfície §3). Imutável; faz parte da Workspace
    /// (snapshot do undo e do perfil). A pose de uma tela é sempre derivada: <see cref="ToPose"/>.
    /// </summary>
    public sealed class LayoutSurface
    {
        public const float MinTiltDeg = 0f;
        public const float MaxTiltDeg = 60f;
        public static readonly Vec3 DefaultCenter = new Vec3(0f, 1.2f, 0f);
        public static readonly LayoutSurface Default = new LayoutSurface(LayoutMode.Cylinder, DefaultCenter, 0f, 0f, SnapOptions.Default);

        public LayoutMode Mode { get; }
        public Vec3 Center { get; }
        /// <summary>Yaw da "frente" (azimute 0°).</summary>
        public float HeadingDeg { get; }
        /// <summary>Inclinação para cima do referencial; só a Esfera usa (o valor fica guardado no Cilindro).</summary>
        public float TiltDeg { get; }
        public SnapOptions Snap { get; }

        public LayoutSurface(LayoutMode mode, Vec3 center, float headingDeg, float tiltDeg, SnapOptions snap)
        {
            Mode = mode;
            Center = center;
            HeadingDeg = Placement.NormalizeAzimuth(headingDeg);
            TiltDeg = Placement.Clamp(tiltDeg, MinTiltDeg, MaxTiltDeg);
            Snap = snap;
        }

        /// <summary>Referencial: yaw da frente e, na Esfera, a inclinação (pitch negativo = para cima).</summary>
        public Quat FrameRotation => Mode == LayoutMode.Sphere
            ? Quat.AngleAxis(HeadingDeg, Vec3.Up) * Quat.AngleAxis(-TiltDeg, Vec3.Right)
            : Quat.AngleAxis(HeadingDeg, Vec3.Up);

        /// <summary>dir(az, el) = (cos el · sin az, sin el, cos el · cos az) no referencial da superfície.</summary>
        public static Vec3 Direction(float azimuthDeg, float elevationDeg)
        {
            double az = azimuthDeg * Angles.DegToRad;
            double el = elevationDeg * Angles.DegToRad;
            return new Vec3((float)(Math.Cos(el) * Math.Sin(az)), (float)Math.Sin(el), (float)(Math.Cos(el) * Math.Cos(az)));
        }

        /// <summary>Cilindro e Esfera põem a tela no mesmo ponto; só a orientação muda.</summary>
        public Pose ToPose(Placement placement)
        {
            if (placement.IsLoose) return placement.LoosePose;
            var frame = FrameRotation;
            var dir = frame * Direction(placement.AzimuthDeg, placement.ElevationDeg);
            var position = Center + dir * placement.DistanceM;
            var rotation = Mode == LayoutMode.Sphere
                ? Quat.LookRotation(dir, frame * Vec3.Up)
                : Quat.AngleAxis(HeadingDeg + placement.AzimuthDeg, Vec3.Up);
            return new Pose(position, rotation);
        }

        /// <summary>O ponto da superfície na direção de <paramref name="world"/> (distância = até ele).</summary>
        public Placement FromWorldPosition(Vec3 world)
        {
            var local = FrameRotation.Inverse * (world - Center);
            float d = local.Length;
            if (d < 1e-6f) return Placement.OnSurface(0f, 0f, Placement.MinDistanceM);
            double el = Math.Asin(Math.Max(-1.0, Math.Min(1.0, local.Y / d))) * Angles.RadToDeg;
            double az = Math.Atan2(local.X, local.Z) * Angles.RadToDeg;
            return Placement.OnSurface((float)az, (float)el, d);
        }

        /// <summary>Distância usada pela curvatura (§10.2): raio horizontal no Cilindro, distância na Esfera.</summary>
        public float CurvatureDistance(Placement placement)
        {
            if (placement.IsLoose) return (placement.LoosePose.Position - Center).Length;
            return Mode == LayoutMode.Cylinder
                ? placement.DistanceM * (float)Math.Cos(placement.ElevationDeg * Angles.DegToRad)
                : placement.DistanceM;
        }

        public static float HeadingOf(Quat rotation)
        {
            var f = rotation * Vec3.Forward;
            return (float)(Math.Atan2(f.X, f.Z) * Angles.RadToDeg);
        }

        public static float PitchUpOf(Quat rotation)
        {
            var f = (rotation * Vec3.Forward).Normalized;
            return (float)(Math.Asin(Math.Max(-1.0, Math.Min(1.0, f.Y))) * Angles.RadToDeg);
        }

        public LayoutSurface WithMode(LayoutMode mode) => new LayoutSurface(mode, Center, HeadingDeg, TiltDeg, Snap);
        public LayoutSurface WithCenter(Vec3 center, float headingDeg) => new LayoutSurface(Mode, center, headingDeg, TiltDeg, Snap);
        public LayoutSurface WithTilt(float tiltDeg) => new LayoutSurface(Mode, Center, HeadingDeg, tiltDeg, Snap);
        public LayoutSurface WithSnap(SnapOptions snap) => new LayoutSurface(Mode, Center, HeadingDeg, TiltDeg, snap);

        public override string ToString() => $"{Mode} center {Center} heading {HeadingDeg:0.#}° tilt {TiltDeg:0.#}°";
    }
}
```

`Assets/_Project/Domain/Layout/CylinderMath.cs`:
```csharp
using System;

namespace ScreenPortal.Domain.Layout
{
    /// <summary>No Cilindro a tela sobe reta (mantém o raio horizontal) e o afastar mexe no raio (mantém a altura).</summary>
    public static class CylinderMath
    {
        public static float HorizontalRadius(Placement p) => p.DistanceM * (float)Math.Cos(p.ElevationDeg * Angles.DegToRad);
        public static float Height(Placement p) => p.DistanceM * (float)Math.Sin(p.ElevationDeg * Angles.DegToRad);

        public static Placement FromAzimuthHeight(float azimuthDeg, float height, float radius)
        {
            double el = Math.Atan2(height, radius) * Angles.RadToDeg;
            float d = (float)Math.Sqrt(radius * radius + height * height);
            return Placement.OnSurface(azimuthDeg, (float)el, d);
        }

        public static Placement FromAzimuthElevationAtRadius(float azimuthDeg, float elevationDeg, float radius)
        {
            float el = Placement.ClampElevation(elevationDeg);
            return Placement.OnSurface(azimuthDeg, el, radius / (float)Math.Cos(el * Angles.DegToRad));
        }

        public static Placement MoveVertically(Placement p, float height) => FromAzimuthHeight(p.AzimuthDeg, height, HorizontalRadius(p));
        public static Placement WithHorizontalRadius(Placement p, float radius) => FromAzimuthHeight(p.AzimuthDeg, Height(p), radius);
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: `Com falha: 0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Domain/Layout Assets/_Project/Tests/EditMode/Layout
git commit -m "domain: layout surface model (Cylinder/Sphere, Placement, ToPose, CylinderMath)"
```

---

### Task 3: Telas guardam `Placement` (migração)

**Files:**
- Create: `Assets/_Project/Domain/Layout/AngularRect.cs`, `ScreenAngles.cs`, `SurfaceSlots.cs`
- Modify: `Domain/Screens/VirtualScreen.cs`, `Domain/Workspace.cs`, `Domain/Commands/ScreenCommand.cs`, `Domain/Commands/CreateScreen.cs`; delete `Domain/Commands/MoveScreen.cs` (+ `.meta`)
- Modify: `Application/Screens/IScreenView.cs`, `Application/Screens/ScreenViewSynchronizer.cs`, `Application/Connection/ConnectionOptions.cs`, `Application/Connection/ConnectionStateMachine.cs:282`
- Modify: `Presentation/Screens/OverlayScreenView.cs` (compila com a interface nova)
- Create: `Assets/_Project/Domain/Commands/MoveScreenOnSurface.cs` (substitui o MoveScreen nos testes existentes)
- Modify tests: `CommandTests.cs`, `VirtualScreenTests.cs`, `WorkspaceTests.cs`, `WorkspaceStoreTests.cs`, `CommandBusTests.cs`, `ScreenViewSynchronizerTests.cs`, `Connection/ConnectionStateMachineTests.cs`
- Test: `Assets/_Project/Tests/EditMode/Layout/SurfaceSlotsTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2.
- Produces: `VirtualScreen.Placement`, `VirtualScreen.WithPlacement(Placement)`, construtor `VirtualScreen(ScreenId, Resolution, Placement, float widthMeters, ...)`; `Workspace.Surface`, `Workspace.WithSurface(LayoutSurface)`; `CreateScreen(Resolution, Placement? placement, float widthMeters, float initialCurvature = 0f, string label = "")` (null = `SurfaceSlots.FirstFree`); `MoveScreenOnSurface(ScreenId, float azimuthDeg, float elevationDeg, float? distanceM = null)`; `ScreenCommand` com `protected virtual VirtualScreen Transform(VirtualScreen screen, Workspace state)` e `protected virtual string Validate(VirtualScreen screen, Workspace state)`; `struct AngularRect(az, el, halfWidthDeg, halfHeightDeg)`; `ScreenAngles.Of(Placement, float widthM, float heightM, LayoutSurface)`, `ScreenAngles.TryOf(VirtualScreen, LayoutSurface, out AngularRect)`; `SurfaceSlots.FirstFree(Workspace, float widthM, float heightM) → Placement`; `IScreenView.Apply(VirtualScreen screen, Pose pose)` e `IScreenView.ApplyTransform(Pose pose, float widthMeters, float heightMeters)`.

- [ ] **Step 1: Teste novo que falha**

`Assets/_Project/Tests/EditMode/Layout/SurfaceSlotsTests.cs`:
```csharp
using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Screens;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class SurfaceSlotsTests
    {
        private static readonly Resolution Hd = Resolution.Create(1920, 1080);

        [Test]
        public void FirstScreen_GoesToTheDefaultPlacement()
        {
            new CreateScreen(Hd, null, 1f).Apply(Workspace.Empty, out var ws);
            Assert.That(ws.Screens[0].Placement, Is.EqualTo(Placement.Default));
        }

        [Test]
        public void NextScreens_GoToTheRight_WithoutOverlap()
        {
            var ws = Workspace.Empty;
            for (int i = 0; i < 3; i++) new CreateScreen(Hd, null, 1f).Apply(ws, out ws);
            for (int i = 1; i < 3; i++)
            {
                Assert.That(ScreenAngles.TryOf(ws.Screens[i - 1], ws.Surface, out var a), Is.True);
                Assert.That(ScreenAngles.TryOf(ws.Screens[i], ws.Surface, out var b), Is.True);
                Assert.That(b.AzimuthDeg, Is.GreaterThan(a.AzimuthDeg));
                Assert.That(b.AzimuthDeg - b.HalfWidthDeg, Is.GreaterThanOrEqualTo(a.AzimuthDeg + a.HalfWidthDeg + SurfaceSlots.GapDeg - 0.01f));
            }
        }

        [Test]
        public void SurfaceChange_ReappliesEveryView()
        {
            var store = new WorkspaceStore();
            store.Dispatch(new CreateScreen(Hd, Placement.Default, 1f));
            var applied = 0;
            var factory = new CountingFactory(() => applied++);
            using (var sync = new ScreenViewSynchronizer(store, factory))
            {
                sync.Start();
                Assert.That(applied, Is.EqualTo(1));
                store.Dispatch(new RecenterLayout(new Vec3(0f, 1.5f, 0f), 90f));
                Assert.That(applied, Is.EqualTo(2), "superfície nova = pose nova, mesmo com a tela intacta");
            }
        }

        private sealed class CountingFactory : IScreenViewFactory
        {
            private readonly System.Action _onApply;
            public CountingFactory(System.Action onApply) => _onApply = onApply;
            public IScreenView Create(VirtualScreen screen) => new View(_onApply);

            private sealed class View : IScreenView
            {
                private readonly System.Action _onApply;
                public View(System.Action onApply) => _onApply = onApply;
                public void Apply(VirtualScreen screen, Pose pose) => _onApply();
                public void ApplyTransform(Pose pose, float widthMeters, float heightMeters) { }
                public void Dispose() { }
            }
        }
    }
}
```
(O `RecenterLayout` entra nesta task, junto; os outros comandos da superfície vêm na Task 4.)

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: FAIL de compilação (`ScreenAngles`, `RecenterLayout`, `Placement` em `VirtualScreen` não existem).

- [ ] **Step 3: Implementar o domínio**

`Assets/_Project/Domain/Layout/AngularRect.cs`:
```csharp
namespace ScreenPortal.Domain.Layout
{
    /// <summary>Retângulo de uma tela em ângulos sobre a superfície (encaixe e espaço livre).</summary>
    public readonly struct AngularRect
    {
        public readonly float AzimuthDeg;
        public readonly float ElevationDeg;
        public readonly float HalfWidthDeg;
        public readonly float HalfHeightDeg;

        public AngularRect(float azimuthDeg, float elevationDeg, float halfWidthDeg, float halfHeightDeg)
        {
            AzimuthDeg = azimuthDeg;
            ElevationDeg = elevationDeg;
            HalfWidthDeg = halfWidthDeg;
            HalfHeightDeg = halfHeightDeg;
        }
    }
}
```

`Assets/_Project/Domain/Layout/ScreenAngles.cs`:
```csharp
using System;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Layout
{
    public static class ScreenAngles
    {
        public static AngularRect Of(Placement placement, float widthM, float heightM, LayoutSurface surface)
        {
            float horizontal = Math.Max(0.01f, surface.CurvatureDistance(placement));
            float distance = Math.Max(0.01f, placement.DistanceM);
            float halfWidth = (float)(Math.Atan(widthM * 0.5f / horizontal) * Angles.RadToDeg);
            float halfHeight = (float)(Math.Atan(heightM * 0.5f / distance) * Angles.RadToDeg);
            return new AngularRect(placement.AzimuthDeg, placement.ElevationDeg, halfWidth, halfHeight);
        }

        /// <summary>false para telas soltas (não participam do encaixe nem ocupam espaço na superfície).</summary>
        public static bool TryOf(VirtualScreen screen, LayoutSurface surface, out AngularRect rect)
        {
            if (screen.Placement.IsLoose)
            {
                rect = default;
                return false;
            }
            rect = Of(screen.Placement, screen.WidthMeters, screen.HeightMeters, surface);
            return true;
        }
    }
}
```

`Assets/_Project/Domain/Layout/SurfaceSlots.cs`:
```csharp
using System;

namespace ScreenPortal.Domain.Layout
{
    /// <summary>Regra de criação (spec superfície §6): posição padrão; se ocupada, o primeiro espaço livre à direita.</summary>
    public static class SurfaceSlots
    {
        public const float GapDeg = 1f;
        private const int MaxIterations = 72;

        public static Placement FirstFree(Workspace workspace, float widthM, float heightM)
        {
            var surface = workspace.Surface;
            var start = Placement.Default;
            var rect = ScreenAngles.Of(start, widthM, heightM, surface);
            float az = start.AzimuthDeg;
            var screens = workspace.Screens;
            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                bool moved = false;
                for (int i = 0; i < screens.Count; i++)
                {
                    if (!ScreenAngles.TryOf(screens[i], surface, out var other)) continue;
                    bool overlapAz = Math.Abs(Placement.NormalizeAzimuth(az - other.AzimuthDeg)) < rect.HalfWidthDeg + other.HalfWidthDeg + GapDeg;
                    bool overlapEl = Math.Abs(start.ElevationDeg - other.ElevationDeg) < rect.HalfHeightDeg + other.HalfHeightDeg + GapDeg;
                    if (!overlapAz || !overlapEl) continue;
                    az = other.AzimuthDeg + other.HalfWidthDeg + GapDeg + rect.HalfWidthDeg + 0.001f;
                    moved = true;
                }
                if (!moved) break;
            }
            return Placement.OnSurface(az, start.ElevationDeg, start.DistanceM);
        }
    }
}
```

`Assets/_Project/Domain/Screens/VirtualScreen.cs` — substituir `Pose` por `Placement`:
- `using ScreenPortal.Domain.Spatial;` → `using ScreenPortal.Domain.Layout;`
- `public Pose Pose { get; }` → `public Placement Placement { get; }` (com o comentário `/// <summary>Sobre a superfície ou solta; a pose 3D é <c>surface.ToPose(Placement)</c>.</summary>`)
- no construtor: parâmetro `Pose pose` → `Placement placement`; `Pose = pose;` → `Placement = placement;`
- `WithPose(Pose pose)` vira:
```csharp
        public VirtualScreen WithPlacement(Placement placement) =>
            new VirtualScreen(Id, Resolution, placement, WidthMeters, Curvature, LastNonZeroCurvature, FrameRateMode, Label);
```
- nos demais `With*`, trocar o argumento `Pose` por `Placement`; comentário de `WithResolution`: "Mantém placement e largura física".

`Assets/_Project/Domain/Workspace.cs`:
- `using ScreenPortal.Domain.Layout;`
- `Empty` → `new Workspace(Array.Empty<VirtualScreen>(), ScreenId.None, 1, LayoutSurface.Default);`
- campo: `public LayoutSurface Surface { get; }` (comentário: "Superfície de layout (ADR 0005): parte do snapshot do undo.")
- construtor privado ganha `LayoutSurface surface` (último parâmetro) e `Surface = surface;`
- todos os `new Workspace(...)` internos passam `Surface` como último argumento
- novo método:
```csharp
        public Workspace WithSurface(LayoutSurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            return new Workspace(_screens, FocusedScreenId, NextScreenId, surface);
        }
```

`Assets/_Project/Domain/Commands/ScreenCommand.cs` — substituir o corpo da classe por:
```csharp
    /// <summary>Base para comandos que transformam uma tela existente.</summary>
    public abstract class ScreenCommand : ICommand
    {
        public ScreenId ScreenId { get; }
        public virtual bool IsUndoable => true;

        protected ScreenCommand(ScreenId screenId) => ScreenId = screenId;

        /// <summary>Transformação que só depende da tela.</summary>
        protected virtual VirtualScreen Transform(VirtualScreen screen) => screen;

        /// <summary>Comandos que precisam da superfície sobrescrevem esta.</summary>
        protected virtual VirtualScreen Transform(VirtualScreen screen, Workspace state) => Transform(screen);

        /// <summary>null = válido; senão, o motivo da rejeição.</summary>
        protected virtual string Validate(VirtualScreen screen, Workspace state) => null;

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            if (!state.TryGetScreen(ScreenId, out var screen))
            {
                next = state;
                return CommandResult.Rejected(ScreenId + " não existe.");
            }
            string error = Validate(screen, state);
            if (error != null)
            {
                next = state;
                return CommandResult.Rejected(ScreenId + ": " + error);
            }
            next = state.WithScreenReplaced(Transform(screen, state));
            return CommandResult.Ok;
        }

        public override string ToString() => GetType().Name + "(" + ScreenId + ")";
    }
```

`Assets/_Project/Domain/Commands/CreateScreen.cs` — trocar `Pose` por `Placement?`:
```csharp
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>
    /// Cria um monitor com resolução fixa (brief §10.3). A nova tela recebe o foco.
    /// Sem placement: nasce no primeiro espaço livre da superfície (spec superfície §6).
    /// </summary>
    public sealed class CreateScreen : ICommand
    {
        public Resolution Resolution { get; }
        public Placement? Placement { get; }
        public float WidthMeters { get; }
        public float InitialCurvature { get; }
        public string Label { get; }
        public bool IsUndoable => true;

        public CreateScreen(Resolution resolution, Placement? placement, float widthMeters, float initialCurvature = 0f, string label = "")
        {
            Resolution = resolution;
            Placement = placement;
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
            float width = ScreenLimits.ClampWidth(WidthMeters);
            var placement = Placement ?? SurfaceSlots.FirstFree(state, width, width / Resolution.AspectRatio);
            var screen = new VirtualScreen(id, Resolution, placement, width, InitialCurvature, label: Label);
            next = state.WithScreenAdded(screen).WithFocus(id);
            return CommandResult.Ok;
        }

        public override string ToString() => $"CreateScreen({Resolution}, {WidthMeters:0.00} m)";
    }
}
```

`Assets/_Project/Domain/Commands/MoveScreenOnSurface.cs`:
```csharp
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>
    /// Põe a tela num ponto da superfície (spec superfície §4). Sem distância: mantém a atual (ou, se a tela estava
    /// solta, a distância até ela). O arraste manda a distância junto: mover + afastar = um passo de undo.
    /// </summary>
    public sealed class MoveScreenOnSurface : ScreenCommand
    {
        public float AzimuthDeg { get; }
        public float ElevationDeg { get; }
        public float? DistanceM { get; }

        public MoveScreenOnSurface(ScreenId screenId, float azimuthDeg, float elevationDeg, float? distanceM = null) : base(screenId)
        {
            AzimuthDeg = azimuthDeg;
            ElevationDeg = elevationDeg;
            DistanceM = distanceM;
        }

        protected override VirtualScreen Transform(VirtualScreen screen, Workspace state)
        {
            var current = screen.Placement;
            float distance = DistanceM ?? (current.IsLoose
                ? state.Surface.FromWorldPosition(current.LoosePose.Position).DistanceM
                : current.DistanceM);
            return screen.WithPlacement(Placement.OnSurface(AzimuthDeg, ElevationDeg, distance));
        }
    }
}
```

`Assets/_Project/Domain/Commands/RecenterLayout.cs`:
```csharp
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Novo centro e frente (recentrar, §10.12). Placements intactos: o layout inteiro acompanha.</summary>
    public sealed class RecenterLayout : ICommand
    {
        public Vec3 Center { get; }
        public float HeadingDeg { get; }
        public bool IsUndoable => true;

        public RecenterLayout(Vec3 center, float headingDeg)
        {
            Center = center;
            HeadingDeg = headingDeg;
        }

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            next = state.WithSurface(state.Surface.WithCenter(Center, HeadingDeg));
            return CommandResult.Ok;
        }
    }
}
```

Apagar `Assets/_Project/Domain/Commands/MoveScreen.cs` e `MoveScreen.cs.meta`.

- [ ] **Step 4: Application e Presentation acompanham**

`Assets/_Project/Application/Screens/IScreenView.cs`:
```csharp
using System;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Screens
{
    /// <summary>View passiva de uma tela (brief §8.3): só reflete o estado; nunca o altera.</summary>
    public interface IScreenView : IDisposable
    {
        /// <summary>Estado confirmado. <paramref name="pose"/> = superfície.ToPose(screen.Placement).</summary>
        void Apply(VirtualScreen screen, Pose pose);

        /// <summary>Só posição/tamanho, para a pré-visualização do arraste (não é estado).</summary>
        void ApplyTransform(Pose pose, float widthMeters, float heightMeters);
    }
}
```

`Assets/_Project/Application/Screens/ScreenViewSynchronizer.cs`:
- `using ScreenPortal.Domain.Layout;`
- `struct Entry` ganha `public LayoutSurface Surface;`
- em `Sync`, no início: `var surface = workspace.Surface;`
- ramo "existe": trocar o `if (!ReferenceEquals(entry.Applied, screen)) { ... }` por:
```csharp
                    if (!ReferenceEquals(entry.Applied, screen) || !ReferenceEquals(entry.Surface, surface))
                    {
                        entry.View.Apply(screen, surface.ToPose(screen.Placement));
                        entry.Applied = screen;
                        entry.Surface = surface;
                    }
```
- ramo "nova": `view.Apply(screen, surface.ToPose(screen.Placement));` e `new Entry { View = view, Applied = screen, Surface = surface, Seen = true }`
- comentário da classe: acrescentar "Superfície nova (outra instância) reaplica todas as telas."

`Assets/_Project/Application/Connection/ConnectionOptions.cs`: remover a linha `public Pose DefaultScreenPose ...` e o `using ScreenPortal.Domain.Spatial;`.

`Assets/_Project/Application/Connection/ConnectionStateMachine.cs` (linha ~282): trocar `_options.DefaultScreenPose` por `null` (a tela do PC nasce no primeiro espaço livre).

`Assets/_Project/Presentation/Screens/OverlayScreenView.cs` — acrescentar `using Pose = ScreenPortal.Domain.Spatial.Pose;` e substituir `Apply` por:
```csharp
        public void Apply(VirtualScreen screen, Pose pose)
        {
            // Resolução só muda via RecreateScreen (raro): recria a layer com a nova external surface.
            if (!screen.Resolution.Equals(_resolution)) BuildOverlay(screen.Resolution);
            ApplyTransform(pose, screen.WidthMeters, screen.HeightMeters);
            _root.name = string.IsNullOrEmpty(screen.Label) ? "Screen " + screen.Id.Value : screen.Label;
        }

        public void ApplyTransform(Pose pose, float widthMeters, float heightMeters)
        {
            var t = _root.transform;
            t.SetLocalPositionAndRotation(pose.Position.ToUnity(), pose.Rotation.ToUnity());
            t.localScale = new Vector3(widthMeters, heightMeters, 1f);
        }
```

- [ ] **Step 5: Testes existentes acompanham**

Em todos os arquivos abaixo, acrescentar `using ScreenPortal.Domain.Layout;` e fazer as trocas:

| Arquivo | Trecho atual | Novo |
|---|---|---|
| `CommandTests.cs` | `private static readonly Pose Front = Pose.At(new Vec3(0f, 1.2f, 1.3f));` | `private static readonly Placement Front = Placement.Default;` |
| `CommandTests.cs` | `Assert.That(s.Pose, Is.EqualTo(Front));` (2×) | `Assert.That(s.Placement, Is.EqualTo(Front));` |
| `CommandTests.cs` | o teste `MoveScreen_ChangesOnlyPose` inteiro | o teste abaixo |
| `CommandTests.cs` | `new MoveScreen(unknown, Front)` | `new MoveScreenOnSurface(unknown, 10f, 0f)` |
| `CommandTests.cs` | `new MoveScreen(new ScreenId(1), Front).IsUndoable` | `new MoveScreenOnSurface(new ScreenId(1), 10f, 0f).IsUndoable` |
| `VirtualScreenTests.cs` | `Pose.Identity` (5×) | `Placement.Default` |
| `VirtualScreenTests.cs` | `s.WithPose(Pose.At(new Vec3(1f, 2f, 3f)))` | `s.WithPlacement(Placement.OnSurface(20f, 5f, 2f))` |
| `VirtualScreenTests.cs` | `Assert.That(s.Pose, Is.EqualTo(Pose.Identity));` | `Assert.That(s.Placement, Is.EqualTo(Placement.Default));` |
| `WorkspaceTests.cs` | `Pose.Identity` | `Placement.Default` |
| `WorkspaceStoreTests.cs` | `Pose.At(new Vec3(0f, 1.2f, 1.3f))` | `Placement.Default` |
| `CommandBusTests.cs` | `Pose.Identity` (2×) | `Placement.Default` |
| `ScreenViewSynchronizerTests.cs` | `public void Apply(VirtualScreen screen) => Applied.Add(screen);` | `public void Apply(VirtualScreen screen, Pose pose) => Applied.Add(screen);` + `public void ApplyTransform(Pose pose, float widthMeters, float heightMeters) { }` |
| `ScreenViewSynchronizerTests.cs` | `Pose.Identity, 1f)` | `Placement.Default, 1f)` |
| `Connection/ConnectionStateMachineTests.cs` | `new MoveScreen(r.Store.State.Screens[0].Id, Pose.At(new Vec3(-1f, 1f, 1f)))` | `new MoveScreenOnSurface(r.Store.State.Screens[0].Id, -30f, 10f)` |
| `Connection/ConnectionStateMachineTests.cs` | `Assert.That(r.Store.State.Screens[0].Pose.Position, Is.EqualTo(new Vec3(-1f, 1f, 1f)), "layout intacto");` | `Assert.That(r.Store.State.Screens[0].Placement.AzimuthDeg, Is.EqualTo(-30f), "layout intacto");` |

Teste que substitui `MoveScreen_ChangesOnlyPose` em `CommandTests.cs`:
```csharp
        [Test]
        public void MoveScreenOnSurface_ChangesOnlyPlacement_KeepsDistanceWhenOmitted()
        {
            var ws = WithOneScreen(out var id);
            new MoveScreenOnSurface(id, -30f, 12f).Apply(ws, out var next);
            var s = Get(next, id);
            Assert.That(s.Placement, Is.EqualTo(Placement.OnSurface(-30f, 12f, Front.DistanceM)));
            Assert.That(s.WidthMeters, Is.EqualTo(1.4f));
            Assert.That(s.Resolution, Is.EqualTo(Ultrawide));

            new MoveScreenOnSurface(id, 5f, 0f, 2.2f).Apply(next, out next);
            Assert.That(Get(next, id).Placement.DistanceM, Is.EqualTo(2.2f));
        }
```

- [ ] **Step 6: Rodar tudo**

Run:
```powershell
dotnet test tests-dotnet/UnityPureCode.Tests -v q
dotnet test host/Host.Tests -v q
```
Expected: os dois com `Com falha: 0` (o host compila Domain/Application via `Host.Shared`).

- [ ] **Step 7: Commit**

```bash
git add -A Assets/_Project tests-dotnet
git commit -m "domain: screens store a Placement on the layout surface; pose derived by the surface; MoveScreen -> MoveScreenOnSurface"
```

---

### Task 4: Comandos da superfície

**Files:**
- Create: `Assets/_Project/Domain/Commands/SetLayoutMode.cs`, `AlignSurfaceToPosture.cs`, `SetSurfaceTilt.cs`, `SetScreenDistance.cs`, `SetScreenLoose.cs`, `ReturnScreenToSurface.cs`, `SetSnapOptions.cs`
- Test: `Assets/_Project/Tests/EditMode/Layout/LayoutCommandTests.cs`

**Interfaces:**
- Consumes: Tasks 2–3 (`RecenterLayout` e `MoveScreenOnSurface` já existem).
- Produces: `SetLayoutMode(LayoutMode)`, `AlignSurfaceToPosture(Pose head)`, `SetSurfaceTilt(float deg)`, `SetScreenDistance(ScreenId, float meters)`, `SetScreenLoose(ScreenId, Pose)`, `ReturnScreenToSurface(ScreenId)`, `SetSnapOptions(SnapOptions)` — todos `IsUndoable = true`.

- [ ] **Step 1: Teste que falha**

`Assets/_Project/Tests/EditMode/Layout/LayoutCommandTests.cs`:
```csharp
using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;
using static ScreenPortal.Tests.SpatialMathTests;

namespace ScreenPortal.Tests
{
    public class LayoutCommandTests
    {
        private static readonly Resolution Hd = Resolution.Create(1920, 1080);

        private static WorkspaceStore Store(params Placement[] placements)
        {
            var store = new WorkspaceStore();
            foreach (var p in placements) store.Dispatch(new CreateScreen(Hd, p, 1f));
            return store;
        }

        private static Vec3 WorldOf(Workspace ws, int index) => ws.Surface.ToPose(ws.Screens[index].Placement).Position;

        [Test]
        public void SetLayoutMode_KeepsEveryScreenWhereItIs_EvenLeavingATiltedSphere_AndUndoRestoresExactly()
        {
            var store = Store(Placement.OnSurface(-40f, 10f, 1.5f), Placement.OnSurface(35f, -20f, 2f));
            store.Dispatch(new SetLayoutMode(LayoutMode.Sphere));
            store.Dispatch(new SetSurfaceTilt(40f));
            var before = store.State;
            var p0 = WorldOf(before, 0);
            var p1 = WorldOf(before, 1);

            Assert.That(store.Dispatch(new SetLayoutMode(LayoutMode.Cylinder)).Accepted, Is.True);
            AssertNear(WorldOf(store.State, 0), p0, 0.001f);
            AssertNear(WorldOf(store.State, 1), p1, 0.001f);

            store.Undo();
            Assert.That(store.State, Is.SameAs(before));
        }

        [Test]
        public void SetLayoutMode_ToTheSameMode_IsRejected() =>
            Assert.That(Store().Dispatch(new SetLayoutMode(LayoutMode.Cylinder)).Accepted, Is.False);

        [Test]
        public void RecenterLayout_KeepsPlacements_AndMovesAllScreensTogether()
        {
            var store = Store(Placement.OnSurface(-30f, 0f, 1.3f), Placement.OnSurface(30f, 5f, 1.3f));
            var placements = new[] { store.State.Screens[0].Placement, store.State.Screens[1].Placement };
            float gapBefore = (WorldOf(store.State, 1) - WorldOf(store.State, 0)).Length;

            store.Dispatch(new RecenterLayout(new Vec3(1f, 1.4f, 2f), 90f));

            Assert.That(store.State.Screens[0].Placement, Is.EqualTo(placements[0]));
            Assert.That(store.State.Screens[1].Placement, Is.EqualTo(placements[1]));
            Assert.That((WorldOf(store.State, 1) - WorldOf(store.State, 0)).Length, Is.EqualTo(gapBefore).Within(0.001f));
            var front = store.State.Surface.ToPose(Placement.OnSurface(0f, 0f, 1f)).Position;
            AssertNear(front, new Vec3(2f, 1.4f, 2f), 0.001f);
        }

        [Test]
        public void LooseScreens_KeepTheirWorldPose_OnModeRecenterAndTilt()
        {
            var loosePose = new Pose(new Vec3(0.5f, 1.6f, 0.9f), Quat.AngleAxis(20f, Vec3.Up));
            var store = Store(Placement.Default);
            var id = store.State.Screens[0].Id;
            store.Dispatch(new SetScreenLoose(id, loosePose));

            store.Dispatch(new SetLayoutMode(LayoutMode.Sphere));
            store.Dispatch(new SetSurfaceTilt(30f));
            store.Dispatch(new RecenterLayout(new Vec3(3f, 1f, 0f), 45f));

            Assert.That(store.State.Screens[0].Placement.IsLoose, Is.True);
            Assert.That(store.State.Surface.ToPose(store.State.Screens[0].Placement), Is.EqualTo(loosePose));
        }

        [Test]
        public void ReturnScreenToSurface_PicksTheNearestPoint_AndRejectsScreensAlreadyOnIt()
        {
            var store = Store(Placement.Default);
            var id = store.State.Screens[0].Id;
            var target = Placement.OnSurface(20f, 5f, 1.5f);
            var world = store.State.Surface.ToPose(target).Position;
            Assert.That(store.Dispatch(new ReturnScreenToSurface(id)).Accepted, Is.False, "já está na superfície");

            store.Dispatch(new SetScreenLoose(id, Pose.At(world)));
            Assert.That(store.Dispatch(new ReturnScreenToSurface(id)).Accepted, Is.True);
            var back = store.State.Screens[0].Placement;
            Assert.That(back.IsLoose, Is.False);
            Assert.That(back.AzimuthDeg, Is.EqualTo(20f).Within(0.01f));
            Assert.That(back.ElevationDeg, Is.EqualTo(5f).Within(0.01f));
            Assert.That(back.DistanceM, Is.EqualTo(1.5f).Within(0.001f));
        }

        [Test]
        public void SetScreenDistance_SphereUsesDistance_CylinderKeepsHeight_LooseIsRejected()
        {
            var store = Store(Placement.OnSurface(0f, 30f, 1.2f));
            var id = store.State.Screens[0].Id;
            float height = CylinderMath.Height(store.State.Screens[0].Placement);

            store.Dispatch(new SetScreenDistance(id, 2f));
            Assert.That(CylinderMath.HorizontalRadius(store.State.Screens[0].Placement), Is.EqualTo(2f).Within(0.001f));
            Assert.That(CylinderMath.Height(store.State.Screens[0].Placement), Is.EqualTo(height).Within(0.001f));

            store.Dispatch(new SetLayoutMode(LayoutMode.Sphere));
            store.Dispatch(new SetScreenDistance(id, 3f));
            Assert.That(store.State.Screens[0].Placement.DistanceM, Is.EqualTo(3f).Within(0.001f));

            store.Dispatch(new SetScreenLoose(id, Pose.At(new Vec3(0f, 1f, 1f))));
            Assert.That(store.Dispatch(new SetScreenDistance(id, 1f)).Accepted, Is.False);
        }

        [Test]
        public void AlignSurfaceToPosture_UsesHeadPositionYawAndPitch_ClampedTo0To60()
        {
            var store = Store();
            var head = new Pose(new Vec3(0.1f, 0.9f, 0.2f), Quat.AngleAxis(30f, Vec3.Up) * Quat.AngleAxis(-35f, Vec3.Right));
            store.Dispatch(new AlignSurfaceToPosture(head));
            var s = store.State.Surface;
            AssertNear(s.Center, head.Position);
            Assert.That(s.HeadingDeg, Is.EqualTo(30f).Within(0.01f));
            Assert.That(s.TiltDeg, Is.EqualTo(35f).Within(0.01f));

            store.Dispatch(new AlignSurfaceToPosture(new Pose(head.Position, Quat.AngleAxis(-80f, Vec3.Right))));
            Assert.That(store.State.Surface.TiltDeg, Is.EqualTo(60f));
            store.Dispatch(new AlignSurfaceToPosture(new Pose(head.Position, Quat.AngleAxis(20f, Vec3.Right))));
            Assert.That(store.State.Surface.TiltDeg, Is.EqualTo(0f));
        }

        [Test]
        public void SetSnapOptions_IsStoredAndUndoable()
        {
            var store = Store();
            var grid = SnapOptions.Default.WithAngularGrid(true);
            store.Dispatch(new SetSnapOptions(grid));
            Assert.That(store.State.Surface.Snap, Is.EqualTo(grid));
            store.Undo();
            Assert.That(store.State.Surface.Snap, Is.EqualTo(SnapOptions.Default));
        }
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q`
Expected: FAIL de compilação (`SetLayoutMode` etc. não existem).

- [ ] **Step 3: Implementar**

`Assets/_Project/Domain/Commands/SetLayoutMode.cs`:
```csharp
using ScreenPortal.Domain.Layout;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>
    /// Troca Cilindro ↔ Esfera sem mover nenhuma tela (brief §10.12): os placements são reexpressos no referencial
    /// novo (importa quando a Esfera tem tilt). Telas soltas ficam intactas. Um passo de undo.
    /// </summary>
    public sealed class SetLayoutMode : ICommand
    {
        public LayoutMode Mode { get; }
        public bool IsUndoable => true;

        public SetLayoutMode(LayoutMode mode) => Mode = mode;

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            var from = state.Surface;
            if (from.Mode == Mode)
            {
                next = state;
                return CommandResult.Rejected("Já está no modo " + Mode + ".");
            }
            var to = from.WithMode(Mode);
            var ws = state.WithSurface(to);
            var screens = state.Screens;
            for (int i = 0; i < screens.Count; i++)
            {
                var screen = screens[i];
                if (screen.Placement.IsLoose) continue;
                var world = from.ToPose(screen.Placement).Position;
                ws = ws.WithScreenReplaced(screen.WithPlacement(to.FromWorldPosition(world)));
            }
            next = ws;
            return CommandResult.Ok;
        }
    }
}
```

`Assets/_Project/Domain/Commands/AlignSurfaceToPosture.cs`:
```csharp
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Centro = cabeça; frente = yaw do olhar; tilt = inclinação do olhar para cima (0°–60°, só a Esfera usa).</summary>
    public sealed class AlignSurfaceToPosture : ICommand
    {
        public Pose Head { get; }
        public bool IsUndoable => true;

        public AlignSurfaceToPosture(Pose head) => Head = head;

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            var surface = state.Surface
                .WithCenter(Head.Position, LayoutSurface.HeadingOf(Head.Rotation))
                .WithTilt(LayoutSurface.PitchUpOf(Head.Rotation));
            next = state.WithSurface(surface);
            return CommandResult.Ok;
        }
    }
}
```

`Assets/_Project/Domain/Commands/SetSurfaceTilt.cs`:
```csharp
namespace ScreenPortal.Domain.Commands
{
    /// <summary>Ajuste fino do tilt (0°–60°).</summary>
    public sealed class SetSurfaceTilt : ICommand
    {
        public float TiltDeg { get; }
        public bool IsUndoable => true;

        public SetSurfaceTilt(float tiltDeg) => TiltDeg = tiltDeg;

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            next = state.WithSurface(state.Surface.WithTilt(TiltDeg));
            return CommandResult.Ok;
        }
    }
}
```

`Assets/_Project/Domain/Commands/SetScreenDistance.cs`:
```csharp
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Esfera: distância ao longo da direção. Cilindro: raio horizontal, mantendo a altura.</summary>
    public sealed class SetScreenDistance : ScreenCommand
    {
        public float Meters { get; }

        public SetScreenDistance(ScreenId screenId, float meters) : base(screenId) => Meters = meters;

        protected override string Validate(VirtualScreen screen, Workspace state) =>
            screen.Placement.IsLoose ? "tela solta não está na superfície." : null;

        protected override VirtualScreen Transform(VirtualScreen screen, Workspace state)
        {
            var p = screen.Placement;
            return screen.WithPlacement(state.Surface.Mode == LayoutMode.Sphere
                ? Placement.OnSurface(p.AzimuthDeg, p.ElevationDeg, Meters)
                : CylinderMath.WithHorizontalRadius(p, Meters));
        }
    }
}
```

`Assets/_Project/Domain/Commands/SetScreenLoose.cs`:
```csharp
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Tira a tela da superfície (pose livre). Liberdade: toda tela pode sair (ADR 0005).</summary>
    public sealed class SetScreenLoose : ScreenCommand
    {
        public Pose Pose { get; }

        public SetScreenLoose(ScreenId screenId, Pose pose) : base(screenId) => Pose = pose;

        protected override VirtualScreen Transform(VirtualScreen screen) => screen.WithPlacement(Placement.Loose(Pose));
    }
}
```

`Assets/_Project/Domain/Commands/ReturnScreenToSurface.cs`:
```csharp
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Volta a tela solta ao ponto da superfície mais próximo da pose atual.</summary>
    public sealed class ReturnScreenToSurface : ScreenCommand
    {
        public ReturnScreenToSurface(ScreenId screenId) : base(screenId) { }

        protected override string Validate(VirtualScreen screen, Workspace state) =>
            screen.Placement.IsLoose ? null : "já está na superfície.";

        protected override VirtualScreen Transform(VirtualScreen screen, Workspace state) =>
            screen.WithPlacement(state.Surface.FromWorldPosition(screen.Placement.LoosePose.Position));
    }
}
```

`Assets/_Project/Domain/Commands/SetSnapOptions.cs`:
```csharp
using ScreenPortal.Domain.Layout;

namespace ScreenPortal.Domain.Commands
{
    public sealed class SetSnapOptions : ICommand
    {
        public SnapOptions Options { get; }
        public bool IsUndoable => true;

        public SetSnapOptions(SnapOptions options) => Options = options;

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            next = state.WithSurface(state.Surface.WithSnap(Options));
            return CommandResult.Ok;
        }
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q` → Expected: `Com falha: 0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Domain/Commands Assets/_Project/Tests/EditMode/Layout
git commit -m "domain: layout surface commands (mode, recenter, posture, tilt, distance, loose, return, snap options)"
```

---

### Task 5: Encaixe angular (`SurfaceSnapper`)

**Files:**
- Create: `Assets/_Project/Domain/Layout/SurfaceSnapper.cs`
- Test: `Assets/_Project/Tests/EditMode/Layout/SurfaceSnapperTests.cs`

**Interfaces:**
- Consumes: `AngularRect`, `SnapOptions` (Tasks 2–3).
- Produces: `struct SnapResult { AzimuthDeg, ElevationDeg, SnappedAzimuth, SnappedElevation, GuideAzimuthDeg, GuideElevationDeg }`; `SurfaceSnapper.ThresholdDeg = 1.5f`; `SurfaceSnapper.Snap(AngularRect moving, IReadOnlyList<AngularRect> others, SnapOptions options) → SnapResult`.

- [ ] **Step 1: Teste que falha**

`Assets/_Project/Tests/EditMode/Layout/SurfaceSnapperTests.cs`:
```csharp
using NUnit.Framework;
using ScreenPortal.Domain.Layout;

namespace ScreenPortal.Tests
{
    public class SurfaceSnapperTests
    {
        private static readonly AngularRect Other = new AngularRect(0f, 0f, 10f, 6f); // bordas em ±10°
        private static readonly AngularRect[] Others = { Other };
        private static readonly SnapOptions NeighborsOnly = new SnapOptions(true, false, 5f);
        private static readonly SnapOptions Off = new SnapOptions(false, false, 5f);

        [Test]
        public void SnapsLeftEdgeToNeighborsRightEdge_WithinThreshold()
        {
            // borda esquerda da tela movida a 11° (1° do encaixe perfeito em 10°)
            var moving = new AngularRect(19f, 3f, 8f, 5f);
            var r = SurfaceSnapper.Snap(moving, Others, NeighborsOnly);
            Assert.That(r.SnappedAzimuth, Is.True);
            Assert.That(r.AzimuthDeg, Is.EqualTo(18f).Within(1e-4f));
            Assert.That(r.GuideAzimuthDeg, Is.EqualTo(10f).Within(1e-4f));
        }

        [Test]
        public void DoesNotSnap_BeyondThreshold()
        {
            var moving = new AngularRect(21f, 17f, 8f, 5f); // borda a 13°, 3° do encaixe; altura longe de qualquer alinhamento
            var r = SurfaceSnapper.Snap(moving, Others, NeighborsOnly);
            Assert.That(r.SnappedAzimuth, Is.False);
            Assert.That(r.AzimuthDeg, Is.EqualTo(21f));
            Assert.That(r.SnappedElevation, Is.False);
        }

        [Test]
        public void AlignsTopsAndCenters()
        {
            var moving = new AngularRect(40f, 0.8f, 8f, 6f); // centro a 0,8° do centro da vizinha
            var r = SurfaceSnapper.Snap(moving, Others, NeighborsOnly);
            Assert.That(r.SnappedElevation, Is.True);
            Assert.That(r.ElevationDeg, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void OptionsOff_NeverSnap()
        {
            var r = SurfaceSnapper.Snap(new AngularRect(19f, 0.5f, 8f, 6f), Others, Off);
            Assert.That(r.SnappedAzimuth || r.SnappedElevation, Is.False);
        }

        [Test]
        public void AngularGrid_SnapsToTheStep()
        {
            var grid = new SnapOptions(false, true, 5f);
            var r = SurfaceSnapper.Snap(new AngularRect(9f, -4.2f, 8f, 5f), new AngularRect[0], grid);
            Assert.That(r.AzimuthDeg, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(r.ElevationDeg, Is.EqualTo(-5f).Within(1e-4f));
            var far = SurfaceSnapper.Snap(new AngularRect(7.5f, 0f, 8f, 5f), new AngularRect[0], grid);
            Assert.That(far.SnappedAzimuth, Is.False, "2,5° do passo: fora do limiar");
        }
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q` → Expected: FAIL (`SurfaceSnapper` não existe).

- [ ] **Step 3: Implementar**

`Assets/_Project/Domain/Layout/SurfaceSnapper.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace ScreenPortal.Domain.Layout
{
    public readonly struct SnapResult
    {
        public readonly float AzimuthDeg;
        public readonly float ElevationDeg;
        public readonly bool SnappedAzimuth;
        public readonly bool SnappedElevation;
        /// <summary>Linha-guia (azimute da borda/centro alinhado) — só vale se <see cref="SnappedAzimuth"/>.</summary>
        public readonly float GuideAzimuthDeg;
        public readonly float GuideElevationDeg;

        public SnapResult(float azimuthDeg, float elevationDeg, bool snappedAzimuth, bool snappedElevation, float guideAzimuthDeg, float guideElevationDeg)
        {
            AzimuthDeg = azimuthDeg;
            ElevationDeg = elevationDeg;
            SnappedAzimuth = snappedAzimuth;
            SnappedElevation = snappedElevation;
            GuideAzimuthDeg = guideAzimuthDeg;
            GuideElevationDeg = guideElevationDeg;
        }
    }

    /// <summary>
    /// Encaixe em ângulos (spec superfície §5): bordas, centros e topos das vizinhas, e grade angular. Só age dentro
    /// do limiar — continuar arrastando desgruda sozinho, porque o encaixe é recalculado a partir da posição bruta.
    /// Sem alocação (chamado a cada quadro do arraste).
    /// </summary>
    public static class SurfaceSnapper
    {
        public const float ThresholdDeg = 1.5f;

        public static SnapResult Snap(AngularRect moving, IReadOnlyList<AngularRect> others, SnapOptions options)
        {
            var az = new Axis(moving.AzimuthDeg);
            var el = new Axis(moving.ElevationDeg);
            float mw = moving.HalfWidthDeg;
            float mh = moving.HalfHeightDeg;

            if (options.NeighborSnap && others != null)
            {
                for (int i = 0; i < others.Count; i++)
                {
                    var o = others[i];
                    float ow = o.HalfWidthDeg;
                    float oh = o.HalfHeightDeg;
                    az.Try(o.AzimuthDeg + ow + mw, o.AzimuthDeg + ow); // minha esquerda na direita dela
                    az.Try(o.AzimuthDeg - ow - mw, o.AzimuthDeg - ow); // minha direita na esquerda dela
                    az.Try(o.AzimuthDeg, o.AzimuthDeg);                // centros
                    az.Try(o.AzimuthDeg - ow + mw, o.AzimuthDeg - ow); // esquerdas alinhadas
                    az.Try(o.AzimuthDeg + ow - mw, o.AzimuthDeg + ow); // direitas alinhadas
                    el.Try(o.ElevationDeg, o.ElevationDeg);                // centros
                    el.Try(o.ElevationDeg + oh - mh, o.ElevationDeg + oh); // topos alinhados
                    el.Try(o.ElevationDeg - oh + mh, o.ElevationDeg - oh); // bases alinhadas
                    el.Try(o.ElevationDeg + oh + mh, o.ElevationDeg + oh); // em cima dela
                    el.Try(o.ElevationDeg - oh - mh, o.ElevationDeg - oh); // embaixo dela
                }
            }

            if (options.AngularGrid)
            {
                float step = options.GridStepDeg;
                float gridAz = (float)Math.Round(moving.AzimuthDeg / step) * step;
                float gridEl = (float)Math.Round(moving.ElevationDeg / step) * step;
                az.Try(gridAz, gridAz);
                el.Try(gridEl, gridEl);
            }

            return new SnapResult(az.Value, el.Value, az.Snapped, el.Snapped, az.Guide, el.Guide);
        }

        private struct Axis
        {
            private readonly float _raw;
            private float _bestDelta;
            public float Value;
            public float Guide;
            public bool Snapped;

            public Axis(float raw)
            {
                _raw = raw;
                _bestDelta = ThresholdDeg;
                Value = raw;
                Guide = 0f;
                Snapped = false;
            }

            public void Try(float target, float guide)
            {
                float delta = Math.Abs(target - _raw);
                if (delta > _bestDelta) return;
                _bestDelta = delta;
                Value = target;
                Guide = guide;
                Snapped = true;
            }
        }
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q` → Expected: `Com falha: 0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Domain/Layout Assets/_Project/Tests/EditMode/Layout
git commit -m "domain: angular snapping to neighbors and grid (SurfaceSnapper)"
```

---

### Task 6: Acerto do raio e interseção com a superfície

**Files:**
- Create: `Assets/_Project/Application/Manipulation/ManipulationInput.cs`, `ScreenHitTest.cs`, `SurfaceRay.cs`
- Test: `Assets/_Project/Tests/EditMode/Manipulation/ScreenHitTestTests.cs`

**Interfaces:**
- Consumes: Domain (Tasks 1–3).
- Produces: `struct ManipulationInput { HasRay, Origin, Direction, SelectHeld, GripHeld, DistanceDeltaM, CancelPressed, UndoPressed, RedoPressed }`; `interface IManipulationInput { bool Read(out ManipulationInput input); }`; `NullManipulationInput.Instance`; `enum HitRegion { None, Content, Frame, Handle }`; `struct ScreenHit { Screen, Region, Distance, Point, LocalX, LocalY }`; `ScreenHitTest.FrameWidthM = 0.03f`, `HandleSizeM = 0.06f`, `RaycastPlane(origin, dir, pose, out t, out lx, out ly)`, `Raycast(origin, dir, pose, width, height, out t, out lx, out ly)`, `Classify(lx, ly, w, h)`, `Pick(Workspace, origin, dir)`; `SurfaceRay.TryIntersect(LayoutSurface, float radius, Vec3 origin, Vec3 dir, out float azimuthDeg, out float vertical)` (vertical = elevação na Esfera, altura em metros no Cilindro).

- [ ] **Step 1: Teste que falha**

`Assets/_Project/Tests/EditMode/Manipulation/ScreenHitTestTests.cs`:
```csharp
using NUnit.Framework;
using ScreenPortal.App.Manipulation;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class ScreenHitTestTests
    {
        // tela 1 m × 0,5625 m em (0; 1,2; 1,3), sem rotação; raio saindo do centro da superfície
        private static readonly Vec3 Eye = LayoutSurface.DefaultCenter;

        private static Workspace OneScreen()
        {
            new CreateScreen(Resolution.Create(1920, 1080), Placement.OnSurface(0f, 0f, 1.3f), 1f).Apply(Workspace.Empty, out var ws);
            return ws;
        }

        private static Vec3 To(float x, float y) => (new Vec3(x, 1.2f + y, 1.3f) - Eye).Normalized;

        [TestCase(0f, 0f, HitRegion.Content)]
        [TestCase(0.45f, 0.2f, HitRegion.Content)]
        [TestCase(0.515f, 0f, HitRegion.Frame)]
        [TestCase(0f, 0.29f, HitRegion.Frame)]
        [TestCase(0.5f, 0.28f, HitRegion.Handle)]
        [TestCase(-0.49f, -0.27f, HitRegion.Handle)]
        [TestCase(0.6f, 0f, HitRegion.None)]
        public void Pick_ClassifiesContentFrameAndHandles(float x, float y, HitRegion expected)
        {
            var hit = ScreenHitTest.Pick(OneScreen(), Eye, To(x, y));
            Assert.That(hit.Region, Is.EqualTo(expected));
            if (expected != HitRegion.None) Assert.That(hit.Distance, Is.GreaterThan(1.29f));
        }

        [Test]
        public void Pick_ReturnsTheNearestScreen()
        {
            var ws = OneScreen();
            new CreateScreen(Resolution.Create(1920, 1080), Placement.OnSurface(0f, 0f, 0.8f), 1f).Apply(ws, out ws);
            var hit = ScreenHitTest.Pick(ws, Eye, To(0f, 0f));
            Assert.That(hit.Screen, Is.EqualTo(new ScreenId(2)));
        }

        [Test]
        public void RayPointingAway_HitsNothing() =>
            Assert.That(ScreenHitTest.Pick(OneScreen(), Eye, new Vec3(0f, 0f, -1f)).Region, Is.EqualTo(HitRegion.None));

        [Test]
        public void SurfaceRay_FromTheCenter_ReturnsTheRayAngles()
        {
            var sphere = LayoutSurface.Default.WithMode(LayoutMode.Sphere);
            var dir = LayoutSurface.Direction(25f, 10f);
            Assert.That(SurfaceRay.TryIntersect(sphere, 1.5f, Eye, dir, out float az, out float el), Is.True);
            Assert.That(az, Is.EqualTo(25f).Within(0.01f));
            Assert.That(el, Is.EqualTo(10f).Within(0.01f));

            Assert.That(SurfaceRay.TryIntersect(LayoutSurface.Default, 1.5f, Eye, dir, out az, out float height), Is.True);
            Assert.That(az, Is.EqualTo(25f).Within(0.01f));
            Assert.That(height, Is.EqualTo(1.5f * System.Math.Tan(10.0 * System.Math.PI / 180.0)).Within(0.001f));
        }
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q` → Expected: FAIL (`ScreenPortal.App.Manipulation` não existe).

- [ ] **Step 3: Implementar**

`Assets/_Project/Application/Manipulation/ManipulationInput.cs`:
```csharp
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Manipulation
{
    /// <summary>
    /// Um ponteiro num quadro (controle, mão ou, na Fase 3, o mouse). Adapters traduzem o hardware para isto
    /// (brief §8.3: mouse e mãos geram os mesmos Commands). Struct mutável: preenchida sem alocação.
    /// </summary>
    public struct ManipulationInput
    {
        public bool HasRay;
        public Vec3 Origin;
        public Vec3 Direction;
        /// <summary>Gatilho (controle) ou pinça do indicador (mão).</summary>
        public bool SelectHeld;
        /// <summary>Grip (controle) ou mão fechada: arrastar solta a tela da superfície.</summary>
        public bool GripHeld;
        /// <summary>Quanto afastar (+) ou aproximar (−) neste quadro: analógico ou puxar/empurrar a mão.</summary>
        public float DistanceDeltaM;
        public bool CancelPressed;
        public bool UndoPressed;
        public bool RedoPressed;
    }

    public interface IManipulationInput
    {
        /// <summary>false = dispositivo ausente neste quadro.</summary>
        bool Read(out ManipulationInput input);
    }

    public sealed class NullManipulationInput : IManipulationInput
    {
        public static readonly NullManipulationInput Instance = new NullManipulationInput();

        public bool Read(out ManipulationInput input)
        {
            input = default;
            return false;
        }
    }
}
```

`Assets/_Project/Application/Manipulation/ScreenHitTest.cs`:
```csharp
using System;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Manipulation
{
    public enum HitRegion
    {
        None,
        /// <summary>Meio da tela: reservado aos cliques no Windows (Fase 3); não manipula.</summary>
        Content,
        /// <summary>Faixa fora da borda: mover.</summary>
        Frame,
        /// <summary>Cantos: redimensionar.</summary>
        Handle,
    }

    public readonly struct ScreenHit
    {
        public readonly ScreenId Screen;
        public readonly HitRegion Region;
        public readonly float Distance;
        public readonly Vec3 Point;
        public readonly float LocalX;
        public readonly float LocalY;

        public ScreenHit(ScreenId screen, HitRegion region, float distance, Vec3 point, float localX, float localY)
        {
            Screen = screen;
            Region = region;
            Distance = distance;
            Point = point;
            LocalX = localX;
            LocalY = localY;
        }
    }

    /// <summary>Raio × tela como matemática pura (sem colliders): moldura, alças e menu testáveis sem Unity.</summary>
    public static class ScreenHitTest
    {
        public const float FrameWidthM = 0.03f;
        public const float HandleSizeM = 0.06f;

        /// <summary>Interseção com o plano da pose (normal = +Z local). lx/ly em metros a partir do centro.</summary>
        public static bool RaycastPlane(Vec3 origin, Vec3 direction, Pose pose, out float t, out float localX, out float localY)
        {
            t = 0f;
            localX = 0f;
            localY = 0f;
            var normal = pose.Rotation * Vec3.Forward;
            float denom = Vec3.Dot(direction, normal);
            if (Math.Abs(denom) < 1e-6f) return false;
            t = Vec3.Dot(pose.Position - origin, normal) / denom;
            if (t <= 0f) return false;
            var local = pose.Rotation.Inverse * (origin + direction * t - pose.Position);
            localX = local.X;
            localY = local.Y;
            return true;
        }

        /// <summary>Como <see cref="RaycastPlane"/>, mas só dentro do retângulo (painéis).</summary>
        public static bool Raycast(Vec3 origin, Vec3 direction, Pose pose, float width, float height, out float t, out float localX, out float localY) =>
            RaycastPlane(origin, direction, pose, out t, out localX, out localY)
            && Math.Abs(localX) <= width * 0.5f && Math.Abs(localY) <= height * 0.5f;

        public static HitRegion Classify(float localX, float localY, float width, float height)
        {
            float ax = Math.Abs(localX);
            float ay = Math.Abs(localY);
            float hw = width * 0.5f;
            float hh = height * 0.5f;
            if (ax > hw + FrameWidthM || ay > hh + FrameWidthM) return HitRegion.None;
            if (ax >= hw - HandleSizeM * 0.5f && ay >= hh - HandleSizeM * 0.5f) return HitRegion.Handle;
            if (ax > hw || ay > hh) return HitRegion.Frame;
            return HitRegion.Content;
        }

        /// <summary>A tela mais próxima que o raio acerta (qualquer região ≠ None). Sem alocação.</summary>
        public static ScreenHit Pick(Workspace workspace, Vec3 origin, Vec3 direction)
        {
            var best = default(ScreenHit);
            float bestT = float.MaxValue;
            var surface = workspace.Surface;
            var screens = workspace.Screens;
            for (int i = 0; i < screens.Count; i++)
            {
                var screen = screens[i];
                var pose = surface.ToPose(screen.Placement);
                if (!RaycastPlane(origin, direction, pose, out float t, out float lx, out float ly)) continue;
                var region = Classify(lx, ly, screen.WidthMeters, screen.HeightMeters);
                if (region == HitRegion.None || t >= bestT) continue;
                bestT = t;
                best = new ScreenHit(screen.Id, region, t, origin + direction * t, lx, ly);
            }
            return best;
        }
    }
}
```

`Assets/_Project/Application/Manipulation/SurfaceRay.cs`:
```csharp
using System;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Manipulation
{
    /// <summary>
    /// Onde o raio cruza a superfície com um raio dado: esfera de raio r (Esfera) ou cilindro vertical de raio r
    /// (Cilindro). Usa a interseção mais distante (o usuário está dentro). vertical = elevação (°) na Esfera,
    /// altura (m) relativa ao centro no Cilindro.
    /// </summary>
    public static class SurfaceRay
    {
        public static bool TryIntersect(LayoutSurface surface, float radius, Vec3 origin, Vec3 direction, out float azimuthDeg, out float vertical)
        {
            azimuthDeg = 0f;
            vertical = 0f;
            var inverse = surface.FrameRotation.Inverse;
            var o = inverse * (origin - surface.Center);
            var d = (inverse * direction).Normalized;
            Vec3 p;
            if (surface.Mode == LayoutMode.Sphere)
            {
                float b = Vec3.Dot(o, d);
                float c = Vec3.Dot(o, o) - radius * radius;
                float disc = b * b - c;
                if (disc < 0f) return false;
                float t = -b + (float)Math.Sqrt(disc);
                if (t <= 0f) return false;
                p = o + d * t;
                vertical = (float)(Math.Asin(Math.Max(-1.0, Math.Min(1.0, p.Y / radius))) * Angles.RadToDeg);
            }
            else
            {
                float a = d.X * d.X + d.Z * d.Z;
                if (a < 1e-8f) return false;
                float b = o.X * d.X + o.Z * d.Z;
                float c = o.X * o.X + o.Z * o.Z - radius * radius;
                float disc = b * b - a * c;
                if (disc < 0f) return false;
                float t = (-b + (float)Math.Sqrt(disc)) / a;
                if (t <= 0f) return false;
                p = o + d * t;
                vertical = p.Y;
            }
            azimuthDeg = (float)(Math.Atan2(p.X, p.Z) * Angles.RadToDeg);
            return true;
        }
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q` → Expected: `Com falha: 0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Application/Manipulation Assets/_Project/Tests/EditMode/Manipulation
git commit -m "app: pointer input, pure ray hit-testing (content/frame/handles) and ray-surface intersection"
```

---

### Task 7: `ScreenManipulationController`

**Files:**
- Create: `Assets/_Project/Application/Manipulation/ManipulationPreview.cs`, `ScreenManipulationController.cs`
- Test: `Assets/_Project/Tests/EditMode/Manipulation/ScreenManipulationControllerTests.cs`

**Interfaces:**
- Consumes: Tasks 2–6; `WorkspaceStore`, `ICommandBus`, `SetFocus`, `ResizeScreen`, `ScreenLimits`.
- Produces: `enum ManipulationState { Idle, Hover, Moving, Resizing }`; `ManipulationPreview { Active, Screen, Pose, Placement, IsLoose, WidthM, HeightM, HasGuideAzimuth, GuideAzimuthDeg, HasGuideElevation, GuideElevationDeg }`; `ScreenManipulationController(WorkspaceStore, ICommandBus)` com `State`, `HoverHit`, `HoverPointer`, `ActivePointer`, `Preview`, `IsDragging`, `Update(in ManipulationInput right, in ManipulationInput left)`.

- [ ] **Step 1: Teste que falha**

`Assets/_Project/Tests/EditMode/Manipulation/ScreenManipulationControllerTests.cs`:
```csharp
using System.Collections.Generic;
using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Manipulation;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class ScreenManipulationControllerTests
    {
        private static readonly Vec3 Eye = LayoutSurface.DefaultCenter;

        private sealed class Recorder : ICommandObserver
        {
            public readonly List<ICommand> Undoable = new List<ICommand>();
            public void OnCommandProcessed(ICommand command, CommandResult result)
            {
                if (result.Accepted && command.IsUndoable) Undoable.Add(command);
            }
        }

        private sealed class Rig
        {
            public readonly WorkspaceStore Store = new WorkspaceStore();
            public readonly Recorder Recorder = new Recorder();
            public readonly CommandBus Bus;
            public readonly ScreenManipulationController Controller;

            public Rig(params Placement[] screens)
            {
                foreach (var p in screens) Store.Dispatch(new CreateScreen(Resolution.Create(1920, 1080), p, 1f));
                Bus = new CommandBus(Store, new ICommandObserver[] { Recorder });
                Controller = new ScreenManipulationController(Store, Bus);
            }

            public Pose PoseOf(int index) => Store.State.Surface.ToPose(Store.State.Screens[index].Placement);

            /// <summary>Direção do olho até um ponto local da tela (metros a partir do centro).</summary>
            public Vec3 Toward(int index, float x, float y)
            {
                var pose = PoseOf(index);
                return (pose.Position + pose.Rotation * new Vec3(x, y, 0f) - Eye).Normalized;
            }

            public void Right(Vec3 dir, bool select, bool grip = false, float delta = 0f, bool cancel = false, ManipulationInput left = default) =>
                Controller.Update(Ray(dir, select, grip, delta, cancel), left);
        }

        private static ManipulationInput Ray(Vec3 dir, bool select, bool grip = false, float delta = 0f, bool cancel = false) =>
            new ManipulationInput { HasRay = true, Origin = Eye, Direction = dir, SelectHeld = select, GripHeld = grip, DistanceDeltaM = delta, CancelPressed = cancel };

        private static Vec3 Yawed(Vec3 dir, float degrees) => Quat.AngleAxis(degrees, Vec3.Up) * dir;

        private const float FrameX = 0.515f; // faixa da moldura à direita

        [Test]
        public void PointingAtTheFrame_Hovers_ContentDoesNot()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            r.Right(r.Toward(0, FrameX, 0f), false);
            Assert.That(r.Controller.State, Is.EqualTo(ManipulationState.Hover));
            Assert.That(r.Controller.HoverHit.Region, Is.EqualTo(HitRegion.Frame));
            r.Right(r.Toward(0, 0f, 0f), false);
            Assert.That(r.Controller.State, Is.EqualTo(ManipulationState.Idle));
            r.Right(r.Toward(0, 0f, 0f), true);
            Assert.That(r.Controller.IsDragging, Is.False, "clicar no meio não pega a tela");
        }

        [Test]
        public void DragFrame_MovesOnTheCylinder_OneCommandOnRelease_UndoRestores()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            var start = r.Store.State;
            var grab = r.Toward(0, FrameX, 0f);
            r.Right(grab, false);
            r.Right(grab, true);
            Assert.That(r.Controller.State, Is.EqualTo(ManipulationState.Moving));
            r.Right(Yawed(grab, 10f), true);
            Assert.That(r.Controller.Preview.Active, Is.True);
            Assert.That(r.Controller.Preview.Placement.AzimuthDeg, Is.EqualTo(10f).Within(0.05f));
            Assert.That(r.Recorder.Undoable, Is.Empty, "nada muda durante o arraste");

            r.Right(Yawed(grab, 10f), false);
            Assert.That(r.Recorder.Undoable.Count, Is.EqualTo(1));
            Assert.That(r.Recorder.Undoable[0], Is.TypeOf<MoveScreenOnSurface>());
            var p = r.Store.State.Screens[0].Placement;
            Assert.That(p.AzimuthDeg, Is.EqualTo(10f).Within(0.05f));
            Assert.That(p.ElevationDeg, Is.EqualTo(0f).Within(0.05f));
            Assert.That(p.DistanceM, Is.EqualTo(1.3f).Within(0.001f));
            Assert.That(r.Controller.Preview.Active, Is.False);

            r.Bus.Undo();
            Assert.That(r.Store.State.Screens[0].Placement, Is.EqualTo(start.Screens[0].Placement));
        }

        [Test]
        public void DistanceDelta_ChangesTheRadius_InTheSameCommand()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            var grab = r.Toward(0, FrameX, 0f);
            r.Right(grab, false);
            r.Right(grab, true);
            r.Right(grab, true, delta: 0.2f);
            r.Right(grab, false);
            Assert.That(r.Recorder.Undoable.Count, Is.EqualTo(1));
            Assert.That(r.Store.State.Screens[0].Placement.DistanceM, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(r.Store.State.Screens[0].Placement.AzimuthDeg, Is.EqualTo(0f).Within(0.05f));
        }

        [Test]
        public void Cancel_RestoresWithoutCommands()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            var grab = r.Toward(0, FrameX, 0f);
            r.Right(grab, false);
            r.Right(grab, true);
            r.Right(Yawed(grab, 15f), true);
            r.Right(Yawed(grab, 15f), true, cancel: true);
            Assert.That(r.Controller.IsDragging, Is.False);
            Assert.That(r.Controller.Preview.Active, Is.False);
            r.Right(Yawed(grab, 15f), false);
            Assert.That(r.Recorder.Undoable, Is.Empty);
            Assert.That(r.Store.State.Screens[0].Placement.AzimuthDeg, Is.EqualTo(0f));
        }

        [Test]
        public void Handle_ResizesProportionally_ResolutionNeverChanges()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            float hh = 0.5f / (16f / 9f) * 1f; // metade da altura (0,28125)
            var corner = r.Toward(0, 0.5f, hh);
            r.Right(corner, false);
            Assert.That(r.Controller.HoverHit.Region, Is.EqualTo(HitRegion.Handle));
            r.Right(corner, true);
            Assert.That(r.Controller.State, Is.EqualTo(ManipulationState.Resizing));
            r.Right(r.Toward(0, 0.6f, hh * 1.2f), true);
            r.Right(r.Toward(0, 0.6f, hh * 1.2f), false);
            Assert.That(r.Recorder.Undoable.Count, Is.EqualTo(1));
            Assert.That(r.Recorder.Undoable[0], Is.TypeOf<ResizeScreen>());
            Assert.That(r.Store.State.Screens[0].WidthMeters, Is.EqualTo(1.2f).Within(0.002f));
            Assert.That(r.Store.State.Screens[0].Resolution, Is.EqualTo(Resolution.Create(1920, 1080)));
        }

        [Test]
        public void Grip_MakesTheScreenLoose()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            var grab = r.Toward(0, FrameX, 0f);
            r.Right(grab, false);
            r.Right(grab, true);
            r.Right(Yawed(grab, 10f), true, grip: true);
            Assert.That(r.Controller.Preview.IsLoose, Is.True);
            r.Right(Yawed(grab, 10f), false, grip: true);
            Assert.That(r.Recorder.Undoable.Count, Is.EqualTo(1));
            Assert.That(r.Recorder.Undoable[0], Is.TypeOf<SetScreenLoose>());
            Assert.That(r.Store.State.Screens[0].Placement.IsLoose, Is.True);
        }

        [Test]
        public void Grid_SnapsWhileDragging_OtherHandSelectTurnsSnappingOff()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            r.Store.Dispatch(new SetSnapOptions(new SnapOptions(false, true, 5f)));
            var grab = r.Toward(0, FrameX, 0f);
            r.Right(grab, false);
            r.Right(grab, true);
            r.Right(Yawed(grab, 9f), true);
            Assert.That(r.Controller.Preview.Placement.AzimuthDeg, Is.EqualTo(10f).Within(0.01f), "gruda na grade");
            Assert.That(r.Controller.Preview.HasGuideAzimuth, Is.True);
            var otherSelect = new ManipulationInput { SelectHeld = true };
            r.Right(Yawed(grab, 9f), true, left: otherSelect);
            Assert.That(r.Controller.Preview.Placement.AzimuthDeg, Is.EqualTo(9f).Within(0.05f), "sem encaixe");
        }

        [Test]
        public void OnlyOneManipulationAtATime()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f), Placement.OnSurface(-60f, 0f, 1.3f));
            var grabA = r.Toward(0, FrameX, 0f);
            var leftAtB = Ray(r.Toward(1, FrameX, 0f), false);
            r.Right(grabA, false, left: leftAtB);
            r.Right(grabA, true, left: leftAtB);
            var leftPress = Ray(r.Toward(1, FrameX, 0f), true);
            r.Right(Yawed(grabA, 5f), true, left: leftPress);
            Assert.That(r.Controller.ActivePointer, Is.EqualTo(0));
            Assert.That(r.Controller.Preview.Screen, Is.EqualTo(r.Store.State.Screens[0].Id));
            r.Right(Yawed(grabA, 5f), false, left: leftPress);
            Assert.That(r.Recorder.Undoable.Count, Is.EqualTo(1));
            Assert.That(r.Store.State.Screens[1].Placement.AzimuthDeg, Is.EqualTo(-60f).Within(0.01f));
        }
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q` → Expected: FAIL (`ScreenManipulationController` não existe).

- [ ] **Step 3: Implementar**

`Assets/_Project/Application/Manipulation/ManipulationPreview.cs`:
```csharp
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Manipulation
{
    /// <summary>
    /// Estado transitório do arraste (não é estado da workspace): a view desenha isto até o Commit.
    /// Uma instância reutilizada — sem alocação por quadro.
    /// </summary>
    public sealed class ManipulationPreview
    {
        public bool Active { get; private set; }
        public ScreenId Screen { get; private set; }
        public Pose Pose { get; private set; }
        public Placement Placement { get; private set; }
        public bool IsLoose { get; private set; }
        public float WidthM { get; private set; }
        public float HeightM { get; private set; }
        public bool HasGuideAzimuth { get; private set; }
        public float GuideAzimuthDeg { get; private set; }
        public bool HasGuideElevation { get; private set; }
        public float GuideElevationDeg { get; private set; }

        internal void Begin(ScreenId screen, Pose pose, Placement placement, float widthM, float heightM)
        {
            Active = true;
            Screen = screen;
            Pose = pose;
            Placement = placement;
            IsLoose = placement.IsLoose;
            WidthM = widthM;
            HeightM = heightM;
            ClearGuides();
        }

        internal void SetPlacement(Placement placement, Pose pose)
        {
            Placement = placement;
            Pose = pose;
            IsLoose = false;
        }

        internal void SetLoose(Pose pose)
        {
            Pose = pose;
            Placement = Placement.Loose(pose);
            IsLoose = true;
        }

        internal void SetSize(float widthM, float heightM)
        {
            WidthM = widthM;
            HeightM = heightM;
        }

        internal void SetGuides(bool azimuth, float azimuthDeg, bool elevation, float elevationDeg)
        {
            HasGuideAzimuth = azimuth;
            GuideAzimuthDeg = azimuthDeg;
            HasGuideElevation = elevation;
            GuideElevationDeg = elevationDeg;
        }

        internal void ClearGuides() => SetGuides(false, 0f, false, 0f);

        internal void End()
        {
            Active = false;
            Screen = ScreenId.None;
            ClearGuides();
        }
    }
}
```

`Assets/_Project/Application/Manipulation/ScreenManipulationController.cs`:
```csharp
using System;
using System.Collections.Generic;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Manipulation
{
    public enum ManipulationState
    {
        Idle,
        Hover,
        Moving,
        Resizing,
    }

    /// <summary>
    /// State Machine da manipulação (brief §10.10, spec 2B §4): Idle → Hover(moldura/alça) → Moving | Resizing →
    /// Commit | Cancel. Pré-visualização pura a cada quadro; **um** Command ao soltar. Um ponteiro por vez:
    /// o outro só modifica (sem encaixe, solto) ou cancela. Ponteiro 0 = direita, 1 = esquerda. Sem alocação por quadro.
    /// </summary>
    public sealed class ScreenManipulationController
    {
        private readonly WorkspaceStore _store;
        private readonly ICommandBus _bus;
        private readonly List<AngularRect> _others = new List<AngularRect>();
        private bool _prevSelectRight;
        private bool _prevSelectLeft;

        private ScreenId _screen;
        private Placement _startPlacement;
        private Pose _startPose;
        private float _startWidth;
        private float _aspect;
        private float _startRadius;
        private float _radiusOffset;
        private float _offsetAzimuth;
        private float _offsetVertical;
        private float _resizeStartDistance;
        private bool _loose;
        private float _looseDistance;
        private Vec3 _looseOffset;

        public ManipulationState State { get; private set; }
        public ScreenHit HoverHit { get; private set; }
        public int HoverPointer { get; private set; } = -1;
        public int ActivePointer { get; private set; } = -1;
        public ManipulationPreview Preview { get; } = new ManipulationPreview();
        public bool IsDragging => State == ManipulationState.Moving || State == ManipulationState.Resizing;

        public ScreenManipulationController(WorkspaceStore store, ICommandBus bus)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public void Update(in ManipulationInput right, in ManipulationInput left)
        {
            if (IsDragging)
            {
                if (ActivePointer == 0) UpdateDrag(in right, in left);
                else UpdateDrag(in left, in right);
            }
            else
            {
                UpdateHover(in right, in left);
            }
            _prevSelectRight = right.SelectHeld;
            _prevSelectLeft = left.SelectHeld;
        }

        private void UpdateHover(in ManipulationInput right, in ManipulationInput left)
        {
            State = ManipulationState.Idle;
            HoverHit = default;
            HoverPointer = -1;
            var ws = _store.State;
            if (TryHover(0, in right, _prevSelectRight, ws)) return;
            TryHover(1, in left, _prevSelectLeft, ws);
        }

        /// <returns>true se começou um arraste.</returns>
        private bool TryHover(int pointer, in ManipulationInput input, bool previousSelect, Workspace ws)
        {
            if (!input.HasRay) return false;
            var hit = ScreenHitTest.Pick(ws, input.Origin, input.Direction);
            if (hit.Region != HitRegion.Frame && hit.Region != HitRegion.Handle) return false;
            if (HoverPointer < 0)
            {
                HoverHit = hit;
                HoverPointer = pointer;
                State = ManipulationState.Hover;
            }
            if (!input.SelectHeld || previousSelect) return false;
            Begin(pointer, in input, hit, ws);
            return true;
        }

        private void Begin(int pointer, in ManipulationInput input, ScreenHit hit, Workspace ws)
        {
            if (!ws.TryGetScreen(hit.Screen, out var screen)) return;
            var surface = ws.Surface;
            _screen = screen.Id;
            ActivePointer = pointer;
            HoverHit = hit;
            HoverPointer = pointer;
            _startPlacement = screen.Placement;
            _startWidth = screen.WidthMeters;
            _aspect = screen.Resolution.AspectRatio;
            _startPose = surface.ToPose(screen.Placement);
            Preview.Begin(screen.Id, _startPose, screen.Placement, screen.WidthMeters, screen.HeightMeters);
            if (ws.FocusedScreenId != screen.Id) _bus.Send(new SetFocus(screen.Id));

            if (hit.Region == HitRegion.Handle)
            {
                State = ManipulationState.Resizing;
                _resizeStartDistance = Math.Max(0.01f, (hit.Point - _startPose.Position).Length);
                return;
            }

            State = ManipulationState.Moving;
            CollectOthers(ws, screen.Id);
            _loose = false;
            if (screen.Placement.IsLoose) EnterLoose(in input);
            else AnchorSurfaceGrab(surface, in input, screen.Placement);
        }

        private void UpdateDrag(in ManipulationInput active, in ManipulationInput other)
        {
            if (active.CancelPressed || other.CancelPressed)
            {
                Reset();
                return;
            }
            if (!active.SelectHeld)
            {
                Commit();
                return;
            }
            if (!active.HasRay) return;
            if (State == ManipulationState.Resizing)
            {
                UpdateResize(in active);
                return;
            }

            bool loose = _startPlacement.IsLoose || active.GripHeld || other.GripHeld;
            if (loose)
            {
                if (!_loose) EnterLoose(in active);
                UpdateLoose(in active);
                return;
            }

            var surface = _store.State.Surface;
            if (_loose)
            {
                // grip solto no meio do arraste: volta a andar sobre a superfície a partir de onde está
                _loose = false;
                AnchorSurfaceGrab(surface, in active, surface.FromWorldPosition(Preview.Pose.Position));
            }

            _radiusOffset += active.DistanceDeltaM;
            float radius = Placement.ClampDistance(_startRadius + _radiusOffset);
            if (!SurfaceRay.TryIntersect(surface, radius, active.Origin, active.Direction, out float az, out float vertical)) return;
            az += _offsetAzimuth;
            vertical += _offsetVertical;
            var candidate = surface.Mode == LayoutMode.Cylinder
                ? CylinderMath.FromAzimuthHeight(az, vertical, radius)
                : Placement.OnSurface(az, vertical, radius);

            Preview.ClearGuides();
            if (!other.SelectHeld) candidate = Snap(surface, candidate, radius);
            Preview.SetPlacement(candidate, surface.ToPose(candidate));
        }

        private void AnchorSurfaceGrab(LayoutSurface surface, in ManipulationInput input, Placement placement)
        {
            _startRadius = surface.Mode == LayoutMode.Cylinder ? CylinderMath.HorizontalRadius(placement) : placement.DistanceM;
            _radiusOffset = 0f;
            if (SurfaceRay.TryIntersect(surface, _startRadius, input.Origin, input.Direction, out float az, out float vertical))
            {
                _offsetAzimuth = placement.AzimuthDeg - az;
                _offsetVertical = Vertical(surface, placement) - vertical;
            }
            else
            {
                _offsetAzimuth = 0f;
                _offsetVertical = 0f;
            }
        }

        private static float Vertical(LayoutSurface surface, Placement p) =>
            surface.Mode == LayoutMode.Cylinder ? CylinderMath.Height(p) : p.ElevationDeg;

        private Placement Snap(LayoutSurface surface, Placement candidate, float radius)
        {
            var rect = ScreenAngles.Of(candidate, Preview.WidthM, Preview.HeightM, surface);
            var result = SurfaceSnapper.Snap(rect, _others, surface.Snap);
            if (!result.SnappedAzimuth && !result.SnappedElevation) return candidate;
            Preview.SetGuides(result.SnappedAzimuth, result.GuideAzimuthDeg, result.SnappedElevation, result.GuideElevationDeg);
            return surface.Mode == LayoutMode.Cylinder
                ? CylinderMath.FromAzimuthElevationAtRadius(result.AzimuthDeg, result.ElevationDeg, radius)
                : Placement.OnSurface(result.AzimuthDeg, result.ElevationDeg, radius);
        }

        private void EnterLoose(in ManipulationInput input)
        {
            var center = Preview.Pose.Position;
            var dir = input.Direction.Normalized;
            _looseDistance = Math.Max(0.1f, Vec3.Dot(center - input.Origin, dir));
            _looseOffset = center - (input.Origin + dir * _looseDistance);
            _loose = true;
        }

        private void UpdateLoose(in ManipulationInput input)
        {
            var dir = input.Direction.Normalized;
            var position = input.Origin + dir * _looseDistance + _looseOffset;
            var toScreen = position - input.Origin;
            var flat = new Vec3(toScreen.X, 0f, toScreen.Z);
            var rotation = flat.Length > 1e-3f ? Quat.LookRotation(flat, Vec3.Up) : Preview.Pose.Rotation;
            Preview.ClearGuides();
            Preview.SetLoose(new Pose(position, rotation));
        }

        private void UpdateResize(in ManipulationInput input)
        {
            if (!ScreenHitTest.RaycastPlane(input.Origin, input.Direction, _startPose, out _, out float lx, out float ly)) return;
            float distance = (float)Math.Sqrt(lx * lx + ly * ly);
            float width = ScreenLimits.ClampWidth(_startWidth * distance / _resizeStartDistance);
            Preview.SetSize(width, width / _aspect);
        }

        private void Commit()
        {
            var id = _screen;
            bool resizing = State == ManipulationState.Resizing;
            bool loose = Preview.IsLoose;
            var placement = Preview.Placement;
            var pose = Preview.Pose;
            float width = Preview.WidthM;
            Reset();

            if (resizing)
            {
                if (Math.Abs(width - _startWidth) > 1e-4f) _bus.Send(new ResizeScreen(id, width));
                return;
            }
            if (loose)
            {
                bool unchanged = _startPlacement.IsLoose && pose.Equals(_startPlacement.LoosePose);
                if (!unchanged) _bus.Send(new SetScreenLoose(id, pose));
                return;
            }
            if (!placement.Equals(_startPlacement))
                _bus.Send(new MoveScreenOnSurface(id, placement.AzimuthDeg, placement.ElevationDeg, placement.DistanceM));
        }

        private void Reset()
        {
            State = ManipulationState.Idle;
            ActivePointer = -1;
            _loose = false;
            Preview.End();
        }

        private void CollectOthers(Workspace ws, ScreenId moving)
        {
            _others.Clear();
            var screens = ws.Screens;
            for (int i = 0; i < screens.Count; i++)
            {
                if (screens[i].Id == moving) continue;
                if (ScreenAngles.TryOf(screens[i], ws.Surface, out var rect)) _others.Add(rect);
            }
        }
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q` → Expected: `Com falha: 0`.
Se `Handle_ResizesProportionally` falhar por poucos milímetros, conferir a conta do teste (`hh` = 0,5 / (16/9) = 0,28125) antes de mexer no código.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Application/Manipulation Assets/_Project/Tests/EditMode/Manipulation
git commit -m "app: ScreenManipulationController (hover, move on surface, distance, loose, resize, snap, cancel; one command per gesture)"
```

---

### Task 8: Menu de pulso, telas de teste e roteador de entrada

**Files:**
- Create: `Assets/_Project/Application/Menu/Ports.cs`, `MenuLayout.cs`, `TestScreens.cs`, `WristMenu.cs`, `Assets/_Project/Application/Manipulation/InteractionRouter.cs`
- Test: `Assets/_Project/Tests/EditMode/Manipulation/WristMenuTests.cs`

**Interfaces:**
- Consumes: Tasks 3–7.
- Produces: `IHeadPoseSource { Pose HeadPose }`, `FixedHeadPose(Pose)`, `IRecenterEvents { event Action Recentered }`, `IWristMenuPanel { bool Visible; Pose Pose; void Render(WristMenu menu, int hoverIndex) }`; `enum MenuItemId { Undo, Redo, ToggleMode, Recenter, AlignPosture, ToggleGrid, AddTestScreen, ReturnToSurface, RemoveScreen }`; `MenuLayout` (`Columns`, `ButtonWidthM`, `ButtonHeightM`, `GapM`, `StatusHeightM`, `PanelWidth`, `PanelHeight(int)`, `ButtonCenter(int index, int count, out float x, out float y)`, `ButtonAt(float lx, float ly, int count)`); `TestScreens` (`LabelPrefix`, `IsTestScreen(VirtualScreen)`, `Next(int index) → CreateScreen`); `WristMenu(WorkspaceStore, ICommandBus, IHeadPoseSource)` com `Items`, `IsEnabled`, `Label`, `StatusLine`, `Invoke`; `InteractionRouter(IManipulationInput right, IManipulationInput left, ScreenManipulationController, WristMenu, IWristMenuPanel panel, ICommandBus)` com `Tick()`, `LastRight`, `LastLeft`, `MenuHover`.

- [ ] **Step 1: Teste que falha**

`Assets/_Project/Tests/EditMode/Manipulation/WristMenuTests.cs`:
```csharp
using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class WristMenuTests
    {
        private static readonly Pose Head = new Pose(new Vec3(0f, 1.3f, 0f), Quat.AngleAxis(30f, Vec3.Up));

        private sealed class Rig
        {
            public readonly WorkspaceStore Store = new WorkspaceStore();
            public readonly CommandBus Bus;
            public readonly WristMenu Menu;

            public Rig()
            {
                Bus = new CommandBus(Store, null);
                Menu = new WristMenu(Store, Bus, new FixedHeadPose(Head));
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
            public bool Visible => true;
            public Pose Pose => Pose.At(new Vec3(0f, 1f, 0.4f));
            public int LastHover = -2;
            public void Render(WristMenu menu, int hoverIndex) => LastHover = hoverIndex;
        }

        [Test]
        public void AddTestScreen_CreatesNumberedTestScreens_AndUndoBecomesAvailable()
        {
            var r = new Rig();
            Assert.That(r.Menu.IsEnabled(MenuItemId.Undo), Is.False);
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(2));
            Assert.That(r.Store.State.Screens[0].Label, Is.EqualTo("Teste 1"));
            Assert.That(TestScreens.IsTestScreen(r.Store.State.Screens[1]), Is.True);
            Assert.That(r.Menu.IsEnabled(MenuItemId.Undo), Is.True);
            Assert.That(r.Menu.StatusLine, Is.EqualTo("Cilindro · 2 telas"));
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
            Assert.That(r.Menu.IsEnabled(MenuItemId.RemoveScreen), Is.False);
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            Assert.That(r.Menu.IsEnabled(MenuItemId.RemoveScreen), Is.True);
            Assert.That(r.Menu.IsEnabled(MenuItemId.ReturnToSurface), Is.False, "tela na superfície");
            r.Menu.Invoke(MenuItemId.RemoveScreen);
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(0));
        }

        [Test]
        public void MenuLayout_ButtonAtFindsEachButtonCenter()
        {
            int n = WristMenu.Items.Length;
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

            int index = System.Array.IndexOf(WristMenu.Items, MenuItemId.AddTestScreen);
            MenuLayout.ButtonCenter(index, WristMenu.Items.Length, out float x, out float y);
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

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests -v q` → Expected: FAIL (`ScreenPortal.App.Menu` não existe).

- [ ] **Step 3: Implementar**

`Assets/_Project/Application/Menu/Ports.cs`:
```csharp
using System;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Menu
{
    /// <summary>Pose da cabeça no mundo (recentrar, alinhar à postura).</summary>
    public interface IHeadPoseSource
    {
        Pose HeadPose { get; }
    }

    public sealed class FixedHeadPose : IHeadPoseSource
    {
        public FixedHeadPose(Pose pose) => HeadPose = pose;
        public Pose HeadPose { get; }
    }

    /// <summary>Recentrar do sistema (Quest): o app emite RecenterLayout.</summary>
    public interface IRecenterEvents
    {
        event Action Recentered;
    }

    /// <summary>Painel do menu de pulso (Presentation). Pose = centro do painel; face visível para −Z local.</summary>
    public interface IWristMenuPanel
    {
        bool Visible { get; }
        Pose Pose { get; }
        void Render(WristMenu menu, int hoverIndex);
    }
}
```

`Assets/_Project/Application/Menu/MenuLayout.cs`:
```csharp
using System;

namespace ScreenPortal.App.Menu
{
    /// <summary>Grade do menu de pulso em metros, no espaço do painel (origem no centro, +Y para cima).</summary>
    public static class MenuLayout
    {
        public const int Columns = 2;
        public const float ButtonWidthM = 0.075f;
        public const float ButtonHeightM = 0.03f;
        public const float GapM = 0.006f;
        public const float StatusHeightM = 0.025f;

        public static float PanelWidth => Columns * ButtonWidthM + (Columns + 1) * GapM;

        public static int Rows(int count) => (count + Columns - 1) / Columns;

        public static float PanelHeight(int count) => StatusHeightM + Rows(count) * (ButtonHeightM + GapM) + GapM;

        public static void ButtonCenter(int index, int count, out float x, out float y)
        {
            int column = index % Columns;
            int row = index / Columns;
            float left = -PanelWidth * 0.5f;
            float top = PanelHeight(count) * 0.5f - StatusHeightM;
            x = left + GapM + column * (ButtonWidthM + GapM) + ButtonWidthM * 0.5f;
            y = top - GapM - row * (ButtonHeightM + GapM) - ButtonHeightM * 0.5f;
        }

        /// <returns>Índice do botão, ou −1.</returns>
        public static int ButtonAt(float localX, float localY, int count)
        {
            for (int i = 0; i < count; i++)
            {
                ButtonCenter(i, count, out float x, out float y);
                if (Math.Abs(localX - x) <= ButtonWidthM * 0.5f && Math.Abs(localY - y) <= ButtonHeightM * 0.5f) return i;
            }
            return -1;
        }
    }
}
```

`Assets/_Project/Application/Menu/TestScreens.cs`:
```csharp
using System;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Menu
{
    /// <summary>Telas sem vídeo (spec 2B §5) para testar o layout antes da 2C. Telas normais do domínio.</summary>
    public static class TestScreens
    {
        public const string LabelPrefix = "Teste ";
        private static readonly Resolution[] Cycle =
        {
            Resolution.Create(1920, 1080), Resolution.Create(2560, 1440), Resolution.Create(1440, 2560),
        };

        public static bool IsTestScreen(VirtualScreen screen) =>
            screen.Label.StartsWith(LabelPrefix, StringComparison.Ordinal);

        /// <param name="index">0, 1, 2… → "Teste 1", "Teste 2"…</param>
        public static CreateScreen Next(int index)
        {
            var resolution = Cycle[index % Cycle.Length];
            float width = resolution.Orientation == Orientation.Portrait ? 0.6f : 1f;
            return new CreateScreen(resolution, null, width, 0f, LabelPrefix + (index + 1));
        }
    }
}
```

`Assets/_Project/Application/Menu/WristMenu.cs`:
```csharp
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
        AddTestScreen,
        ReturnToSurface,
        RemoveScreen,
    }

    /// <summary>Ações do menu de pulso (spec 2B §5). Tudo vira Command (ou undo/redo) no bus.</summary>
    public sealed class WristMenu
    {
        public static readonly MenuItemId[] Items =
        {
            MenuItemId.Undo, MenuItemId.Redo, MenuItemId.ToggleMode, MenuItemId.Recenter, MenuItemId.AlignPosture,
            MenuItemId.ToggleGrid, MenuItemId.AddTestScreen, MenuItemId.ReturnToSurface, MenuItemId.RemoveScreen,
        };

        private readonly WorkspaceStore _store;
        private readonly ICommandBus _bus;
        private readonly IHeadPoseSource _head;
        private int _testScreens;
        private Workspace _statusFor;
        private string _status = string.Empty;

        public WristMenu(WorkspaceStore store, ICommandBus bus, IHeadPoseSource head)
        {
            _store = store;
            _bus = bus;
            _head = head;
        }

        /// <summary>Recalculada só quando o estado muda (sem alocar por quadro).</summary>
        public string StatusLine
        {
            get
            {
                var ws = _store.State;
                if (ReferenceEquals(ws, _statusFor)) return _status;
                _statusFor = ws;
                int n = ws.Screens.Count;
                _status = (ws.Surface.Mode == LayoutMode.Cylinder ? "Cilindro" : "Esfera") + " · " + n + (n == 1 ? " tela" : " telas");
                return _status;
            }
        }

        public bool IsEnabled(MenuItemId id)
        {
            var ws = _store.State;
            switch (id)
            {
                case MenuItemId.Undo: return _store.CanUndo;
                case MenuItemId.Redo: return _store.CanRedo;
                case MenuItemId.RemoveScreen: return ws.Contains(ws.FocusedScreenId);
                case MenuItemId.ReturnToSurface:
                    return ws.TryGetScreen(ws.FocusedScreenId, out var focused) && focused.Placement.IsLoose;
                default: return true;
            }
        }

        public string Label(MenuItemId id)
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
                case MenuItemId.AddTestScreen: return "+ Tela de teste";
                case MenuItemId.ReturnToSurface: return "Voltar à superfície";
                case MenuItemId.RemoveScreen: return "Remover tela";
                default: return id.ToString();
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
    }
}
```

`Assets/_Project/Application/Manipulation/InteractionRouter.cs`:
```csharp
using ScreenPortal.App.Menu;

namespace ScreenPortal.App.Manipulation
{
    /// <summary>
    /// Um quadro de entrada: lê os dois ponteiros, trata atalhos (X/Y), dá prioridade ao menu de pulso (o raio que
    /// está no menu não manipula telas atrás dele) e alimenta o controlador. Sem alocação por quadro.
    /// </summary>
    public sealed class InteractionRouter
    {
        private readonly IManipulationInput _right;
        private readonly IManipulationInput _left;
        private readonly ScreenManipulationController _manipulation;
        private readonly WristMenu _menu;
        private readonly IWristMenuPanel _panel;
        private readonly ICommandBus _bus;
        private bool _prevRightSelect;

        public ManipulationInput LastRight { get; private set; }
        public ManipulationInput LastLeft { get; private set; }
        public int MenuHover { get; private set; } = -1;

        public InteractionRouter(IManipulationInput right, IManipulationInput left, ScreenManipulationController manipulation,
            WristMenu menu, IWristMenuPanel panel, ICommandBus bus)
        {
            _right = right ?? NullManipulationInput.Instance;
            _left = left ?? NullManipulationInput.Instance;
            _manipulation = manipulation;
            _menu = menu;
            _panel = panel;
            _bus = bus;
        }

        public void Tick()
        {
            if (!_right.Read(out var right)) right = default;
            if (!_left.Read(out var left)) left = default;
            LastRight = right;
            LastLeft = left;

            if (!_manipulation.IsDragging)
            {
                if (left.UndoPressed) _bus.Undo();
                else if (left.RedoPressed) _bus.Redo();
            }

            MenuHover = -1;
            bool rightSelect = right.SelectHeld;
            int count = WristMenu.Items.Length;
            if (_panel != null && _panel.Visible && !_manipulation.IsDragging && right.HasRay
                && ScreenHitTest.Raycast(right.Origin, right.Direction, _panel.Pose, MenuLayout.PanelWidth, MenuLayout.PanelHeight(count),
                    out _, out float lx, out float ly))
            {
                MenuHover = MenuLayout.ButtonAt(lx, ly, count);
                if (MenuHover >= 0 && rightSelect && !_prevRightSelect) _menu.Invoke(WristMenu.Items[MenuHover]);
                right.HasRay = false; // SelectHeld continua: o controlador só pega a tela num gatilho novo
            }
            _prevRightSelect = rightSelect;

            _manipulation.Update(in right, in left);
            _panel?.Render(_menu, MenuHover);
        }
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run:
```powershell
dotnet test tests-dotnet/UnityPureCode.Tests -v q
dotnet test host/Host.Tests -v q
```
Expected: os dois com `Com falha: 0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Application Assets/_Project/Tests/EditMode/Manipulation
git commit -m "app: wrist menu actions, test screens and the interaction router (menu priority, X/Y undo/redo)"
```

---

# Parte B — Unity

> Antes da Parte B, com a Unity aberta: `powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command refresh` e conferir que compila (`Unity_ReadConsole` ou Editor.log sem `error CS`). Se o gatilho não responder, focar a janela da Unity (CLAUDE.md).

### Task 9: Views (padrão de teste) e apresentador da manipulação

**Files:**
- Create: `Assets/_Project/Presentation/Screens/TestPatternTexture.cs`, `TestPatternScreenView.cs`, `Assets/_Project/Presentation/Manipulation/ManipulationPresenter.cs`
- Modify: `Assets/_Project/Presentation/Screens/OverlayScreenViewFactory.cs`

**Interfaces:**
- Consumes: Tasks 3, 7, 8.
- Produces: `OverlayScreenViewFactory(Transform root, Material unlit = null)` com `TryGetView(ScreenId, out IScreenView)`; `ManipulationPresenter(Transform parent, Material unlit, ScreenManipulationController, WorkspaceStore, OverlayScreenViewFactory, InteractionRouter)` com `Tick()` e `Dispose()`.

- [ ] **Step 1: Textura e view de teste**

`Assets/_Project/Presentation/Screens/TestPatternTexture.cs`:
```csharp
using UnityEngine;

namespace ScreenPortal.Presentation.Screens
{
    /// <summary>Grade clara com borda colorida por número (telas de teste, spec 2B §5). Criada uma vez por tela.</summary>
    public static class TestPatternTexture
    {
        public static Texture2D Create(int number)
        {
            const int width = 256;
            const int height = 144;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "TestPattern" + number,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            Color32 accent = Color.HSVToRGB(number * 0.17f % 1f, 0.6f, 0.9f);
            var grid = new Color32(150, 150, 150, 255);
            var background = new Color32(235, 235, 235, 255);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                bool border = x < 4 || y < 4 || x >= width - 4 || y >= height - 4;
                bool line = x % 16 == 0 || y % 16 == 0;
                pixels[y * width + x] = border ? accent : line ? grid : background;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
```

`Assets/_Project/Presentation/Screens/TestPatternScreenView.cs`:
```csharp
using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Screens;
using UnityEngine;
using Pose = ScreenPortal.Domain.Spatial.Pose; // UnityEngine.Pose também existe

namespace ScreenPortal.Presentation.Screens
{
    /// <summary>
    /// Tela de teste (sem vídeo): quad com textura + rótulo. Mesma interface da view com vídeo; na 2C a tela real
    /// troca só a origem da imagem. Não usa OVROverlay (a layer ficaria por cima do rótulo e da moldura).
    /// </summary>
    public sealed class TestPatternScreenView : IScreenView
    {
        private readonly GameObject _root;
        private readonly Transform _quad;
        private readonly TextMesh _label;
        private readonly Material _material;
        private readonly Texture2D _texture;
        private readonly System.Action<TestPatternScreenView> _onDisposed;

        public ScreenId Id { get; }

        public TestPatternScreenView(Transform parent, VirtualScreen screen, Material unlit, System.Action<TestPatternScreenView> onDisposed = null)
        {
            Id = screen.Id;
            _onDisposed = onDisposed;
            _root = new GameObject(screen.Label);
            _root.transform.SetParent(parent, false);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Pattern";
            quad.GetComponent<Collider>().enabled = false; // acerto do raio é puro (ScreenHitTest)
            quad.transform.SetParent(_root.transform, false);
            _quad = quad.transform;
            _texture = TestPatternTexture.Create(screen.Id.Value);
            if (unlit != null)
            {
                _material = new Material(unlit) { mainTexture = _texture };
                quad.GetComponent<MeshRenderer>().sharedMaterial = _material;
            }

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(_root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -0.003f);
            _label = labelGo.AddComponent<TextMesh>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelGo.GetComponent<MeshRenderer>().sharedMaterial = _label.font.material;
            _label.fontSize = 96;
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            _label.color = Color.black;
            _label.text = screen.Label + "\n" + screen.Resolution.Width + "×" + screen.Resolution.Height;
        }

        public void Apply(VirtualScreen screen, Pose pose) => ApplyTransform(pose, screen.WidthMeters, screen.HeightMeters);

        public void ApplyTransform(Pose pose, float widthMeters, float heightMeters)
        {
            _root.transform.SetLocalPositionAndRotation(pose.Position.ToUnity(), pose.Rotation.ToUnity());
            _quad.localScale = new Vector3(widthMeters, heightMeters, 1f);
            _label.characterSize = 0.01f * widthMeters;
        }

        public void Dispose()
        {
            _onDisposed?.Invoke(this);
            Destroy(_root);
            Destroy(_material);
            Destroy(_texture);
        }

        private static void Destroy(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
```

- [ ] **Step 2: Fábrica escolhe a view**

Substituir o corpo de `Assets/_Project/Presentation/Screens/OverlayScreenViewFactory.cs` por:
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
    /// Cria as views: OVROverlay (vídeo do PC) ou padrão de teste (telas "Teste N"). Informa a Surface de cada
    /// overlay ao decoder e dá acesso às views para a pré-visualização do arraste.
    /// </summary>
    public sealed class OverlayScreenViewFactory : IScreenViewFactory, IScreenSurfaceProvider
    {
        private readonly Transform _root;
        private readonly Material _unlit;
        private readonly Dictionary<ScreenId, IScreenView> _views = new Dictionary<ScreenId, IScreenView>();
        private readonly Dictionary<ScreenId, OverlayScreenView> _overlays = new Dictionary<ScreenId, OverlayScreenView>();

        public OverlayScreenViewFactory(Transform root, Material unlit = null)
        {
            _root = root;
            _unlit = unlit;
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
                var overlay = new OverlayScreenView(_root, screen, v =>
                {
                    _views.Remove(v.Id);
                    _overlays.Remove(v.Id);
                });
                _overlays[screen.Id] = overlay;
                view = overlay;
            }
            _views[screen.Id] = view;
            return view;
        }

        public bool TryGetView(ScreenId screen, out IScreenView view) => _views.TryGetValue(screen, out view);

        public bool TryGetSurface(ScreenId screen, out IntPtr surface)
        {
            surface = IntPtr.Zero;
            if (!_overlays.TryGetValue(screen, out var view) || view.Overlay == null) return false;
            surface = view.Overlay.externalSurfaceObject;
            return surface != IntPtr.Zero;
        }
    }
}
```

- [ ] **Step 3: Apresentador**

`Assets/_Project/Presentation/Manipulation/ManipulationPresenter.cs`:
```csharp
using System;
using ScreenPortal.App;
using ScreenPortal.App.Manipulation;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Presentation.Screens;
using UnityEngine;
using Object = UnityEngine.Object;
using Pose = ScreenPortal.Domain.Spatial.Pose;

namespace ScreenPortal.Presentation.Manipulation
{
    /// <summary>
    /// Desenha o que o controlador decide (nunca muda estado): raios, moldura + alças no hover/arraste, guias do
    /// encaixe e a pré-visualização da tela. Ao terminar sem Command (cancelar), devolve a view ao estado confirmado.
    /// Sem alocação por quadro (arrays e materiais criados no construtor).
    /// </summary>
    public sealed class ManipulationPresenter : IDisposable
    {
        private const float FrameOutsetM = 0.015f;
        private const float LineWidthM = 0.004f;
        private const float HandleSizeM = 0.04f;
        private const float RayLengthM = 3f;
        private const int GuideArcPoints = 12;
        private static readonly Color HoverColor = new Color(1f, 1f, 1f);
        private static readonly Color DragColor = new Color(0.25f, 0.8f, 1f);
        private static readonly Color GuideColor = new Color(1f, 0.8f, 0.2f);
        private static readonly Color RayColor = new Color(0.9f, 0.9f, 0.9f);

        private readonly ScreenManipulationController _controller;
        private readonly WorkspaceStore _store;
        private readonly OverlayScreenViewFactory _views;
        private readonly InteractionRouter _router;
        private GameObject _root;
        private readonly LineRenderer _frame;
        private readonly LineRenderer _guideAzimuth;
        private readonly LineRenderer _guideElevation;
        private readonly LineRenderer _rayRight;
        private readonly LineRenderer _rayLeft;
        private readonly Transform[] _handles = new Transform[4];
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly Vector3[] _two = new Vector3[2];
        private readonly Vector3[] _arc = new Vector3[GuideArcPoints];
        private ScreenId _previewing;

        public ManipulationPresenter(Transform parent, Material unlit, ScreenManipulationController controller,
            WorkspaceStore store, OverlayScreenViewFactory views, InteractionRouter router)
        {
            _controller = controller;
            _store = store;
            _views = views;
            _router = router;
            _root = new GameObject("Manipulation");
            _root.transform.SetParent(parent, false);
            _frame = Line("Frame", unlit, 4, true, DragColor);
            _guideAzimuth = Line("GuideAzimuth", unlit, 2, false, GuideColor);
            _guideElevation = Line("GuideElevation", unlit, GuideArcPoints, false, GuideColor);
            _rayRight = Line("RayRight", unlit, 2, false, RayColor);
            _rayLeft = Line("RayLeft", unlit, 2, false, RayColor);
            var handleMaterial = unlit != null ? new Material(unlit) { color = DragColor } : null;
            for (int i = 0; i < 4; i++)
            {
                var handle = GameObject.CreatePrimitive(PrimitiveType.Quad);
                handle.name = "Handle" + i;
                handle.GetComponent<Collider>().enabled = false;
                if (handleMaterial != null) handle.GetComponent<MeshRenderer>().sharedMaterial = handleMaterial;
                handle.transform.SetParent(_root.transform, false);
                handle.SetActive(false);
                _handles[i] = handle.transform;
            }
        }

        public void Tick()
        {
            DrawRay(_rayRight, _router.LastRight);
            DrawRay(_rayLeft, _router.LastLeft);

            var ws = _store.State;
            var preview = _controller.Preview;
            if (preview.Active)
            {
                if (_views.TryGetView(preview.Screen, out var view)) view.ApplyTransform(preview.Pose, preview.WidthM, preview.HeightM);
                _previewing = preview.Screen;
                DrawFrame(preview.Pose, preview.WidthM, preview.HeightM, DragColor, _controller.State == ManipulationState.Resizing);
                DrawGuides(preview, ws.Surface);
                return;
            }

            if (!_previewing.IsNone)
            {
                RestoreConfirmed(ws, _previewing);
                _previewing = ScreenId.None;
            }
            _guideAzimuth.enabled = false;
            _guideElevation.enabled = false;

            var hover = _controller.HoverHit;
            if (_controller.State == ManipulationState.Hover && ws.TryGetScreen(hover.Screen, out var screen))
                DrawFrame(ws.Surface.ToPose(screen.Placement), screen.WidthMeters, screen.HeightMeters, HoverColor, hover.Region == HitRegion.Handle);
            else
                HideFrame();
        }

        private void RestoreConfirmed(Workspace ws, ScreenId id)
        {
            if (ws.TryGetScreen(id, out var screen) && _views.TryGetView(id, out var view))
                view.Apply(screen, ws.Surface.ToPose(screen.Placement));
        }

        private void DrawRay(LineRenderer line, ManipulationInput input)
        {
            if (!input.HasRay)
            {
                line.enabled = false;
                return;
            }
            var hit = ScreenHitTest.Pick(_store.State, input.Origin, input.Direction);
            float length = hit.Region != HitRegion.None ? hit.Distance : RayLengthM;
            _two[0] = input.Origin.ToUnity();
            _two[1] = (input.Origin + input.Direction * length).ToUnity();
            line.SetPositions(_two);
            line.enabled = true;
        }

        private void DrawFrame(Pose pose, float width, float height, Color color, bool emphasizeHandles)
        {
            var position = pose.Position.ToUnity();
            var rotation = pose.Rotation.ToUnity();
            float x = width * 0.5f + FrameOutsetM;
            float y = height * 0.5f + FrameOutsetM;
            _corners[0] = position + rotation * new Vector3(-x, -y, 0f);
            _corners[1] = position + rotation * new Vector3(-x, y, 0f);
            _corners[2] = position + rotation * new Vector3(x, y, 0f);
            _corners[3] = position + rotation * new Vector3(x, -y, 0f);
            _frame.SetPositions(_corners);
            if (_frame.sharedMaterial != null) _frame.sharedMaterial.color = color;
            _frame.enabled = true;

            float size = HandleSizeM * (emphasizeHandles ? 1.5f : 1f);
            for (int i = 0; i < 4; i++)
            {
                float sx = i < 2 ? -1f : 1f;
                float sy = i == 1 || i == 2 ? 1f : -1f;
                _handles[i].SetPositionAndRotation(position + rotation * new Vector3(sx * width * 0.5f, sy * height * 0.5f, -0.002f), rotation);
                _handles[i].localScale = new Vector3(size, size, 1f);
                _handles[i].gameObject.SetActive(true);
            }
        }

        private void HideFrame()
        {
            _frame.enabled = false;
            for (int i = 0; i < 4; i++) _handles[i].gameObject.SetActive(false);
        }

        private void DrawGuides(ManipulationPreview preview, LayoutSurface surface)
        {
            var p = preview.Placement;
            bool onSurface = !preview.IsLoose;
            if (onSurface && preview.HasGuideAzimuth)
            {
                _two[0] = surface.ToPose(Placement.OnSurface(preview.GuideAzimuthDeg, p.ElevationDeg - 25f, p.DistanceM)).Position.ToUnity();
                _two[1] = surface.ToPose(Placement.OnSurface(preview.GuideAzimuthDeg, p.ElevationDeg + 25f, p.DistanceM)).Position.ToUnity();
                _guideAzimuth.SetPositions(_two);
                _guideAzimuth.enabled = true;
            }
            else
            {
                _guideAzimuth.enabled = false;
            }

            if (onSurface && preview.HasGuideElevation)
            {
                for (int i = 0; i < GuideArcPoints; i++)
                {
                    float az = p.AzimuthDeg - 40f + 80f * i / (GuideArcPoints - 1);
                    _arc[i] = surface.ToPose(Placement.OnSurface(az, preview.GuideElevationDeg, p.DistanceM)).Position.ToUnity();
                }
                _guideElevation.SetPositions(_arc);
                _guideElevation.enabled = true;
            }
            else
            {
                _guideElevation.enabled = false;
            }
        }

        private LineRenderer Line(string name, Material unlit, int points, bool loop, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = loop;
            line.positionCount = points;
            line.widthMultiplier = LineWidthM;
            if (unlit != null) line.sharedMaterial = new Material(unlit) { color = color };
            line.enabled = false;
            return line;
        }

        public void Dispose()
        {
            if (_root == null) return;
            if (Application.isPlaying) Object.Destroy(_root);
            else Object.DestroyImmediate(_root);
            _root = null;
        }
    }
}
```

- [ ] **Step 4: Compilar na Unity**

Run: `powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command refresh` e conferir o console (sem `error CS`). A Composition ainda não usa estas classes — só precisa compilar.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Presentation
git commit -m "presentation: test-pattern screen view, factory picks the view, manipulation presenter (rays, frame, handles, guides, preview)"
```

---

### Task 10: Painel do menu de pulso

**Files:**
- Create: `Assets/_Project/Presentation/Menu/WristMenuPanel.cs`

**Interfaces:**
- Consumes: `IWristMenuPanel`, `WristMenu`, `MenuLayout` (Task 8).
- Produces: `WristMenuPanel : MonoBehaviour, IWristMenuPanel` com campos serializados `anchor`, `head`, `leftHand`, `unlit`, `palmNormalLocal`.

- [ ] **Step 1: Implementar**

`Assets/_Project/Presentation/Menu/WristMenuPanel.cs`:
```csharp
using ScreenPortal.App.Menu;
using UnityEngine;
using Pose = ScreenPortal.Domain.Spatial.Pose;

namespace ScreenPortal.Presentation.Menu
{
    /// <summary>
    /// Menu de pulso (spec 2B §5): aparece com a palma esquerda virada para o rosto ou segurando Menu (≡) no controle
    /// esquerdo; fica acima da mão, virado para a cabeça. Acionado pelo raio da mão/controle direito (InteractionRouter).
    /// </summary>
    public sealed class WristMenuPanel : MonoBehaviour, IWristMenuPanel
    {
        [SerializeField] private Transform anchor;
        [SerializeField] private Transform head;
        [SerializeField] private OVRHand leftHand;
        [SerializeField] private Material unlit;
        [Tooltip("Normal da palma no espaço local da âncora da mão esquerda (calibrado no Simulator/Quest).")]
        [SerializeField] private Vector3 palmNormalLocal = Vector3.up;
        [SerializeField] private float palmFacingThreshold = 0.5f;
        [SerializeField] private float heightAboveHandM = 0.12f;

        private GameObject _content;
        private TextMesh _status;
        private TextMesh[] _labels;
        private MeshRenderer[] _buttons;
        private Material _normal;
        private Material _hover;
        private Material _disabled;

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
            var items = WristMenu.Items;
            for (int i = 0; i < items.Length; i++)
            {
                bool enabled = menu.IsEnabled(items[i]);
                _labels[i].text = menu.Label(items[i]);
                _buttons[i].sharedMaterial = !enabled ? _disabled : i == hoverIndex ? _hover : _normal;
            }
            _status.text = menu.StatusLine;
        }

        private void Build()
        {
            int count = WristMenu.Items.Length;
            _content = new GameObject("Content");
            _content.transform.SetParent(transform, false);
            _normal = Tinted(new Color(0.18f, 0.2f, 0.24f));
            _hover = Tinted(new Color(0.25f, 0.55f, 0.9f));
            _disabled = Tinted(new Color(0.1f, 0.1f, 0.12f));

            var background = Quad("Background", Tinted(new Color(0.06f, 0.06f, 0.08f)));
            background.localPosition = new Vector3(0f, 0f, 0.002f);
            background.localScale = new Vector3(MenuLayout.PanelWidth, MenuLayout.PanelHeight(count), 1f);

            _status = Text("Status", new Vector3(0f, MenuLayout.PanelHeight(count) * 0.5f - MenuLayout.StatusHeightM * 0.5f, -0.002f));
            _labels = new TextMesh[count];
            _buttons = new MeshRenderer[count];
            for (int i = 0; i < count; i++)
            {
                MenuLayout.ButtonCenter(i, count, out float x, out float y);
                var button = Quad("Button" + i, _normal);
                button.localPosition = new Vector3(x, y, 0f);
                button.localScale = new Vector3(MenuLayout.ButtonWidthM, MenuLayout.ButtonHeightM, 1f);
                _buttons[i] = button.GetComponent<MeshRenderer>();
                _labels[i] = Text("Label" + i, new Vector3(x, y, -0.002f));
            }
            _content.SetActive(false);
        }

        private Material Tinted(Color color) => unlit != null ? new Material(unlit) { color = color } : null;

        private Transform Quad(string name, Material material)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.GetComponent<Collider>().enabled = false;
            if (material != null) quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            quad.transform.SetParent(_content.transform, false);
            return quad.transform;
        }

        private TextMesh Text(string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_content.transform, false);
            go.transform.localPosition = localPosition;
            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
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

- [ ] **Step 2: Compilar e commit**

Run: `tools/unity-cmd.ps1 -Command refresh` (sem `error CS`).
```bash
git add Assets/_Project/Presentation/Menu
git commit -m "presentation: wrist menu panel (palm-facing or left Menu button; ray-operated)"
```

---

### Task 11: Adaptadores OVR (controle, mãos, cabeça, recentrar)

**Files:**
- Create: `Assets/_Project/Infrastructure/Unity/OvrPointerInput.cs`, `OvrHeadPose.cs`, `OvrRecenterEvents.cs`
- Modify: `Assets/_Project/Infrastructure/Unity/ScreenPortal.Infrastructure.asmdef` (referência `Oculus.VR`)

**Interfaces:**
- Consumes: `IManipulationInput`, `IHeadPoseSource`, `IRecenterEvents`.
- Produces: `OvrPointerInput(OVRInput.Controller controller, OVRHand hand, Transform trackingSpace, Transform head, bool isLeft)`, `OvrHeadPose(Transform head)`, `OvrRecenterEvents`.

- [ ] **Step 1: asmdef**

Em `ScreenPortal.Infrastructure.asmdef`, acrescentar `"Oculus.VR"` à lista `references`.

- [ ] **Step 2: Implementar**

`Assets/_Project/Infrastructure/Unity/OvrPointerInput.cs`:
```csharp
using ScreenPortal.App.Manipulation;
using ScreenPortal.Domain.Spatial;
using UnityEngine;

namespace ScreenPortal.Infrastructure.Unity
{
    /// <summary>
    /// Adapter (brief §8.3): mão rastreada (OVRHand) ou, sem mão, controle Touch → <see cref="ManipulationInput"/>.
    /// Mão: pinça do indicador = selecionar; mão fechada = solto; puxar/empurrar = distância.
    /// Controle: gatilho, grip, analógico Y; B/Y cancela; X/Y (esquerdo) desfaz/refaz.
    /// </summary>
    public sealed class OvrPointerInput : IManipulationInput
    {
        public const float TriggerThreshold = 0.55f;
        public const float StickDeadZone = 0.2f;
        public const float StickSpeedMps = 1.5f;
        public const float HandPushGain = 3f;
        public const float FistStrength = 0.9f;

        private readonly OVRInput.Controller _controller;
        private readonly OVRHand _hand;
        private readonly Transform _trackingSpace;
        private readonly Transform _head;
        private readonly bool _isLeft;
        private float _lastHandDistance = -1f;

        public OvrPointerInput(OVRInput.Controller controller, OVRHand hand, Transform trackingSpace, Transform head, bool isLeft)
        {
            _controller = controller;
            _hand = hand;
            _trackingSpace = trackingSpace;
            _head = head;
            _isLeft = isLeft;
        }

        public bool Read(out ManipulationInput input)
        {
            input = default;
            if (_hand != null && _hand.IsTracked && _hand.IsPointerPoseValid) return ReadHand(ref input);
            _lastHandDistance = -1f;
            if (_trackingSpace == null || !OVRInput.IsControllerConnected(_controller)) return false;

            var position = _trackingSpace.TransformPoint(OVRInput.GetLocalControllerPosition(_controller));
            var rotation = _trackingSpace.rotation * OVRInput.GetLocalControllerRotation(_controller);
            input.HasRay = true;
            input.Origin = ToDomain(position);
            input.Direction = ToDomain(rotation * Vector3.forward);
            input.SelectHeld = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, _controller) > TriggerThreshold;
            input.GripHeld = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, _controller) > TriggerThreshold;
            float stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, _controller).y;
            input.DistanceDeltaM = Mathf.Abs(stick) < StickDeadZone ? 0f : stick * StickSpeedMps * Time.deltaTime;
            input.CancelPressed = OVRInput.GetDown(OVRInput.Button.Two, _controller); // B (direito) / Y (esquerdo)
            if (_isLeft)
            {
                input.UndoPressed = OVRInput.GetDown(OVRInput.Button.One, _controller); // X
                input.RedoPressed = input.CancelPressed;                                  // Y
            }
            return true;
        }

        private bool ReadHand(ref ManipulationInput input)
        {
            var pointer = _hand.PointerPose;
            input.HasRay = true;
            input.Origin = ToDomain(pointer.position);
            input.Direction = ToDomain(pointer.forward);
            input.SelectHeld = _hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            input.GripHeld = _hand.GetFingerPinchStrength(OVRHand.HandFinger.Middle) > FistStrength
                             && _hand.GetFingerPinchStrength(OVRHand.HandFinger.Ring) > FistStrength;
            float distance = _head != null ? Vector3.Distance(_hand.transform.position, _head.position) : 0f;
            input.DistanceDeltaM = _lastHandDistance < 0f || !input.SelectHeld ? 0f : (distance - _lastHandDistance) * HandPushGain;
            _lastHandDistance = distance;
            return true;
        }

        private static Vec3 ToDomain(Vector3 v) => new Vec3(v.x, v.y, v.z);
    }
}
```

`Assets/_Project/Infrastructure/Unity/OvrHeadPose.cs`:
```csharp
using ScreenPortal.App.Menu;
using ScreenPortal.Domain.Spatial;
using UnityEngine;
using Pose = ScreenPortal.Domain.Spatial.Pose;

namespace ScreenPortal.Infrastructure.Unity
{
    public sealed class OvrHeadPose : IHeadPoseSource
    {
        private readonly Transform _head;

        public OvrHeadPose(Transform head) => _head = head;

        public Pose HeadPose
        {
            get
            {
                var p = _head.position;
                var r = _head.rotation;
                return new Pose(new Vec3(p.x, p.y, p.z), new Quat(r.x, r.y, r.z, r.w));
            }
        }
    }
}
```

`Assets/_Project/Infrastructure/Unity/OvrRecenterEvents.cs`:
```csharp
using System;
using ScreenPortal.App.Menu;

namespace ScreenPortal.Infrastructure.Unity
{
    /// <summary>Recentrar do Quest (OVRDisplay.RecenteredPose) → o app recentra a superfície (spec superfície §6).</summary>
    public sealed class OvrRecenterEvents : IRecenterEvents, IDisposable
    {
        private Action _handlers;
        private bool _hooked;

        public event Action Recentered
        {
            add
            {
                _handlers += value;
                Hook();
            }
            remove => _handlers -= value;
        }

        private void Hook()
        {
            if (_hooked || OVRManager.display == null) return;
            OVRManager.display.RecenteredPose += Raise;
            _hooked = true;
        }

        private void Raise() => _handlers?.Invoke();

        public void Dispose()
        {
            if (_hooked && OVRManager.display != null) OVRManager.display.RecenteredPose -= Raise;
            _hooked = false;
        }
    }
}
```

- [ ] **Step 3: Compilar e commit**

Run: `tools/unity-cmd.ps1 -Command refresh` (sem `error CS`).
```bash
git add Assets/_Project/Infrastructure/Unity
git commit -m "infra: OVR adapters (controller/hand pointer, head pose, system recenter)"
```

---

### Task 12: Composição, cena e testes da Unity

**Files:**
- Modify: `Assets/_Project/Composition/AppEnvironment.cs`, `AppInstaller.cs`, `AppEntryPoint.cs`, `AppLifetimeScope.cs`, `ScreenPortal.Composition.asmdef` (+ `Oculus.VR`)
- Modify: `Assets/_Project/Editor/MainSceneBuilder.cs`
- Modify: `Assets/_Project/Tests/Composition/CompositionTests.cs`

**Interfaces:**
- Consumes: tudo das Tasks 3–11.
- Produces: `AppEnvironment` com `RightPointer`, `LeftPointer`, `Head`, `Recenter`, `MenuPanel`, `UnlitMaterial` (opcionais); cena `Main` com mãos, material `Assets/_Project/Presentation/Materials/Unlit.mat` e `WristMenu`.

- [ ] **Step 1: AppEnvironment**

Substituir `Assets/_Project/Composition/AppEnvironment.cs` por:
```csharp
using ScreenPortal.App.Connection;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using ScreenPortal.Presentation.Connection;
using UnityEngine;

namespace ScreenPortal.Composition
{
    /// <summary>
    /// O que varia por plataforma/teste: descoberta, pareamento, raiz das telas, painel de status, e (2B) ponteiros,
    /// cabeça, recentrar do sistema, menu de pulso e o material unlit. Os da 2B são opcionais (testes/Editor sem rig).
    /// A raiz das telas fica na origem do mundo: o acerto do raio usa coordenadas de mundo.
    /// </summary>
    public sealed class AppEnvironment
    {
        public Transform ScreensRoot { get; }
        public ConnectionStatusView StatusView { get; }
        public IHostDiscovery Discovery { get; }
        public IPairingStore Pairing { get; }
        public ConnectionOptions Options { get; }
        public IManipulationInput RightPointer { get; }
        public IManipulationInput LeftPointer { get; }
        public IHeadPoseSource Head { get; }
        public IRecenterEvents Recenter { get; }
        public IWristMenuPanel MenuPanel { get; }
        public Material UnlitMaterial { get; }

        public AppEnvironment(Transform screensRoot, ConnectionStatusView statusView, IHostDiscovery discovery,
            IPairingStore pairing, ConnectionOptions options,
            IManipulationInput rightPointer = null, IManipulationInput leftPointer = null, IHeadPoseSource head = null,
            IRecenterEvents recenter = null, IWristMenuPanel menuPanel = null, Material unlitMaterial = null)
        {
            ScreensRoot = screensRoot;
            StatusView = statusView;
            Discovery = discovery;
            Pairing = pairing;
            Options = options;
            RightPointer = rightPointer;
            LeftPointer = leftPointer;
            Head = head;
            Recenter = recenter;
            MenuPanel = menuPanel;
            UnlitMaterial = unlitMaterial;
        }
    }
}
```

- [ ] **Step 2: AppInstaller**

Em `Assets/_Project/Composition/AppInstaller.cs`:
- usings: `using ScreenPortal.App.Manipulation;`, `using ScreenPortal.App.Menu;`, `using ScreenPortal.Domain.Layout;`, `using ScreenPortal.Domain.Spatial;`, `using ScreenPortal.Presentation.Manipulation;`
- trocar `var views = new OverlayScreenViewFactory(env.ScreensRoot);` por `var views = new OverlayScreenViewFactory(env.ScreensRoot, env.UnlitMaterial);`
- antes de `builder.RegisterEntryPoint<AppEntryPoint>().AsSelf();` acrescentar:
```csharp
            // 2B: manipulação por controle/mãos + menu de pulso (spec 2026-09-29-fase2b-manipulacao-design.md)
            builder.RegisterInstance(env.Head ?? new FixedHeadPose(Pose.At(LayoutSurface.DefaultCenter))).As<IHeadPoseSource>();
            builder.Register<ScreenManipulationController>(Lifetime.Singleton);
            builder.Register<WristMenu>(Lifetime.Singleton);
            builder.Register(r => new InteractionRouter(env.RightPointer, env.LeftPointer,
                r.Resolve<ScreenManipulationController>(), r.Resolve<WristMenu>(), env.MenuPanel, r.Resolve<ICommandBus>()), Lifetime.Singleton);
            builder.Register(r => new ManipulationPresenter(env.ScreensRoot, env.UnlitMaterial,
                r.Resolve<ScreenManipulationController>(), r.Resolve<WorkspaceStore>(), views, r.Resolve<InteractionRouter>()), Lifetime.Singleton);
```

- [ ] **Step 3: AppEntryPoint**

Substituir `Assets/_Project/Composition/AppEntryPoint.cs` por:
```csharp
using System;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using ScreenPortal.App.Screens;
using ScreenPortal.Presentation.Manipulation;
using VContainer.Unity;

namespace ScreenPortal.Composition
{
    /// <summary>Liga views, painel de status, conexão e (2B) entrada/menu/manipulação; a tela do PC vem da conexão.</summary>
    public sealed class AppEntryPoint : IStartable, ITickable, IDisposable
    {
        private readonly ScreenViewSynchronizer _synchronizer;
        private readonly ConnectionStateMachine _connection;
        private readonly IControlChannel _channel;
        private readonly IHostDiscovery _discovery;
        private readonly AppEnvironment _env;
        private readonly InteractionRouter _router;
        private readonly ManipulationPresenter _presenter;
        private readonly WristMenu _menu;

        public AppEntryPoint(ScreenViewSynchronizer synchronizer, ConnectionStateMachine connection,
            IControlChannel channel, IHostDiscovery discovery, AppEnvironment env,
            InteractionRouter router, ManipulationPresenter presenter, WristMenu menu)
        {
            _synchronizer = synchronizer;
            _connection = connection;
            _channel = channel;
            _discovery = discovery;
            _env = env;
            _router = router;
            _presenter = presenter;
            _menu = menu;
        }

        public void Start()
        {
            _synchronizer.Start();
            if (_env.StatusView != null) _env.StatusView.Bind(_connection);
            if (_env.Recenter != null) _env.Recenter.Recentered += OnSystemRecentered;
            _connection.Start();
        }

        public void Tick()
        {
            _connection.Tick();
            _router.Tick();
            _presenter.Tick();
        }

        private void OnSystemRecentered() => _menu.Invoke(MenuItemId.Recenter);

        public void Dispose()
        {
            if (_env.Recenter != null) _env.Recenter.Recentered -= OnSystemRecentered;
            (_env.Recenter as IDisposable)?.Dispose();
            _presenter.Dispose();
            _channel.Close();
            _discovery.Stop();
            _synchronizer.Dispose();
        }
    }
}
```

- [ ] **Step 4: AppLifetimeScope e asmdef**

Em `ScreenPortal.Composition.asmdef`, acrescentar `"Oculus.VR"` às `references`.

Em `Assets/_Project/Composition/AppLifetimeScope.cs`:
- usings: `using ScreenPortal.App.Manipulation;`, `using ScreenPortal.App.Menu;`, `using ScreenPortal.Presentation.Menu;`
- novos campos, depois de `editorHostAddress`:
```csharp
        [Header("2B — manipulação")]
        [SerializeField] private Transform headAnchor;
        [SerializeField] private Transform trackingSpace;
        [SerializeField] private OVRHand leftHand;
        [SerializeField] private OVRHand rightHand;
        [SerializeField] private WristMenuPanel menuPanel;
        [SerializeField] private Material unlitMaterial;
```
- em `Configure`, trocar a chamada `AppInstaller.Install(...)` por:
```csharp
            IManipulationInput right = trackingSpace != null
                ? new OvrPointerInput(OVRInput.Controller.RTouch, rightHand, trackingSpace, headAnchor, false) : null;
            IManipulationInput left = trackingSpace != null
                ? new OvrPointerInput(OVRInput.Controller.LTouch, leftHand, trackingSpace, headAnchor, true) : null;
            IHeadPoseSource head = headAnchor != null ? new OvrHeadPose(headAnchor) : null;
            AppInstaller.Install(builder, new AppEnvironment(
                screensRoot != null ? screensRoot : transform, statusView, discovery, pairing, options,
                right, left, head, new OvrRecenterEvents(), menuPanel, unlitMaterial));
```

- [ ] **Step 5: Cena**

Em `Assets/_Project/Editor/MainSceneBuilder.cs`:
- constante nova: `private const string UnlitMaterialPath = "Assets/_Project/Presentation/Materials/Unlit.mat";`
- depois de `cam.backgroundColor = Color.black;`:
```csharp
            var rig = rigGo.GetComponent<OVRCameraRig>();
            var leftHand = AddHand(rig.leftHandAnchor, "LeftOVRHand", 0);   // OVRHand.Hand.HandLeft
            var rightHand = AddHand(rig.rightHandAnchor, "RightOVRHand", 1); // OVRHand.Hand.HandRight
            var unlit = LoadOrCreateUnlitMaterial();

            var menuGo = new GameObject("WristMenu");
            var menu = menuGo.AddComponent<ScreenPortal.Presentation.Menu.WristMenuPanel>();
            var menuSo = new SerializedObject(menu);
            menuSo.FindProperty("anchor").objectReferenceValue = rig.leftHandAnchor;
            menuSo.FindProperty("head").objectReferenceValue = rig.centerEyeAnchor;
            menuSo.FindProperty("leftHand").objectReferenceValue = leftHand;
            menuSo.FindProperty("unlit").objectReferenceValue = unlit;
            menuSo.ApplyModifiedPropertiesWithoutUndo();
```
- no bloco do `scope` (`so`), antes de `so.ApplyModifiedPropertiesWithoutUndo();`:
```csharp
            so.FindProperty("headAnchor").objectReferenceValue = rig.centerEyeAnchor;
            so.FindProperty("trackingSpace").objectReferenceValue = rig.trackingSpace;
            so.FindProperty("leftHand").objectReferenceValue = leftHand;
            so.FindProperty("rightHand").objectReferenceValue = rightHand;
            so.FindProperty("menuPanel").objectReferenceValue = menu;
            so.FindProperty("unlitMaterial").objectReferenceValue = unlit;
```
- métodos novos na classe:
```csharp
        /// <summary>OVRHand sem malha: só raio (PointerPose) e pinça. HandType é serializado e internal no SDK.</summary>
        private static OVRHand AddHand(Transform anchor, string name, int handType)
        {
            var go = new GameObject(name);
            go.transform.SetParent(anchor, false);
            var hand = go.AddComponent<OVRHand>();
            var so = new SerializedObject(hand);
            so.FindProperty("HandType").intValue = handType;
            so.ApplyModifiedPropertiesWithoutUndo();
            return hand;
        }

        /// <summary>Material URP/Unlit como asset: garante o shader no build (Shader.Find em runtime não garante).</summary>
        private static Material LoadOrCreateUnlitMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(UnlitMaterialPath);
            if (existing != null) return existing;
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Presentation/Materials"))
                AssetDatabase.CreateFolder("Assets/_Project/Presentation", "Materials");
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Unlit" };
            AssetDatabase.CreateAsset(material, UnlitMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }
```

- [ ] **Step 6: Teste de composição**

Em `Assets/_Project/Tests/Composition/CompositionTests.cs`, acrescentar `using ScreenPortal.App.Manipulation;` e, dentro do `using (var container ...)`:
```csharp
                Assert.That(container.Resolve<ScreenManipulationController>(), Is.Not.Null);
                Assert.That(container.Resolve<InteractionRouter>(), Is.Not.Null);
```

- [ ] **Step 7: Gerar a cena e rodar os testes da Unity**

Run:
```powershell
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command refresh
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command build-main-scene
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command test-editmode
dotnet test tests-dotnet/UnityPureCode.Tests -v q
dotnet test host/Host.Tests -v q
```
Expected: cena gerada ("OK Assets/_Project/Scenes/Main.unity"); EditMode sem falhas; harness e host `Com falha: 0`.

- [ ] **Step 8: Commit**

```bash
git add Assets/_Project ProjectSettings
git commit -m "composition: wire manipulation, wrist menu, OVR adapters and system recenter; Main scene gets hands, unlit material and the wrist menu"
```

---

### Task 13: Verificação no Meta XR Simulator (automatizada)

**Files:**
- Create: `docs/measurements/fase2b-simulator.md`

- [ ] **Step 1: Preparar**

Carregar as skills `meta-xr-operator-unity-cli-workflow` e `meta-xr-operator-hand-tracking` e seguir o setup delas (Simulator ativo, Play Mode na cena `Main`, ferramentas `openxr_*`). Sem PC host conectado: a tela do PC não aparece; os testes usam telas de teste.

- [ ] **Step 2: Roteiro** (um item por linha no relatório: ✅/❌ + observação)

| # | Ação (controle direito salvo indicação) | Esperado |
|---|---|---|
| 1 | Segurar Menu (≡) no controle esquerdo | menu aparece acima da mão, virado para a cabeça; status "Cilindro · 0 telas" |
| 2 | Raio no "+ Tela de teste" e gatilho, 3× | 3 telas lado a lado à frente, sem sobreposição; "Teste 1/2/3" |
| 3 | Apontar a moldura da Teste 1 | moldura branca + 4 alças; apontar o meio: nada |
| 4 | Gatilho na moldura e girar o controle 20° à direita; soltar | tela segue o raio; ao soltar fica lá |
| 5 | X no controle esquerdo; depois Y | desfaz (volta) e refaz |
| 6 | Arrastar para cima no Cilindro | tela sobe reta (sem inclinar) |
| 7 | Durante o arraste, analógico para cima | tela afasta; soltar = um passo de undo |
| 8 | Arrastar a borda perto da vizinha | gruda (guia amarela); continuar arrastando desgruda |
| 9 | Idem segurando o gatilho esquerdo | não gruda |
| 10 | Gatilho numa alça e afastar | tela cresce proporcional; resolução no rótulo não muda |
| 11 | Arrastar segurando o grip | tela sai da superfície; menu "Voltar à superfície" habilita; usar → volta |
| 12 | Menu "Cilindro → Esfera" | nenhuma tela sai do lugar; orientação muda; undo volta |
| 13 | Olhar para a esquerda e "Recentrar" | o layout inteiro gira para a nova frente |
| 14 | B durante um arraste | tela volta ao lugar, sem passo de undo |
| 15 | Mãos: pinçar a moldura e mover; puxar/empurrar | move; afasta/aproxima |
| 16 | Mãos: palma esquerda virada para o rosto | menu aparece (se não aparecer ou aparecer com a palma para baixo: calibrar abaixo) |
| 17 | Menu "Remover tela" | remove a tela em foco; undo a traz de volta |

**Calibração da palma (item 16):** se o menu não aparece com a palma para o rosto, testar no Inspector do `WristMenu` os valores de `palmNormalLocal` na ordem `(0,-1,0)`, `(1,0,0)`, `(-1,0,0)` até acertar; gravar o valor em `MainSceneBuilder` (`menuSo.FindProperty("palmNormalLocal").vector3Value = ...`) e regenerar a cena.

- [ ] **Step 3: Relatório e correções**

Escrever `docs/measurements/fase2b-simulator.md` (data, commit, tabela do roteiro, capturas relevantes). Falhas viram correção com teste (se a lógica for pura) antes de seguir. Máximo de 3 tentativas por falha (skill `meta-xr-operator-unity-test-mechanics`); se persistir, parar e reportar ao Cayo.

- [ ] **Step 4: Conferência visual do Cayo (~5 min)**

Pedir ao Cayo para abrir o Simulator e repetir os itens 2, 4, 8, 12 e 16, conferindo o visual (moldura, guias, menu).

- [ ] **Step 5: Commit**

```bash
git add docs/measurements/fase2b-simulator.md Assets/_Project
git commit -m "docs: Phase 2B Simulator verification"
```

---

### Task 14: Checagem curta no Quest e fechamento

**Files:**
- Modify: `docs/STATUS.md`, `docs/specs/2026-09-29-fase2b-manipulacao-design.md` (seção "Desvios na implementação")

- [ ] **Step 1: Quest (~10 min, Cayo com o óculos)**

Por que não basta o Simulator: tremor real da mão e conforto da pinça a ~1,3 m, e o controle físico.
```powershell
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command boot-main
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command build-apk -TimeoutMinutes 40
powershell -ExecutionPolicy Bypass -File tools/quest.ps1 -Install Builds/Phase0.apk -Launch
```
(O `build-apk` gera `Builds/Phase0.apk` com a cena de boot atual — a `Main`.) Repetir os itens 2, 4, 7, 10, 11, 15 e 16 do roteiro; anotar conforto e tremor em `docs/measurements/fase2b-simulator.md` (seção "Quest").

- [ ] **Step 2: Spec e STATUS**

Na spec 2B, acrescentar a seção "Desvios na implementação" com os 4 itens de "Desvios da spec" deste plano. No STATUS, linha 2B → ✅ com links para a spec, este plano e o relatório; "Fase atual" → próxima 2C.

- [ ] **Step 3: Commit**

```bash
git add docs
git commit -m "docs: Phase 2B done (Quest check, spec deviations, STATUS)"
```

Depois: skill `superpowers:finishing-a-development-branch` (merge na `main` com os testes verdes).
