package com.screenportal.video

import android.view.Surface

/**
 * Fachada chamada pela Unity (NativeVideoBridge.cs) via JNI.
 *
 * Regra: só usar APIs Java/Android aqui — o .aar não embarca a kotlin-stdlib, e as asserções
 * de nulidade do Kotlin estão desligadas no build (ver build.gradle.kts).
 */
object NativeVideo {
    init {
        System.loadLibrary("screenportal")
    }

    /** Inicia uma tela: escuta UDP em [port] e decodifica direto em [surface]. Retorna 0 em erro. */
    @JvmStatic
    fun start(streamId: Int, port: Int, surface: Surface?, width: Int, height: Int): Long =
        nativeStart(streamId, port, surface, width, height)

    @JvmStatic
    fun stop(handle: Long) {
        if (handle != 0L) nativeStop(handle)
    }

    /** Linha CSV "SPSTAT,..." do último intervalo (chamar ~1x/s). */
    @JvmStatic
    fun takeStats(handle: Long): String? = if (handle != 0L) nativeTakeStats(handle) else null

    @JvmStatic private external fun nativeStart(streamId: Int, port: Int, surface: Surface?, width: Int, height: Int): Long
    @JvmStatic private external fun nativeStop(handle: Long)
    @JvmStatic private external fun nativeTakeStats(handle: Long): String?
}
