using ScreenPortal.Domain.Commands;

namespace ScreenPortal.App
{
    /// <summary>Porta de entrada de toda intenção de mudança (views, atalhos, rede).</summary>
    public interface ICommandBus
    {
        CommandResult Send(ICommand command);
        bool Undo();
        bool Redo();
    }
}
