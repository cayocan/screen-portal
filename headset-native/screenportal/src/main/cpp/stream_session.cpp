#include "stream_session.h"

#include <android/log.h>
#include <arpa/inet.h>
#include <media/NdkMediaFormat.h>
#include <sys/resource.h>
#include <sys/socket.h>
#include <sys/time.h>
#include <unistd.h>

#include <cinttypes>
#include <cstdio>
#include <cstring>

#define LOG_TAG "ScreenPortal"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, LOG_TAG, __VA_ARGS__)
#define LOGW(...) __android_log_print(ANDROID_LOG_WARN, LOG_TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, LOG_TAG, __VA_ARGS__)

namespace sp {

namespace {
constexpr int64_t kInputTimeoutUs = 2000;    // não segura a thread de rede
constexpr int64_t kOutputTimeoutUs = 10000;
}  // namespace

StreamSession::StreamSession(const SessionConfig& config, ANativeWindow* window)
    : config_(config), window_(window) {
    ANativeWindow_acquire(window_);
}

StreamSession::~StreamSession() {
    stop();
    ANativeWindow_release(window_);
}

bool StreamSession::configureCodec() {
    codec_ = AMediaCodec_createDecoderByType("video/avc");
    if (!codec_) {
        LOGE("stream %d: sem decoder video/avc", config_.streamId);
        return false;
    }

    AMediaFormat* format = AMediaFormat_new();
    AMediaFormat_setString(format, AMEDIAFORMAT_KEY_MIME, "video/avc");
    AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_WIDTH, config_.width);
    AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_HEIGHT, config_.height);
    // NÃO usar MAX = resolução exata: 1080 linhas são codificadas como 1088 e o decoder se
    // reconfigura a cada IDR — com 4+ telas o fps desaba (medido em 28/09/2026, fase0.md §3).
    AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_MAX_WIDTH, 4096);
    AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_MAX_HEIGHT, 4096);
    // Low-latency: chave padrão (API 30+) + extensão da Qualcomm (XR2 Gen 2 do Quest 3).
    AMediaFormat_setInt32(format, "low-latency", 1);
    AMediaFormat_setInt32(format, "vendor.qti-ext-dec-low-latency.enable", 1);
    AMediaFormat_setInt32(format, "priority", 0);  // realtime
    AMediaFormat_setInt32(format, "operating-rate", 120);

    media_status_t st = AMediaCodec_configure(codec_, format, window_, nullptr, 0);
    AMediaFormat_delete(format);
    if (st != AMEDIA_OK) {
        LOGE("stream %d: configure falhou (%d)", config_.streamId, st);
        return false;
    }
    st = AMediaCodec_start(codec_);
    if (st != AMEDIA_OK) {
        LOGE("stream %d: start falhou (%d)", config_.streamId, st);
        return false;
    }
    return true;
}

bool StreamSession::start() {
    if (!configureCodec()) return false;

    fd_ = socket(AF_INET, SOCK_DGRAM, 0);
    if (fd_ < 0) return false;
    int rcvbuf = 4 * 1024 * 1024;
    setsockopt(fd_, SOL_SOCKET, SO_RCVBUF, &rcvbuf, sizeof(rcvbuf));
    timeval tv{0, 100000};
    setsockopt(fd_, SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof(tv));

    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = htonl(INADDR_ANY);
    addr.sin_port = htons(static_cast<uint16_t>(config_.port));
    if (bind(fd_, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) != 0) {
        LOGE("stream %d: bind na porta %d falhou", config_.streamId, config_.port);
        return false;
    }

    statsSinceUs_ = monotonicUs();
    running_ = true;
    receiveThread_ = std::thread(&StreamSession::receiveLoop, this);
    outputThread_ = std::thread(&StreamSession::outputLoop, this);
    LOGI("stream %d: ouvindo udp/%d, decoder %dx%d", config_.streamId, config_.port, config_.width, config_.height);
    return true;
}

void StreamSession::stop() {
    if (!running_.exchange(false)) return;
    if (receiveThread_.joinable()) receiveThread_.join();
    if (outputThread_.joinable()) outputThread_.join();
    clock_.stop();
    if (codec_) {
        AMediaCodec_stop(codec_);
        AMediaCodec_delete(codec_);
        codec_ = nullptr;
    }
    if (fd_ >= 0) {
        close(fd_);
        fd_ = -1;
    }
}

void StreamSession::receiveLoop() {
    setpriority(PRIO_PROCESS, 0, -10);
    uint8_t packet[kMaxPacket + 64];
    sockaddr_in from{};
    socklen_t fromLen = sizeof(from);

    while (running_) {
        ssize_t n = recvfrom(fd_, packet, sizeof(packet), 0, reinterpret_cast<sockaddr*>(&from), &fromLen);
        if (n <= 0) continue;
        clock_.startIfNeeded(from);  // o host é quem manda o vídeo: sem configuração manual de IP

        {
            std::lock_guard<std::mutex> lock(statsMutex_);
            stats_.packets++;
            stats_.bytes += static_cast<uint64_t>(n);
        }

        if (!reassembler_.push(packet, static_cast<size_t>(n))) continue;
        const PacketHeader& h = reassembler_.frameHeader();
        submitFrame(reassembler_.frameData(), reassembler_.frameSize(), h.captureTimeUs, h.isKeyframe());
    }
}

void StreamSession::submitFrame(const uint8_t* data, size_t size, uint64_t captureTimeUs, bool keyframe) {
    {
        std::lock_guard<std::mutex> lock(statsMutex_);
        stats_.framesReceived++;
    }
    if (waitingForKeyframe_ && !keyframe) return;

    ssize_t idx = AMediaCodec_dequeueInputBuffer(codec_, kInputTimeoutUs);
    if (idx < 0) {
        // Decoder saturado: descartar é melhor que acumular latência. Delta seguinte não teria
        // referência, então espera o próximo IDR (o host força um a cada 1 s na Fase 0).
        waitingForKeyframe_ = true;
        std::lock_guard<std::mutex> lock(statsMutex_);
        stats_.framesDroppedDecoder++;
        return;
    }

    size_t capacity = 0;
    uint8_t* buf = AMediaCodec_getInputBuffer(codec_, static_cast<size_t>(idx), &capacity);
    if (!buf || capacity < size) {
        AMediaCodec_queueInputBuffer(codec_, static_cast<size_t>(idx), 0, 0, 0, 0);
        waitingForKeyframe_ = true;
        LOGW("stream %d: input buffer pequeno (%zu < %zu)", config_.streamId, capacity, size);
        return;
    }
    std::memcpy(buf, data, size);
    // pts = captureTimeUs do host: volta na saída e permite medir a latência sem tabela extra.
    AMediaCodec_queueInputBuffer(codec_, static_cast<size_t>(idx), 0, size, captureTimeUs,
                                 keyframe ? AMEDIACODEC_BUFFER_FLAG_KEY_FRAME : 0);
    waitingForKeyframe_ = false;
}

void StreamSession::outputLoop() {
    setpriority(PRIO_PROCESS, 0, -10);
    AMediaCodecBufferInfo info{};
    while (running_) {
        ssize_t idx = AMediaCodec_dequeueOutputBuffer(codec_, &info, kOutputTimeoutUs);
        if (idx == AMEDIACODEC_INFO_OUTPUT_FORMAT_CHANGED) {
            AMediaFormat* f = AMediaCodec_getOutputFormat(codec_);
            LOGI("stream %d: formato de saída %s", config_.streamId, AMediaFormat_toString(f));
            AMediaFormat_delete(f);
            continue;
        }
        if (idx < 0) continue;

        // render=true: o frame vai direto para a Surface da layer (sem cópia).
        AMediaCodec_releaseOutputBuffer(codec_, static_cast<size_t>(idx), info.size != 0);

        std::lock_guard<std::mutex> lock(statsMutex_);
        stats_.framesDecoded++;
        if (clock_.hasOffset()) {
            int64_t latency = static_cast<int64_t>(clock_.toHostUs(monotonicUs())) - info.presentationTimeUs;
            stats_.latencySumUs += latency;
            stats_.latencySamples++;
            if (latency > stats_.latencyMaxUs) stats_.latencyMaxUs = latency;
            if (latency < stats_.latencyMinUs) stats_.latencyMinUs = latency;
        }
    }
}

std::string StreamSession::takeStatsLine() {
    SessionStats s;
    uint64_t now = monotonicUs();
    double seconds;
    {
        std::lock_guard<std::mutex> lock(statsMutex_);
        s = stats_;
        stats_ = SessionStats{};
        seconds = static_cast<double>(now - statsSinceUs_) / 1e6;
        statsSinceUs_ = now;
    }
    uint64_t droppedTotal = reassembler_.framesDropped();
    s.framesDroppedNetwork = static_cast<uint32_t>(droppedTotal - lastDroppedNetwork_);
    lastDroppedNetwork_ = droppedTotal;
    if (seconds <= 0) seconds = 1;

    double avgMs = s.latencySamples ? static_cast<double>(s.latencySumUs) / s.latencySamples / 1000.0 : -1;
    double minMs = s.latencySamples ? static_cast<double>(s.latencyMinUs) / 1000.0 : -1;
    double maxMs = s.latencySamples ? static_cast<double>(s.latencyMaxUs) / 1000.0 : -1;

    char line[256];
    // SPSTAT,stream,rx_fps,dec_fps,mbps,drop_net,drop_dec,lat_avg_ms,lat_min_ms,lat_max_ms,rtt_ms
    snprintf(line, sizeof(line), "SPSTAT,%d,%.1f,%.1f,%.2f,%u,%u,%.1f,%.1f,%.1f,%.1f", config_.streamId,
             s.framesReceived / seconds, s.framesDecoded / seconds, s.bytes * 8.0 / seconds / 1e6,
             s.framesDroppedNetwork, s.framesDroppedDecoder, avgMs, minMs, maxMs, clock_.bestRttUs() / 1000.0);
    LOGI("%s", line);
    return line;
}

}  // namespace sp
