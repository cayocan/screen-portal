// Ponte JNI: com.screenportal.video.NativeAudio (Kotlin) ↔ AudioSession (C++). A Unity só liga/desliga; o áudio não
// passa pelo C#.
#include <jni.h>

#include "audio_session.h"

using sp::AudioConfig;
using sp::AudioSession;

extern "C" {

JNIEXPORT jlong JNICALL Java_com_screenportal_video_NativeAudio_nativeStart(JNIEnv* env, jclass, jstring host, jint rxPort,
                                                                           jint txPort, jboolean playback, jboolean mic) {
    if (!host) return 0;
    const char* chars = env->GetStringUTFChars(host, nullptr);
    AudioConfig config;
    config.host = chars;
    env->ReleaseStringUTFChars(host, chars);
    config.rxPort = rxPort;
    config.txPort = txPort;
    config.playback = playback;
    config.mic = mic;
    auto* session = new AudioSession(config);
    if (!session->start()) {
        delete session;
        return 0;
    }
    return reinterpret_cast<jlong>(session);
}

JNIEXPORT void JNICALL Java_com_screenportal_video_NativeAudio_nativeStop(JNIEnv*, jclass, jlong handle) {
    delete reinterpret_cast<AudioSession*>(handle);
}

JNIEXPORT jstring JNICALL Java_com_screenportal_video_NativeAudio_nativeTakeStats(JNIEnv* env, jclass, jlong handle) {
    if (!handle) return nullptr;
    return env->NewStringUTF(reinterpret_cast<AudioSession*>(handle)->takeStats().c_str());
}

}  // extern "C"
