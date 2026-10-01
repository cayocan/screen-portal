package com.screenportal.video

/**
 * Fachada do áudio (spec 2G §4) chamada pela Unity (NativeAudioLink.cs) via JNI: som do PC (UDP → Opus → AAudio) e
 * microfone (AAudio → Opus → UDP), tudo no C++. Mesma regra do NativeVideo: só APIs Java/Android aqui.
 */
object NativeAudio {
    init {
        System.loadLibrary("screenportal")
    }

    /** Liga o áudio: recebe o som do PC em [rxPort] e manda o microfone para [host]:[txPort]. Retorna 0 em erro. */
    @JvmStatic
    fun start(host: String?, rxPort: Int, txPort: Int, playback: Boolean, mic: Boolean): Long =
        nativeStart(host, rxPort, txPort, playback, mic)

    @JvmStatic
    fun stop(handle: Long) {
        if (handle != 0L) nativeStop(handle)
    }

    /** Linha "SPAUDIO,rx_pkt,underruns,buffer_ms,mic_pkt" do último intervalo (chamar ~1x/s). */
    @JvmStatic
    fun takeStats(handle: Long): String? = if (handle != 0L) nativeTakeStats(handle) else null

    @JvmStatic private external fun nativeStart(host: String?, rxPort: Int, txPort: Int, playback: Boolean, mic: Boolean): Long
    @JvmStatic private external fun nativeStop(handle: Long)
    @JvmStatic private external fun nativeTakeStats(handle: Long): String?
}
