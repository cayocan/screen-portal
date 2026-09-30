# Fase 2D — Recriar com outra resolução — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O usuário escolhe "Recriar ▸" no menu do pulso, escolhe outra proporção/resolução para a tela em foco (monitor virtual) e o host troca o monitor SudoVDA por um novo com a resolução exata, levando as janelas junto; o undo faz o caminho inverso.

**Architecture:** Parte A é código puro do headset (Application, testado no `dotnet test tests-dotnet/UnityPureCode.Tests`): item e modo "recriar" do `WristMenu`, que só envia o `RecreateScreen` já existente; o `HostSync` já pede `AcquireScreen` com o tamanho novo na mesma chave. Parte B é o host: plano puro de movimento de janela (normal/maximizada/minimizada) em `Host.Domain`, orquestração da recriação (`MonitorRecreator`, driver e mover-janelas por interface, testada em xUnit) e as peças Win32 (`Win32WindowMover`, adaptador do driver, `ScreenHost`). Parte C (fila) roda na Unity, no Simulator com o host real e no Quest.

**Tech Stack:** Unity 6.3 (C# 9), NUnit (harness `tests-dotnet`); host .NET 10 (SudoVDA IOCTL, Win32 `user32`/`dwmapi`), xUnit.

## Global Constraints

- Spec: `docs/specs/2026-09-30-fase2d-recriar-design.md`. ADRs 0012 (escopo: sem contorno fantasma, sem resolução personalizada, recriar sem confirmação), 0009 (aluguéis, sync declarativo), 0007 (SudoVDA; `SetWindowPos` duas vezes entre DPIs).
- **C# 9 no código da Unity** (Domain, Application, testes EditMode): sem `record`, `init`, namespaces de arquivo. O host usa C# moderno.
- Domain e Application **nunca** referenciam UnityEngine. Toda mudança de estado é Command via `ICommandBus`; o menu só envia `RecreateScreen` (já existe; mantém pose e largura).
- **Zero alocação por quadro** em `WristMenu.Label/IsEnabled/StatusLine` (o painel lê a cada quadro): strings do modo recriar são montadas uma vez, ao entrar no modo.
- Menu: rótulo do item **"Recriar ▸"**; título do modo **"Recriar: <nome>"** (na linha de status); resolução atual marcada **"<W>×<H> · atual"** e apagada; "← Voltar" (`WristMenu.BackLabel`) sai sem enviar nada. No máximo **12 itens** por página (`WristMenu.MaxItems`).
- Item apagado: sem PC (`IHostCatalog.IsConnected == false`), sem tela em foco, ou tela em foco que não é `ScreenSourceKind.Virtual` (espelho e tela de teste não recriam).
- O aviso "Mais de 4 telas" **não** se aplica ao recriar (a contagem não muda).
- Host: `AcquireScreen` da mesma chave, virtual → virtual, com outro tamanho = recriar: **cria o novo → move as janelas → para o stream do antigo → remove o antigo → aluguel aponta para o novo**. Falha ao criar: antigo intacto, resposta de falha.
- Janelas: todas as de nível superior **visíveis** (e não "cloaked" nem travadas) no monitor antigo; posição por `WindowRelocation.Map`; maximizada: restaura → move → maximiza; minimizada: move só a posição de restauração (`SetWindowPlacement`), continua minimizada.
- Testes: `dotnet test tests-dotnet/UnityPureCode.Tests` (base 215) e `dotnet test host/Host.Tests` (base 71) verdes ao fim de cada tarefa. Host.App só compila (`dotnet build host/Host.App`); não rodar o host nesta parte.
- Comentários e docs em português, no estilo curto do código ao redor. Commits pequenos, um propósito cada, sem linhas de atribuição.
- `WristMenu.cs`, `MenuItemId` e `WristMenuTests` também mudam na 2E ("Curvatura ▸"): mudanças localizadas, sem refatorar o resto do menu.

## Desvios e decisões onde a spec é omissa (registrar na spec na Task 8)

1. **Título na linha de status:** o painel não tem título; no modo recriar a linha de status mostra "Recriar: <nome>" (sem mudança no Presentation).
2. **Marca da atual por texto:** "2560×1440 · atual" (sem glifo novo na fonte), apagada pelo material de desabilitado que o painel já usa.
3. **Orientação inicial** do seletor no modo recriar = a da tela em foco (a resolução atual aparece marcada sem trocar a orientação). Ela continua valendo depois (como na 2C, a orientação do seletor é lembrada).
4. **Alvo fixo:** o modo recriar guarda a tela em foco ao entrar; mudar o foco no meio não troca o alvo. Esconder o painel sai do modo.
5. **Falha ao mover janelas ou ao remover o antigo não desfaz a recriação:** o monitor novo já está alugado; o erro vai para o log (janelas não movidas voltam a um monitor físico quando o antigo sai, como antes da 2D).
6. **Troca de tipo** na mesma chave (não acontece: a origem nunca muda) continua no caminho antigo (destrói e cria).
7. **Coordenadas de área de trabalho:** `rcNormalPosition` (maximizada/minimizada) vem em coordenadas de área de trabalho; a conversão usa o canto da área de trabalho do monitor principal (`SPI_GETWORKAREA`). Com a barra de tarefas embaixo o deslocamento é 0.

---

## File Structure

```
Assets/_Project/Application/Menu/WristMenu.cs              + MenuItemId.Recreate, modo recriar
Assets/_Project/Tests/EditMode/Manipulation/WristMenuTests.cs   + testes do recriar
Assets/_Project/Tests/EditMode/Screens/HostSyncTests.cs   + undo do recriar
host/Host.Domain/Display/WindowRelocation.cs               + WindowShowState, WindowSnapshot, WindowMove, Plan
host/Host.Domain/Screens/MonitorRecreation.cs              NOVO  IVirtualMonitors<T>, IWindowMover<T>, RecreateResult<T>, MonitorRecreator<T>
host/Host.Tests/Screens/WindowPlanTests.cs                 NOVO
host/Host.Tests/Screens/MonitorRecreationTests.cs          NOVO
host/Host.App/Display/VirtualDisplayMonitors.cs            NOVO  IVirtualDisplayDriver → IVirtualMonitors<VirtualMonitor>
host/Host.App/Display/Win32WindowMover.cs                  NOVO  IWindowMover<VirtualMonitor> (Win32)
host/Host.App/Control/ScreenHost.cs                        Acquire: recriar em vez de destruir-e-criar
docs/measurements/fase2d-recriar.md                        NOVO (Parte C)
docs/STATUS.md, docs/specs/2026-09-30-fase2d-recriar-design.md   (Parte C)
```

---

## Parte A — Código puro (headset)

### Task 1: "Recriar ▸" e modo recriar do seletor

**Files:**
- Modify: `Assets/_Project/Application/Menu/WristMenu.cs`
- Test: `Assets/_Project/Tests/EditMode/Manipulation/WristMenuTests.cs`

**Interfaces:**
- Consumes: `RecreateScreen(ScreenId, Resolution)` (Domain, existente); `ResolutionCatalog.Get/Label`; `IHostCatalog.IsConnected`.
- Produces: `MenuItemId.Recreate`; `WristMenu.IsRecreating` (bool); `WristMenu.RecreateTitlePrefix = "Recriar: "`, `WristMenu.CurrentSuffix = " · atual"`, `WristMenu.RecreateLabel = "Recriar ▸"`.

- [ ] **Step 1: Write the failing tests** (acrescentar ao fim de `WristMenuTests`, antes do último `}` da classe; `using ScreenPortal.Domain.Screens;` já existe)

```csharp
        private static void AddVirtual2560(Rig r)
        {
            r.Menu.Invoke(MenuItemId.AddVirtual);
            r.Menu.Invoke(1); // 16:9
            r.Menu.Invoke(1); // 2560×1440
        }

        [Test]
        public void Recreate_DisabledWithoutPc_WithoutFocus_OrWhenTheFocusIsNotAVirtualMonitor()
        {
            var r = new Rig();
            int item = r.Menu.IndexOf(MenuItemId.Recreate);
            Assert.That(r.Menu.Label(item), Is.EqualTo(WristMenu.RecreateLabel));
            Assert.That(r.Menu.IsEnabled(item), Is.False, "sem tela em foco");

            r.Menu.Invoke(MenuItemId.AddTestScreen);
            Assert.That(r.Menu.IsEnabled(item), Is.False, "tela de teste");
            r.Menu.Invoke(MenuItemId.AddMirror);
            r.Menu.Invoke(1);
            Assert.That(r.Menu.IsEnabled(item), Is.False, "espelho");

            AddVirtual2560(r);
            Assert.That(r.Menu.IsEnabled(item), Is.True);
            r.Host.IsConnected = false;
            Assert.That(r.Menu.IsEnabled(item), Is.False, "sem PC");
        }

        [Test]
        public void Recreate_OpensTheAspects_WithTheTitle_AndMarksTheCurrentResolution()
        {
            var r = new Rig();
            AddVirtual2560(r);
            r.Menu.Invoke(MenuItemId.Recreate);
            Assert.That(r.Menu.IsRecreating, Is.True);
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Aspects));
            Assert.That(r.Menu.Orientation, Is.EqualTo(Orientation.Landscape));
            Assert.That(r.Menu.StatusLine, Is.EqualTo("Recriar: 2560×1440"));

            r.Menu.Invoke(1); // 16:9
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Resolutions));
            Assert.That(r.Menu.Label(0), Is.EqualTo("1920×1080"));
            Assert.That(r.Menu.IsEnabled(0), Is.True);
            Assert.That(r.Menu.Label(1), Is.EqualTo("2560×1440 · atual"));
            Assert.That(r.Menu.IsEnabled(1), Is.False);

            var before = r.Store.State;
            r.Menu.Invoke(1); // apagado: nada acontece
            Assert.That(r.Store.State, Is.SameAs(before));
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Resolutions));
        }

        [Test]
        public void Recreate_StartsInTheOrientationOfTheFocusedScreen()
        {
            var r = new Rig();
            r.Menu.Invoke(MenuItemId.AddVirtual);
            r.Menu.Invoke(0); // Paisagem → Retrato
            r.Menu.Invoke(1); // 9:16
            r.Menu.Invoke(1); // 1440×2560
            Assert.That(r.Store.State.Screens[0].Resolution, Is.EqualTo(Resolution.Create(1440, 2560)));

            r.Menu.Invoke(MenuItemId.AddVirtual);
            r.Menu.Invoke(0); // Retrato → Paisagem
            r.Menu.Invoke(r.Menu.ItemCount - 1); // voltar
            Assert.That(r.Menu.Orientation, Is.EqualTo(Orientation.Landscape));

            r.Menu.Invoke(MenuItemId.Recreate);
            Assert.That(r.Menu.Orientation, Is.EqualTo(Orientation.Portrait));
            r.Menu.Invoke(1); // 9:16
            Assert.That(r.Menu.Label(1), Is.EqualTo("1440×2560 · atual"));
        }

        [Test]
        public void PickingAResolution_SendsRecreateScreen_ReturnsToMain_AndUndoRestores()
        {
            var r = new Rig();
            AddVirtual2560(r);
            var id = r.Store.State.FocusedScreenId;
            float width = r.Store.State.Screens[0].WidthMeters;

            r.Menu.Invoke(MenuItemId.Recreate);
            r.Menu.Invoke(0); // Paisagem → Retrato
            r.Menu.Invoke(1); // 9:16
            r.Menu.Invoke(1); // 1440×2560

            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Main));
            Assert.That(r.Menu.IsRecreating, Is.False);
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(1));
            var screen = r.Store.State.Screens[0];
            Assert.That(screen.Id, Is.EqualTo(id));
            Assert.That(screen.Resolution, Is.EqualTo(Resolution.Create(1440, 2560)));
            Assert.That(screen.WidthMeters, Is.EqualTo(width).Within(1e-5f), "mesma largura; a altura segue a proporção");
            Assert.That(r.Menu.StatusLine, Is.EqualTo("Cilindro · 1 tela"));

            r.Menu.Invoke(MenuItemId.Undo);
            Assert.That(r.Store.State.Screens[0].Resolution, Is.EqualTo(Resolution.Create(2560, 1440)));
        }

        [Test]
        public void Back_LeavesRecreateMode_WithoutSendingAnything()
        {
            var r = new Rig();
            AddVirtual2560(r);
            var before = r.Store.State;

            r.Menu.Invoke(MenuItemId.Recreate);
            r.Menu.Invoke(1); // 16:9
            r.Menu.Invoke(r.Menu.ItemCount - 1); // voltar → proporções, ainda recriando
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Aspects));
            Assert.That(r.Menu.IsRecreating, Is.True);
            r.Menu.Invoke(r.Menu.ItemCount - 1); // voltar → principal
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Main));
            Assert.That(r.Menu.IsRecreating, Is.False);
            Assert.That(r.Store.State, Is.SameAs(before));
            Assert.That(r.Menu.StatusLine, Is.EqualTo("Cilindro · 1 tela"));

            r.Menu.Invoke(MenuItemId.AddVirtual); // "+ Monitor virtual" não marca nada
            r.Menu.Invoke(1);
            Assert.That(r.Menu.Label(1), Is.EqualTo("2560×1440"));
            Assert.That(r.Menu.IsEnabled(1), Is.True);

            r.Menu.OnHidden();
            r.Menu.Invoke(MenuItemId.Recreate);
            r.Menu.OnHidden(); // esconder o painel também sai do modo
            Assert.That(r.Menu.IsRecreating, Is.False);
            Assert.That(r.Store.State, Is.SameAs(before));
        }

        [Test]
        public void Recreate_DoesNotShowTheTooManyScreensWarning()
        {
            var r = new Rig();
            for (int i = 0; i < 5; i++) AddVirtual2560(r);
            Assert.That(r.Menu.StatusLine, Is.EqualTo(WristMenu.TooManyNotice));
            r.Menu.OnHidden();

            r.Menu.Invoke(MenuItemId.Recreate);
            r.Menu.Invoke(1);
            r.Menu.Invoke(0); // 1920×1080
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(5));
            Assert.That(r.Menu.StatusLine, Is.EqualTo("Cilindro · 5 telas"));
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests`
Expected: FAIL de compilação (`MenuItemId.Recreate`, `WristMenu.IsRecreating`, `WristMenu.RecreateLabel` não existem).

- [ ] **Step 3: Implementar em `WristMenu.cs`**

3a. Enum — `Recreate` no fim:

```csharp
        AddTestScreen,
        Recreate,
    }
```

3b. Constantes, logo depois de `BackLabel`:

```csharp
        public const string RecreateLabel = "Recriar ▸";
        public const string RecreateTitlePrefix = "Recriar: ";
        public const string CurrentSuffix = " · atual";
```

3c. Itens da página principal — `MenuItemId.Recreate` depois de `MenuItemId.RemoveScreen` nos dois arrays:

```csharp
            MenuItemId.ToggleGrid, MenuItemId.AddVirtual, MenuItemId.AddMirror, MenuItemId.ReturnToSurface, MenuItemId.RemoveScreen,
            MenuItemId.Recreate,
        };
```

e

```csharp
            MenuItemId.ToggleGrid, MenuItemId.AddVirtual, MenuItemId.AddMirror, MenuItemId.ReturnToSurface, MenuItemId.RemoveScreen,
            MenuItemId.Recreate, MenuItemId.AddTestScreen,
        };
```

3d. Campos, depois de `_mirrorLabels`:

```csharp
        // Modo recriar (spec 2D §3): tela alvo fixada ao entrar; textos montados uma vez (o painel lê a cada quadro).
        private ScreenId _recreate = ScreenId.None;
        private Resolution _recreateFrom;
        private string _recreateTitle = string.Empty;
        private string _recreateCurrentLabel = string.Empty;

        public bool IsRecreating => !_recreate.IsNone;
```

3e. `StatusLine` — primeira linha do `get`:

```csharp
                if (IsRecreating) return _recreateTitle;
```

3f. `Label(int)` — página de resoluções:

```csharp
                case MenuPage.Resolutions:
                    if (IsCurrent(index)) return _recreateCurrentLabel;
                    return ResolutionCatalog.Label(_aspect, index, Orientation);
```

3g. `Label(MenuItemId)`:

```csharp
                case MenuItemId.Recreate: return RecreateLabel;
```

3h. `IsEnabled(int)` — separar resoluções de espelhos:

```csharp
                case MenuPage.Resolutions: return _host.IsConnected && !IsCurrent(index);
                case MenuPage.Mirrors: return _host.IsConnected;
```

3i. `IsEnabled(MenuItemId)`:

```csharp
                case MenuItemId.Recreate:
                    return _host.IsConnected && ws.TryGetScreen(ws.FocusedScreenId, out var target)
                           && target.Source.Kind == ScreenSourceKind.Virtual;
```

3j. `Invoke(int)` — voltar sai do modo ao chegar na principal; escolher resolução recria:

```csharp
            if (IsBack(index))
            {
                Page = Page == MenuPage.Resolutions ? MenuPage.Aspects : MenuPage.Main;
                if (Page == MenuPage.Main) ExitRecreate();
                return;
            }
```

```csharp
                case MenuPage.Resolutions:
                {
                    var resolution = ResolutionCatalog.Get(_aspect, index, Orientation);
                    if (IsRecreating) Recreate(resolution);
                    else Create(resolution, ScreenSource.Virtual, ResolutionCatalog.Label(_aspect, index, Orientation));
                    return;
                }
```

3k. `Invoke(MenuItemId)`:

```csharp
                case MenuItemId.Recreate:
                    EnterRecreate(ws);
                    break;
```

3l. Métodos novos, depois de `Create`:

```csharp
        private void EnterRecreate(Workspace ws)
        {
            ws.TryGetScreen(ws.FocusedScreenId, out var target);
            _recreate = target.Id;
            _recreateFrom = target.Resolution;
            _recreateTitle = RecreateTitlePrefix + target.Label;
            _recreateCurrentLabel = target.Resolution + CurrentSuffix;
            Orientation = target.Orientation; // a atual aparece marcada sem trocar a orientação
            Page = MenuPage.Aspects;
        }

        private void Recreate(Resolution resolution)
        {
            _bus.Send(new RecreateScreen(_recreate, resolution)); // undo recria na anterior (ADR 0012)
            ExitRecreate();
            Page = MenuPage.Main;
        }

        private void ExitRecreate()
        {
            _recreate = ScreenId.None;
            _recreateTitle = string.Empty;
        }

        private bool IsCurrent(int index) =>
            IsRecreating && ResolutionCatalog.Get(_aspect, index, Orientation).Equals(_recreateFrom);
```

3m. `OnHidden` — sair do modo:

```csharp
        public void OnHidden()
        {
            Page = MenuPage.Main;
            _notice = string.Empty;
            ExitRecreate();
        }
```

3n. Resumo do XML doc da classe: acrescentar "e o modo recriar (2D §3)".

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests-dotnet/UnityPureCode.Tests`
Expected: PASS (215 + 6 = 221).

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Application/Menu/WristMenu.cs Assets/_Project/Tests/EditMode/Manipulation/WristMenuTests.cs
git commit -m "feat(menu): Recreate item and recreate mode of the resolution picker (Phase 2D)"
```

---

### Task 2: `HostSync` — undo do recriar pede o tamanho anterior na mesma chave

**Files:**
- Test: `Assets/_Project/Tests/EditMode/Screens/HostSyncTests.cs`

**Interfaces:**
- Consumes: `HostSync` (existente; troca de resolução mantém o slot e pede `AcquireScreen`), `ICommandBus.Undo()`.
- Produces: nada novo (teste de caracterização do caminho de undo exigido pela spec §5).

- [ ] **Step 1: Write the test** (depois de `Recreate_KeepsTheStreamSlot_AndReopensTheDecoderWithTheNewResolution`)

```csharp
        [Test]
        public void UndoOfRecreate_AcquiresThePreviousSizeOnTheSameKey_WithoutReleasing()
        {
            var r = new Rig();
            var id = r.AddVirtual();
            r.Link();
            r.Push(new ScreenReadyMessage((uint)id.Value, 2560, 1440));
            r.C.Bus.Send(new RecreateScreen(id, Resolution.Create(1920, 1080)));
            r.Tick();
            r.Push(new ScreenReadyMessage((uint)id.Value, 1920, 1080));

            r.C.Bus.Undo();
            r.Tick();
            var acquire = r.C.Channel.Last<AcquireScreenMessage>();
            Assert.That(acquire.ScreenKey, Is.EqualTo((uint)id.Value));
            Assert.That(acquire.Width, Is.EqualTo(2560u));
            Assert.That(acquire.Height, Is.EqualTo(1440u));
            Assert.That(r.C.Channel.Count<ReleaseScreenMessage>(), Is.EqualTo(0), "recriar não libera a chave");

            r.Push(new ScreenReadyMessage((uint)id.Value, 2560, 1440));
            Assert.That(r.Video.LastResolution, Is.EqualTo(Resolution.Create(2560, 1440)));
            Assert.That(r.Video.LastPort, Is.EqualTo(9000));
        }
```

- [ ] **Step 2: Run** `dotnet test tests-dotnet/UnityPureCode.Tests` — Expected: PASS (o comportamento já existe; se falhar, é bug do `HostSync`: corrigir antes de seguir).

- [ ] **Step 3: Commit**

```bash
git add Assets/_Project/Tests/EditMode/Screens/HostSyncTests.cs
git commit -m "test(sync): undo of RecreateScreen acquires the previous size on the same key"
```

---

## Parte B — Host (código puro + Win32 compilado)

### Task 3: Plano de movimento de janela (normal, maximizada, minimizada)

**Files:**
- Modify: `host/Host.Domain/Display/WindowRelocation.cs`
- Test: `host/Host.Tests/Screens/WindowPlanTests.cs`

**Interfaces:**
- Consumes: `RectI`, `WindowRelocation.Map(window, from, to)` (existentes).
- Produces (`ScreenPortal.Host.Domain.Display`): `enum WindowShowState { Normal, Maximized, Minimized }`; `readonly record struct WindowSnapshot(WindowShowState State, RectI Rect, RectI RestoreRect)`; `readonly record struct WindowMove(WindowShowState State, RectI Rect, RectI RestoreRect)`; `WindowRelocation.Plan(WindowSnapshot window, RectI from, RectI to, int workspaceX, int workspaceY) → WindowMove`.

- [ ] **Step 1: Write the failing test**

```csharp
using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.Tests.Screens;

public class WindowPlanTests
{
    private static readonly RectI From = new(2560, 0, 2560, 1440);
    private static readonly RectI To = new(5120, 0, 1920, 1080);

    [Fact]
    public void Normal_MapsTheWindowRect_AndRestoreFollowsInWorkspaceCoordinates()
    {
        var window = new WindowSnapshot(WindowShowState.Normal, new RectI(3040, 450, 960, 540), default);

        var move = WindowRelocation.Plan(window, From, To, 0, 40);

        Assert.Equal(WindowShowState.Normal, move.State);
        Assert.Equal(WindowRelocation.Map(window.Rect, From, To), move.Rect);
        Assert.Equal(move.Rect with { Y = move.Rect.Y - 40 }, move.RestoreRect);
    }

    [Fact]
    public void Maximized_UsesTheRestoreRect_ConvertedFromWorkspaceCoordinates()
    {
        // Barra de tarefas no topo: área de trabalho começa em y = 40. Restauração (workspace) 3040,230 = tela 3040,270,
        // centro (3520, 540) = (37,5 %, 37,5 %) do monitor antigo.
        var window = new WindowSnapshot(WindowShowState.Maximized, From, new RectI(3040, 230, 960, 540));

        var move = WindowRelocation.Plan(window, From, To, 0, 40);

        Assert.Equal(WindowShowState.Maximized, move.State);
        Assert.Equal(new RectI(5360, 135, 960, 540), move.Rect);
        Assert.Equal(new RectI(5360, 95, 960, 540), move.RestoreRect);
    }

    [Fact]
    public void Minimized_IgnoresTheIconRect_AndMovesOnlyTheRestorePosition()
    {
        var window = new WindowSnapshot(WindowShowState.Minimized, new RectI(-32000, -32000, 160, 28), new RectI(3040, 270, 960, 540));

        var move = WindowRelocation.Plan(window, From, To, 0, 0);

        Assert.Equal(WindowShowState.Minimized, move.State);
        Assert.Equal(new RectI(5360, 135, 960, 540), move.RestoreRect);
        Assert.Equal(move.RestoreRect, move.Rect);
    }

    [Fact]
    public void Maximized_LargerThanTheNewMonitor_ShrinksTheRestoreRect()
    {
        var window = new WindowSnapshot(WindowShowState.Maximized, From, new RectI(2560, 0, 2400, 1400));

        var move = WindowRelocation.Plan(window, From, To, 0, 0);

        Assert.True(move.RestoreRect.Width <= To.Width && move.RestoreRect.Height <= To.Height);
        Assert.True(move.RestoreRect.X >= To.X && move.RestoreRect.Right <= To.Right);
    }
}
```

- [ ] **Step 2: Run** `dotnet test host/Host.Tests --filter WindowPlanTests` — Expected: FAIL de compilação (`WindowSnapshot`, `Plan` não existem).

- [ ] **Step 3: Implementar** (acrescentar em `WindowRelocation.cs`)

Antes de `public static class WindowRelocation`:

```csharp
public enum WindowShowState { Normal, Maximized, Minimized }

/// <summary>
/// Uma janela como o Windows a descreve: <see cref="Rect"/> na tela (<c>GetWindowRect</c>) e a posição de restauração
/// (<c>rcNormalPosition</c>), em coordenadas de área de trabalho.
/// </summary>
public readonly record struct WindowSnapshot(WindowShowState State, RectI Rect, RectI RestoreRect);

/// <summary>
/// Para onde a janela vai (spec 2D §4). Normal: <see cref="Rect"/> (tela) por <c>SetWindowPos</c>. Maximizada: restaura em
/// <see cref="RestoreRect"/>, ajusta em <see cref="Rect"/> e maximiza. Minimizada: só <see cref="RestoreRect"/> por
/// <c>SetWindowPlacement</c>, continua minimizada.
/// </summary>
public readonly record struct WindowMove(WindowShowState State, RectI Rect, RectI RestoreRect);
```

Dentro de `WindowRelocation`:

```csharp
    /// <summary>
    /// Maximizada e minimizada levam a posição de restauração (o retângulo atual é o maximizado ou o ícone).
    /// <paramref name="workspaceX"/>/<paramref name="workspaceY"/>: canto da área de trabalho do monitor principal na tela
    /// (coordenadas de área de trabalho = tela − esse canto).
    /// </summary>
    public static WindowMove Plan(WindowSnapshot window, RectI from, RectI to, int workspaceX, int workspaceY)
    {
        var source = window.State == WindowShowState.Normal
            ? window.Rect
            : Offset(window.RestoreRect, workspaceX, workspaceY);
        var target = Map(source, from, to);
        return new WindowMove(window.State, target, Offset(target, -workspaceX, -workspaceY));
    }

    private static RectI Offset(RectI r, int dx, int dy) => r with { X = r.X + dx, Y = r.Y + dy };
```

- [ ] **Step 4: Run** `dotnet test host/Host.Tests` — Expected: PASS (71 + 4 = 75).

- [ ] **Step 5: Commit**

```bash
git add host/Host.Domain/Display/WindowRelocation.cs host/Host.Tests/Screens/WindowPlanTests.cs
git commit -m "feat(host): window move plan for normal, maximized and minimized windows (Phase 2D)"
```

---

### Task 4: `MonitorRecreator` — ordem da recriação e troca do aluguel

**Files:**
- Create: `host/Host.Domain/Screens/MonitorRecreation.cs`
- Test: `host/Host.Tests/Screens/MonitorRecreationTests.cs`

**Interfaces:**
- Consumes: `ScreenLeaseTable`, `ScreenLease`, `ScreenLeaseKind` (existentes).
- Produces (`ScreenPortal.Host.Domain.Screens`):
  - `interface IVirtualMonitors<TMonitor>`: `TMonitor Create(int width, int height)` (lança se falhar), `void Remove(TMonitor monitor)`, `string DisplayName(TMonitor monitor)`.
  - `interface IWindowMover<TMonitor>`: `int MoveWindows(TMonitor from, TMonitor to)`.
  - `sealed record RecreateResult<TMonitor>(bool Ok, TMonitor? Monitor, int WindowsMoved, string Error) where TMonitor : class`.
  - `sealed class MonitorRecreator<TMonitor>(IVirtualMonitors<TMonitor> monitors, IWindowMover<TMonitor> windows, Action<string> log) where TMonitor : class` com `RecreateResult<TMonitor> Recreate(ScreenLeaseTable leases, ScreenLease old, TMonitor oldMonitor, int width, int height, Action stopOldStream)`.

- [ ] **Step 1: Write the failing test**

```csharp
using ScreenPortal.Host.Domain.Screens;

namespace ScreenPortal.Host.Tests.Screens;

public class MonitorRecreationTests
{
    private sealed record Monitor(string Name);

    private sealed class Fakes : IVirtualMonitors<Monitor>, IWindowMover<Monitor>
    {
        public readonly List<string> Calls = new();
        public bool FailCreate, FailMove, FailRemove;

        public Monitor Create(int width, int height)
        {
            Calls.Add($"create {width}×{height}");
            if (FailCreate) throw new InvalidOperationException("IOCTL falhou");
            return new Monitor(@"\\.\DISPLAY9");
        }

        public void Remove(Monitor monitor)
        {
            Calls.Add("remove " + monitor.Name);
            if (FailRemove) throw new InvalidOperationException("não saiu");
        }

        public string DisplayName(Monitor monitor) => monitor.Name;

        public int MoveWindows(Monitor from, Monitor to)
        {
            Calls.Add($"move {from.Name} → {to.Name}");
            if (FailMove) throw new InvalidOperationException("acesso negado");
            return 2;
        }
    }

    private static readonly Monitor Old = new(@"\\.\DISPLAY5");

    private static (ScreenLeaseTable, ScreenLease) Leased()
    {
        var leases = new ScreenLeaseTable();
        var lease = new ScreenLease("dev-1", 3, ScreenLeaseKind.Virtual, 2560, 1440, 0, Old.Name);
        leases.Add(lease);
        return (leases, lease);
    }

    private static (MonitorRecreator<Monitor>, List<string> log) Recreator(Fakes fakes)
    {
        var log = new List<string>();
        return (new MonitorRecreator<Monitor>(fakes, fakes, log.Add), log);
    }

    [Fact]
    public void Recreate_CreatesTheNewFirst_MovesTheWindows_ThenStopsAndRemovesTheOld()
    {
        var fakes = new Fakes();
        var (recreator, _) = Recreator(fakes);
        var (leases, lease) = Leased();

        var result = recreator.Recreate(leases, lease, Old, 1920, 1080, () => fakes.Calls.Add("stop"));

        Assert.True(result.Ok);
        Assert.Equal(@"\\.\DISPLAY9", result.Monitor!.Name);
        Assert.Equal(2, result.WindowsMoved);
        Assert.Equal(new[] { "create 1920×1080", @"move \\.\DISPLAY5 → \\.\DISPLAY9", "stop", @"remove \\.\DISPLAY5" }, fakes.Calls);
    }

    [Fact]
    public void Recreate_TheLeaseKeepsTheKey_WithTheNewSizeAndDisplay_AndIsLive()
    {
        var fakes = new Fakes();
        var (recreator, _) = Recreator(fakes);
        var (leases, lease) = Leased();
        leases.Expire("dev-1", 3, DateTimeOffset.UnixEpoch.AddSeconds(10)); // estava removida (undo dentro do prazo)

        recreator.Recreate(leases, lease, Old, 1080, 1920, () => { });

        Assert.Equal(1, leases.Count);
        Assert.True(leases.TryGet("dev-1", 3, out var now));
        Assert.Equal(ScreenLeaseKind.Virtual, now.Kind);
        Assert.Equal((1080, 1920), (now.Width, now.Height));
        Assert.Equal(@"\\.\DISPLAY9", now.DisplayName);
        Assert.Null(now.ExpiresAt);
    }

    [Fact]
    public void CreateFails_KeepsTheOldMonitorAndLease_AndReportsTheFailure()
    {
        var fakes = new Fakes { FailCreate = true };
        var (recreator, _) = Recreator(fakes);
        var (leases, lease) = Leased();
        bool stopped = false;

        var result = recreator.Recreate(leases, lease, Old, 1920, 1080, () => stopped = true);

        Assert.False(result.Ok);
        Assert.Null(result.Monitor);
        Assert.Contains("IOCTL falhou", result.Error);
        Assert.Equal(new[] { "create 1920×1080" }, fakes.Calls);
        Assert.False(stopped);
        Assert.True(leases.TryGet("dev-1", 3, out var kept));
        Assert.Same(lease, kept);
    }

    [Fact]
    public void MoveOrRemoveFailing_StillSwapsToTheNewMonitor_AndLogs()
    {
        var fakes = new Fakes { FailMove = true, FailRemove = true };
        var (recreator, log) = Recreator(fakes);
        var (leases, lease) = Leased();

        var result = recreator.Recreate(leases, lease, Old, 1920, 1080, () => fakes.Calls.Add("stop"));

        Assert.True(result.Ok);
        Assert.Equal(0, result.WindowsMoved);
        Assert.Contains("stop", fakes.Calls);
        Assert.Contains(@"remove \\.\DISPLAY5", fakes.Calls);
        Assert.True(leases.TryGet("dev-1", 3, out var now));
        Assert.Equal(@"\\.\DISPLAY9", now.DisplayName);
        Assert.Equal(2, log.Count);
        Assert.Contains(log, l => l.Contains("acesso negado"));
        Assert.Contains(log, l => l.Contains("não saiu"));
    }
}
```

- [ ] **Step 2: Run** `dotnet test host/Host.Tests --filter MonitorRecreationTests` — Expected: FAIL de compilação (`MonitorRecreator` não existe).

- [ ] **Step 3: Implementar `host/Host.Domain/Screens/MonitorRecreation.cs`**

```csharp
namespace ScreenPortal.Host.Domain.Screens;

/// <summary>Driver de monitor virtual como a recriação o vê (spec 2D §4). <c>Create</c> lança se falhar.</summary>
public interface IVirtualMonitors<TMonitor>
{
    TMonitor Create(int width, int height);
    void Remove(TMonitor monitor);
    /// <summary><c>\\.\DISPLAYn</c> do monitor.</summary>
    string DisplayName(TMonitor monitor);
}

/// <summary>Move as janelas de nível superior visíveis de um monitor para outro (spec 2D §4).</summary>
public interface IWindowMover<TMonitor>
{
    /// <returns>Quantas janelas foram movidas.</returns>
    int MoveWindows(TMonitor from, TMonitor to);
}

public sealed record RecreateResult<TMonitor>(bool Ok, TMonitor? Monitor, int WindowsMoved, string Error) where TMonitor : class;

/// <summary>
/// "Recriar com outra resolução" no host (spec 2D §4): cria o monitor novo → move as janelas do antigo para ele → para o
/// stream do antigo e o remove → o aluguel passa ao novo (mesma chave, tamanho novo, sem prazo). Se criar falhar, nada
/// muda. Falha ao mover ou ao remover vai para o log: o novo já está em uso. Não é thread-safe (o ScreenHost chama com
/// o lock).
/// </summary>
public sealed class MonitorRecreator<TMonitor>(IVirtualMonitors<TMonitor> monitors, IWindowMover<TMonitor> windows, Action<string> log)
    where TMonitor : class
{
    public RecreateResult<TMonitor> Recreate(ScreenLeaseTable leases, ScreenLease old, TMonitor oldMonitor, int width, int height,
        Action stopOldStream)
    {
        TMonitor created;
        try
        {
            created = monitors.Create(width, height);
        }
        catch (Exception e)
        {
            return new RecreateResult<TMonitor>(false, null, 0, "Falha ao criar monitor: " + e.Message);
        }

        int moved = 0;
        try { moved = windows.MoveWindows(oldMonitor, created); }
        catch (Exception e) { log($"telas: falha ao mover as janelas de {old.DisplayName}: {e.Message}"); }

        stopOldStream();
        try { monitors.Remove(oldMonitor); }
        catch (Exception e) { log($"telas: falha ao remover {old.DisplayName}: {e.Message}"); }

        leases.Add(new ScreenLease(old.DeviceId, old.ScreenKey, ScreenLeaseKind.Virtual, width, height, 0, monitors.DisplayName(created)));
        return new RecreateResult<TMonitor>(true, created, moved, "");
    }
}
```

- [ ] **Step 4: Run** `dotnet test host/Host.Tests` — Expected: PASS (75 + 4 = 79).

- [ ] **Step 5: Commit**

```bash
git add host/Host.Domain/Screens/MonitorRecreation.cs host/Host.Tests/Screens/MonitorRecreationTests.cs
git commit -m "feat(host): MonitorRecreator orders recreate as create, move windows, stop and remove old, swap lease"
```

---

### Task 5: Mover janelas no Win32 e recriar no `ScreenHost`

**Files:**
- Create: `host/Host.App/Display/VirtualDisplayMonitors.cs`
- Create: `host/Host.App/Display/Win32WindowMover.cs`
- Modify: `host/Host.App/Control/ScreenHost.cs` (`Acquire`, novo `Recreate`)

**Interfaces:**
- Consumes: `IVirtualMonitors<T>`, `IWindowMover<T>`, `MonitorRecreator<T>` (Task 4); `WindowRelocation.Plan`, `WindowSnapshot`, `WindowShowState` (Task 3); `IVirtualDisplayDriver`, `VirtualMonitor`, `Win32Displays.Current`, `WindowOps.Move` (existentes).
- Produces: `VirtualDisplayMonitors(IVirtualDisplayDriver driver, int refresh) : IVirtualMonitors<VirtualMonitor>`; `Win32WindowMover(Action<string> log) : IWindowMover<VirtualMonitor>`.

Sem teste automático (Win32 real): verificação por compilação aqui e no roteiro da Task 7.

- [ ] **Step 1: `VirtualDisplayMonitors.cs`**

```csharp
using ScreenPortal.Host.Domain.Screens;

namespace ScreenPortal.Host.App.Display;

/// <summary>O driver de monitor virtual visto pela recriação (spec 2D §4).</summary>
public sealed class VirtualDisplayMonitors(IVirtualDisplayDriver driver, int refresh) : IVirtualMonitors<VirtualMonitor>
{
    public VirtualMonitor Create(int width, int height) => driver.Create(width, height, refresh);
    public void Remove(VirtualMonitor monitor) => driver.Remove(monitor);
    public string DisplayName(VirtualMonitor monitor) => monitor.Display.DeviceName;
}
```

- [ ] **Step 2: `Win32WindowMover.cs`**

```csharp
using System.Runtime.InteropServices;
using ScreenPortal.Host.Domain.Display;
using ScreenPortal.Host.Domain.Screens;

namespace ScreenPortal.Host.App.Display;

/// <summary>
/// Leva as janelas de nível superior visíveis (não "cloaked") do monitor antigo para o novo (spec 2D §4), com posição
/// relativa mantida (<see cref="WindowRelocation.Plan"/>). Maximizada: restaura → move → maximiza. Minimizada: só a
/// posição de restauração, continua minimizada. Os monitores são resolvidos pelo nome na hora (HMONITOR muda a cada
/// mudança de topologia, ADR 0007).
/// </summary>
public sealed class Win32WindowMover(Action<string> log) : IWindowMover<VirtualMonitor>
{
    public int MoveWindows(VirtualMonitor from, VirtualMonitor to)
    {
        var source = Win32Displays.Current(from.Display.DeviceName);
        var target = Win32Displays.Current(to.Display.DeviceName);
        if (source.HMonitor == IntPtr.Zero) return 0; // sem HMONITOR, MonitorFromWindow(NULL) casaria janelas soltas
        var workspace = WorkspaceOrigin();
        int moved = 0;
        foreach (var hwnd in WindowsOn(source.HMonitor))
        {
            try
            {
                if (Move(hwnd, source.Bounds, target.Bounds, workspace)) moved++;
            }
            catch (Exception e)
            {
                log($"telas: janela 0x{hwnd:X} não foi movida: {e.Message}");
            }
        }
        return moved;
    }

    private static bool Move(IntPtr hwnd, RectI from, RectI to, (int X, int Y) workspace)
    {
        var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(hwnd, ref placement) || !GetWindowRect(hwnd, out var rect)) return false;
        var state = IsIconic(hwnd) ? WindowShowState.Minimized : IsZoomed(hwnd) ? WindowShowState.Maximized : WindowShowState.Normal;
        var snapshot = new WindowSnapshot(state, ToRectI(rect), ToRectI(placement.NormalPosition));
        var move = WindowRelocation.Plan(snapshot, from, to, workspace.X, workspace.Y);
        switch (move.State)
        {
            case WindowShowState.Normal:
                WindowOps.Move(hwnd, move.Rect);
                return true;
            case WindowShowState.Maximized:
                placement.NormalPosition = FromRectI(move.RestoreRect);
                placement.ShowCmd = SwShowNoActivate;             // restaura já no monitor novo
                if (!SetWindowPlacement(hwnd, ref placement)) return false;
                WindowOps.Move(hwnd, move.Rect);                  // duas chamadas: DPI diferente (ADR 0007)
                ShowWindow(hwnd, SwShowMaximized);
                return true;
            default:
                placement.NormalPosition = FromRectI(move.RestoreRect);
                placement.ShowCmd = SwShowMinNoActive;            // continua minimizada
                return SetWindowPlacement(hwnd, ref placement);
        }
    }

    private static List<IntPtr> WindowsOn(IntPtr hmonitor)
    {
        var result = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            // Janela travada fica: SetWindowPos esperaria por ela com o lock do ScreenHost preso.
            if (IsWindowVisible(hwnd) && !IsCloaked(hwnd) && !IsHungAppWindow(hwnd)
                && MonitorFromWindow(hwnd, MonitorDefaultToNull) == hmonitor) result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    /// <summary>Canto da área de trabalho do monitor principal: origem das coordenadas de <c>rcNormalPosition</c>.</summary>
    private static (int X, int Y) WorkspaceOrigin() =>
        SystemParametersInfo(SpiGetWorkArea, 0, out var work, 0) ? (work.Left, work.Top) : (0, 0);

    private static RectI ToRectI(Rect r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    private static Rect FromRectI(RectI r) => new() { Left = r.X, Top = r.Y, Right = r.Right, Bottom = r.Bottom };

    private const int SwShowMaximized = 3, SwShowNoActivate = 4, SwShowMinNoActive = 7;
    private const uint MonitorDefaultToNull = 0, SpiGetWorkArea = 0x0030, DwmwaCloaked = 14;

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public Point MinPosition;
        public Point MaxPosition;
        public Rect NormalPosition;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, out Rect rect, uint winIni);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out int value, int size);
}
```

- [ ] **Step 3: `ScreenHost.Acquire` recria em vez de destruir-e-criar**

Trocar

```csharp
                Destroy(deviceId, request.ScreenKey); // RecreateScreen: troca o monitor
```

por

```csharp
                if (request.ScreenKind == ScreenKind.Virtual && lease.Kind == ScreenLeaseKind.Virtual
                    && _live.TryGetValue((deviceId, request.ScreenKey), out var old) && old.Monitor != null)
                    return Recreate(deviceId, request, lease, old.Monitor); // RecreateScreen (spec 2D §4)
                Destroy(deviceId, request.ScreenKey); // outro tipo na mesma chave: troca o monitor
```

e acrescentar, depois de `CreateVirtual`:

```csharp
    /// <summary>
    /// Recriar (spec 2D §4): cria o novo → move as janelas → para o stream do antigo e o remove → o aluguel passa ao
    /// novo. Se criar falhar, o antigo fica intacto (com o stream) e a resposta é de falha. Chamar com _lock.
    /// </summary>
    private ScreenAcquireResult Recreate(string deviceId, AcquireScreenMessage request, ScreenLease lease, VirtualMonitor oldMonitor)
    {
        if (!ScreenAcquireResult.TryValidateVirtualSize(request.Width, request.Height, out var invalid)) return invalid;
        int width = (int)request.Width, height = (int)request.Height;
        var recreator = new MonitorRecreator<VirtualMonitor>(new VirtualDisplayMonitors(_driver!, 60), new Win32WindowMover(_log), _log);
        var result = recreator.Recreate(_leases, lease, oldMonitor, width, height, () => StopStream(deviceId, request.ScreenKey));
        if (!result.Ok)
        {
            _log($"telas: recriar {deviceId}/{request.ScreenKey} em {width}×{height} falhou; {lease.DisplayName} mantido ({result.Error})");
            return new ScreenAcquireResult(false, 0, 0, result.Error);
        }
        string name = result.Monitor!.Display.DeviceName;
        _live[(deviceId, request.ScreenKey)] = new Live { Monitor = result.Monitor, DisplayName = name };
        _log($"telas: {deviceId}/{request.ScreenKey} recriado {lease.Width}×{lease.Height} → {width}×{height} " +
             $"({lease.DisplayName} → {name}, {result.WindowsMoved} janela(s) movida(s))");
        return new ScreenAcquireResult(true, width, height, "");
    }
```

(`_driver` não é nulo: o monitor antigo foi criado por ele.)

- [ ] **Step 4: Build e testes**

Run: `dotnet build host/Host.App` — Expected: `0 Erro(s)` (não rodar o host).
Run: `dotnet test host/Host.Tests` — Expected: PASS (79).

- [ ] **Step 5: Commit**

```bash
git add host/Host.App/Display/VirtualDisplayMonitors.cs host/Host.App/Display/Win32WindowMover.cs host/Host.App/Control/ScreenHost.cs
git commit -m "feat(host): recreate a virtual monitor by creating the new one and moving the windows before removing the old"
```

---

## Parte C — Unity (fila: não executar no worktree; roda no checkout principal com o Editor aberto)

### Task 6: Compilar na Unity, testes EditMode e painel

**Files:**
- Verificar: `Assets/_Project/Presentation/Menu/WristMenuPanel.cs` (esperado: **sem mudança**; nenhum arquivo novo em `Assets/`, então nenhum `.meta` novo)

Por que não precisa de Presentation: o título do modo recriar vai na linha de status (`StatusLine`), a marca da atual é texto no rótulo e o apagado usa o material de desabilitado que o painel já aplica por `IsEnabled`. O `Layout` do painel reage a página/contagem/orientação — iguais aos do "+ Monitor virtual".

- [ ] **Step 1: Recompilar e rodar EditMode**

```powershell
unity --proxy-disable --no-banner command recompile
unity --proxy-disable --no-banner command run_tests --mode EditMode
```

Expected: todos verdes (inclui os 6 testes novos do menu e o do `HostSync`). Sem MCP: `powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command test-editmode`.

- [ ] **Step 2: Conferir o painel no Play Mode** (Simulator, sem host): menu do pulso com build de desenvolvimento = **12 itens** (limite `MaxItems`); "Recriar ▸" aparece apagado. Checar se o glifo `▸` existe na fonte do painel (se aparecer quadrado, trocar por `›` em `WristMenu.RecreateLabel` e ajustar o teste). Checar que "Recriar: 2560×1440" cabe na linha de status e "3840×2160 · atual" cabe no botão (0,075 m).

- [ ] **Step 3: Merge com a 2E** — a 2E acrescenta "Curvatura ▸" à página principal: com o item de teste, a principal passa de 12 (`MaxItems`). Resolver no merge: ou subir `MaxItems`/o painel para 14 (o painel cria `MaxItems` botões), ou tirar o item de teste da principal. Decidir com o Cayo.

- [ ] **Step 4: Commit** (só se algo mudou)

```bash
git add Assets/_Project/Application/Menu/WristMenu.cs Assets/_Project/Tests/EditMode/Manipulation/WristMenuTests.cs
git commit -m "fix(menu): recreate label glyph for the panel font"
```

---

### Task 7: Meta XR Simulator + host real

**Files:**
- Create: `docs/measurements/fase2d-recriar.md`

- [ ] **Step 1: Preparar** — como na 2C (Task 14): host `dotnet run --project host/Host.App -- serve` como administrador (console visível); skills `meta-xr-operator-unity-cli-workflow` e `meta-xr-operator-hand-tracking`; Play Mode na cena `Main` com o Simulator (armadilhas em `docs/measurements/fase2b-simulator.md` §3).

- [ ] **Step 2: Roteiro** (✅/❌ + observação por linha; dados pelo `eval` no `WorkspaceStore` e pelo log do host)

| # | Ação | Esperado |
|---|---|---|
| 1 | Sem tela em foco / foco no espelho automático | "Recriar ▸" apagado |
| 2 | "+ Monitor virtual" → 16:9 → 2560×1440; abrir o Bloco de Notas e movê-lo para esse monitor (snippet da 2C, Task 14) | monitor 2560×1440 no Windows; janela nele |
| 3 | "Recriar ▸" | página de proporções, status "Recriar: 2560×1440", Paisagem |
| 4 | 16:9 | "2560×1440 · atual" apagado |
| 5 | "← Voltar" duas vezes | principal, nada enviado (log do host sem Acquire) |
| 6 | Recriar → 16:9 → 1920×1080 | log "recriado 2560×1440 → 1920×1080 (… 1 janela(s) movida(s))"; em Configurações de vídeo o monitor antigo some e o novo tem 1920×1080; o Bloco de Notas está no novo, mesma posição relativa; a tela no headset mantém lugar e largura |
| 7 | Desfazer (X) | log "recriado 1920×1080 → 2560×1440"; a janela volta |
| 8 | Maximizar o Bloco de Notas; recriar em Retrato 9:16 → 1440×2560 | janela maximizada no monitor em pé |
| 9 | Minimizar; recriar de novo; restaurar pela barra de tarefas | a janela restaura no monitor novo |
| 10 | Barra de tarefas no topo (se der) — repetir 9 | posição de restauração correta (valida o desvio 7) |

- [ ] **Step 3: Relatório e correções** — `docs/measurements/fase2d-recriar.md` (data, commit, tabela). Falha de lógica pura vira teste antes da correção; no máximo 3 tentativas por falha (skill `meta-xr-operator-unity-test-mechanics`).

- [ ] **Step 4: Commit**

```bash
git add docs/measurements/fase2d-recriar.md
git commit -m "docs: Phase 2D Simulator verification with the real host"
```

---

### Task 8: Quest e fechamento

**Files:**
- Modify: `docs/measurements/fase2d-recriar.md` (seção "Quest"), `docs/specs/2026-09-30-fase2d-recriar-design.md` (seção "6. Desvios na implementação"), `docs/STATUS.md`

Por que o Simulator não basta: o vídeo (decoder MediaCodec reaberto com a resolução nova, layer reconstruída) só é conclusivo no Quest.

- [ ] **Step 1: Build e instalação**

```powershell
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command boot-main
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command build-apk -TimeoutMinutes 40
powershell -ExecutionPolicy Bypass -File tools/quest.ps1 -Install Builds/Phase0.apk -Launch
```

- [ ] **Step 2: Roteiro no Quest (Cayo, ~5 min)**

| # | Ação | Anotar |
|---|---|---|
| 1 | Tela virtual com vídeo e uma janela; recriar em outra proporção | a janela vai junto; o vídeo reaparece (tempo até o 1º quadro) |
| 2 | Desfazer | a janela volta; vídeo volta |
| 3 | Recriar 3 vezes seguidas rápido | nenhuma tela presa em "Falha"; monitores órfãos em Configurações de vídeo? |

- [ ] **Step 3: Spec, STATUS e desvios** — na spec 2D, seção "6. Desvios na implementação" com os 7 itens de "Desvios e decisões" deste plano (e o que o Quest mudar). STATUS: linha 2D → ✅ com links para a spec, este plano, o relatório e o ADR 0012.

- [ ] **Step 4: Commit**

```bash
git add docs/measurements/fase2d-recriar.md docs/specs/2026-09-30-fase2d-recriar-design.md docs/STATUS.md
git commit -m "docs: Phase 2D done (Quest check, spec deviations, STATUS)"
```
