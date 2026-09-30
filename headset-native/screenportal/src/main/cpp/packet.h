// Espelho de protocol/video-packet-v1.md e host/Host.Domain/Video/VideoPacketHeader.cs.
// Manter os três em sincronia.
#pragma once

#include <cstddef>
#include <cstdint>
#include <cstring>

namespace sp {

constexpr uint16_t kPacketMagic = 0x5053;
constexpr uint8_t kPacketVersion = 1;
constexpr size_t kHeaderSize = 24;
constexpr size_t kMaxPayload = 1400;
constexpr size_t kMaxPacket = kHeaderSize + kMaxPayload;

constexpr uint8_t kFlagKeyframe = 1u << 0;
constexpr uint8_t kFlagConfig = 1u << 1;

struct PacketHeader {
    uint8_t flags;
    uint16_t streamId;
    uint16_t fragIndex;
    uint16_t fragCount;
    uint16_t payloadSize;
    uint32_t frameId;
    uint64_t captureTimeUs;

    bool isKeyframe() const { return (flags & kFlagKeyframe) != 0; }
};

// ARM64 e x86 são little-endian, igual ao wire format: memcpy direto é seguro.
inline bool parseHeader(const uint8_t* data, size_t len, PacketHeader& out) {
    if (len < kHeaderSize) return false;
    uint16_t magic;
    std::memcpy(&magic, data, 2);
    if (magic != kPacketMagic || data[2] != kPacketVersion) return false;
    out.flags = data[3];
    std::memcpy(&out.streamId, data + 4, 2);
    std::memcpy(&out.fragIndex, data + 6, 2);
    std::memcpy(&out.fragCount, data + 8, 2);
    std::memcpy(&out.payloadSize, data + 10, 2);
    std::memcpy(&out.frameId, data + 12, 4);
    std::memcpy(&out.captureTimeUs, data + 16, 8);
    return out.fragCount > 0 && out.fragIndex < out.fragCount && out.payloadSize <= kMaxPayload &&
           len >= kHeaderSize + out.payloadSize;
}

// true se candidate é mais novo que current (com wrap de u32).
inline bool isNewer(uint32_t current, uint32_t candidate) {
    return static_cast<int32_t>(candidate - current) > 0;
}

// --- Sincronização de relógio (porta 9100 no host) ---
constexpr uint32_t kClockMagic = 0x434B4C43;  // "CLKC"
constexpr size_t kClockMessageSize = 24;
constexpr uint16_t kClockPort = 9100;

// --- Pedido de keyframe (headset → host, UDP 9101). Espelho de host/Host.Domain/Video/KeyframeRequest.cs ---
constexpr uint32_t kKeyframeRequestMagic = 0x5152464B;  // "KFRQ"
constexpr size_t kKeyframeRequestSize = 8;
constexpr uint16_t kKeyframeRequestPort = 9101;

inline void writeKeyframeRequest(uint8_t* dst, uint16_t streamId) {
    uint32_t magic = kKeyframeRequestMagic;
    uint16_t reserved = 0;
    std::memcpy(dst, &magic, 4);
    std::memcpy(dst + 4, &streamId, 2);
    std::memcpy(dst + 6, &reserved, 2);
}

}  // namespace sp
