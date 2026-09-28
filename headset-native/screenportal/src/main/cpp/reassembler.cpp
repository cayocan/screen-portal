#include "reassembler.h"

#include <algorithm>
#include <cstring>

namespace sp {

FrameReassembler::FrameReassembler(size_t maxFrameBytes) : frame_(maxFrameBytes), received_(65536, 0) {}

void FrameReassembler::begin(const PacketHeader& h) {
    std::fill(received_.begin(), received_.begin() + fragCount_, 0);  // só índices < fragCount_ anterior
    hasFrame_ = true;
    active_ = true;
    frameId_ = h.frameId;
    fragCount_ = h.fragCount;
    receivedCount_ = 0;
    lastFragSize_ = 0;
    first_ = h;
}

bool FrameReassembler::push(const uint8_t* packet, size_t len) {
    PacketHeader h{};
    if (!parseHeader(packet, len, h)) return false;

    if (!active_ || h.frameId != frameId_) {
        if (hasFrame_ && !isNewer(frameId_, h.frameId)) return false;  // atrasado
        if (active_) {
            ++dropped_;
            waitingForKeyframe_ = true;
        }
        begin(h);
    }

    if (h.fragCount != fragCount_ || received_[h.fragIndex]) return false;
    size_t offset = static_cast<size_t>(h.fragIndex) * kMaxPayload;
    if (offset + h.payloadSize > frame_.size()) {
        active_ = false;
        ++dropped_;
        return false;
    }

    std::memcpy(frame_.data() + offset, packet + kHeaderSize, h.payloadSize);
    received_[h.fragIndex] = 1;
    ++receivedCount_;
    if (h.fragIndex == h.fragCount - 1) lastFragSize_ = h.payloadSize;

    if (receivedCount_ != fragCount_) return false;

    active_ = false;
    if (waitingForKeyframe_ && !first_.isKeyframe()) {
        ++dropped_;
        return false;
    }
    waitingForKeyframe_ = false;
    ++completed_;
    completedSize_ = static_cast<size_t>(fragCount_ - 1) * kMaxPayload + lastFragSize_;
    return true;
}

}  // namespace sp
