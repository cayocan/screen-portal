# ADR 0003 — Zona de passthrough sempre definida pelo usuário (sem detecção de teclado)

- **Status:** Aceito (28/09/2026, decisão do Cayo)
- **Substitui:** a decisão D7 original ("MRUK Keyboard Tracking + Passthrough Windows") e o modelo
  "Sugestão ⊕ Ajuste" do brief §9.

## Contexto
O pilar B previa que o app detectasse o teclado com o Keyboard Tracking genérico do MRUK e sugerisse a
zona. Na Fase 0 (Quest 3, Horizon OS v83, MRUK 207), com o teclado do Cayo (TKL, sem teclado numérico):
- as extensões `XR_META_dynamic_object_tracker` e `XR_META_dynamic_object_keyboard` carregaram e o app
  pediu o tracking corretamente;
- o serviço do sistema viu um objeto candidato, mas registrou continuamente
  `Object in query has empty predictions or tracking flags` e **nunca reconheceu o teclado**.
A Meta não publica quais teclados o modelo reconhece. Um recurso central do produto não pode depender disso.

## Decisão
- A zona é **sempre desenhada pelo usuário**: pelo mouse (retângulo padrão + arrastar/roda, caminho
  principal) ou por 2 cantos tocados com controle/mãos. Detalhes no brief §9.2.
- A zona é salva **ancorada na mesa** (spatial anchor) e no perfil, e **restaurada automaticamente**;
  o usuário só redesenha em mesa nova ou se a âncora não localizar.
- Sai do MVP: detecção de teclado, "seguir o teclado", margens calculadas, congelar/recuperar tracking,
  "recalcular sugestão". Também sai a sugestão pelo plano da mesa do Space Setup (decisão: nada automático).

## Consequências
- ✅ Funciona com qualquer teclado, mouse, mesa e iluminação. Menos código e menos casos de borda.
- ✅ Critério de aceite novo (brief §4.2): criar a zona em < 60 s só com o mouse (< 30 s com controle) e
  restauração sem reconfigurar em ≥ 95 % das aberturas na mesma mesa.
- ⚠️ Primeira experiência tem um passo manual a mais (mitigado: fluxo rápido, dá para pular).
- ⚠️ Se o teclado mudar de lugar na mesa, a zona não acompanha: o usuário arrasta (segundos).
- A permissão de Spatial Data/Scene e o `sceneSupport = Required` deixam de ser necessários para o
  produto; o MRUK vira candidato a remoção na Fase 4 (manter enquanto a cena de spike existir).
  Spatial anchors usam `OVRSpatialAnchor` do Core SDK (exige só Anchor Support).
- A cena `Phase0_Passthrough` continua útil para validar o shader de Passthrough Window com feather e a
  oclusão overlay × janela; isso é feito na Fase 4a com a zona manual.
