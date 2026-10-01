using System.Net;
using System.Net.Sockets;
using Concentus;
using Concentus.Enums;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ScreenPortal.Host.Domain.Audio;

namespace ScreenPortal.Host.App.Audio;

/// <summary>
/// <c>ScreenPortalHost audio-test</c> (spec 2G §5, sem headset): o <see cref="AudioHost"/> troca a saída padrão, um tom
/// tocado na saída padrão chega como Opus em udp/9300 (decodificado aqui); um tom Opus mandado para a porta do microfone
/// aparece no "Microfone (Steam Streaming Microphone)". No fim a saída padrão volta.
/// </summary>
public static class AudioSelfTest
{
    public static int Run()
    {
        static void Log(string m) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {m}");
        using var enumerator = new MMDeviceEnumerator();
        string before = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).FriendlyName;
        Log("saída padrão antes: " + before);

        using var receiver = new UdpClient(9300);
        receiver.Client.ReceiveTimeout = 200;
        double pcRms;
        bool micOk;
        using (var audio = new AudioHost(Log))
        {
            var result = audio.Start("teste", 1, "127.0.0.1", 9300, playback: true, mic: true);
            Log($"AudioStarted: mic_port {result.MicPort}, drivers do Steam {result.Speakers}, microfone {result.Mic}");
            Log("saída padrão agora: " + enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).FriendlyName);

            pcRms = PlayAndReceive(enumerator, receiver);
            Log($"som do PC → udp/9300: RMS decodificado {pcRms:F3} {(pcRms > 0.05 ? "✅" : "❌")}");

            micOk = result.Mic && MicRoundTrip(enumerator, result.MicPort, Log);
        }
        string after = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).FriendlyName;
        Log($"saída padrão depois: {after} {(after == before ? "✅ devolvida" : "❌ não voltou")}");
        return pcRms > 0.05 && micOk && after == before ? 0 : 1;
    }

    private static double PlayAndReceive(MMDeviceEnumerator enumerator, UdpClient receiver)
    {
        var decoder = OpusCodecFactory.CreateDecoder(48000, 2);
        var pcm = new short[5760 * 2];
        double sum = 0;
        long count = 0;
        var output = new WasapiOut(enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia), AudioClientShareMode.Shared, false, 20);
        output.Init(new SignalGenerator(48000, 2) { Frequency = 440, Gain = 0.3 }.Take(TimeSpan.FromSeconds(2)));
        output.Play();
        var end = DateTime.UtcNow.AddSeconds(2.5);
        var from = new IPEndPoint(IPAddress.Any, 0);
        while (DateTime.UtcNow < end)
        {
            byte[] packet;
            try { packet = receiver.Receive(ref from); }
            catch (SocketException) { continue; }
            if (!AudioPacketHeader.TryRead(packet, out _, out var payload)) continue;
            int n = decoder.Decode(payload, pcm, 5760, false);
            for (int i = 0; i < n * 2; i++)
            {
                double s = pcm[i] / 32768.0;
                sum += s * s;
                count++;
            }
        }
        output.Dispose();
        return count > 0 ? Math.Sqrt(sum / count) : 0;
    }

    private static bool MicRoundTrip(MMDeviceEnumerator enumerator, int micPort, Action<string> log)
    {
        MMDevice? mic = null;
        foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            if (d.FriendlyName.Contains(AudioHost.SteamMic)) mic = d;
        if (mic == null) return false;

        double sum = 0;
        long count = 0;
        using var capture = new WasapiCapture(mic);
        capture.DataAvailable += (_, e) =>
        {
            for (int i = 0; i + 4 <= e.BytesRecorded; i += 4)
            {
                float s = BitConverter.ToSingle(e.Buffer, i);
                sum += s * s;
                count++;
            }
        };
        capture.StartRecording();

        var encoder = OpusCodecFactory.CreateEncoder(48000, 1, OpusApplication.OPUS_APPLICATION_VOIP);
        encoder.Bitrate = 24000;
        using var sender = new UdpClient();
        var target = new IPEndPoint(IPAddress.Loopback, micPort);
        var frame = new short[960];
        var opus = new byte[1200];
        var packet = new byte[AudioPacketHeader.Size + 1200];
        double phase = 0;
        for (uint seq = 0; seq < 100; seq++) // 2 s de quadros de 20 ms
        {
            for (int i = 0; i < frame.Length; i++)
            {
                frame[i] = (short)(Math.Sin(phase) * 0.3 * 32767);
                phase += 2 * Math.PI * 440 / 48000;
            }
            int n = encoder.Encode(frame, 960, opus, opus.Length);
            int size = new AudioPacketHeader(1, seq, 0).Write(packet, opus.AsSpan(0, n));
            sender.Send(packet, size, target);
            Thread.Sleep(20);
        }
        Thread.Sleep(300);
        capture.StopRecording();
        double rms = count > 0 ? Math.Sqrt(sum / count) : 0;
        log($"udp/{micPort} → Microfone (Steam Streaming Microphone): RMS {rms:F3} {(rms > 0.05 ? "✅" : "❌")}");
        return rms > 0.05;
    }
}
