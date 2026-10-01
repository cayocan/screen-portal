#include "audio_session.h"

#include <android/log.h>
#include <arpa/inet.h>
#include <media/NdkMediaFormat.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <unistd.h>

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <cstring>

#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, "ScreenPortal", __VA_ARGS__)
#define LOGW(...) __android_log_print(ANDROID_LOG_WARN, "ScreenPortal", __VA_ARGS__)

namespace sp {
namespace {

constexpr int kRate = 48000;
constexpr int kPlayChannels = 2;
constexpr size_t kStartSamples = kRate / 1000 * 40 * kPlayChannels;  // 40 ms no buffer para começar a tocar
constexpr size_t kMaxSamples = kRate / 1000 * 120 * kPlayChannels;   // acima de 120 ms, descarta os mais velhos
constexpr size_t kMicFrame = 960;                                    // 20 ms mono
constexpr int kHeader = 18;
constexpr int kMaxPacket = 2048;

uint64_t nowUs() {
    return static_cast<uint64_t>(std::chrono::duration_cast<std::chrono::microseconds>(
                                     std::chrono::steady_clock::now().time_since_epoch())
                                     .count());
}

void putU32(uint8_t* p, uint32_t v) { memcpy(p, &v, 4); }  // ARM64 é little-endian, como o protocolo
void putU64(uint8_t* p, uint64_t v) { memcpy(p, &v, 8); }

int udpSocket(int bindPort, int timeoutMs) {
    int s = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (s < 0) return -1;
    if (bindPort > 0) {
        sockaddr_in addr{};
        addr.sin_family = AF_INET;
        addr.sin_port = htons(static_cast<uint16_t>(bindPort));
        addr.sin_addr.s_addr = htonl(INADDR_ANY);
        if (bind(s, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) != 0) {
            close(s);
            return -1;
        }
    }
    timeval tv{0, timeoutMs * 1000};
    setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof(tv));
    return s;
}

}  // namespace

size_t PcmRing::write(const int16_t* data, size_t count) {
    size_t w = write_.load(std::memory_order_relaxed);
    size_t free = buffer_.size() - (w - read_.load(std::memory_order_acquire));
    count = std::min(count, free);
    for (size_t i = 0; i < count; ++i) buffer_[(w + i) % buffer_.size()] = data[i];
    write_.store(w + count, std::memory_order_release);
    return count;
}

size_t PcmRing::read(int16_t* data, size_t count) {
    size_t r = read_.load(std::memory_order_relaxed);
    count = std::min(count, write_.load(std::memory_order_acquire) - r);
    for (size_t i = 0; i < count; ++i) data[i] = buffer_[(r + i) % buffer_.size()];
    read_.store(r + count, std::memory_order_release);
    return count;
}

AudioSession::AudioSession(const AudioConfig& config)
    : config_(config), playRing_(kRate * kPlayChannels / 2), micRing_(kRate / 2) {}

AudioSession::~AudioSession() { stop(); }

bool AudioSession::start() {
    running_ = true;
    bool ok = true;
    if (config_.playback) ok = startPlayback() && ok;
    if (config_.mic && !startMic()) LOGW("áudio: microfone não abriu (permissão de microfone?)");
    return ok;
}

// ---------- som do PC ----------

bool AudioSession::startPlayback() {
    rxSocket_ = udpSocket(config_.rxPort, 200);
    if (rxSocket_ < 0) {
        LOGW("áudio: udp/%d indisponível", config_.rxPort);
        return false;
    }

    decoder_ = AMediaCodec_createDecoderByType("audio/opus");
    if (!decoder_) return false;
    AMediaFormat* format = AMediaFormat_new();
    AMediaFormat_setString(format, AMEDIAFORMAT_KEY_MIME, "audio/opus");
    AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_SAMPLE_RATE, kRate);
    AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_CHANNEL_COUNT, kPlayChannels);
    // OpusHead (RFC 7845): pre-skip 0, 48 kHz, ganho 0, mapeamento 0; csd-1/csd-2 = atraso do codec e pre-roll em ns.
    uint8_t head[19] = {'O', 'p', 'u', 's', 'H', 'e', 'a', 'd', 1, kPlayChannels, 0, 0, 0x80, 0xBB, 0, 0, 0, 0, 0};
    int64_t codecDelayNs = 0, prerollNs = 80'000'000;
    AMediaFormat_setBuffer(format, "csd-0", head, sizeof(head));
    AMediaFormat_setBuffer(format, "csd-1", &codecDelayNs, sizeof(codecDelayNs));
    AMediaFormat_setBuffer(format, "csd-2", &prerollNs, sizeof(prerollNs));
    media_status_t st = AMediaCodec_configure(decoder_, format, nullptr, nullptr, 0);
    AMediaFormat_delete(format);
    if (st != AMEDIA_OK || AMediaCodec_start(decoder_) != AMEDIA_OK) {
        LOGW("áudio: decoder Opus falhou (%d)", st);
        return false;
    }

    AAudioStreamBuilder* builder = nullptr;
    AAudio_createStreamBuilder(&builder);
    AAudioStreamBuilder_setDirection(builder, AAUDIO_DIRECTION_OUTPUT);
    AAudioStreamBuilder_setSampleRate(builder, kRate);
    AAudioStreamBuilder_setChannelCount(builder, kPlayChannels);
    AAudioStreamBuilder_setFormat(builder, AAUDIO_FORMAT_PCM_I16);
    AAudioStreamBuilder_setPerformanceMode(builder, AAUDIO_PERFORMANCE_MODE_LOW_LATENCY);
    AAudioStreamBuilder_setUsage(builder, AAUDIO_USAGE_MEDIA);
    AAudioStreamBuilder_setDataCallback(builder, &AudioSession::onPlay, this);
    aaudio_result_t r = AAudioStreamBuilder_openStream(builder, &output_);
    AAudioStreamBuilder_delete(builder);
    if (r != AAUDIO_OK || AAudioStream_requestStart(output_) != AAUDIO_OK) {
        LOGW("áudio: saída AAudio falhou (%d)", r);
        return false;
    }
    rxThread_ = std::thread(&AudioSession::receiveLoop, this);
    LOGI("áudio: som do PC em udp/%d (Opus → AAudio, buffer %d ms)", config_.rxPort, 40);
    return true;
}

void AudioSession::receiveLoop() {
    uint8_t packet[kMaxPacket];
    while (running_) {
        ssize_t n = recv(rxSocket_, packet, sizeof(packet), 0);
        if (n <= kHeader || memcmp(packet, "SPAU", 4) != 0 || packet[4] != 1) {
            drainDecoder();
            continue;
        }
        uint32_t seq;
        uint64_t captureUs;
        memcpy(&seq, packet + 6, 4);
        memcpy(&captureUs, packet + 10, 8);
        if (hasSeq_ && static_cast<int32_t>(seq - lastSeq_) <= 0) continue;  // atrasado ou repetido
        hasSeq_ = true;
        lastSeq_ = seq;
        rxPackets_++;

        ssize_t idx = AMediaCodec_dequeueInputBuffer(decoder_, 5000);
        if (idx >= 0) {
            size_t capacity = 0;
            uint8_t* in = AMediaCodec_getInputBuffer(decoder_, static_cast<size_t>(idx), &capacity);
            size_t len = std::min(static_cast<size_t>(n - kHeader), capacity);
            memcpy(in, packet + kHeader, len);
            AMediaCodec_queueInputBuffer(decoder_, static_cast<size_t>(idx), 0, len, captureUs, 0);
        }
        drainDecoder();
    }
}

void AudioSession::drainDecoder() {
    AMediaCodecBufferInfo info;
    while (true) {
        ssize_t idx = AMediaCodec_dequeueOutputBuffer(decoder_, &info, 0);
        if (idx == AMEDIACODEC_INFO_OUTPUT_FORMAT_CHANGED || idx == AMEDIACODEC_INFO_OUTPUT_BUFFERS_CHANGED) continue;
        if (idx < 0) return;
        size_t size = 0;
        uint8_t* out = AMediaCodec_getOutputBuffer(decoder_, static_cast<size_t>(idx), &size);
        if (out && info.size > 0)
            playRing_.write(reinterpret_cast<const int16_t*>(out + info.offset), static_cast<size_t>(info.size) / 2);
        AMediaCodec_releaseOutputBuffer(decoder_, static_cast<size_t>(idx), false);
    }
}

aaudio_data_callback_result_t AudioSession::onPlay(AAudioStream*, void* user, void* data, int32_t frames) {
    auto* self = static_cast<AudioSession*>(user);
    auto* out = static_cast<int16_t*>(data);
    size_t needed = static_cast<size_t>(frames) * kPlayChannels;
    size_t available = self->playRing_.available();
    if (!self->playing_) {
        if (available < kStartSamples) {
            memset(out, 0, needed * sizeof(int16_t));
            return AAUDIO_CALLBACK_RESULT_CONTINUE;
        }
        self->playing_ = true;
    }
    if (available > kMaxSamples) self->playRing_.skip(available - kStartSamples);  // não acumula atraso
    size_t got = self->playRing_.read(out, needed);
    if (got < needed) {
        memset(out + got, 0, (needed - got) * sizeof(int16_t));
        self->playing_ = false;  // falta de dados: volta a encher 40 ms
        self->underruns_++;
    }
    return AAUDIO_CALLBACK_RESULT_CONTINUE;
}

// ---------- microfone ----------

bool AudioSession::startMic() {
    txSocket_ = udpSocket(0, 0);
    if (txSocket_ < 0) return false;

    AAudioStreamBuilder* builder = nullptr;
    AAudio_createStreamBuilder(&builder);
    AAudioStreamBuilder_setDirection(builder, AAUDIO_DIRECTION_INPUT);
    AAudioStreamBuilder_setSampleRate(builder, kRate);
    AAudioStreamBuilder_setChannelCount(builder, 1);
    AAudioStreamBuilder_setFormat(builder, AAUDIO_FORMAT_PCM_I16);
    AAudioStreamBuilder_setPerformanceMode(builder, AAUDIO_PERFORMANCE_MODE_LOW_LATENCY);
    AAudioStreamBuilder_setInputPreset(builder, AAUDIO_INPUT_PRESET_VOICE_COMMUNICATION);
    AAudioStreamBuilder_setDataCallback(builder, &AudioSession::onRecord, this);
    aaudio_result_t r = AAudioStreamBuilder_openStream(builder, &input_);
    AAudioStreamBuilder_delete(builder);
    if (r != AAUDIO_OK) {
        input_ = nullptr;
        return false;
    }

    encoder_ = AMediaCodec_createEncoderByType("audio/opus");
    if (!encoder_) return false;
    AMediaFormat* format = AMediaFormat_new();
    AMediaFormat_setString(format, AMEDIAFORMAT_KEY_MIME, "audio/opus");
    AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_SAMPLE_RATE, kRate);
    AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_CHANNEL_COUNT, 1);
    AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_BIT_RATE, 24000);
    media_status_t st = AMediaCodec_configure(encoder_, format, nullptr, nullptr, AMEDIACODEC_CONFIGURE_FLAG_ENCODE);
    AMediaFormat_delete(format);
    if (st != AMEDIA_OK || AMediaCodec_start(encoder_) != AMEDIA_OK) {
        LOGW("áudio: encoder Opus falhou (%d)", st);
        return false;
    }
    if (AAudioStream_requestStart(input_) != AAUDIO_OK) return false;
    micThread_ = std::thread(&AudioSession::micLoop, this);
    LOGI("áudio: microfone → %s:%d (Opus 24 kbps)", config_.host.c_str(), config_.txPort);
    return true;
}

aaudio_data_callback_result_t AudioSession::onRecord(AAudioStream*, void* user, void* data, int32_t frames) {
    static_cast<AudioSession*>(user)->micRing_.write(static_cast<const int16_t*>(data), static_cast<size_t>(frames));
    return AAUDIO_CALLBACK_RESULT_CONTINUE;
}

void AudioSession::micLoop() {
    sockaddr_in host{};
    host.sin_family = AF_INET;
    host.sin_port = htons(static_cast<uint16_t>(config_.txPort));
    inet_pton(AF_INET, config_.host.c_str(), &host.sin_addr);
    uint8_t packet[kMaxPacket];
    AMediaCodecBufferInfo info;
    while (running_) {
        if (micRing_.available() >= kMicFrame) {
            ssize_t idx = AMediaCodec_dequeueInputBuffer(encoder_, 5000);
            if (idx >= 0) {
                size_t capacity = 0;
                uint8_t* in = AMediaCodec_getInputBuffer(encoder_, static_cast<size_t>(idx), &capacity);
                size_t samples = std::min(kMicFrame, capacity / 2);
                micRing_.read(reinterpret_cast<int16_t*>(in), samples);
                AMediaCodec_queueInputBuffer(encoder_, static_cast<size_t>(idx), 0, samples * 2, nowUs(), 0);
            }
        } else {
            std::this_thread::sleep_for(std::chrono::milliseconds(5));
        }
        while (true) {
            ssize_t idx = AMediaCodec_dequeueOutputBuffer(encoder_, &info, 0);
            if (idx == AMEDIACODEC_INFO_OUTPUT_FORMAT_CHANGED || idx == AMEDIACODEC_INFO_OUTPUT_BUFFERS_CHANGED) continue;
            if (idx < 0) break;
            size_t size = 0;
            uint8_t* out = AMediaCodec_getOutputBuffer(encoder_, static_cast<size_t>(idx), &size);
            bool config = (info.flags & AMEDIACODEC_BUFFER_FLAG_CODEC_CONFIG) != 0;
            if (out && !config && info.size > 0 && info.size <= kMaxPacket - kHeader) {
                memcpy(packet, "SPAU", 4);
                packet[4] = 1;
                packet[5] = 1;
                putU32(packet + 6, micSeq_++);
                putU64(packet + 10, nowUs());
                memcpy(packet + kHeader, out + info.offset, static_cast<size_t>(info.size));
                sendto(txSocket_, packet, kHeader + info.size, 0, reinterpret_cast<sockaddr*>(&host), sizeof(host));
                micPackets_++;
            }
            AMediaCodec_releaseOutputBuffer(encoder_, static_cast<size_t>(idx), false);
        }
    }
}

// ---------- ciclo de vida ----------

void AudioSession::stop() {
    if (!running_.exchange(false)) return;
    if (rxThread_.joinable()) rxThread_.join();
    if (micThread_.joinable()) micThread_.join();
    if (output_) {
        AAudioStream_requestStop(output_);
        AAudioStream_close(output_);
        output_ = nullptr;
    }
    if (input_) {
        AAudioStream_requestStop(input_);
        AAudioStream_close(input_);
        input_ = nullptr;
    }
    if (decoder_) {
        AMediaCodec_stop(decoder_);
        AMediaCodec_delete(decoder_);
        decoder_ = nullptr;
    }
    if (encoder_) {
        AMediaCodec_stop(encoder_);
        AMediaCodec_delete(encoder_);
        encoder_ = nullptr;
    }
    if (rxSocket_ >= 0) close(rxSocket_);
    if (txSocket_ >= 0) close(txSocket_);
    rxSocket_ = txSocket_ = -1;
    LOGI("áudio: parado");
}

std::string AudioSession::takeStats() {
    char line[128];
    snprintf(line, sizeof(line), "SPAUDIO,%u,%u,%zu,%u", rxPackets_.exchange(0), underruns_.exchange(0),
             playRing_.available() / kPlayChannels / (kRate / 1000), micPackets_.exchange(0));
    return line;
}

}  // namespace sp
