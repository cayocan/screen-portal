namespace ScreenPortal.Host.Domain.Audio;

/// <summary>O que o host faz com o áudio (spec 2G §1, ADR 0015), dado o que o headset pediu e os drivers presentes.</summary>
public readonly record struct AudioPlan(bool Stream, bool SwitchDefaultOutput, bool CaptureSteamSpeakers, bool Mic)
{
    public static AudioPlan For(bool playbackRequested, bool micRequested, bool steamSpeakers, bool steamMic) => new(
        Stream: playbackRequested,
        // Só troca a saída padrão quando há a saída silenciosa do Steam; sem ela, captura a padrão (som também no PC).
        SwitchDefaultOutput: playbackRequested && steamSpeakers,
        CaptureSteamSpeakers: playbackRequested && steamSpeakers,
        Mic: micRequested && steamMic);
}
