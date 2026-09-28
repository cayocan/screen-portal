// Sincronização de relógio headset ↔ host (protocol/video-packet-v1.md).
// Pinga o host a cada 250 ms e usa a amostra de menor RTT das últimas 32.
#pragma once

#include <netinet/in.h>

#include <atomic>
#include <cstdint>
#include <mutex>
#include <thread>

namespace sp {

uint64_t monotonicUs();

class ClockSync {
public:
    ClockSync() = default;
    ~ClockSync();

    // Idempotente: só inicia na primeira chamada (o host é descoberto pelo 1º pacote de vídeo).
    void startIfNeeded(const sockaddr_in& hostVideoAddr);
    void stop();

    bool hasOffset() const { return hasOffset_.load(std::memory_order_acquire); }
    // Converte o relógio local para o do host.
    uint64_t toHostUs(uint64_t localUs) const {
        return static_cast<uint64_t>(static_cast<int64_t>(localUs) + offsetUs_.load(std::memory_order_relaxed));
    }
    int64_t bestRttUs() const { return bestRttUs_.load(std::memory_order_relaxed); }

private:
    void loop();

    std::thread thread_;
    std::atomic<bool> running_{false};
    std::atomic<bool> hasOffset_{false};
    std::atomic<int64_t> offsetUs_{0};
    std::atomic<int64_t> bestRttUs_{-1};
    sockaddr_in host_{};
    int fd_ = -1;
    std::once_flag started_;
};

}  // namespace sp
