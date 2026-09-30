using System;
using UnityEngine;

namespace ScreenPortal.Spikes.Phase0
{
    /// <summary>
    /// Chamadas JNI para com.screenportal.video.NativeVideo (headset-native/.aar).
    /// Só passa a Surface da compositor layer e um handle: o vídeo nunca atravessa o C#.
    /// Na Fase 2C isto virou o adapter <c>NativeScreenVideo : IScreenVideo</c>.
    /// </summary>
    public static class NativeVideoBridge
    {
        private static IntPtr s_class;
        private static IntPtr s_start;
        private static IntPtr s_stop;
        private static IntPtr s_takeStats;
        private static readonly jvalue[] s_args5 = new jvalue[5];
        private static readonly jvalue[] s_args1 = new jvalue[1];

        public static bool IsAvailable => Application.platform == RuntimePlatform.Android;

        private static void EnsureInitialized()
        {
            if (s_class != IntPtr.Zero) return;
            IntPtr local = AndroidJNI.FindClass("com/screenportal/video/NativeVideo");
            if (local == IntPtr.Zero)
                throw new InvalidOperationException("com.screenportal.video.NativeVideo não encontrado — o .aar está em Assets/Plugins/Android?");
            s_class = AndroidJNI.NewGlobalRef(local);
            AndroidJNI.DeleteLocalRef(local);
            s_start = AndroidJNI.GetStaticMethodID(s_class, "start", "(IILandroid/view/Surface;II)J");
            s_stop = AndroidJNI.GetStaticMethodID(s_class, "stop", "(J)V");
            s_takeStats = AndroidJNI.GetStaticMethodID(s_class, "takeStats", "(J)Ljava/lang/String;");
        }

        /// <returns>Handle da sessão nativa, ou 0 em erro.</returns>
        public static long Start(int streamId, int port, IntPtr surfaceObject, int width, int height)
        {
            if (!IsAvailable) return 0;
            EnsureInitialized();
            s_args5[0].i = streamId;
            s_args5[1].i = port;
            s_args5[2].l = surfaceObject;
            s_args5[3].i = width;
            s_args5[4].i = height;
            return AndroidJNI.CallStaticLongMethod(s_class, s_start, s_args5);
        }

        public static void Stop(long handle)
        {
            if (!IsAvailable || handle == 0) return;
            EnsureInitialized();
            s_args1[0].j = handle;
            AndroidJNI.CallStaticVoidMethod(s_class, s_stop, s_args1);
        }

        /// <summary>Linha CSV "SPSTAT,..." do último intervalo. Aloca uma string: chamar ~1x/s, nunca por frame.</summary>
        public static string TakeStats(long handle)
        {
            if (!IsAvailable || handle == 0) return null;
            EnsureInitialized();
            s_args1[0].j = handle;
            return AndroidJNI.CallStaticStringMethod(s_class, s_takeStats, s_args1);
        }
    }
}
