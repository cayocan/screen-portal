# Fase 2E — Telas curvas — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Toda tela pode ser curvada (0–100 %, só na horizontal) pelo menu do pulso ("Curvatura ▸") ou pela alça no topo da
moldura; tela curva com vídeo é uma layer cilíndrica nítida, e laser, mãos, moldura e alças acompanham a curva.

**Architecture:** Parte A é código puro (Domain + Application, testado no `dotnet test`): presets, `CurvedGeometry` (raio,
arco, limite de 120°, centro do cilindro, rótulo), hit-test no cilindro com as mesmas coordenadas locais da tela plana
(o `Classify` não muda, só ganha a região da alça de curvatura), página "Curvatura" do menu, arraste da alça (prévia → um
`SetScreenCurvature`) e o `HostSync` reabrindo o decoder quando a layer troca de forma. Parte B é Unity (fila, precisa do
Editor): `IScreenView` com raio, `CurvedQuadMesh`, `VideoScreenView` quad ↔ cilindro, tela de teste e moldura curvas,
verificação no Simulator e no Quest.

**Tech Stack:** Unity 6.3 (C# 9), Meta XR Core SDK 207 (`OVROverlay` Cylinder/Quad, Underlay, external surface), VContainer,
NUnit (harness `tests-dotnet/UnityPureCode.Tests` e Test Runner da Unity).

## Global Constraints

- Spec: `docs/specs/2026-09-30-fase2e-telas-curvas-design.md`. ADRs 0010 (telas como layers Underlay) e 0011 (toda tela curva
  é layer cilíndrica). Medição do spike: `docs/measurements/fase2e-spike.md`.
- **C# 9 no código da Unity**: sem `record`, `init`, namespaces de arquivo.
- Domain e Application **nunca** referenciam UnityEngine. Toda mudança de estado é Command via `ICommandBus`; views nunca
  mudam estado. Namespace da Application: `ScreenPortal.App`.
- **Zero alocação por quadro** em `InteractionRouter.Tick`, `ScreenManipulationController.Update`, `ScreenHitTest.Pick`,
  `ManipulationPresenter.Tick` e no `Render` do painel. Strings (rótulos) só quando o valor muda, em cache.
- **O quadro de vídeo nunca atravessa o C#**: curva → `OVROverlay` Cylinder; plana → Quad. Nada de malha com vídeo no eye buffer.
- Regras do brief §10.2: curvatura 0–100 %, curva só horizontal, **raio = distância ÷ curvatura**, largura e resolução
  **nunca** mudam ao curvar, rótulo "≈ NNNNR".
- **Arco máximo 120°**: curvatura efetiva c' = min(c, 120° (em rad) × d ÷ largura); acima disso o rótulo diz " · arco limitado".
- Presets: **Plana 0 · Suave 0,3 · Média 0,6 · Envolvente 1,0** (Média = `ScreenLimits.DefaultCurvature`).
- Rótulo: `"Média · 60 % · ≈ 1300R"` (R em mm arredondado a 100; nome do preset só quando coincide) + `" · arco limitado"`.
- Alça de curvatura: **30 cm** ao longo da normal da tela = 100 %; prévia local sem Command; **um** `SetScreenCurvature` ao soltar.
- Telas novas nascem planas. Atalhos de teclado/mouse e curvatura no seletor de criação ficam **fora** (Fase 3).
- Plana ↔ curva recria a layer e o decoder (35–100 ms medidos); mudar o valor de uma tela já curva só muda o raio.
- Testes: `dotnet test tests-dotnet/UnityPureCode.Tests` em toda tarefa (base: 215 passando). Testes que usam UnityEngine vão
  em `Assets/_Project/Tests/Composition/` (o harness compila `Tests/EditMode/**` sem Unity).
- Não criar `.meta` à mão (a Unity gera ao importar). Nunca `git add -A`; adicionar caminhos exatos. `ProjectSettings/` fica como está.
- Unity pela CLI (Editor aberto): `unity --proxy-disable --no-banner command recompile` / `run_tests --mode EditMode`, ou
  `tools/unity-cmd.ps1 -Command refresh|test-editmode`. Nunca `unity command eval` na `main`.
- Branch `phase2e/curved-screens`. Commits pequenos, um propósito cada, sem linhas de atribuição.

## Decisões onde a spec é omissa (avisar o Cayo)

1. **Rótulo mostra a curvatura efetiva** (a que se vê): com arco limitado, "Envolvente" numa tela larga aparece como
   `"70 % · ≈ 1400R · arco limitado"`. O menu marca o preset pelo valor **pedido** (`VirtualScreen.Curvature`). Plana: `"Plana"`.
2. **Alça de curvatura só na faixa da moldura**, no meio do topo, 10 cm de largura (`ScreenHitTest.CurvatureHandleWidthM`):
   não invade o conteúdo (é onde fica a barra de título do Windows, que a Fase 3 vai clicar).
3. **"Para frente" = para longe do usuário** (ao longo da normal da tela, +Z local): empurrar a alça curva mais, puxar achata.
   Mede o deslocamento da **origem** do ponteiro (mão/controle) desde o gatilho; o analógico não entra (na mão ele já é o
   próprio empurrar/puxar e contaria duas vezes).
4. **Página "Curvatura" fica aberta** depois de escolher um preset (dá para comparar); a linha de status mostra o rótulo da
   tela em foco. "← Voltar" volta à principal.
5. **Redimensionar tela curva** usa as coordenadas locais do cilindro (arco): a distância inicial vem do `ScreenHit`, não do
   ponto 3D (na tela plana dá o mesmo número).
6. **`HostSync` reabre o decoder** quando a tela passa de plana para curva ou volta (a layer nova tem outra Surface): fecha o
   decoder e volta a `Ready` no mesmo slot; o `StartStream` repetido faz o host pedir um IDR. Sem novo `AcquireScreen`.
7. **A prévia nunca troca a forma da layer**: durante o arraste a layer de vídeo mantém a forma confirmada (só o raio muda se
   já for curva); a moldura mostra a curva prevista. Trocar a layer no meio do arraste deixaria o decoder na Surface velha.

---

## File Structure

```
Assets/_Project/Domain/Screens/CurvaturePresets.cs            NOVO  presets com nome (menu, rótulo)
Assets/_Project/Domain/Layout/CurvedGeometry.cs               NOVO  c', R, arco, limite 120°, centro do cilindro, ponto na tela, rótulo
Assets/_Project/Application/Manipulation/ScreenHitTest.cs     + RaycastCylinder/RaycastScreen, Pick curvo, HitRegion.CurvatureHandle
Assets/_Project/Application/Manipulation/ManipulationPreview.cs  + Curvature
Assets/_Project/Application/Manipulation/ScreenManipulationController.cs  + estado Curving; resize em coordenadas do arco
Assets/_Project/Application/Menu/CurvatureMenu.cs             NOVO  rótulos da página e status (em cache)
Assets/_Project/Application/Menu/WristMenu.cs                 + item "Curvatura ▸" e página Curvature (localizado)
Assets/_Project/Application/Screens/HostSync.cs               + plana ↔ curva reabre o decoder
Assets/_Project/Tests/EditMode/CurvaturePresetsTests.cs       NOVO
Assets/_Project/Tests/EditMode/Layout/CurvedGeometryTests.cs  NOVO
Assets/_Project/Tests/EditMode/Manipulation/{ScreenHitTestTests,ScreenManipulationControllerTests,WristMenuTests}.cs  +casos
Assets/_Project/Tests/EditMode/Screens/HostSyncTests.cs       +caso
--- Unity (fila)
Assets/_Project/Application/Screens/{IScreenView,ScreenViewSynchronizer}.cs   raio na view
Assets/_Project/Presentation/Screens/CurvedQuadMesh.cs        NOVO  malha de arco sem alocação por quadro
Assets/_Project/Presentation/Screens/{VideoScreenView,TestPatternScreenView}.cs   forma pela curvatura
Assets/_Project/Presentation/Manipulation/ManipulationPresenter.cs   moldura/alças curvas, alça de curvatura, rótulo
Assets/_Project/Tests/Composition/CurvedQuadMeshTests.cs      NOVO  (Unity EditMode)
docs/measurements/fase2e-telas-curvas.md, docs/brief.md (tabelas §15), docs/STATUS.md
```

---

# Parte A — Código puro (Domain / Application; `dotnet test`)

### Task 1: `CurvaturePresets`

**Files:**
- Create: `Assets/_Project/Domain/Screens/CurvaturePresets.cs`
- Test: `Assets/_Project/Tests/EditMode/CurvaturePresetsTests.cs`

**Interfaces:**
- Produces: `CurvaturePresets.Count` (4), `Flat` (0), `Tolerance` (0,005), `Value(int)`, `Name(int)`, `IndexOf(float) → int` (−1 = nenhum).

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Tests
{
    public class CurvaturePresetsTests
    {
        [Test]
        public void FourPresets_InMenuOrder_MediumIsTheDefault()
        {
            Assert.That(CurvaturePresets.Count, Is.EqualTo(4));
            Assert.That(CurvaturePresets.Name(0), Is.EqualTo("Plana"));
            Assert.That(CurvaturePresets.Name(1), Is.EqualTo("Suave"));
            Assert.That(CurvaturePresets.Name(2), Is.EqualTo("Média"));
            Assert.That(CurvaturePresets.Name(3), Is.EqualTo("Envolvente"));
            Assert.That(CurvaturePresets.Value(0), Is.EqualTo(0f));
            Assert.That(CurvaturePresets.Value(1), Is.EqualTo(0.3f));
            Assert.That(CurvaturePresets.Value(2), Is.EqualTo(ScreenLimits.DefaultCurvature));
            Assert.That(CurvaturePresets.Value(3), Is.EqualTo(1f));
        }

        [TestCase(0f, 0)]
        [TestCase(0.3f, 1)]
        [TestCase(0.302f, 1)]
        [TestCase(0.6f, 2)]
        [TestCase(1f, 3)]
        [TestCase(0.45f, -1)]
        [TestCase(0.01f, -1)]
        public void IndexOf_FindsThePresetWithinTolerance(float curvature, int expected) =>
            Assert.That(CurvaturePresets.IndexOf(curvature), Is.EqualTo(expected));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests --filter CurvaturePresetsTests`
Expected: FAIL (compilação: `CurvaturePresets` não existe).

- [ ] **Step 3: Write minimal implementation**

```csharp
using System;

namespace ScreenPortal.Domain.Screens
{
    /// <summary>Presets de curvatura (brief §10.2, spec 2E §3), na ordem do menu. Índice 0 = Plana.</summary>
    public static class CurvaturePresets
    {
        public const int Count = 4;
        public const int Flat = 0;
        /// <summary>Quanto um valor pode diferir do preset e ainda ser ele.</summary>
        public const float Tolerance = 0.005f;

        private static readonly float[] Values = { 0f, 0.3f, ScreenLimits.DefaultCurvature, 1f };
        private static readonly string[] Names = { "Plana", "Suave", "Média", "Envolvente" };

        public static float Value(int index) => Values[index];
        public static string Name(int index) => Names[index];

        /// <returns>Índice do preset igual a <paramref name="curvature"/>, ou −1.</returns>
        public static int IndexOf(float curvature)
        {
            for (int i = 0; i < Count; i++)
                if (Math.Abs(Values[i] - curvature) <= Tolerance) return i;
            return -1;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests`
Expected: PASS (215 + 8).

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Domain/Screens/CurvaturePresets.cs Assets/_Project/Tests/EditMode/CurvaturePresetsTests.cs
git commit -m "feat(domain): curvature presets (Plana, Suave, Media, Envolvente)"
```

---

### Task 2: `CurvedGeometry`

**Files:**
- Create: `Assets/_Project/Domain/Layout/CurvedGeometry.cs`
- Test: `Assets/_Project/Tests/EditMode/Layout/CurvedGeometryTests.cs`

**Interfaces:**
- Consumes: `CurvaturePresets` (Task 1), `LayoutSurface.CurvatureDistance(Placement)`, `ScreenLimits.Clamp01`.
- Produces (struct imutável, sem alocação):
  - `CurvedGeometry.MaxArcDeg` (120), campos `Curvature`, `EffectiveCurvature`, `DistanceM`, `WidthM`, `RadiusM` (0 = plana), `ArcLimited`;
  - `IsCurved`, `ArcRad`, `ArcDeg`, `Percent` (int), `RoundedRadiusMm` (int);
  - `static Of(float curvature, float distanceM, float widthM)`, `static Of(float curvature, Placement, float widthM, LayoutSurface)`,
    `static Of(VirtualScreen, LayoutSurface)`;
  - `static Pose CylinderCenter(Pose screenPose, float radiusM)`;
  - `static Vec3 SurfacePoint(Pose screenPose, float radiusM, float localX, float localY)` (x ao longo do arco; R = 0 → plano);
  - `string Label()` (aloca: chamar só quando o valor muda).

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class CurvedGeometryTests
    {
        [Test]
        public void Radius_IsDistanceOverCurvature_ArcIsWidthOverRadius()
        {
            var g = CurvedGeometry.Of(0.6f, 1.3f, 1f);
            Assert.That(g.IsCurved, Is.True);
            Assert.That(g.EffectiveCurvature, Is.EqualTo(0.6f));
            Assert.That(g.ArcLimited, Is.False);
            Assert.That(g.RadiusM, Is.EqualTo(1.3f / 0.6f).Within(1e-5f));
            Assert.That(g.ArcRad, Is.EqualTo(1f / (1.3f / 0.6f)).Within(1e-5f));
        }

        [Test]
        public void Envolvente_PutsTheCylinderAxisAtTheDistance()
        {
            var g = CurvedGeometry.Of(1f, 1.3f, 1f);
            Assert.That(g.RadiusM, Is.EqualTo(1.3f).Within(1e-6f));
        }

        [Test]
        public void Flat_HasNoRadiusNorArc()
        {
            var g = CurvedGeometry.Of(0f, 1.3f, 1f);
            Assert.That(g.IsCurved, Is.False);
            Assert.That(g.RadiusM, Is.EqualTo(0f));
            Assert.That(g.ArcRad, Is.EqualTo(0f));
            Assert.That(g.Label(), Is.EqualTo("Plana"));
        }

        [Test]
        public void ArcAbove120Degrees_IsLimited()
        {
            var g = CurvedGeometry.Of(1f, 1f, 3f); // 3 m a 1 m: arco pedido 172°
            Assert.That(g.ArcLimited, Is.True);
            Assert.That(g.ArcDeg, Is.EqualTo(120f).Within(0.01f));
            Assert.That(g.EffectiveCurvature, Is.EqualTo(2.0 * System.Math.PI / 3.0 / 3.0).Within(1e-5));
            Assert.That(g.Curvature, Is.EqualTo(1f), "o valor pedido fica guardado");
        }

        [Test]
        public void CurvatureIsClampedTo01()
        {
            Assert.That(CurvedGeometry.Of(5f, 1.3f, 1f).EffectiveCurvature, Is.EqualTo(1f));
            Assert.That(CurvedGeometry.Of(-1f, 1.3f, 1f).IsCurved, Is.False);
        }

        [Test]
        public void Distance_ComesFromTheSurface()
        {
            var placement = Placement.OnSurface(0f, 30f, 1.3f);
            var cylinder = LayoutSurface.Default;
            Assert.That(CurvedGeometry.Of(0.5f, placement, 1f, cylinder).DistanceM,
                Is.EqualTo(1.3f * System.Math.Cos(30.0 * System.Math.PI / 180.0)).Within(1e-5));
            Assert.That(CurvedGeometry.Of(0.5f, placement, 1f, cylinder.WithMode(LayoutMode.Sphere)).DistanceM, Is.EqualTo(1.3f).Within(1e-6f));

            var loose = Placement.Loose(Pose.At(LayoutSurface.DefaultCenter + new Vec3(0f, 0f, 0.9f)));
            Assert.That(CurvedGeometry.Of(0.5f, loose, 1f, cylinder).DistanceM, Is.EqualTo(0.9f).Within(1e-5f), "solta: do centro da superfície");
        }

        [Test]
        public void CylinderCenter_IsBehindTheUser_AtTheSurfaceCenterForEnvolvente()
        {
            var surface = LayoutSurface.Default;
            var pose = surface.ToPose(Placement.OnSurface(40f, 0f, 1.3f));
            var g = CurvedGeometry.Of(1f, surface.CurvatureDistance(Placement.OnSurface(40f, 0f, 1.3f)), 1f);
            var center = CurvedGeometry.CylinderCenter(pose, g.RadiusM);
            Assert.That((center.Position - surface.Center).Length, Is.LessThan(1e-4f));
            Assert.That(center.Rotation, Is.EqualTo(pose.Rotation));
        }

        [Test]
        public void SurfacePoint_LiesOnTheArc_AndOnThePlaneWhenFlat()
        {
            var pose = new Pose(new Vec3(1f, 2f, 3f), Quat.AngleAxis(30f, Vec3.Up));
            Assert.That((CurvedGeometry.SurfacePoint(pose, 2f, 0f, 0f) - pose.Position).Length, Is.LessThan(1e-6f));

            var center = CurvedGeometry.CylinderCenter(pose, 2f);
            var p = CurvedGeometry.SurfacePoint(pose, 2f, 0.5f, 0.1f);
            var fromAxis = pose.Rotation.Inverse * (p - center.Position);
            Assert.That(fromAxis.Y, Is.EqualTo(0.1f).Within(1e-6f));
            Assert.That(System.Math.Sqrt(fromAxis.X * fromAxis.X + fromAxis.Z * fromAxis.Z), Is.EqualTo(2f).Within(1e-5));
            Assert.That(System.Math.Atan2(fromAxis.X, fromAxis.Z) * 2.0, Is.EqualTo(0.5f).Within(1e-5), "x é o comprimento do arco");

            var flat = CurvedGeometry.SurfacePoint(pose, 0f, 0.5f, 0.1f);
            Assert.That((flat - (pose.Position + pose.Rotation * new Vec3(0.5f, 0.1f, 0f))).Length, Is.LessThan(1e-6f));
        }

        [Test]
        public void Label_NamesThePreset_RoundsTheRadiusTo100mm_AndWarnsWhenLimited()
        {
            Assert.That(CurvedGeometry.Of(0.6f, 0.78f, 1f).Label(), Is.EqualTo("Média · 60 % · ≈ 1300R"));
            Assert.That(CurvedGeometry.Of(0.6f, 1.3f, 1f).Label(), Is.EqualTo("Média · 60 % · ≈ 2200R"));
            Assert.That(CurvedGeometry.Of(0.45f, 1.3f, 1f).Label(), Is.EqualTo("45 % · ≈ 2900R"));
            Assert.That(CurvedGeometry.Of(1f, 1f, 3f).Label(), Is.EqualTo("70 % · ≈ 1400R · arco limitado"));
        }

        [TestCase(0.3f)]
        [TestCase(1f)]
        public void Invariant_CurvingKeepsResolutionAndWidth_ArcTimesRadiusIsTheWidth(float curvature)
        {
            var resolution = Resolution.Create(2560, 1080);
            new CreateScreen(resolution, Placement.OnSurface(0f, 0f, 1.3f), 1.4f).Apply(Workspace.Empty, out var ws);
            var id = ws.Screens[0].Id;
            new SetScreenCurvature(id, curvature).Apply(ws, out ws);
            var screen = ws.Screens[0];
            var g = CurvedGeometry.Of(screen, ws.Surface);
            Assert.That(screen.Resolution, Is.EqualTo(resolution));
            Assert.That(screen.WidthMeters, Is.EqualTo(1.4f));
            Assert.That(g.WidthM, Is.EqualTo(1.4f));
            Assert.That(g.ArcRad * g.RadiusM, Is.EqualTo(1.4f).Within(1e-5f));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests --filter CurvedGeometryTests`
Expected: FAIL (compilação: `CurvedGeometry` não existe).

- [ ] **Step 3: Write minimal implementation**

```csharp
using System;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Layout
{
    /// <summary>
    /// Geometria da tela curva (brief §10.2, spec 2E §3): cilindro de eixo vertical (Y local da tela), curva só na
    /// horizontal. R = d ÷ c', com c' limitada para o arco não passar de 120°. A tela passa pela pose (centro do arco) e o
    /// eixo fica R atrás dela, do lado do usuário. Struct: sem alocação (só <see cref="Label"/> aloca).
    /// </summary>
    public readonly struct CurvedGeometry
    {
        public const float MaxArcDeg = 120f;
        private const float MinDistanceM = 0.01f;
        private const float MinWidthM = 0.001f;

        /// <summary>Pedida (0..1), como está na tela.</summary>
        public readonly float Curvature;
        /// <summary>c' = min(c, 120° × d ÷ largura).</summary>
        public readonly float EffectiveCurvature;
        public readonly float DistanceM;
        public readonly float WidthM;
        /// <summary>0 = plana.</summary>
        public readonly float RadiusM;
        public readonly bool ArcLimited;

        private CurvedGeometry(float curvature, float effective, float distanceM, float widthM, float radiusM, bool arcLimited)
        {
            Curvature = curvature;
            EffectiveCurvature = effective;
            DistanceM = distanceM;
            WidthM = widthM;
            RadiusM = radiusM;
            ArcLimited = arcLimited;
        }

        public bool IsCurved => RadiusM > 0f;
        public float ArcRad => IsCurved ? WidthM / RadiusM : 0f;
        public float ArcDeg => (float)(ArcRad * Angles.RadToDeg);
        public int Percent => (int)Math.Round(EffectiveCurvature * 100.0, MidpointRounding.AwayFromZero);
        /// <summary>R em mm arredondado a 100 (rótulo "≈ NNNNR"); 0 = plana.</summary>
        public int RoundedRadiusMm => IsCurved ? (int)Math.Round(RadiusM * 10.0, MidpointRounding.AwayFromZero) * 100 : 0;

        public static CurvedGeometry Of(float curvature, float distanceM, float widthM)
        {
            float c = ScreenLimits.Clamp01(curvature);
            float d = float.IsNaN(distanceM) ? MinDistanceM : Math.Max(MinDistanceM, distanceM);
            float w = float.IsNaN(widthM) ? MinWidthM : Math.Max(MinWidthM, widthM);
            float max = (float)(MaxArcDeg * Angles.DegToRad) * d / w;
            bool limited = c > max;
            float effective = limited ? max : c;
            return new CurvedGeometry(c, effective, d, w, effective > 0f ? d / effective : 0f, limited);
        }

        /// <summary>d = <see cref="LayoutSurface.CurvatureDistance"/> (raio horizontal no Cilindro, distância na Esfera; solta: do centro).</summary>
        public static CurvedGeometry Of(float curvature, Placement placement, float widthM, LayoutSurface surface) =>
            Of(curvature, surface.CurvatureDistance(placement), widthM);

        public static CurvedGeometry Of(VirtualScreen screen, LayoutSurface surface) =>
            Of(screen.Curvature, screen.Placement, screen.WidthMeters, surface);

        /// <summary>Centro do cilindro = posição da tela − frente da tela × R, mesma rotação (transform da layer cilíndrica).</summary>
        public static Pose CylinderCenter(Pose screenPose, float radiusM) =>
            new Pose(screenPose.Position - (screenPose.Rotation * Vec3.Forward) * radiusM, screenPose.Rotation);

        /// <summary>Ponto da tela nas coordenadas locais (x ao longo do arco, y na altura, metros a partir do centro).</summary>
        public static Vec3 SurfacePoint(Pose screenPose, float radiusM, float localX, float localY)
        {
            if (radiusM <= 0f) return screenPose.Position + screenPose.Rotation * new Vec3(localX, localY, 0f);
            double theta = localX / radiusM;
            var local = new Vec3((float)(radiusM * Math.Sin(theta)), localY, (float)(radiusM * (Math.Cos(theta) - 1.0)));
            return screenPose.Position + screenPose.Rotation * local;
        }

        /// <summary>"Média · 60 % · ≈ 1300R" (+ " · arco limitado"). Aloca: guardar em cache pelo valor.</summary>
        public string Label()
        {
            if (!IsCurved) return CurvaturePresets.Name(CurvaturePresets.Flat);
            int preset = CurvaturePresets.IndexOf(EffectiveCurvature);
            string text = (preset > 0 ? CurvaturePresets.Name(preset) + " · " : string.Empty) + Percent + " % · ≈ " + RoundedRadiusMm + "R";
            return ArcLimited ? text + " · arco limitado" : text;
        }

        public override string ToString() => IsCurved ? $"curva c'={EffectiveCurvature:0.###} R={RadiusM:0.###} m arco={ArcDeg:0.#}°" : "plana";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Domain/Layout/CurvedGeometry.cs Assets/_Project/Tests/EditMode/Layout/CurvedGeometryTests.cs
git commit -m "feat(domain): CurvedGeometry (radius, 120 deg arc limit, cylinder center, label)"
```

---

### Task 3: Hit-test curvo e região da alça de curvatura

**Files:**
- Modify: `Assets/_Project/Application/Manipulation/ScreenHitTest.cs`
- Test: `Assets/_Project/Tests/EditMode/Manipulation/ScreenHitTestTests.cs`

**Interfaces:**
- Consumes: `CurvedGeometry.Of(VirtualScreen, LayoutSurface)`, `CurvedGeometry.SurfacePoint` (testes).
- Produces:
  - `HitRegion.CurvatureHandle` (novo membro, **no fim** do enum);
  - `ScreenHitTest.CurvatureHandleWidthM` (0,1 m);
  - `ScreenHitTest.RaycastCylinder(Vec3 origin, Vec3 direction, Pose pose, float radius, float maxHalfX, out float t, out float localX, out float localY) → bool`;
  - `ScreenHitTest.RaycastScreen(Vec3 origin, Vec3 direction, Pose pose, float radiusM, out float t, out float localX, out float localY) → bool` (R = 0 → plano; sem limite de x);
  - `Pick` usa o cilindro nas telas curvas; `ScreenHit.LocalX` passa a ser o comprimento de arco (igual ao x plano quando R = 0).

- [ ] **Step 1: Write the failing tests**

Em `ScreenHitTestTests.cs`, trocar o caso `(0f, 0.29f, Frame)` (o meio do topo agora é a alça de curvatura) e acrescentar:

```csharp
        [TestCase(0f, 0f, HitRegion.Content)]
        [TestCase(0.45f, 0.2f, HitRegion.Content)]
        [TestCase(0.515f, 0f, HitRegion.Frame)]
        [TestCase(0.2f, 0.29f, HitRegion.Frame)]
        [TestCase(0f, -0.29f, HitRegion.Frame)]
        [TestCase(0f, 0.29f, HitRegion.CurvatureHandle)]
        [TestCase(0.04f, 0.295f, HitRegion.CurvatureHandle)]
        [TestCase(0.5f, 0.28f, HitRegion.Handle)]
        [TestCase(-0.49f, -0.27f, HitRegion.Handle)]
        [TestCase(0.6f, 0f, HitRegion.None)]
        public void Pick_ClassifiesContentFrameAndHandles(float x, float y, HitRegion expected)
```

```csharp
        private static Workspace OneCurvedScreen(float curvature)
        {
            var ws = OneScreen();
            new SetScreenCurvature(ws.Screens[0].Id, curvature).Apply(ws, out ws);
            return ws;
        }

        private static Vec3 ToCurved(Workspace ws, float x, float y)
        {
            var screen = ws.Screens[0];
            var pose = ws.Surface.ToPose(screen.Placement);
            var r = CurvedGeometry.Of(screen, ws.Surface).RadiusM;
            return (CurvedGeometry.SurfacePoint(pose, r, x, y) - Eye).Normalized;
        }

        [TestCase(0f, 0f, HitRegion.Content)]
        [TestCase(0.45f, 0.2f, HitRegion.Content)]
        [TestCase(0.515f, 0f, HitRegion.Frame)]
        [TestCase(-0.515f, 0.1f, HitRegion.Frame)]
        [TestCase(0f, 0.29f, HitRegion.CurvatureHandle)]
        [TestCase(0.5f, 0.28f, HitRegion.Handle)]
        [TestCase(-0.49f, -0.27f, HitRegion.Handle)]
        [TestCase(0.6f, 0f, HitRegion.None)]
        public void CurvedScreen_SameLocalCoordinates_SameRegions(float x, float y, HitRegion expected)
        {
            var ws = OneCurvedScreen(0.6f);
            var hit = ScreenHitTest.Pick(ws, Eye, ToCurved(ws, x, y));
            Assert.That(hit.Region, Is.EqualTo(expected));
            if (expected == HitRegion.None) return;
            Assert.That(hit.LocalX, Is.EqualTo(x).Within(1e-3f), "x ao longo do arco");
            Assert.That(hit.LocalY, Is.EqualTo(y).Within(1e-3f));
        }

        [TestCase(22f, HitRegion.Content)]  // arco 0,499 m
        [TestCase(23f, HitRegion.Frame)]    // 0,522 m
        [TestCase(24f, HitRegion.None)]     // 0,545 m
        public void Envolvente_FromTheAxis_TheRayAngleIsTheArc(float yawDeg, HitRegion expected)
        {
            // c = 1 a 1,3 m: o eixo do cilindro fica no olho; o ângulo do raio × R = x no arco
            var ws = OneCurvedScreen(1f);
            var hit = ScreenHitTest.Pick(ws, Eye, Quat.AngleAxis(yawDeg, Vec3.Up) * Vec3.Forward);
            Assert.That(hit.Region, Is.EqualTo(expected));
            if (expected != HitRegion.None)
                Assert.That(hit.LocalX, Is.EqualTo(yawDeg * System.Math.PI / 180.0 * 1.3).Within(1e-3));
        }

        [Test]
        public void CurvedScreen_EdgesComeCloser_HitIsOnTheArcNotOnThePlane()
        {
            var ws = OneCurvedScreen(1f);
            var hit = ScreenHitTest.Pick(ws, Eye, ToCurved(ws, 0.45f, 0f));
            Assert.That(hit.Distance, Is.EqualTo(1.3f).Within(1e-3f), "do eixo, todo ponto do arco está a R");
            var flat = ScreenHitTest.Pick(OneScreen(), Eye, (new Vec3(0.45f, 1.2f, 1.3f) - Eye).Normalized);
            Assert.That(flat.Distance, Is.GreaterThan(1.37f));
        }

        [Test]
        public void CurvedScreen_RayPassingBeside_HitsNothing()
        {
            var ws = OneCurvedScreen(0.6f);
            Assert.That(ScreenHitTest.Pick(ws, Eye, Quat.AngleAxis(50f, Vec3.Up) * Vec3.Forward).Region, Is.EqualTo(HitRegion.None));
            Assert.That(ScreenHitTest.Pick(ws, Eye, new Vec3(0f, 0f, -1f)).Region, Is.EqualTo(HitRegion.None));
            Assert.That(ScreenHitTest.Pick(ws, Eye, Vec3.Up).Region, Is.EqualTo(HitRegion.None));
        }

        [Test]
        public void CurvedScreen_FromBehindTheArc_HitsTheNearSide()
        {
            // origem fora do cilindro, atrás da tela, olhando para o usuário: acerta a face de trás (a mais próxima)
            var ws = OneCurvedScreen(0.6f);
            var origin = new Vec3(0f, 1.2f, 2.5f);
            var hit = ScreenHitTest.Pick(ws, origin, new Vec3(0f, 0f, -1f));
            Assert.That(hit.Region, Is.EqualTo(HitRegion.Content));
            Assert.That(hit.Distance, Is.EqualTo(1.2f).Within(1e-3f));
        }

        [Test]
        public void RaycastScreen_Flat_EqualsThePlane()
        {
            var pose = new Pose(new Vec3(0f, 1.2f, 1.3f), Quat.Identity);
            var dir = (new Vec3(0.3f, 1.3f, 1.3f) - Eye).Normalized;
            Assert.That(ScreenHitTest.RaycastScreen(Eye, dir, pose, 0f, out float t, out float lx, out float ly), Is.True);
            Assert.That(ScreenHitTest.RaycastPlane(Eye, dir, pose, out float tp, out float px, out float py), Is.True);
            Assert.That(t, Is.EqualTo(tp));
            Assert.That(lx, Is.EqualTo(px));
            Assert.That(ly, Is.EqualTo(py));
        }
```

(`using ScreenPortal.Domain.Layout;` e `ScreenPortal.Domain.Commands` já estão no arquivo.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests --filter ScreenHitTestTests`
Expected: FAIL (compilação: `HitRegion.CurvatureHandle`, `RaycastScreen` não existem).

- [ ] **Step 3: Implement**

No enum, depois de `Handle`:

```csharp
        /// <summary>Meio do topo da moldura: curvar (spec 2E §3).</summary>
        CurvatureHandle,
```

Constante e `Classify` (a alça de curvatura vem depois dos cantos e antes da moldura):

```csharp
        public const float CurvatureHandleWidthM = 0.1f;

        public static HitRegion Classify(float localX, float localY, float width, float height)
        {
            float ax = Math.Abs(localX);
            float ay = Math.Abs(localY);
            float hw = width * 0.5f;
            float hh = height * 0.5f;
            if (ax > hw + FrameWidthM || ay > hh + FrameWidthM) return HitRegion.None;
            if (ax >= hw - HandleSizeM * 0.5f && ay >= hh - HandleSizeM * 0.5f) return HitRegion.Handle;
            if (localY > hh && ax <= CurvatureHandleWidthM * 0.5f) return HitRegion.CurvatureHandle;
            if (ax > hw || ay > hh) return HitRegion.Frame;
            return HitRegion.Content;
        }
```

Raio × cilindro (depois de `Raycast`):

```csharp
        /// <summary>
        /// Interseção com o cilindro da tela curva (eixo = Y local, R atrás da pose). Tenta a interseção mais próxima e depois
        /// a mais distante: de dentro (o normal) vale a face interna; de fora, a face mais próxima. lx = comprimento do arco
        /// a partir do centro, ly = altura: as mesmas coordenadas da tela plana. Só aceita |lx| ≤ <paramref name="maxHalfX"/>.
        /// </summary>
        public static bool RaycastCylinder(Vec3 origin, Vec3 direction, Pose pose, float radius, float maxHalfX,
            out float t, out float localX, out float localY)
        {
            t = 0f;
            localX = 0f;
            localY = 0f;
            var inverse = pose.Rotation.Inverse;
            var o = inverse * (origin - pose.Position);
            var d = inverse * direction;
            float ox = o.X;
            float oz = o.Z + radius; // relativo ao eixo
            float a = d.X * d.X + d.Z * d.Z;
            if (a < 1e-8f) return false;
            float b = ox * d.X + oz * d.Z;
            float c = ox * ox + oz * oz - radius * radius;
            float disc = b * b - a * c;
            if (disc < 0f) return false;
            float sq = (float)Math.Sqrt(disc);
            for (int k = 0; k < 2; k++)
            {
                float root = (-b + (k == 0 ? -sq : sq)) / a;
                if (root <= 0f) continue;
                float px = ox + d.X * root;
                float pz = oz + d.Z * root;
                if (pz <= 0f) continue; // metade de trás do cilindro: nunca é tela (arco ≤ 120°)
                float x = (float)(Math.Atan2(px, pz) * radius);
                if (Math.Abs(x) > maxHalfX) continue;
                t = root;
                localX = x;
                localY = o.Y + d.Y * root;
                return true;
            }
            return false;
        }

        /// <summary>Plano (R = 0) ou cilindro, sem limite de x: arrastes que saem da tela (redimensionar).</summary>
        public static bool RaycastScreen(Vec3 origin, Vec3 direction, Pose pose, float radiusM, out float t, out float localX, out float localY) =>
            radiusM > 0f
                ? RaycastCylinder(origin, direction, pose, radiusM, float.MaxValue, out t, out localX, out localY)
                : RaycastPlane(origin, direction, pose, out t, out localX, out localY);
```

`Pick` (acrescentar `using ScreenPortal.Domain.Layout;`):

```csharp
            for (int i = 0; i < screens.Count; i++)
            {
                var screen = screens[i];
                var pose = surface.ToPose(screen.Placement);
                var curve = CurvedGeometry.Of(screen, surface);
                float t, lx, ly;
                bool hit = curve.IsCurved
                    ? RaycastCylinder(origin, direction, pose, curve.RadiusM, screen.WidthMeters * 0.5f + FrameWidthM, out t, out lx, out ly)
                    : RaycastPlane(origin, direction, pose, out t, out lx, out ly);
                if (!hit) continue;
                var region = Classify(lx, ly, screen.WidthMeters, screen.HeightMeters);
                if (region == HitRegion.None || t >= bestT) continue;
                bestT = t;
                best = new ScreenHit(screen.Id, region, t, origin + direction * t, lx, ly);
            }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests`
Expected: PASS (inclui `ScreenManipulationControllerTests`, que usam a moldura da direita e não mudam).

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Application/Manipulation/ScreenHitTest.cs Assets/_Project/Tests/EditMode/Manipulation/ScreenHitTestTests.cs
git commit -m "feat(app): curved hit-test on the screen cylinder, curvature handle region"
```

---

### Task 4: Página "Curvatura" no menu do pulso

**Files:**
- Create: `Assets/_Project/Application/Menu/CurvatureMenu.cs`
- Modify: `Assets/_Project/Application/Menu/WristMenu.cs` (só acréscimos: item, página, casos nos `switch`)
- Test: `Assets/_Project/Tests/EditMode/Manipulation/WristMenuTests.cs`

**Interfaces:**
- Consumes: `CurvaturePresets`, `CurvedGeometry.Of(VirtualScreen, LayoutSurface).Label()`, `SetScreenCurvature`.
- Produces: `MenuItemId.Curvature` e `MenuPage.Curvature` (no fim dos enums); `CurvatureMenu.Label(int, float)`,
  `CurvatureMenu.MarkedPrefix` ("● "), `CurvatureMenu.NoFocusStatus`, `CurvatureMenu.Status(Workspace)` (cache por workspace).

- [ ] **Step 1: Write the failing tests**

```csharp
        [Test]
        public void CurvatureItem_NeedsAFocusedScreen()
        {
            var r = new Rig();
            int index = r.Menu.IndexOf(MenuItemId.Curvature);
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            Assert.That(r.Menu.Label(index), Is.EqualTo("Curvatura ▸"));
            Assert.That(r.Menu.IsEnabled(index), Is.False, "sem tela em foco");
            r.Menu.Invoke(MenuItemId.Curvature);
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Main));
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            Assert.That(r.Menu.IsEnabled(index), Is.True);
        }

        [Test]
        public void CurvaturePage_ListsPresets_MarksTheCurrent_AndSendsSetScreenCurvature()
        {
            var r = new Rig();
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            r.Menu.Invoke(MenuItemId.Curvature);
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Curvature));
            Assert.That(r.Menu.ItemCount, Is.EqualTo(5));
            Assert.That(r.Menu.Label(0), Is.EqualTo("● Plana"));
            Assert.That(r.Menu.Label(1), Is.EqualTo("Suave"));
            Assert.That(r.Menu.Label(2), Is.EqualTo("Média"));
            Assert.That(r.Menu.Label(3), Is.EqualTo("Envolvente"));
            Assert.That(r.Menu.Label(4), Is.EqualTo(WristMenu.BackLabel));
            Assert.That(r.Menu.StatusLine, Is.EqualTo("Plana"));

            r.Menu.Invoke(2); // Média
            var screen = r.Store.State.Screens[0];
            Assert.That(screen.Curvature, Is.EqualTo(0.6f));
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Curvature), "fica na página para comparar");
            Assert.That(r.Menu.Label(0), Is.EqualTo("Plana"));
            Assert.That(r.Menu.Label(2), Is.EqualTo("● Média"));
            Assert.That(r.Menu.StatusLine, Is.EqualTo(CurvedGeometry.Of(screen, r.Store.State.Surface).Label()));
            Assert.That(r.Menu.StatusLine, Does.StartWith("Média · 60 % · ≈ "));

            r.Menu.Invoke(MenuItemId.Undo);
            Assert.That(r.Store.State.Screens[0].Curvature, Is.EqualTo(0f));
            Assert.That(r.Store.State.Screens[0].Resolution, Is.EqualTo(screen.Resolution));

            r.Menu.Invoke(4); // voltar
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Main));
        }

        [Test]
        public void CurvaturePage_WithoutFocus_DisablesPresets()
        {
            var r = new Rig();
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            r.Menu.Invoke(MenuItemId.Curvature);
            r.Menu.Invoke(MenuItemId.RemoveScreen); // foco some (ex.: undo da criação)
            Assert.That(r.Menu.IsEnabled(2), Is.False);
            Assert.That(r.Menu.IsEnabled(4), Is.True, "voltar");
            Assert.That(r.Menu.StatusLine, Is.EqualTo(CurvatureMenu.NoFocusStatus));
            r.Menu.Invoke(2);
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(0));
        }
```

(acrescentar `using ScreenPortal.Domain.Layout;` — já existe — e nada mais.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests --filter WristMenuTests`
Expected: FAIL (compilação: `MenuItemId.Curvature`, `MenuPage.Curvature`, `CurvatureMenu` não existem).

- [ ] **Step 3: Implement**

`CurvatureMenu.cs`:

```csharp
using ScreenPortal.Domain;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Menu
{
    /// <summary>
    /// Página "Curvatura" do menu de pulso (spec 2E §3): os presets, com o atual marcado, e o rótulo da tela em foco na
    /// linha de status. Strings em cache (sem alocar por quadro).
    /// </summary>
    public sealed class CurvatureMenu
    {
        public const string MarkedPrefix = "● ";
        public const string NoFocusStatus = "Curvatura: sem tela em foco";

        private static readonly string[] Marked = BuildMarked();

        private Workspace _statusFor;
        private string _status = string.Empty;

        public static int ItemCount => CurvaturePresets.Count;

        /// <param name="current">Curvatura pedida da tela em foco (a marca segue o valor guardado, não o efetivo).</param>
        public static string Label(int index, float current) =>
            CurvaturePresets.IndexOf(current) == index ? Marked[index] : CurvaturePresets.Name(index);

        public string Status(Workspace ws)
        {
            if (ReferenceEquals(ws, _statusFor)) return _status;
            _statusFor = ws;
            _status = ws.TryGetScreen(ws.FocusedScreenId, out var screen)
                ? CurvedGeometry.Of(screen, ws.Surface).Label()
                : NoFocusStatus;
            return _status;
        }

        private static string[] BuildMarked()
        {
            var marked = new string[CurvaturePresets.Count];
            for (int i = 0; i < marked.Length; i++) marked[i] = MarkedPrefix + CurvaturePresets.Name(i);
            return marked;
        }
    }
}
```

`WristMenu.cs` — acréscimos:

```csharp
    public enum MenuItemId
    {
        ...
        AddTestScreen,
        Curvature,
    }

    public enum MenuPage
    {
        ...
        Mirrors,
        Curvature,
    }
```

Nas duas listas da página principal, `MenuItemId.Curvature` logo depois de `MenuItemId.RemoveScreen`. Campo
`private readonly CurvatureMenu _curvature = new CurvatureMenu();`. Casos novos:

```csharp
        // ItemCount
                    case MenuPage.Curvature: return CurvatureMenu.ItemCount + 1;

        // StatusLine, primeira linha do get
                if (Page == MenuPage.Curvature) return _curvature.Status(_store.State);

        // Label(int), switch (Page)
                case MenuPage.Curvature:
                    return CurvatureMenu.Label(index, FocusedCurvature());

        // Label(MenuItemId)
                case MenuItemId.Curvature: return "Curvatura ▸";

        // IsEnabled(int), switch (Page)
                case MenuPage.Curvature: return HasFocusedScreen();

        // IsEnabled(MenuItemId)
                case MenuItemId.Curvature: return ws.Contains(ws.FocusedScreenId);

        // Invoke(int), switch (Page)
                case MenuPage.Curvature:
                    _bus.Send(new SetScreenCurvature(_store.State.FocusedScreenId, CurvaturePresets.Value(index)));
                    return; // fica na página (spec 2E: comparar presets)

        // Invoke(MenuItemId)
                case MenuItemId.Curvature:
                    Page = MenuPage.Curvature;
                    break;
```

Auxiliares (no fim da classe):

```csharp
        private bool HasFocusedScreen()
        {
            var ws = _store.State;
            return ws.Contains(ws.FocusedScreenId);
        }

        private float FocusedCurvature()
        {
            var ws = _store.State;
            return ws.TryGetScreen(ws.FocusedScreenId, out var screen) ? screen.Curvature : -1f;
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests`
Expected: PASS (inclusive `MenuLayout_ButtonAtFindsEachButtonCenter`, agora com 12 itens na principal com teste).

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Application/Menu/CurvatureMenu.cs Assets/_Project/Application/Menu/WristMenu.cs Assets/_Project/Tests/EditMode/Manipulation/WristMenuTests.cs
git commit -m "feat(app): wrist menu Curvature page (presets, current marked, label in status)"
```

---

### Task 5: Alça de curvatura no controlador (prévia → um `SetScreenCurvature`) e redimensionar tela curva

**Files:**
- Modify: `Assets/_Project/Application/Manipulation/ManipulationPreview.cs`
- Modify: `Assets/_Project/Application/Manipulation/ScreenManipulationController.cs`
- Test: `Assets/_Project/Tests/EditMode/Manipulation/ScreenManipulationControllerTests.cs`

**Interfaces:**
- Consumes: `HitRegion.CurvatureHandle`, `ScreenHitTest.RaycastScreen` (Task 3), `CurvedGeometry` (Task 2).
- Produces: `ManipulationState.Curving` (no fim do enum); `ScreenManipulationController.CurvatureDragM` (0,3);
  `ManipulationPreview.Curvature` (curvatura pedida em prévia; = a da tela fora do arraste de curvatura).
  `InteractionRouter` não muda (o controlador recebe os mesmos `ManipulationInput`).

- [ ] **Step 1: Write the failing tests**

```csharp
        private static float TopY => 0.5f / (16f / 9f) + 0.015f; // faixa de cima da moldura

        private static ManipulationInput RayFrom(Vec3 origin, Vec3 dir, bool select) =>
            new ManipulationInput { HasRay = true, Origin = origin, Direction = dir, SelectHeld = select };

        [Test]
        public void CurvatureHandle_PushingCurves_PreviewOnly_OneCommandOnRelease_UndoRestores()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            var grab = r.Toward(0, 0f, TopY);
            r.Right(grab, false);
            Assert.That(r.Controller.HoverHit.Region, Is.EqualTo(HitRegion.CurvatureHandle));
            r.Right(grab, true);
            Assert.That(r.Controller.State, Is.EqualTo(ManipulationState.Curving));
            Assert.That(r.Controller.IsDragging, Is.True);

            var pushed = Eye + new Vec3(0f, 0f, 0.15f); // 15 cm para a frente (normal da tela = +Z)
            r.Controller.Update(RayFrom(pushed, grab, true), default);
            Assert.That(r.Controller.Preview.Curvature, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(r.Recorder.Undoable, Is.Empty, "nada muda durante o arraste");
            Assert.That(r.Store.State.Screens[0].Curvature, Is.EqualTo(0f));

            r.Controller.Update(RayFrom(pushed, grab, false), default);
            Assert.That(r.Recorder.Undoable.Count, Is.EqualTo(1));
            Assert.That(r.Recorder.Undoable[0], Is.TypeOf<SetScreenCurvature>());
            var screen = r.Store.State.Screens[0];
            Assert.That(screen.Curvature, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(screen.WidthMeters, Is.EqualTo(1f), "curvar não muda a largura");
            Assert.That(screen.Resolution, Is.EqualTo(Resolution.Create(1920, 1080)));
            Assert.That(r.Controller.Preview.Active, Is.False);

            r.Bus.Undo();
            Assert.That(r.Store.State.Screens[0].Curvature, Is.EqualTo(0f));
        }

        [Test]
        public void CurvatureHandle_PullingFlattens_ClampedTo01()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            r.Store.Dispatch(new SetScreenCurvature(r.Store.State.Screens[0].Id, 0.3f));
            var grab = r.Toward(0, 0f, TopY);
            r.Right(grab, false);
            r.Right(grab, true);
            Assert.That(r.Controller.State, Is.EqualTo(ManipulationState.Curving));
            r.Controller.Update(RayFrom(Eye + new Vec3(0f, 0f, -0.5f), grab, true), default);
            Assert.That(r.Controller.Preview.Curvature, Is.EqualTo(0f));
            r.Controller.Update(RayFrom(Eye + new Vec3(0f, 0f, 0.5f), grab, true), default);
            Assert.That(r.Controller.Preview.Curvature, Is.EqualTo(1f));
            r.Controller.Update(RayFrom(Eye + new Vec3(0f, 0f, 0.5f), grab, false), default);
            Assert.That(r.Store.State.Screens[0].Curvature, Is.EqualTo(1f));
        }

        [Test]
        public void CurvatureHandle_Unchanged_OrCancelled_SendsNothing()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            var grab = r.Toward(0, 0f, TopY);
            r.Right(grab, false);
            r.Right(grab, true);
            r.Right(grab, false);
            Assert.That(r.Recorder.Undoable, Is.Empty, "soltar sem mexer");

            r.Right(grab, true);
            r.Controller.Update(RayFrom(Eye + new Vec3(0f, 0f, 0.1f), grab, true), default);
            r.Right(grab, true, cancel: true);
            Assert.That(r.Controller.IsDragging, Is.False);
            r.Right(grab, false);
            Assert.That(r.Recorder.Undoable, Is.Empty);
            Assert.That(r.Store.State.Screens[0].Curvature, Is.EqualTo(0f));
        }

        [Test]
        public void Preview_CarriesTheScreenCurvature_WhileMoving()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            r.Store.Dispatch(new SetScreenCurvature(r.Store.State.Screens[0].Id, 0.6f));
            var grab = r.Toward(0, FrameX, 0f);
            r.Right(grab, false);
            r.Right(grab, true);
            Assert.That(r.Controller.State, Is.EqualTo(ManipulationState.Moving));
            Assert.That(r.Controller.Preview.Curvature, Is.EqualTo(0.6f));
        }

        [Test]
        public void Handle_OnACurvedScreen_ResizesByTheArc()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            r.Store.Dispatch(new SetScreenCurvature(r.Store.State.Screens[0].Id, 0.6f));
            var ws = r.Store.State;
            var pose = r.PoseOf(0);
            float radius = CurvedGeometry.Of(ws.Screens[0], ws.Surface).RadiusM;
            float hh = 0.5f / (16f / 9f);
            var corner = (CurvedGeometry.SurfacePoint(pose, radius, 0.5f, hh) - Eye).Normalized;
            r.Right(corner, false);
            Assert.That(r.Controller.HoverHit.Region, Is.EqualTo(HitRegion.Handle));
            r.Right(corner, true);
            var farther = (CurvedGeometry.SurfacePoint(pose, radius, 0.6f, hh * 1.2f) - Eye).Normalized;
            r.Right(farther, true);
            r.Right(farther, false);
            Assert.That(r.Store.State.Screens[0].WidthMeters, Is.EqualTo(1.2f).Within(0.002f));
            Assert.That(r.Store.State.Screens[0].Curvature, Is.EqualTo(0.6f), "redimensionar não muda a curvatura");
        }
```

(acrescentar `using ScreenPortal.Domain;` não é preciso; `CurvedGeometry` está em `ScreenPortal.Domain.Layout`, já importado.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests --filter ScreenManipulationControllerTests`
Expected: FAIL (compilação: `ManipulationState.Curving`, `Preview.Curvature` não existem).

- [ ] **Step 3: Implement**

`ManipulationPreview`:

```csharp
        /// <summary>Curvatura pedida (0..1): a da tela, ou o valor em prévia durante o arraste da alça de curvatura.</summary>
        public float Curvature { get; private set; }

        internal void Begin(ScreenId screen, Pose pose, Placement placement, float widthM, float heightM, float curvature)
        {
            ...
            Curvature = curvature;
            ClearGuides();
        }

        internal void SetCurvature(float curvature) => Curvature = curvature;
```

`ScreenManipulationController`:

```csharp
    public enum ManipulationState
    {
        Idle,
        Hover,
        Moving,
        Resizing,
        /// <summary>Arrastando a alça de curvatura (spec 2E §3).</summary>
        Curving,
    }
```

- Summary da classe: "Idle → Hover(moldura/alça) → Moving | Resizing | Curving → Commit | Cancel".
- `public const float CurvatureDragM = 0.3f;` com o comentário "30 cm ao longo da normal da tela = 100 % (spec 2E §3)".
- Campos: `_startCurvature`, `_startCurveRadius` (float), `_curveOrigin` (Vec3), `_curveNormal` (Vec3).
- `IsDragging => State == Moving || State == Resizing || State == Curving`.
- `TryHover`: aceita `HitRegion.CurvatureHandle` além de `Frame` e `Handle`.
- `Begin`:

```csharp
            _startCurvature = screen.Curvature;
            _startCurveRadius = CurvedGeometry.Of(screen, surface).RadiusM;
            Preview.Begin(screen.Id, _startPose, screen.Placement, screen.WidthMeters, screen.HeightMeters, screen.Curvature);
            if (ws.FocusedScreenId != screen.Id) _bus.Send(new SetFocus(screen.Id));

            if (hit.Region == HitRegion.Handle)
            {
                State = ManipulationState.Resizing;
                // coordenadas locais do hit: comprimento do arco na tela curva (na plana é o mesmo que o ponto 3D)
                _resizeStartDistance = Math.Max(0.01f, (float)Math.Sqrt(hit.LocalX * hit.LocalX + hit.LocalY * hit.LocalY));
                return;
            }
            if (hit.Region == HitRegion.CurvatureHandle)
            {
                State = ManipulationState.Curving;
                _curveOrigin = input.Origin;
                _curveNormal = _startPose.Rotation * Vec3.Forward;
                return;
            }
```

- `UpdateDrag`, depois do bloco de `Resizing`:

```csharp
            if (State == ManipulationState.Curving)
            {
                UpdateCurving(in active);
                return;
            }
```

```csharp
        /// <summary>Empurrar a mão para longe (ao longo da normal) curva; puxar achata. Só prévia.</summary>
        private void UpdateCurving(in ManipulationInput input)
        {
            float pushed = Vec3.Dot(input.Origin - _curveOrigin, _curveNormal);
            Preview.SetCurvature(ScreenLimits.Clamp01(_startCurvature + pushed / CurvatureDragM));
        }
```

- `UpdateResize` usa `ScreenHitTest.RaycastScreen(input.Origin, input.Direction, _startPose, _startCurveRadius, out _, out float lx, out float ly)`.
- `Commit`:

```csharp
            bool curving = State == ManipulationState.Curving;
            float curvature = Preview.Curvature;
            ...
            Reset();

            if (curving)
            {
                if (Math.Abs(curvature - _startCurvature) > 1e-4f) _bus.Send(new SetScreenCurvature(id, curvature));
                return;
            }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests`
Expected: PASS (os testes antigos de redimensionar continuam: na tela plana a distância pelo hit local é a mesma).

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Application/Manipulation/ManipulationPreview.cs Assets/_Project/Application/Manipulation/ScreenManipulationController.cs Assets/_Project/Tests/EditMode/Manipulation/ScreenManipulationControllerTests.cs
git commit -m "feat(app): curvature handle drag (local preview, one SetScreenCurvature on release); resize curved screens by arc"
```

---

### Task 6: `HostSync` — plana ↔ curva reabre o decoder na layer nova

**Files:**
- Modify: `Assets/_Project/Application/Screens/HostSync.cs`
- Test: `Assets/_Project/Tests/EditMode/Screens/HostSyncTests.cs`

**Interfaces:**
- Consumes: `VirtualScreen.Curvature` (c' > 0 ⇔ c > 0).
- Produces: nada público. Contrato com a Parte B: a `VideoScreenView` recria a `OVROverlay` no mesmo `Apply` confirmado que
  troca a forma; o `HostSync` fecha o decoder no mesmo evento e o reabre no próximo `Tick` (`TryOpen` devolve false até a
  Surface nova existir).

- [ ] **Step 1: Write the failing test**

```csharp
        [Test]
        public void FlatToCurved_ReopensTheDecoderOnTheNewLayer_SameSlot_NoNewAcquire_CurvedToCurvedDoesNot()
        {
            var r = new Rig();
            var id = r.AddVirtual();
            r.Link();
            r.Push(new ScreenReadyMessage((uint)id.Value, 2560, 1440));
            r.Push(new StreamStartedMessage(0, 2560, 1440));
            Assert.That(r.Video.Opens, Is.EqualTo(1));
            int acquires = r.C.Channel.Count<AcquireScreenMessage>();
            int starts = r.C.Channel.Count<StartStreamMessage>();

            r.C.Bus.Send(new SetScreenCurvature(id, 0.6f)); // layer quad → cilindro: Surface nova
            r.Tick();
            Assert.That(r.Video.Closes, Is.EqualTo(1));
            Assert.That(r.Video.Opens, Is.EqualTo(2));
            Assert.That(r.Video.LastPort, Is.EqualTo(9000), "mesmo slot");
            Assert.That(r.C.Channel.Count<StartStreamMessage>(), Is.EqualTo(starts + 1), "o host responde com IDR");
            Assert.That(r.C.Channel.Count<AcquireScreenMessage>(), Is.EqualTo(acquires), "o monitor continua o mesmo");

            r.C.Bus.Send(new SetScreenCurvature(id, 1f)); // curva → curva: só o raio
            r.Tick();
            Assert.That(r.Video.Opens, Is.EqualTo(2));

            r.C.Bus.Undo();
            r.C.Bus.Undo(); // volta a plana
            r.Tick();
            Assert.That(r.Video.Opens, Is.EqualTo(3));
            Assert.That(r.C.Channel.Count<AcquireScreenMessage>(), Is.EqualTo(acquires));
        }

        [Test]
        public void ShapeChange_WhileOffline_ClosesTheDecoder_ReconnectOpensTheNewOne()
        {
            var r = new Rig();
            var id = r.AddVirtual();
            r.Link();
            r.Push(new ScreenReadyMessage((uint)id.Value, 2560, 1440));
            r.C.Channel.Push(ChannelEventKind.Disconnected);
            r.Tick();
            Assert.That(r.Video.Open.Contains(id), Is.True, "offline o decoder fica");
            r.C.Bus.Send(new SetScreenCurvature(id, 0.6f));
            Assert.That(r.Video.Open.Contains(id), Is.False, "a Surface antiga morreu com a layer");

            r.Tick(); // tentativa de reconexão
            r.C.Channel.Push(ChannelEventKind.Connected);
            r.C.Channel.Push(ChannelEventKind.Message, NoMonitors);
            r.Tick();
            r.Push(new ScreenReadyMessage((uint)id.Value, 2560, 1440));
            Assert.That(r.Video.Opens, Is.EqualTo(2), "reabre na Surface nova");
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests --filter HostSyncTests`
Expected: FAIL (`Closes` 0 / `Opens` 1: o `HostSync` ignora a curvatura).

- [ ] **Step 3: Implement**

`Entry` ganha `public bool Curved;`. Em `Reconcile`:

```csharp
                var s = screens[i];
                if (!s.Source.HasVideo) continue;
                bool curved = s.Curvature > 0f;
                var e = Find(s.Id);
                if (e == null)
                {
                    e = new Entry { Id = s.Id, Source = s.Source, Resolution = s.Resolution, Curved = curved, Phase = Phase.Unlinked };
                    ...
                }
                else if (!e.Resolution.Equals(s.Resolution) || !e.Source.Equals(s.Source))
                {
                    ... (como está)
                }
                else if (curved != e.Curved)
                {
                    // spec 2E: plana ↔ curva recria a layer (Surface nova). O monitor e o slot ficam; o decoder reabre na
                    // Surface nova e o StartStream repetido faz o host mandar um IDR.
                    CloseDecoder(e);
                    if (e.Phase == Phase.Starting || e.Phase == Phase.Streaming || e.Phase == Phase.Live)
                    {
                        e.NotBefore = 0;
                        SetPhase(e, Phase.Ready);
                    }
                }
                e.Curved = curved;
                e.Seen = true;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Application/Screens/HostSync.cs Assets/_Project/Tests/EditMode/Screens/HostSyncTests.cs
git commit -m "feat(app): HostSync reopens the decoder when a screen turns curved or flat (new layer surface)"
```

---

# Parte B — Unity (fila; precisa do Editor aberto)

> Executar no checkout com o Editor aberto, depois de mesclar a Parte A. Cada tarefa termina com recompile sem erros +
> `dotnet test` + EditMode da Unity verdes.

### Task 7: `IScreenView` com raio (Application + Presentation juntos)

**Files:**
- Modify: `Assets/_Project/Application/Screens/IScreenView.cs`, `Assets/_Project/Application/Screens/ScreenViewSynchronizer.cs`
- Modify: `Assets/_Project/Presentation/Screens/VideoScreenView.cs`, `Assets/_Project/Presentation/Screens/TestPatternScreenView.cs`,
  `Assets/_Project/Presentation/Manipulation/ManipulationPresenter.cs`
- Test: `Assets/_Project/Tests/EditMode/ScreenViewSynchronizerTests.cs`

**Interfaces:**
- Produces: `IScreenView.Apply(VirtualScreen screen, Pose pose, float radiusM)` e
  `IScreenView.ApplyTransform(Pose pose, float widthMeters, float heightMeters, float radiusM)`; `radiusM` =
  `CurvedGeometry.RadiusM` (0 = plana). Nesta tarefa as views só guardam o raio (continuam planas).

- [ ] **Step 1: Write the failing test** (a `FakeView` passa a guardar o raio)

```csharp
        private sealed class FakeView : IScreenView
        {
            public readonly List<VirtualScreen> Applied = new List<VirtualScreen>();
            public float LastRadius = -1f;
            public bool Disposed;
            public void Apply(VirtualScreen screen, Pose pose, float radiusM)
            {
                Applied.Add(screen);
                LastRadius = radiusM;
            }
            public void ApplyTransform(Pose pose, float widthMeters, float heightMeters, float radiusM) { }
            public void Dispose() => Disposed = true;
        }

        [Test]
        public void Apply_PassesTheCurvedRadius_AndReappliesWhenTheSurfaceChangesIt()
        {
            var store = new WorkspaceStore();
            var factory = new FakeFactory();
            using var sync = new ScreenViewSynchronizer(store, factory);
            sync.Start();
            store.Dispatch(new CreateScreen(Resolution.Create(1920, 1080), Placement.OnSurface(0f, 30f, 1.3f), 1f, 0.6f));
            var id = store.State.Screens[0].Id;
            var view = factory.Views[id];
            Assert.That(view.LastRadius, Is.EqualTo(CurvedGeometry.Of(store.State.Screens[0], store.State.Surface).RadiusM));
            Assert.That(view.LastRadius, Is.GreaterThan(0f));
            store.Dispatch(new SetLayoutMode(LayoutMode.Sphere)); // d muda de d·cos(el) para d
            Assert.That(view.LastRadius, Is.EqualTo(1.3f / 0.6f).Within(1e-4f));
            store.Dispatch(new SetScreenCurvature(id, 0f));
            Assert.That(view.LastRadius, Is.EqualTo(0f));
        }
```

(C# 9: trocar `using var` por `var sync = ...; ... sync.Dispose();` se o arquivo não usar declarações `using`.)

- [ ] **Step 2: Run to verify it fails** — `dotnet test tests-dotnet/UnityPureCode.Tests --filter ScreenViewSynchronizerTests` → FAIL (assinatura).

- [ ] **Step 3: Implement**

```csharp
    public interface IScreenView : IDisposable
    {
        /// <summary>Estado confirmado. <paramref name="pose"/> = superfície.ToPose(screen.Placement);
        /// <paramref name="radiusM"/> = <c>CurvedGeometry.Of(screen, surface).RadiusM</c> (0 = plana).</summary>
        void Apply(VirtualScreen screen, Pose pose, float radiusM);

        /// <summary>Só posição/tamanho/raio, para a pré-visualização do arraste (não é estado; nunca troca a forma da layer).</summary>
        void ApplyTransform(Pose pose, float widthMeters, float heightMeters, float radiusM);
    }
```

`ScreenViewSynchronizer.Sync`: `entry.View.Apply(screen, surface.ToPose(screen.Placement), CurvedGeometry.Of(screen, surface).RadiusM);`
(nos dois lugares; `using ScreenPortal.Domain.Layout;`).

Views: acrescentar o parâmetro e repassar (`TestPatternScreenView.Apply(screen, pose, radiusM) => ApplyTransform(pose, screen.WidthMeters, screen.HeightMeters, radiusM)`;
`VideoScreenView.Apply` idem). `ManipulationPresenter.Tick`:

```csharp
                var curve = CurvedGeometry.Of(preview.Curvature, preview.Placement, preview.WidthM, ws.Surface);
                if (_views.TryGetView(preview.Screen, out var view)) view.ApplyTransform(preview.Pose, preview.WidthM, preview.HeightM, curve.RadiusM);
```

`RestoreConfirmed`: `view.Apply(screen, ws.Surface.ToPose(screen.Placement), CurvedGeometry.Of(screen, ws.Surface).RadiusM);`

- [ ] **Step 4: Verify** — `dotnet test tests-dotnet/UnityPureCode.Tests` PASS; Unity: `tools/unity-cmd.ps1 -Command refresh` sem erros
  de compilação; `tools/unity-cmd.ps1 -Command test-editmode` verde.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Application/Screens/IScreenView.cs Assets/_Project/Application/Screens/ScreenViewSynchronizer.cs Assets/_Project/Tests/EditMode/ScreenViewSynchronizerTests.cs Assets/_Project/Presentation/Screens/VideoScreenView.cs Assets/_Project/Presentation/Screens/TestPatternScreenView.cs Assets/_Project/Presentation/Manipulation/ManipulationPresenter.cs
git commit -m "feat(app): screen views receive the curved radius"
```

---

### Task 8: `CurvedQuadMesh` + testes EditMode da Unity

**Files:**
- Create: `Assets/_Project/Presentation/Screens/CurvedQuadMesh.cs`
- Create: `Assets/_Project/Tests/Composition/CurvedQuadMeshTests.cs` (asmdef `ScreenPortal.Tests.Composition` já referencia a Presentation)

**Interfaces:**
- Produces: `CurvedQuadMesh(string name)`, `Mesh Mesh`, `int Segments`, `bool Set(float widthM, float heightM, float radiusM)`
  (true = regenerou), `static int SegmentsFor(float widthM, float radiusM)`, `DegreesPerSegment` (3), `MaxSegments` (64), `Dispose()`.
  Malha em metros no espaço local da tela (pose no centro do arco; face visível para −Z, como o Quad da Unity); UV 0..1.

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using ScreenPortal.Presentation.Screens;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace ScreenPortal.Tests
{
    public class CurvedQuadMeshTests
    {
        [Test]
        public void Flat_IsOneQuadOfTheRightSize()
        {
            using (var mesh = new CurvedQuadMesh("t"))
            {
                mesh.Set(1f, 0.5f, 0f);
                Assert.That(mesh.Segments, NUnit.Framework.Is.EqualTo(1));
                Assert.That(mesh.Mesh.vertexCount, NUnit.Framework.Is.EqualTo(4));
                Assert.That(mesh.Mesh.bounds.size.x, NUnit.Framework.Is.EqualTo(1f).Within(1e-5f));
                Assert.That(mesh.Mesh.bounds.size.y, NUnit.Framework.Is.EqualTo(0.5f).Within(1e-5f));
                Assert.That(mesh.Mesh.bounds.size.z, NUnit.Framework.Is.EqualTo(0f).Within(1e-6f));
            }
        }

        [TestCase(1f, 2.1667f, 9)]   // arco 26,44° → ceil(8,8)
        [TestCase(0.05f, 5f, 2)]     // arco 0,57°: mínimo 2
        [TestCase(2.72f, 1.3f, 40)]  // 119,9° → ceil(39,96)
        public void Segments_OneEvery3Degrees_AtLeast2(float width, float radius, int expected) =>
            Assert.That(CurvedQuadMesh.SegmentsFor(width, radius), NUnit.Framework.Is.EqualTo(expected));

        [Test]
        public void Curved_VerticesLieOnTheArc()
        {
            using (var mesh = new CurvedQuadMesh("t"))
            {
                const float r = 1.3f;
                mesh.Set(1f, 0.5f, r);
                var vertices = mesh.Mesh.vertices; // teste: pode alocar
                var axis = new Vector3(0f, 0f, -r);
                foreach (var v in vertices)
                {
                    var flat = new Vector3(v.x, 0f, v.z) - axis;
                    Assert.That(flat.magnitude, NUnit.Framework.Is.EqualTo(r).Within(1e-4f));
                }
                Assert.That(vertices[0].x, NUnit.Framework.Is.EqualTo(r * Mathf.Sin(-0.5f / r)).Within(1e-4f), "borda esquerda no arco");
            }
        }

        [Test]
        public void SameSize_DoesNotRegenerate_NorAllocate()
        {
            using (var mesh = new CurvedQuadMesh("t"))
            {
                Assert.That(mesh.Set(1f, 0.5f, 2f), NUnit.Framework.Is.True);
                Assert.That(mesh.Set(1f, 0.5f, 2f), NUnit.Framework.Is.False);
                Assert.That(() => mesh.Set(1f, 0.5f, 2f), Is.Not.AllocatingGCMemory());
            }
        }

        [Test]
        public void Regenerating_DoesNotAllocate()
        {
            using (var mesh = new CurvedQuadMesh("t"))
            {
                mesh.Set(1f, 0.5f, 2f);
                float r = 2f;
                Assert.That(() => { r += 0.01f; mesh.Set(1f, 0.5f, r); }, Is.Not.AllocatingGCMemory());
            }
        }
    }
}
```

- [ ] **Step 2: Run** — `tools/unity-cmd.ps1 -Command test-editmode` → FAIL (compilação).

- [ ] **Step 3: Implement**

```csharp
using System;
using UnityEngine;

namespace ScreenPortal.Presentation.Screens
{
    /// <summary>
    /// Malha de um arco de cilindro (spec 2E §4) para o furo, o quad de estado, a tela de teste e a moldura: eixo vertical
    /// R atrás do centro, face visível para −Z (como o Quad da Unity). ~1 segmento a cada 3° (mínimo 2); R = 0 → um quad.
    /// Só regenera quando largura, altura ou raio mudam, sem alocar (buffers do tamanho máximo, criados uma vez).
    /// </summary>
    public sealed class CurvedQuadMesh : IDisposable
    {
        public const float DegreesPerSegment = 3f;
        public const int MaxSegments = 64;

        private readonly Vector3[] _vertices = new Vector3[(MaxSegments + 1) * 2];
        private readonly Vector2[] _uv = new Vector2[(MaxSegments + 1) * 2];
        private readonly int[] _triangles = new int[MaxSegments * 6];
        private float _width = -1f;
        private float _height = -1f;
        private float _radius = -1f;

        public Mesh Mesh { get; }
        public int Segments { get; private set; }

        public CurvedQuadMesh(string name)
        {
            Mesh = new Mesh { name = name };
            Mesh.MarkDynamic();
        }

        public static int SegmentsFor(float widthM, float radiusM)
        {
            if (radiusM <= 0f) return 1;
            float arcDeg = widthM / radiusM * Mathf.Rad2Deg;
            return Mathf.Clamp(Mathf.CeilToInt(arcDeg / DegreesPerSegment), 2, MaxSegments);
        }

        /// <returns>true se regenerou.</returns>
        public bool Set(float widthM, float heightM, float radiusM)
        {
            if (widthM == _width && heightM == _height && radiusM == _radius) return false;
            _width = widthM;
            _height = heightM;
            _radius = radiusM;
            int n = SegmentsFor(widthM, radiusM);
            Segments = n;
            float hh = heightM * 0.5f;
            for (int i = 0; i <= n; i++)
            {
                float u = (float)i / n;
                float x = (u - 0.5f) * widthM;
                float px = x;
                float pz = 0f;
                if (radiusM > 0f)
                {
                    float theta = x / radiusM;
                    px = radiusM * Mathf.Sin(theta);
                    pz = radiusM * (Mathf.Cos(theta) - 1f);
                }
                _vertices[2 * i] = new Vector3(px, -hh, pz);
                _vertices[2 * i + 1] = new Vector3(px, hh, pz);
                _uv[2 * i] = new Vector2(u, 0f);
                _uv[2 * i + 1] = new Vector2(u, 1f);
            }
            for (int i = 0; i < n; i++)
            {
                int bl = 2 * i;
                int k = i * 6;
                _triangles[k] = bl;         // mesma ordem do Quad da Unity: (bl, tr, br), (tr, bl, tl)
                _triangles[k + 1] = bl + 3;
                _triangles[k + 2] = bl + 2;
                _triangles[k + 3] = bl + 3;
                _triangles[k + 4] = bl;
                _triangles[k + 5] = bl + 1;
            }
            int vertexCount = (n + 1) * 2;
            Mesh.Clear();
            Mesh.SetVertices(_vertices, 0, vertexCount);
            Mesh.SetUVs(0, _uv, 0, vertexCount);
            Mesh.SetTriangles(_triangles, 0, n * 6, 0);
            return true;
        }

        public void Dispose()
        {
            if (Mesh == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(Mesh);
            else UnityEngine.Object.DestroyImmediate(Mesh);
        }
    }
}
```

- [ ] **Step 4: Verify** — `tools/unity-cmd.ps1 -Command test-editmode` verde (se `AllocatingGCMemory` acusar o `Mesh.Clear`,
  trocar por `SetTriangles` antes de `SetVertices` quando o número de vértices cai, sem `Clear`).

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Presentation/Screens/CurvedQuadMesh.cs Assets/_Project/Tests/Composition/CurvedQuadMeshTests.cs
git commit -m "feat(presentation): CurvedQuadMesh (arc mesh, no per-frame allocation)"
```

---

### Task 9: `VideoScreenView` — layer Quad ↔ Cylinder, furo e quad de estado curvos

**Files:**
- Modify: `Assets/_Project/Presentation/Screens/VideoScreenView.cs`

**Interfaces:**
- Consumes: `CurvedQuadMesh` (Task 8), `IScreenView` com raio (Task 7), contrato do `HostSync` (Task 6).

- [ ] **Step 1: Implement** (sem teste automático: a `OVROverlay` só é conclusiva no Simulator/Quest — Tasks 12 e 13)

Mudanças:

1. Furo e quad de estado deixam de ser `CreatePrimitive(Quad)`: um `GameObject` com `MeshFilter` + `MeshRenderer`, os dois
   com a **mesma** `CurvedQuadMesh _shape` (escala 1; a malha já tem o tamanho). Helper:

```csharp
        private static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            if (material != null) renderer.sharedMaterial = material;
            return go;
        }
```

2. Estado da forma: `private bool _cylinder;` e `private float _layerRadius = 1f;` (último raio válido da layer cilíndrica).

3. `Apply` (confirmado) decide a forma — ponto único (ADR 0011: se o Horizon OS voltar a limitar cilindros, o fallback é aqui):

```csharp
        public void Apply(VirtualScreen screen, Pose pose, float radiusM)
        {
            bool cylinder = radiusM > 0f;
            // Resolução (RecreateScreen) ou forma (plana ↔ curva, spec 2E): recria a layer com uma external surface nova.
            // O HostSync fecha o decoder no mesmo evento e o reabre nela no próximo Tick (StartStream → IDR).
            if (!screen.Resolution.Equals(_resolution) || cylinder != _cylinder)
            {
                if (!screen.Resolution.Equals(_resolution)) _everLive = false; // a última imagem não vale no novo tamanho
                _cylinder = cylinder;
                BuildOverlay(screen.Resolution);
                SetLink(_state, _detail);
            }
            ApplyTransform(pose, screen.WidthMeters, screen.HeightMeters, radiusM);
            _root.name = string.IsNullOrEmpty(screen.Label) ? "Screen " + screen.Id.Value : screen.Label;
        }
```

4. `ApplyTransform` (também a prévia) **nunca** troca a forma:

```csharp
        public void ApplyTransform(Pose pose, float widthMeters, float heightMeters, float radiusM)
        {
            _root.transform.SetLocalPositionAndRotation(pose.Position.ToUnity(), pose.Rotation.ToUnity());
            _width = widthMeters;
            _height = heightMeters;
            if (_cylinder)
            {
                if (radiusM > 0f) _layerRadius = radiusM; // prévia plana numa layer cilíndrica: mantém o último raio
                // centro do cilindro R atrás da tela; scale = (arco, altura, raio) — como no spike 2E
                _layer.localPosition = new Vector3(0f, 0f, -_layerRadius);
                _layer.localScale = new Vector3(widthMeters, heightMeters, _layerRadius);
            }
            else
            {
                _layer.localPosition = Vector3.zero;
                _layer.localScale = new Vector3(widthMeters, heightMeters, 1f);
            }
            // furo e estado seguem a forma que a layer mostra (senão o furo recorta fora da imagem)
            _shape.Set(widthMeters, heightMeters, _cylinder ? _layerRadius : 0f);
            _statusText.characterSize = 0.006f * widthMeters; // texto plano no centro, 3 mm à frente do arco
        }
```

5. `BuildOverlay`: `overlay.currentOverlayShape = _cylinder ? OVROverlay.OverlayShape.Cylinder : OVROverlay.OverlayShape.Quad;`
   e reaplicar posição/escala da layer nova com `_width`, `_height`, `_layerRadius` (substitui o antigo `_size`).

6. `Dispose`: `_shape.Dispose()`.

- [ ] **Step 2: Verify** — recompile sem erros; `test-editmode` verde; `CompositionTests` verdes.

- [ ] **Step 3: Commit**

```bash
git add Assets/_Project/Presentation/Screens/VideoScreenView.cs
git commit -m "feat(presentation): curved video screens are cylinder layers (flat<->curved rebuilds the layer)"
```

---

### Task 10: `TestPatternScreenView` curva

**Files:**
- Modify: `Assets/_Project/Presentation/Screens/TestPatternScreenView.cs`

- [ ] **Step 1: Implement**

- O quad do padrão vira `MeshFilter`/`MeshRenderer` com uma `CurvedQuadMesh _shape` (mesmo helper `MeshObject` da Task 9;
  copiar para cá ou mover para `Presentation/Screens/ScreenMeshes.cs` se preferir um só lugar).
- A tela de teste não tem layer: a prévia pode curvar livremente.

```csharp
        public void Apply(VirtualScreen screen, Pose pose, float radiusM) =>
            ApplyTransform(pose, screen.WidthMeters, screen.HeightMeters, radiusM);

        public void ApplyTransform(Pose pose, float widthMeters, float heightMeters, float radiusM)
        {
            _root.transform.SetLocalPositionAndRotation(pose.Position.ToUnity(), pose.Rotation.ToUnity());
            _shape.Set(widthMeters, heightMeters, radiusM);
            _label.characterSize = 0.01f * widthMeters;
        }
```

- `Dispose`: `_shape.Dispose()`.

- [ ] **Step 2: Verify** — recompile + `test-editmode` verdes.

- [ ] **Step 3: Commit**

```bash
git add Assets/_Project/Presentation/Screens/TestPatternScreenView.cs
git commit -m "feat(presentation): curved test pattern screen"
```

---

### Task 11: `ManipulationPresenter` — moldura, alças e alça de curvatura na curva + rótulo

**Files:**
- Modify: `Assets/_Project/Presentation/Manipulation/ManipulationPresenter.cs`

**Interfaces:**
- Consumes: `CurvedGeometry.Of/SurfacePoint/Label/Percent/RoundedRadiusMm/ArcLimited`, `HitRegion.CurvatureHandle`,
  `ManipulationState.Curving`, `ManipulationPreview.Curvature`.

- [ ] **Step 1: Implement**

1. Moldura: `LineRenderer` com `FrameEdgePoints = 25` pontos por borda horizontal → `positionCount = 2 * FrameEdgePoints`
   (loop): borda de cima da esquerda para a direita, borda de baixo da direita para a esquerda (as verticais são os
   segmentos entre elas). Array `_frameLine = new Vector3[2 * FrameEdgePoints]` criado no construtor.

```csharp
        private void DrawFrame(Pose pose, float width, float height, float radius, Color color, bool emphasizeHandles)
        {
            float x = width * 0.5f + FrameOutsetM;
            float y = height * 0.5f + FrameOutsetM;
            for (int i = 0; i < FrameEdgePoints; i++)
            {
                float lx = -x + 2f * x * i / (FrameEdgePoints - 1);
                _frameLine[i] = CurvedGeometry.SurfacePoint(pose, radius, lx, y).ToUnity();
                _frameLine[2 * FrameEdgePoints - 1 - i] = CurvedGeometry.SurfacePoint(pose, radius, lx, -y).ToUnity();
            }
            _frame.SetPositions(_frameLine);
            if (_frame.sharedMaterial != null) _frame.sharedMaterial.color = color;
            _frame.enabled = true;

            float size = HandleSizeM * (emphasizeHandles ? 1.5f : 1f);
            for (int i = 0; i < 4; i++)
            {
                float lx = (i < 2 ? -1f : 1f) * width * 0.5f;
                float ly = (i == 1 || i == 2 ? 1f : -1f) * height * 0.5f;
                PlaceOnSurface(_handles[i], pose, radius, lx, ly, size, size);
            }
        }

        /// <summary>Quad tangente à tela no ponto local (lx, ly), 2 mm para o lado do usuário.</summary>
        private static void PlaceOnSurface(Transform t, Pose pose, float radius, float lx, float ly, float sx, float sy)
        {
            float yaw = radius > 0f ? lx / radius * Mathf.Rad2Deg : 0f;
            var rotation = pose.Rotation.ToUnity() * Quaternion.Euler(0f, yaw, 0f);
            var position = CurvedGeometry.SurfacePoint(pose, radius, lx, ly).ToUnity() + rotation * new Vector3(0f, 0f, -0.002f);
            t.SetPositionAndRotation(position, rotation);
            t.localScale = new Vector3(sx, sy, 1f);
            t.gameObject.SetActive(true);
        }
```

2. Alça de curvatura: um quad `_curveHandle` (material `DragColor`) de `ScreenHitTest.CurvatureHandleWidthM` × 0,012 m em
   `(0, altura/2 + FrameOutsetM)`, visível quando a moldura aparece; 1,5× quando `HoverHit.Region == CurvatureHandle` ou
   `State == Curving`.

3. Rótulo: `TextMesh _curveLabel` (material `StereoText.MaterialFor`, `characterSize` fixo 0,0025, fundo nenhum) em
   `(0, altura/2 + 0,05)` sobre a tela, visível com hover na alça de curvatura ou arrastando-a. Texto em cache:

```csharp
        private int _labelPercent = -1;
        private int _labelRadius = -1;
        private bool _labelLimited;

        private void ShowCurveLabel(Pose pose, float radius, float height, CurvedGeometry curve)
        {
            if (curve.Percent != _labelPercent || curve.RoundedRadiusMm != _labelRadius || curve.ArcLimited != _labelLimited)
            {
                _labelPercent = curve.Percent;
                _labelRadius = curve.RoundedRadiusMm;
                _labelLimited = curve.ArcLimited;
                _curveLabel.text = curve.Label(); // aloca só quando o valor muda
            }
            PlaceOnSurface(_curveLabel.transform, pose, radius, 0f, height * 0.5f + 0.05f, 1f, 1f);
        }
```

4. `Tick`: com prévia ativa, `var curve = CurvedGeometry.Of(preview.Curvature, preview.Placement, preview.WidthM, ws.Surface)`
   → `ApplyTransform(..., curve.RadiusM)` (Task 7), `DrawFrame(preview.Pose, preview.WidthM, preview.HeightM, curve.RadiusM, ...)`;
   rótulo se `State == Curving`. No hover: `var curve = CurvedGeometry.Of(screen, ws.Surface)`; rótulo se a região for
   `CurvatureHandle`. `HideFrame` esconde também a alça de curvatura e o rótulo.

- [ ] **Step 2: Verify** — recompile + `test-editmode` verdes; `CompositionTests` verdes.

- [ ] **Step 3: Commit**

```bash
git add Assets/_Project/Presentation/Manipulation/ManipulationPresenter.cs
git commit -m "feat(presentation): frame, handles and curvature handle follow the curve; curvature label"
```

---

### Task 12: Verificação no Meta XR Simulator

**Por que o Simulator basta:** menu, presets, hit-test, moldura, alça, prévia e undo são lógica de cena/interação (sem
decoder nem nitidez). A layer cilíndrica aparece no Simulator (sem vídeo real é suficiente para ver a forma).

- [ ] **Step 1:** Play na cena principal com o Meta XR Simulator + Operator (skill `meta-xr-operator-unity-cli-workflow`).
  Criar 2 telas de teste ("+ Tela de teste").
- [ ] **Step 2:** Menu → "Curvatura ▸" (apagado sem foco; aceso com foco) → Suave, Média, Envolvente, Plana: a tela curva, o
  preset atual ganha "● ", a linha de status mostra `"Média · 60 % · ≈ …R"`; "← Voltar" volta. Capturar.
- [ ] **Step 3:** Laser nas bordas da tela curva (Envolvente): o raio termina no arco (não no plano); moldura e alças de canto
  acompanham a curva; mover e redimensionar funcionam curvos.
- [ ] **Step 4:** Alça de curvatura com **controle**: hover mostra o rótulo; gatilho + empurrar o controle 15 cm → prévia ~+50 %;
  soltar → uma entrada de undo; X desfaz para o valor anterior. Repetir com **mãos** (pinça + empurrar).
- [ ] **Step 5:** Tela larga (32:9, 1,5 m) em Envolvente perto (0,6 m): rótulo com " · arco limitado".
- [ ] **Step 6:** Registrar em `docs/measurements/fase2e-telas-curvas.md` (seção Simulator) e commitar
  (`git add docs/measurements/fase2e-telas-curvas.md`; `git commit -m "docs: Phase 2E Simulator verification"`).

---

### Task 13: Verificação no Quest + documentação

**Por que o Quest:** nitidez de layer cilíndrica, troca de layer + decoder (IDR) e desempenho só são conclusivos nele.

- [ ] **Step 1:** `tools/unity-cmd.ps1 -Command build-apk`; `tools/quest.ps1 -Install Builds/<apk> -Launch`; host `ScreenPortalHost serve`.
- [ ] **Step 2:** Nitidez do texto (IDE/navegador) em Suave, Média e Envolvente vs. plana (Cayo no óculos).
- [ ] **Step 3:** Troca plana ↔ curva pelo menu e pela alça: sem piscar perceptível; logcat mostra decoder fechando/abrindo e
  `StartStream` repetido (IDR); o monitor virtual não é recriado.
- [ ] **Step 4:** 4 telas curvas com vídeo a 60 fps (`tools/phase0-streams-test.ps1` ou stats do app): decode ≥ 59 fps, app ≥ 72 fps.
- [ ] **Step 5:** Menu/moldura/laser por cima de uma tela curva (furo curvo, ADR 0010): sem recorte fora da imagem.
- [ ] **Step 6:** Documentação: `docs/measurements/fase2e-telas-curvas.md` (Quest); `docs/brief.md` — linhas das tabelas §15
  ("Nº de telas" e "Limite de 15 compositor layers / 1 cilindro") sem o limite de 1 cilindro nem `CylinderLayerPolicy`
  (nota v1.3.3, ADR 0011); `docs/STATUS.md` (2E concluída; linha 2E da tabela sem "CylinderLayerPolicy, crossfade").
- [ ] **Step 7:** Commit

```bash
git add docs/measurements/fase2e-telas-curvas.md docs/brief.md docs/STATUS.md
git commit -m "docs: Phase 2E done (Quest verification, brief tables, STATUS)"
```
