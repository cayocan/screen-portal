using ScreenPortal.Domain.Commands;

namespace ScreenPortal.App
{
    /// <summary>Recebe cada comando processado (log/replay, e na Fase 1B a sincronização com o host).</summary>
    public interface ICommandObserver
    {
        void OnCommandProcessed(ICommand command, CommandResult result);
    }
}
