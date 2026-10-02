# PassthroughZone
A "janela" para o mundo real sobre a mesa: um recorte onde o passthrough fica limpo para ver teclado, mouse e mesa, mesmo com o resto do mundo escurecido.

**O consumidor fornece:** o retângulo da zona no plano da mesa (preso à âncora espacial da mesa), o formato, a borda suave, o escurecimento do mundo e se está em edição.

**Como se encaixa no resto do app**
- O mundo tem três níveis em Ajustes → Mundo: **Claro** (passthrough puro, como hoje; a zona não muda nada), **Escurecido** (`world-dim` sobre o passthrough fora da zona) e **Escuro** (só telas e zona). A zona só faz efeito a partir de Escurecido.
- A zona fica no plano da mesa, abaixo das telas: nunca cobre uma tela.
- Editar a zona faz parte do modo edição, com as mesmas alças e cores do `ScreenFrame` (seleção `portal`, parte em uso `edit`, `Shift` solta o ímã) e o mesmo desfazer.

**Criar**
- Mouse: menu → Novo → "+ Zona da mesa" → o cursor vira mira sobre a mesa; clique e arraste o retângulo; a roda ajusta a altura do plano. `Esc` cancela.
- Mãos: pinçar dois cantos opostos sobre a mesa.
- Sem mesa detectada: a zona nasce 40 cm à frente e 70 cm abaixo dos olhos, com o toast "Ajuste a altura com a roda".

**Editar**
- Alças de canto (`handle-corner`) redimensionam; alças de borda (pílulas no meio da borda de cima e da direita) mudam uma dimensão só. Arrastar o interior move.
- Rótulo acima, igual ao das telas: "Zona da mesa" + medidas em `caption` ("90 × 45 cm").
- Formato, borda suave, escurecimento e altura ficam em `ZoneSettings`.

**Regras**
- Borda suave padrão de 4 cm: o véu esmaece até a zona em vez de cortar seco (corte duro cansa a vista em sessões longas).
- **Revelar ao olhar para baixo** (padrão ligado): com o mundo Escuro, a zona aparece quando a cabeça inclina 25° para baixo, em 180 ms, e some ao olhar para frente.
- Âncora perdida: a zona some e aparece o aviso de `ZoneSettings`; nunca deixe a zona no lugar errado.
