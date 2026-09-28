# Screen Portal — regras do projeto

Fonte da verdade do produto: [docs/brief.md](docs/brief.md) (v1.3, escopo do MVP fechado em 27/09/2026).
Mudança de escopo só com: (1) atualização do brief, (2) ADR em `docs/adr/`. Ideias novas → backlog (brief §4.3).
Fase atual: **Fase 0 — spike de viabilidade** (brief §14 e §17). Estado em [docs/STATUS.md](docs/STATUS.md).

## Layout do repositório (desvio do brief registrado em docs/adr/0000)
O projeto Unity fica **na raiz** do repositório (não em `headset-unity/`). Onde o brief diz
`headset-unity/Assets/_Project/...`, leia `Assets/_Project/...`.

```
/                       projeto Unity 6.3 LTS (Assets/, Packages/, ProjectSettings/)
├── Assets/_Project/    código do app (Domain, Application, Presentation, Infrastructure, Composition, Spikes, Tests)
├── docs/               brief, ADRs (docs/adr), medições (docs/measurements)
├── protocol/           schemas .fbs + especificação do wire format do spike
├── host/               PC companion (.NET 10) — Host.sln
├── headset-native/     Android library (.aar): Kotlin + C++ NDK
├── driver/             integração com o Virtual Display Driver (Fase 2)
└── tools/              scripts de build/deploy/medição
```

## Regras (brief §13)
- **Domain não referencia UnityEngine**, nem APIs Windows/Android. Asmdefs unidirecionais `Presentation/Infrastructure → Application → Domain`.
- Toda mudança de estado da workspace é um **Command** via `CommandBus`. Views nunca mutam estado.
- **Zero alocação** em `Update`/`LateUpdate` e no caminho de vídeo: sem LINQ, closures, `string` concatenada ou `new` em loop. Pools, `Span`, `NativeArray`.
- **O frame de vídeo nunca atravessa o C#** no headset. Decode → Surface da compositor layer, sempre nativo.
- Sem singletons nem `FindObjectOfType`. DI via VContainer (a partir da Fase 1).
- Todo código de domínio vem com teste (EditMode NUnit no Unity, xUnit no host).
- Mensagens de rede só via schemas em `/protocol`. Nada de JSON ad hoc no caminho quente.
- Toda decisão estrutural nova → ADR em `docs/adr/NNNN-titulo.md`.
- `SpatialMap` (cursor espacial) é código puro em `Host.Domain`. Todo bug de cursor vira caso de teste antes da correção.
- O callback do hook de mouse nunca faz trabalho pesado.
- **`ResizeScreen` nunca altera a resolução.** O host nunca troca o modo de um monitor virtual vivo; só `RecreateScreen`.
- **Mouse-first:** toda ação executável só com mouse + teclado. Mãos são um adapter a mais.
- Cliques de manipulação nunca vazam pro Windows, e cliques normais nunca são engolidos.
- **Escopo:** nada fora dos três pilares do MVP antes da v1.0.
- **Pacotes Unity:** Meta XR (core, interaction.ovr, mrutilitykit — mesma versão exata) + `com.unity.ai.assistant` + `com.unity.pipeline`. Nunca adicionar XR Interaction Toolkit nem templates VR/MR da Unity.
- **Unity pelo terminal:** preferir Unity MCP / `unity` CLI com o Editor aberto. Fallback: `Unity.exe -batchmode -quit -projectPath . -runTests` / `-executeMethod` (só com o Editor fechado). **Nunca** `unity command eval` na `main`.
- Commits pequenos, um propósito cada. PR descreve impacto em latência/performance quando tocar o pipeline.

## Toolchain desta máquina
| Ferramenta | Caminho |
|---|---|
| Unity Editor 6000.3.19f1 | `D:\Unity Editor\6000.3.19f1\Editor\Unity.exe` |
| Unity CLI | `unity` (1.0.0-beta.8) |
| Android SDK / NDK r27c / JDK | `D:\Unity Editor\6000.3.19f1\Editor\Data\PlaybackEngines\AndroidPlayer\{SDK,NDK,OpenJDK}` |
| adb | `...\AndroidPlayer\SDK\platform-tools\adb.exe` |
| .NET | SDK 10 (host usa `net10.0-windows10.0.22621.0`) |
| ffmpeg/ffprobe | chocolatey (só para verificação de streams no PC) |
| GPU do PC de dev | AMD Radeon RX 5500 XT → encoder **AMF** (H.264 via MFT de hardware) |

Comandos: `tools/build-host.ps1`, `tools/build-native.ps1`, `tools/receive-test.ps1` (ver cabeçalho de cada um).

---

# context-mode — MANDATORY routing rules

You have context-mode MCP tools available. These rules are NOT optional — they protect your context window from flooding. A single unrouted command can dump 56 KB into context and waste the entire session.

## BLOCKED commands — do NOT attempt these

### curl / wget — BLOCKED
Any Bash command containing `curl` or `wget` is intercepted and replaced with an error message. Do NOT retry.
Instead use:
- `ctx_fetch_and_index(url, source)` to fetch and index web pages
- `ctx_execute(language: "javascript", code: "const r = await fetch(...)")` to run HTTP calls in sandbox

### Inline HTTP — BLOCKED
Any Bash command containing `fetch('http`, `requests.get(`, `requests.post(`, `http.get(`, or `http.request(` is intercepted and replaced with an error message. Do NOT retry with Bash.
Instead use:
- `ctx_execute(language, code)` to run HTTP calls in sandbox — only stdout enters context

### WebFetch — BLOCKED
WebFetch calls are denied entirely. The URL is extracted and you are told to use `ctx_fetch_and_index` instead.
Instead use:
- `ctx_fetch_and_index(url, source)` then `ctx_search(queries)` to query the indexed content

## REDIRECTED tools — use sandbox equivalents

### Bash (>20 lines output)
Bash is ONLY for: `git`, `mkdir`, `rm`, `mv`, `cd`, `ls`, `npm install`, `pip install`, and other short-output commands.
For everything else, use:
- `ctx_batch_execute(commands, queries)` — run multiple commands + search in ONE call
- `ctx_execute(language: "shell", code: "...")` — run in sandbox, only stdout enters context

### Read (for analysis)
If you are reading a file to **Edit** it → Read is correct (Edit needs content in context).
If you are reading to **analyze, explore, or summarize** → use `ctx_execute_file(path, language, code)` instead. Only your printed summary enters context. The raw file content stays in the sandbox.

### Grep (large results)
Grep results can flood context. Use `ctx_execute(language: "shell", code: "grep ...")` to run searches in sandbox. Only your printed summary enters context.

## Tool selection hierarchy

1. **GATHER**: `ctx_batch_execute(commands, queries)` — Primary tool. Runs all commands, auto-indexes output, returns search results. ONE call replaces 30+ individual calls.
2. **FOLLOW-UP**: `ctx_search(queries: ["q1", "q2", ...])` — Query indexed content. Pass ALL questions as array in ONE call.
3. **PROCESSING**: `ctx_execute(language, code)` | `ctx_execute_file(path, language, code)` — Sandbox execution. Only stdout enters context.
4. **WEB**: `ctx_fetch_and_index(url, source)` then `ctx_search(queries)` — Fetch, chunk, index, query. Raw HTML never enters context.
5. **INDEX**: `ctx_index(content, source)` — Store content in FTS5 knowledge base for later search.

## Subagent routing

When spawning subagents (Agent/Task tool), the routing block is automatically injected into their prompt. Bash-type subagents are upgraded to general-purpose so they have access to MCP tools. You do NOT need to manually instruct subagents about context-mode.

## Output constraints

- Keep responses under 500 words.
- Write artifacts (code, configs, PRDs) to FILES — never return them as inline text. Return only: file path + 1-line description.
- When indexing content, use descriptive source labels so others can `ctx_search(source: "label")` later.

## ctx commands

| Command | Action |
|---------|--------|
| `ctx stats` | Call the `ctx_stats` MCP tool and display the full output verbatim |
| `ctx doctor` | Call the `ctx_doctor` MCP tool, run the returned shell command, display as checklist |
| `ctx upgrade` | Call the `ctx_upgrade` MCP tool, run the returned shell command, display as checklist |
