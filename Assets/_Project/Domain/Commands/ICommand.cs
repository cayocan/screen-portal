namespace ScreenPortal.Domain.Commands
{
    /// <summary>
    /// Toda mudança de estado da workspace (brief §8.2). Função pura: não altera <paramref name="state"/>.
    /// Se rejeitado, <paramref name="next"/> é o próprio <paramref name="state"/>.
    /// </summary>
    public interface ICommand
    {
        /// <summary>false para mudanças que não devem entrar no histórico (ex.: foco).</summary>
        bool IsUndoable { get; }

        CommandResult Apply(Workspace state, out Workspace next);
    }
}
