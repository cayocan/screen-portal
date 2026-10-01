# Fase 2F — Frame rate por tela + orçamento de banda — Design

**Data:** 30/09/2026 · **Aprovado por:** Cayo (brainstorming de 30/09/2026)
**Depende de:** 2A ([ADR 0008](../adr/0008-decode-capacity.md): capacidade do decoder), 2C (N streams, `HostSync`).
**Brief:** §8.8 (frame rate por tela: Strategy + Chain of Responsibility), roadmap da Fase 2 ("frame rate manual por
tela … com o `BandwidthBudget` básico"). Detector de movimento, política de foco completa e bitrate adaptativo à rede
ficam na Fase 5, como o brief prevê.

## 1. Decisões

| # | Decisão |
|---|---|
| 1 | Modos por tela: **Auto** (padrão), **Vídeo**, **Fixo 30**, **Fixo 15**, **Eco**. Sem 90 fps (app a 72 Hz, captura do Windows até 60) |
| 2 | **Foco = para onde o usuário olha** até a Fase 3 (lá o cursor vira o sinal principal e o olhar fica de reserva) |
| 3 | **O modo manual sempre manda** na tela dele: olhar para uma tela em Eco ou Fixo não a acelera. O olhar só decide nas telas em Auto |
| 4 | Bitrate proporcional ao fps (mesma qualidade por quadro): 20 Mbps a 60 fps; Vídeo = 30 Mbps; mínimo 2 Mbps |
| 5 | Orçamento com os limites medidos no ADR 0008; corta na ordem do brief e **nunca rebaixa a tela em foco**; configuração manual que não cabe → aviso no menu, nada é rebaixado em silêncio |
| 6 | O host muda fps e bitrate **ao vivo**, sem recriar o pipeline nem pedir IDR |

## 2. Resolução do fps de cada tela (Application, puro)

Chain of Responsibility, a primeira regra que decide vence:
1. **Manual** (Vídeo → 60 · Fixo 30 → 30 · Fixo 15 → 15 · Eco → 5).
2. **Foco pelo olhar** (só Auto): a tela em foco → 60.
3. **Padrão** (Auto fora do foco) → 15.

Bitrate pedido = `round(20 000 kbps × fps / 60)`, com mínimo de 2 000 kbps; Vídeo = 30 000 kbps.

**`GazeFocus`:** a cada quadro, a tela com vídeo mais perto do centro da visão (ângulo entre a direção da cabeça e o
centro da tela, usando a pose de cada tela), dentro de **25°**. Histerese: uma tela vira foco depois de **0,3 s** sendo
a mais perto; perde o foco **1 s** depois de deixar de ser. Sem alocação por quadro.

## 3. Orçamento (`BandwidthBudget`, puro)

Entrada: o fps pedido de cada tela com vídeo e qual está em foco. Saída: o fps concedido de cada uma e um aviso.
- Limites (ADR 0008): no máximo **4 telas a 60 fps** e no máximo **6 telas** no total (a 7ª em diante fica a 5 fps).
- Para caber, rebaixa nesta ordem: **Eco → Auto fora do foco → Fixo → Vídeo**; dentro de cada grupo, a mais recente
  primeiro. Degraus: 60 → 30 → 15 → 5. A tela em foco nunca é rebaixada.
- Se o que estourar for só de modos manuais (o usuário fixou mais do que cabe), o menu mostra "Essa configuração pode
  causar travadas" e as telas são rebaixadas pela mesma ordem (o aviso explica o porquê).

## 4. Protocolo e sincronização

- Mensagem nova no `control.fbs` (protocolo continua v2, campo novo no union): `SetStreamRate { screen_key:uint; fps:ubyte; kbps:uint; }`.
- O `HostSync` guarda o último (fps, kbps) enviado por tela e só manda quando muda (e de novo depois de um
  `StreamStarted`, porque o pipeline novo começa no padrão).
- Host: `ScreenHost.SetRate(key, fps, kbps)` → `StreamPipeline.SetRate` troca o intervalo mínimo entre quadros
  (limitador da captura) e chama `ICodecAPI.SetValue(AVEncCommonMeanBitRate)` ao vivo. Valores fora de faixa são
  limitados (fps 1–60, kbps 1 000–50 000). O IDR periódico de 5 s segue igual.

## 5. Interface

- Menu do pulso: item **"Taxa de quadros ▸"** (apagado sem tela em foco do menu ou em tela de teste) → página com os 5
  modos para a tela em foco do menu, o atual marcado (" · atual"). Escolher envia o Command `SetFrameRateMode` (já
  existe desde a 1A) — entra no undo e na última sessão.
- Linha de status do menu: "Auto · 60 fps" (modo + fps recebido de verdade, das estatísticas do decoder) para a tela
  em foco do menu; com aviso de orçamento, a linha mostra o aviso.

## 6. Wi-Fi

Medição antes/depois no relatório: 5 telas em Auto olhando uma de cada vez — banda total, perdas de rede por minuto e
quedas de fps no decoder (`SPSTAT`), contra o mesmo cenário a 60 fps fixos de hoje.

## 7. Testes e verificação

**Automáticos:** resolução do fps (manual vence, foco só em Auto, padrão); bitrate por fps; `GazeFocus` (ângulo,
alcance de 25°, histerese 0,3 s / 1 s, telas sem vídeo fora); `BandwidthBudget` (limite de 4×60 e 6 telas, ordem de
corte, foco intocável, aviso); `HostSync` manda `SetStreamRate` só na mudança e depois de `StreamStarted`; codec do
protocolo ida e volta; host: `SetRate` limita valores e muda o intervalo do limitador (xUnit).

**Quest:** olhar de uma tela para outra (a nova fica fluida, a antiga cai para 15); modos pelo menu; Vídeo continua a
60 fora do foco; Eco continua a 5 olhando; aviso com 5 telas em Vídeo; medição de banda/perdas do §6.

**Documentação:** relatório `docs/measurements/fase2f-fps-orcamento.md`; STATUS.
