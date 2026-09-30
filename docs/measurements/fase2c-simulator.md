# Fase 2C — verificação (em andamento)

**Branch:** `phase2c/multi-screen` · **Plano:** [docs/plans/2026-09-30-fase2c-varias-telas.md](../plans/2026-09-30-fase2c-varias-telas.md) ·
**Spec:** [docs/specs/2026-09-30-fase2c-varias-telas-design.md](../specs/2026-09-30-fase2c-varias-telas-design.md) · ADR 0009

## 1. Testes automáticos (30/09/2026, depois da revisão final)
Harness `dotnet test` (Domain + Application + codec): **208**. Host (`Host.Tests`): **66**. Unity Test Runner (EditMode):
**209**, 0 falhas. Build do host: 0 avisos, 0 erros.

## 2. Meta XR Simulator + host real (30/09/2026)
Host `serve` como administrador (SudoVDA), Editor em Play Mode com o Simulator v207, entrada pelo Meta XR Operator (mesma
preparação da 2B). O Editor conecta em 127.0.0.1; o vídeo é no-op no Editor, então as telas ficam em "Conectando vídeo…".

| # | Ação | Resultado |
|---|---|---|
| 1 | Conectar com a workspace vazia | ✅ espelho automático do monitor principal ("DISPLAY1", 2560×1080) criado e pedido ao host |
| 2 | Menu → "+ Monitor virtual" | ✅ "Orientação: Paisagem" + 6 frames na proporção com o nome no meio + "← Voltar" |
| 3 | "Orientação" | ✅ frames em pé: 9:16, 10:16, 9:21, 9:32, 1:1, 3:4 |
| 4 | Paisagem → 16:9 → 2560×1440 | ✅ monitor `\\.\DISPLAY30` 2560×1440 criado no Windows; tela nova no headset |
| 5 | Retrato → 16:9 → 1440×2560 | ✅ monitor `\\.\DISPLAY31` 1440×2560 (em pé, sem rotação do Windows) |
| 6 | "+ Espelhar monitor" | ✅ só monitores que não são do app (os virtuais do app ficam fora) |
| 7 | Criar a 5ª tela com vídeo | ✅ aviso "Mais de 4 telas: pode haver quedas e travamentos"; a tela é criada. ⚠️ ver §3 (falha de pipeline por falta de memória) |
| 8–12 | Remover/undo com janela, expiração, reconexão, watchdog | ⏸️ não executados: o PC não aguenta uma sessão do Simulator (§3) → vão para o Quest (Task 15) |

## 3. Achados
- **Meta XR Simulator vaza memória:** o `synth_env_server.exe` (ambiente sintético) cresceu até 13,8 GB (13:22), 21,9 GB
  (00:55) e 14,5 GB (14:03). Somado à Unity (~11 GB) e ao `MetaXRSimulator.exe` (~4,5 GB), esgota os 16 GB de RAM
  (Windows: "memória virtual insuficiente", eventos 2004). Só entrar em Play Mode leva o commit de 34,6 GB a 38,4 GB
  (limite 41,4 GB). Não é do código do app.
- **Consequências vistas:** (1) o dispositivo D3D do host foi removido (`DXGI_ERROR_DEVICE_REMOVED`), e todo pipeline
  novo falhou até reiniciar o host — as telas 4 e 5 ficaram em "Falha ao iniciar o vídeo" (o headset tratou certo: 3
  tentativas com espera e estado de falha visível); (2) o driver do SudoVDA (UMDF) travou (erro 43) 8 s depois do
  esgotamento, e o host respondeu "SudoVDA não disponível" (tratado certo no headset); (3) Unity e host caíram.
- **Lacuna do host (backlog):** o host não se recupera de `DEVICE_REMOVED` sem reiniciar. Isso também acontece em
  atualização de driver ou TDR. Proposta: detectar a remoção, recriar o `GpuDevice` e os pipelines, e pedir IDR.
  Diagnóstico já no código: em falha de pipeline o host registra `DeviceRemovedReason` e a carga de memória.
- **Ambiente:** o driver VDD que sobrou do spike da 2A (monitor 800×600, `ROOT\DISPLAY\0003`) foi removido.

## 4. Quest (30/09/2026) — bug 2: tela preta depois de reorganizar os monitores no Windows
- **Sintoma:** mover um monitor virtual para cima, para baixo ou para a esquerda do monitor 1 e clicar em Aplicar deixava a
  tela preta no headset. Nas laterais a imagem voltava. O host seguia capturando e codificando a 60 fps.
- **Causa 1:** a sessão WGC fica presa à área do desktop onde o monitor estava ao criar a captura (o HMONITOR não
  muda). Quando o monitor sai dessa área, os quadros vêm pretos. O mesmo deslocamento explica a imagem "puxada para a
  esquerda" vista antes ao remover outro monitor.
  Correção: o host compara a posição de cada tela com stream a cada 1 s e recria a captura quando ela muda
  (`CaptureRebind`, `ScreenHost.FollowDisplayChanges`).
- **Causa 2 (achada na correção):** o pipeline recriado recomeçava os frameIds em 0, e o receptor do headset descartava
  os quadros como atrasados (imagem congelada). Correção: o pipeline novo continua a numeração do anterior.
- ✅ Verificado no Quest: acima, abaixo e à esquerda, a tela volta em ~1 s.
