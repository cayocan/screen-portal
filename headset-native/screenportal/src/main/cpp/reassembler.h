// Remontagem de frames. Mesma semântica de host/Host.Domain/Video/FrameReassembler.cs:
// só o frame mais recente é montado; frame incompleto é descartado e o decoder espera o próximo IDR.
#pragma once

#include <cstdint>
#include <vector>

#include "packet.h"

namespace sp {

class FrameReassembler {
public:
    explicit FrameReassembler(size_t maxFrameBytes = 4 * 1024 * 1024);

    // Retorna true quando o pacote completou um frame; nesse caso frameData()/frameSize()/frameHeader()
    // ficam válidos até a próxima chamada de push().
    bool push(const uint8_t* packet, size_t len);

    const uint8_t* frameData() const { return frame_.data(); }
    size_t frameSize() const { return completedSize_; }
    const PacketHeader& frameHeader() const { return first_; }

    uint64_t framesCompleted() const { return completed_; }
    uint64_t framesDropped() const { return dropped_; }

private:
    void begin(const PacketHeader& h);

    std::vector<uint8_t> frame_;
    std::vector<uint8_t> received_;
    PacketHeader first_{};
    uint32_t frameId_ = 0;
    bool active_ = false;
    bool hasFrame_ = false;
    bool waitingForKeyframe_ = true;
    uint16_t fragCount_ = 0;
    uint16_t receivedCount_ = 0;
    uint16_t lastFragSize_ = 0;
    size_t completedSize_ = 0;
    uint64_t completed_ = 0;
    uint64_t dropped_ = 0;
};

}  // namespace sp
