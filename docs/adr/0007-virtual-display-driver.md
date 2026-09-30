# ADR 0007 — Driver de monitor virtual: SudoVDA no lugar do VDD

- **Status:** Aceito (Fase 2A, 28/09/2026, aprovado pelo Cayo) — revisa a decisão **D3** do brief
- **Spec:** docs/specs/2026-09-28-fase2a-spikes-design.md · **Relatórios:**
  [SudoVDA](../measurements/fase2a-driver-sudovda.md) · [VDD](../measurements/fase2a-driver-vdd.md)

## Contexto
A D3 escolheu o **Virtual Display Driver** (VirtualDrivers/MikeTheTech, MIT, assinado) para os monitores virtuais.
O spike da 2A rodou os mesmos 6 testes automáticos (`ScreenPortalHost display-spike --driver <x>`) com o VDD e com o
**SudoVDA** (SudoMaker; reescrita do VDD usada pelo Apollo), no PC de desenvolvimento (Windows 10 19045, RX 5500 XT).

## Resultado
| Teste | SudoVDA | VDD 25.7.26 |
|---|---|---|
| 1. Resolução exata (2560×1440, 3440×1440, 1920×1080) | ✅ exata na criação; cria 0,25–0,6 s, remove ~0,2 s | ❌ resolução só via lista global no XML + troca de modo; o driver caiu |
| 2. Monitores simultâneos | ✅ 8 (limite do teste) | ❌ driver caiu ao ir de 3 para 4 |
| 3. Retrato 1440×2560 | ✅ WGC + H.264 em 1440×2560 (ffprobe) | ❌ driver offline |
| 4. Recriar movendo janelas | ✅ janela no monitor novo, mesma posição relativa | N/A: só remove o último monitor (`SETDISPLAYCOUNT`) |
| 5. Monitor-palco com recorte | ✅ recorte acompanha o redimensionamento | ❌ driver offline |
| 6. Limite do H.264 da GPU | ✅ até 4096 de largura | (mesmo resultado: é da GPU) |

**VDD:** durante a sequência `SETDISPLAYCOUNT n` + `RELOAD_DRIVER` pelo pipe, o processo do driver (WUDFHost) foi
encerrado 6 vezes em ~6 s (eventos 10110/10111 da DriverFrameworks-UserMode) e o Windows desistiu de reiniciá-lo
(`CM_PROB_FAILED_POST_START`). Mesmo sem as quedas, o modelo do VDD não atende o brief: a resolução não é por monitor
(lista global + `ChangeDisplaySettingsEx`, ou seja, troca de modo de monitor vivo — o que §10.1 proíbe), não há remoção
de um monitor específico, e cada mudança recarrega o driver inteiro (todos os monitores piscam).

**SudoVDA:** API por IOCTL feita para esse uso — cada monitor é criado com largura/altura/refresh exatos e um GUID, e
removido pelo GUID, sem afetar os outros. Mais de 30 ciclos de criar/remover no spike sem nenhuma falha.

## Decisão
Usar o **SudoVDA** para os monitores virtuais (substitui a D3). O VDD sai do projeto.

## Consequências
- **Licença:** MIT e CC0/domínio público (README do SudoVDA) — sem restrição de redistribuição.
- **Watchdog:** sem `PING` periódico o SudoVDA remove os monitores (timeout de 3 s medido). O host pinga a cada 1 s
  enquanto existir. Efeito colateral bom: se o host morrer, os monitores somem sozinhos.
- **Assentamento:** logo após criar um monitor, o WGC recusa o HMONITOR (E_INVALIDARG) e o item de janela nasce 0×0 por
  ~2 s. A 2C precisa da mesma espera com nova tentativa usada no spike, e deve resolver o HMONITOR na hora de capturar
  (o handle muda a cada mudança de topologia).
- **Janelas entre monitores de DPI diferente:** `SetWindowPos` duas vezes (a primeira dispara `WM_DPICHANGED`).
- **Limite do encoder:** o H.264 da RX 5500 XT não abre acima de 4096 de largura; o seletor de resolução (2D) não
  oferece nada acima disso sem checar o encoder da máquina.
- **Pendências para o instalador (fase de distribuição):** confirmar a assinatura do build do SudoVDA que vamos
  distribuir (o protocolo reporta "test build" neste PC, que veio de outra instalação), e o fluxo de instalação
  (`nefconc`/`devcon` com o hardware id `root\sudomaker\sudovda`).
- **Brief:** D3 e a linha de assinatura do driver atualizadas para apontar para este ADR. O VDD foi desinstalado do PC
  de desenvolvimento (`tools/install-vdd.ps1 -Uninstall`); o script fica só como registro do spike.
