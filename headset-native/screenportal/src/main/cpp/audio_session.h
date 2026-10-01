// Áudio do Screen Portal no Quest (spec 2G §4): som do PC (UDP → Opus → AAudio) e microfone (AAudio → Opus → UDP).
// Tudo nativo: o áudio não atravessa o C#. Pacote SPAU: protocol/audio-wire.md.
#pragma once

#include <aaudio/AAudio.h>
#include <media/NdkMediaCodec.h>

#include <atomic>
#include <cstdint>
#include <string>
#include <thread>
#include <vector>

namespace sp {

// Anel SPSC de amostras int16 (um produtor, um consumidor; sem lock no callback do AAudio).
class PcmRing {
public:
    explicit PcmRing(size_t capacity) : buffer_(capacity) {}
    size_t available() const { return write_.load(std::memory_order_acquire) - read_.load(std::memory_order_acquire); }
    size_t write(const int16_t* data, size_t count);
    size_t read(int16_t* data, size_t count);
    void skip(size_t count) { read_.fetch_add(count, std::memory_order_acq_rel); }

private:
    std::vector<int16_t> buffer_;
    std::atomic<size_t> write_{0};
    std::atomic<size_t> read_{0};
};

struct AudioConfig {
    std::string host;  // IP do PC
    int rxPort = 9300; // onde recebo o som do PC
    int txPort = 9301; // onde o host recebe o microfone
    bool playback = true;
    bool mic = true;
};

class AudioSession {
public:
    explicit AudioSession(const AudioConfig& config);
    ~AudioSession();
    bool start();
    void stop();
    // "SPAUDIO,rx_pkt,underruns,buffer_ms,mic_pkt" do último intervalo.
    std::string takeStats();

private:
    // som do PC
    bool startPlayback();
    void receiveLoop();
    void drainDecoder();
    static aaudio_data_callback_result_t onPlay(AAudioStream* stream, void* user, void* data, int32_t frames);
    // microfone
    bool startMic();
    void micLoop();
    static aaudio_data_callback_result_t onRecord(AAudioStream* stream, void* user, void* data, int32_t frames);

    AudioConfig config_;
    std::atomic<bool> running_{false};

    int rxSocket_ = -1;
    AMediaCodec* decoder_ = nullptr;
    AAudioStream* output_ = nullptr;
    PcmRing playRing_;
    std::atomic<bool> playing_{false}; // buffer cheio o bastante para tocar
    std::thread rxThread_;
    uint32_t lastSeq_ = 0;
    bool hasSeq_ = false;

    int txSocket_ = -1;
    AMediaCodec* encoder_ = nullptr;
    AAudioStream* input_ = nullptr;
    PcmRing micRing_;
    std::thread micThread_;
    uint32_t micSeq_ = 0;

    std::atomic<uint32_t> rxPackets_{0};
    std::atomic<uint32_t> underruns_{0};
    std::atomic<uint32_t> micPackets_{0};
};

}  // namespace sp
