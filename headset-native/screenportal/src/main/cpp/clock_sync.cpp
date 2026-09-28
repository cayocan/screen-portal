#include "clock_sync.h"

#include <sys/socket.h>
#include <sys/time.h>
#include <unistd.h>

#include <cstring>
#include <ctime>

#include "packet.h"

namespace sp {

uint64_t monotonicUs() {
    timespec ts{};
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return static_cast<uint64_t>(ts.tv_sec) * 1000000ull + static_cast<uint64_t>(ts.tv_nsec) / 1000ull;
}

ClockSync::~ClockSync() { stop(); }

void ClockSync::startIfNeeded(const sockaddr_in& hostVideoAddr) {
    std::call_once(started_, [&] {
        host_ = hostVideoAddr;
        host_.sin_port = htons(kClockPort);
        fd_ = socket(AF_INET, SOCK_DGRAM, 0);
        if (fd_ < 0) return;
        timeval tv{0, 200000};
        setsockopt(fd_, SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof(tv));
        running_ = true;
        thread_ = std::thread(&ClockSync::loop, this);
    });
}

void ClockSync::stop() {
    running_ = false;
    if (thread_.joinable()) thread_.join();
    if (fd_ >= 0) {
        close(fd_);
        fd_ = -1;
    }
}

void ClockSync::loop() {
    constexpr int kWindow = 32;
    int64_t rtts[kWindow];
    int64_t offsets[kWindow];
    int count = 0;
    int next = 0;
    uint32_t seq = 0;
    uint8_t buf[64];

    while (running_) {
        uint64_t sendUs = monotonicUs();
        uint32_t magic = kClockMagic;
        uint64_t zero = 0;
        ++seq;
        std::memcpy(buf, &magic, 4);
        std::memcpy(buf + 4, &seq, 4);
        std::memcpy(buf + 8, &sendUs, 8);
        std::memcpy(buf + 16, &zero, 8);
        sendto(fd_, buf, kClockMessageSize, 0, reinterpret_cast<const sockaddr*>(&host_), sizeof(host_));

        ssize_t n = recv(fd_, buf, sizeof(buf), 0);
        uint64_t recvUs = monotonicUs();
        if (n == static_cast<ssize_t>(kClockMessageSize)) {
            uint32_t rMagic, rSeq;
            uint64_t rSend, hostUs;
            std::memcpy(&rMagic, buf, 4);
            std::memcpy(&rSeq, buf + 4, 4);
            std::memcpy(&rSend, buf + 8, 8);
            std::memcpy(&hostUs, buf + 16, 8);
            if (rMagic == kClockMagic && rSeq == seq && rSend == sendUs) {
                int64_t rtt = static_cast<int64_t>(recvUs - sendUs);
                int64_t offset = static_cast<int64_t>(hostUs) - static_cast<int64_t>((sendUs + recvUs) / 2);
                rtts[next] = rtt;
                offsets[next] = offset;
                next = (next + 1) % kWindow;
                if (count < kWindow) ++count;

                int best = 0;
                for (int i = 1; i < count; ++i)
                    if (rtts[i] < rtts[best]) best = i;
                offsetUs_.store(offsets[best], std::memory_order_relaxed);
                bestRttUs_.store(rtts[best], std::memory_order_relaxed);
                hasOffset_.store(true, std::memory_order_release);
            }
        }
        usleep(250000);
    }
}

}  // namespace sp
