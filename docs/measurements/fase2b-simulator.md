# Fase 2B — verificação (em andamento)

**Branch:** `phase2b/manipulation` · **Plano:** [docs/plans/2026-09-29-fase2b-manipulacao.md](../plans/2026-09-29-fase2b-manipulacao.md)

## 1. Testes automáticos (30/09/2026)
- Harness `dotnet test`: **168** (Domain + Application, incluindo 9 invariantes da superfície, encaixe, acerto do raio,
  controlador de manipulação, menu e roteador). Host: **40**. Unity Test Runner (EditMode + composição): **169**, 0 falhas.

## 2. Play Mode no Editor, sem XR (30/09/2026)
Via `unity` CLI (`eval` + captura da câmera da cabeça). Sem Simulator: não há controle nem mão, então só o caminho
menu → Command → view foi exercitado.

| Verificação | Resultado |
|---|---|
| Composição sobe em Play Mode (VContainer, OVR adapters, menu, apresentador) | ✅ sem erros no console |
| "+ Tela de teste" ×3 | ✅ Teste 1/2/3 a az 0°, 43,2°, 78,4° (el −5°, 1,3 m), sem sobreposição; status "Cilindro · 3 telas" |
| Views de teste renderizam (textura, borda, rótulo legível, orientada para o eixo) | ✅ captura da CenterEyeAnchor |
| Cilindro → Esfera | ✅ posição mantida (0; 1,087; 1,295), tela inclina 5° para o centro |
| Desfazer | ✅ volta ao Cilindro |

## 3. Meta XR Simulator (pendente)
O projeto não tem o **Meta XR Simulator** nem o servidor MCP do **Meta XR Operator** instalados (instalação pela janela
*AI Tools Setup* do Meta XR Core SDK). Sem eles, o roteiro da Task 13 (controle/mãos, moldura, alças, encaixe, solto,
cancelar, menu de pulso, calibração da palma) não foi executado.
