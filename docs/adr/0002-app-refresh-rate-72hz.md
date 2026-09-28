# ADR 0002 — Taxa de atualização do app: 72 Hz

- **Status:** Aceito (28/09/2026, decisão do Cayo)
- **Contexto:** o brief v1.3 pedia 90 Hz estáveis (§4.2, §11), com 72 Hz como mínimo aceitável.
  Na Fase 0, o app rodou a 72 Hz (padrão do Quest 3) cravados com 1–6 telas, inclusive com o
  decoder saturado (docs/measurements/fase0.md). O vídeo das telas é de no máximo 60 fps, e as telas
  são compositor layers, que o compositor reprojeta na taxa do display independentemente do app.
- **Decisão:** o alvo do app passa a ser **72 Hz estáveis**. Não configurar 90 Hz no MVP.
- **Consequências:**
  - Menos consumo de GPU/CPU e bateria, mais folga térmica para sessões longas (critério de 4 h).
  - O conteúdo das telas (≤ 60 fps) não ganha nada com 90 Hz. O que ganharia é a fluidez da cena e
    do cursor 3D desenhado localmente; reavaliar se a movimentação do cursor parecer "travada" na Fase 3.
  - Brief atualizado: §4.2 e §11 passam a dizer 72 Hz.
  - Reversível: trocar `OVRPlugin.systemDisplayFrequency` para 90 e revalidar o orçamento de frame.
