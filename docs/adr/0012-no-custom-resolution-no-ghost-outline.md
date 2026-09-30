# ADR 0012 — Sem resolução personalizada, sem contorno fantasma, recriar sem confirmação

- **Status:** Aceito (Fase 2D, 30/09/2026, decisão do Cayo no brainstorming)
- **Spec:** docs/specs/2026-09-30-fase2d-recriar-design.md · **Brief:** v1.3.3 (§10.3, §10.4)

## Contexto
A 2D herdou três peças do brief §10.3–10.4: contorno fantasma para posicionar antes de criar, resolução
personalizada (campos de largura e altura, favoritos) e "Recriar com outra resolução" com confirmação. A 2C mudou o
cenário: a tela já nasce à frente da visão, e mover, redimensionar e remover têm undo.

## Decisão
1. **Sem contorno fantasma:** a tela é criada direto, à frente da visão (spec 2C §3), e ajustada depois.
2. **Sem resolução personalizada** nem favoritos de resolução: o seletor fica com as proporções e resoluções do
   catálogo (2C). O Cayo não quer a feature.
3. **Recriar sem confirmação:** `RecreateScreen` é desfazível; o undo recria na resolução anterior e leva as janelas
   de volta. Segue o padrão do app: nada pede confirmação, tudo desfaz.

## Consequências
- A 2D encolhe para "Recriar com outra resolução".
- Sem entrada de texto no headset nesta fase (o teclado físico chega na Fase 3).
- Se a resolução personalizada voltar a ser pedida, entra pelo backlog (brief §4.3) com novo ADR.
