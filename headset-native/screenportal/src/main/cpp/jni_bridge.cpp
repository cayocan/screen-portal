// Ponte JNI: com.screenportal.video.NativeVideo (Kotlin) ↔ StreamSession (C++).
// A Unity só passa a Surface da OVROverlay e recebe um handle; o vídeo nunca passa pelo C#.
#include <android/native_window_jni.h>
#include <jni.h>

#include "stream_session.h"

using sp::SessionConfig;
using sp::StreamSession;

extern "C" {

JNIEXPORT jlong JNICALL Java_com_screenportal_video_NativeVideo_nativeStart(JNIEnv* env, jclass, jint streamId,
                                                                           jint port, jobject surface, jint width,
                                                                           jint height) {
    if (!surface) return 0;
    ANativeWindow* window = ANativeWindow_fromSurface(env, surface);
    if (!window) return 0;

    SessionConfig config;
    config.streamId = streamId;
    config.port = port;
    config.width = width;
    config.height = height;

    auto* session = new StreamSession(config, window);
    ANativeWindow_release(window);  // a sessão mantém a própria referência
    if (!session->start()) {
        delete session;
        return 0;
    }
    return reinterpret_cast<jlong>(session);
}

JNIEXPORT void JNICALL Java_com_screenportal_video_NativeVideo_nativeStop(JNIEnv*, jclass, jlong handle) {
    delete reinterpret_cast<StreamSession*>(handle);
}

JNIEXPORT jstring JNICALL Java_com_screenportal_video_NativeVideo_nativeTakeStats(JNIEnv* env, jclass,
                                                                                 jlong handle) {
    if (!handle) return nullptr;
    std::string line = reinterpret_cast<StreamSession*>(handle)->takeStatsLine();
    return env->NewStringUTF(line.c_str());
}

}  // extern "C"
