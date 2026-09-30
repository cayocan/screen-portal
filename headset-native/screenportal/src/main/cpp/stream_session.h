// Uma tela = uma sessão: UDP ► remontagem ► AMediaCodec ► Surface da compositor layer (zero-copy).
// O frame de vídeo nunca sai do código nativo (regra do CLAUDE.md).
#pragma once

#include <android/native_window.h>
#include <media/NdkMediaCodec.h>
#include <netinet/in.h>

#include <atomic>
#include <cstdint>
#include <mutex>
#include <string>
#include <thread>

#include "clock_sync.h"
#include "reassembler.h"

namespace sp {

struct SessionConfig {
    int streamId = 0;
    int port = 9000;
    int width = 1920;   // tamanho esperado (o decoder se adapta ao SPS se diferir)
    int height = 1080;
};

struct SessionStats {
    // Janela de ~1 s, zerada a cada snapshot.
    uint32_t packets = 0;
    uint32_t framesReceived = 0;
    uint32_t framesDecoded = 0;
    uint32_t framesDroppedNetwork = 0;
    uint32_t framesDroppedDecoder = 0;  // sem input buffer livre → descartado (e espera IDR)
    uint64_t bytes = 0;
    // Latência captura(host) → saída do decoder (headset), já com o offset de relógio.
    int64_t latencySumUs = 0;
    int64_t latencyMaxUs = 0;
    int64_t latencyMinUs = INT64_MAX;
    uint32_t latencySamples = 0;
};

class StreamSession {
public:
    StreamSession(const SessionConfig& config, ANativeWindow* window);
    ~StreamSession();

    bool start();
    void stop();

    // Linha "SPSTAT,..." com as estatísticas desde o último snapshot (CSV; ver docs/measurements).
    std::string takeStatsLine();

private:
    void receiveLoop();
    void outputLoop();
    bool configureCodec();
    void submitFrame(const uint8_t* data, size_t size, uint64_t captureTimeUs, bool keyframe);
    // Pede IDR ao host (KFRQ, UDP 9101), no máximo 1 a cada 100 ms. Só na thread de recepção.
    void requestKeyframe();

    SessionConfig config_;
    ANativeWindow* window_ = nullptr;
    AMediaCodec* codec_ = nullptr;
    int fd_ = -1;
    std::atomic<bool> running_{false};
    std::thread receiveThread_;
    std::thread outputThread_;
    FrameReassembler reassembler_;
    ClockSync clock_;
    bool waitingForKeyframe_ = true;
    sockaddr_in hostAddr_{};  // aprendido do primeiro pacote de vídeo, porta trocada para 9101
    bool hasHost_ = false;
    uint64_t lastKeyframeRequestUs_ = 0;
    uint64_t lastDroppedSeen_ = 0;

    std::mutex statsMutex_;
    SessionStats stats_;
    uint64_t statsSinceUs_ = 0;
    uint64_t lastDroppedNetwork_ = 0;
};

}  // namespace sp
