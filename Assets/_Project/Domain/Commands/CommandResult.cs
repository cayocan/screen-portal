namespace ScreenPortal.Domain.Commands
{
    public readonly struct CommandResult
    {
        public static readonly CommandResult Ok = new CommandResult(true, null);

        public bool Accepted { get; }
        public string Error { get; }

        private CommandResult(bool accepted, string error)
        {
            Accepted = accepted;
            Error = error;
        }

        public static CommandResult Rejected(string error) => new CommandResult(false, error);

        public override string ToString() => Accepted ? "Ok" : "Rejected: " + Error;
    }
}
