# ADR 0004 — Workspace imutável com undo por snapshots; testes NUnit compartilhados Unity ↔ dotnet

- **Status:** Aceito (Fase 1A, 28/09/2026)
- **Contexto:** o brief (§8.2) pede Commands, fonte única da verdade, undo/redo e perfis como Memento.
  O MCP da Unity trava com diálogos modais, então depender só do Test Runner do Editor deixa o TDD lento e frágil.
- **Decisão:**
  - `Workspace` e `VirtualScreen` são imutáveis; cada `ICommand` é uma função pura `Workspace → Workspace`.
    O `WorkspaceStore` guarda snapshots (até 100) para undo/redo; `SetFocus` não entra no histórico.
  - Views observam o store via `ScreenViewSynchronizer` (Application) usando as portas `IScreenView`/`IScreenViewFactory`;
    "mudou" = outra instância de `VirtualScreen` (comparação por referência, sem diff campo a campo).
  - Domain e Application têm `noEngineReferences: true`. Os testes deles usam só NUnit e são compilados
    **também** por `tests-dotnet/UnityPureCode.Tests` (`dotnet test`, `LangVersion 9.0`), além do Test Runner
    da Unity (`tools/unity-cmd.ps1 -Command test-editmode`). Resultado na Fase 1A: 75/75 nos dois.
  - A camada Application usa o asmdef `ScreenPortal.Application`, mas o namespace **`ScreenPortal.App`**:
    `ScreenPortal.Application` sombrearia `UnityEngine.Application` em todo código `ScreenPortal.*`.
- **Consequências:**
  - ✅ Loop de TDD em segundos, sem Editor; o compilador do harness barra UnityEngine e recursos acima do C# 9.
  - ✅ Undo/redo e perfis triviais (snapshots); fácil serializar o estado para o host (Fase 1B).
  - ⚠️ Cada comando aloca (novos arrays/objetos). Aceitável: comandos vêm de ações do usuário, não do caminho
    por frame. Arrastes contínuos usam preview local e geram UM comando ao soltar (brief §10.10).
  - ⚠️ Os testes precisam ficar restritos a NUnit 3.5 (a versão da Unity): usar `Assert.That` com constraints.
  - ⚠️ Código de Presentation/Composition não passa pelo harness dotnet: erros ali (ex.: `Resolution` ambíguo com
    `UnityEngine.Resolution`, referência de asmdef faltando) só aparecem ao compilar na Unity. E se o assembly
    `ScreenPortal.Editor` não compilar, o gatilho por arquivo fica preso na versão anterior — nesse caso é
    preciso focar a janela da Unity para o auto-refresh.
