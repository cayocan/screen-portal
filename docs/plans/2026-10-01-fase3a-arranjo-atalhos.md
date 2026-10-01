# Fase 3A — Plano

> Execução inline, TDD onde é puro, um commit por tarefa. Spec: docs/specs/2026-10-01-fase3a-arranjo-atalhos-design.md.

1. **Protocolo:** `LayoutChanged`/`ScreenAngles` (headset → host), `Hotkey` (host → headset); mensagens, codec, teste.
2. **`DisplayArrangement`** (Host.Domain) com a bateria de testes da spec §5.
3. **Host aplica o arranjo:** `TopologySync` (chave → monitor via `ScreenHost`, retângulos atuais, plano, um
   `SetDisplayConfig` com todas as posições — `DisplayConfigPosition.SetPositions`), `HeadsetSession` trata `LayoutChanged`.
4. **Headset `LayoutSync`** (Application): ângulos das telas com vídeo na superfície, debounce 0,5 s, toggle, Welcome.
5. **Escala:** `DisplayScale` (feito no spike) + `ScaleKeeper` + aplicar ao criar/recriar.
6. **Atalhos:** host `HotkeyService` (thread com message loop, `RegisterHotKey`, `hotkeys.json`, tela sob o cursor) +
   envio pela sessão atual; headset `HotkeyDispatcher` → Commands.
7. **Menu** "Arranjo automático" + composição; Unity; APK; Quest; relatório, STATUS, merge.
