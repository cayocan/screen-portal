# ADR 0006 — Canal de controle: FlatBuffers com código gerado commitado, mDNS nativo, pareamento sem criptografia

- **Status:** Aceito (Fase 1B, 28/09/2026)
- **Spec:** docs/specs/2026-09-28-fase1b-conexao-design.md · **Protocolo:** protocol/control-v1.md ·
  **Verificação:** docs/measurements/fase1b-conexao.md

## Decisões
- **FlatBuffers 25.2.10:** `flatc` fixo em `tools/flatc/`, C# gerado **commitado** (build sem flatc); runtime via NuGet no
  host e `Google.FlatBuffers.dll` (netstandard2.1) em `Assets/_Project/ThirdParty` na Unity. Versões do flatc e do runtime
  mudam juntas. `file_identifier "SPCT"` no schema: rejeita lixo cedo e contorna um bug do C# 25.2.10 (o `VerifyX`
  gerado sem identificador passa `""` e o runtime lança exceção).
- **Código puro compartilhado:** mensagens, codec, framing, `TcpControlChannel` e `ConnectionStateMachine` vivem em
  `Assets/_Project`; o host os compila via `host/Host.Shared` (arquivos vinculados, C# 9). Um só codec; o teste de
  integração do host usa a máquina de estados real do headset contra o servidor real.
- **mDNS nativo:** `DnsServiceRegister` no Windows, anunciando o **IP da rota padrão** (não o de adaptadores de VPN);
  `NsdManager` + multicast lock no Android.
- **`.aar`:** classes com lógica em **Java**; Kotlin só para fachadas triviais. O `.aar` não embarca a kotlin-stdlib, e
  lógica em Kotlin gera referências a `Intrinsics`/`StringsKt`/`Unit` que quebram em runtime. Verificar sempre com a
  varredura do `classes.jar` (tools/build-native.ps1 + busca por `kotlin/`).
- **Pareamento:** código de 6 dígitos confirmado no PC; token de 32 bytes lembrado dos dois lados.

## Consequências
- ✅ No Quest: descoberta em ~1 s, pareamento, vídeo, reabertura sem código, reconexão < 1 s após o host voltar.
- ⚠️ **Sem criptografia até a v1.1:** alguém na mesma rede pode capturar o token e o vídeo (risco no brief §12).
- ⚠️ O código compartilhado precisa continuar C# 9 e sem UnityEngine (garantido pelo harness e pelo `Host.Shared`).
