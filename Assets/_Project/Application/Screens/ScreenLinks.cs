using System;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Screens
{
    /// <summary>Estado visível do vídeo de uma tela (spec 2C §5).</summary>
    public enum ScreenLinkState
    {
        /// <summary>Sem conexão com o PC.</summary>
        Offline,
        /// <summary>Pedido enviado; o host está criando/reatando o monitor.</summary>
        Creating,
        /// <summary>Monitor pronto; abrindo o decoder e o stream, aguardando o primeiro quadro.</summary>
        Connecting,
        /// <summary>Vídeo chegando.</summary>
        Live,
        /// <summary>O host recusou (ver o detalhe).</summary>
        Failed,
    }

    /// <summary>Consulta do estado do vídeo por tela (Presentation). LinkChanged só em mudança real.</summary>
    public interface IScreenLinks
    {
        event Action<ScreenId> LinkChanged;
        bool TryGetLink(ScreenId screen, out ScreenLinkState state, out string detail);
    }
}
