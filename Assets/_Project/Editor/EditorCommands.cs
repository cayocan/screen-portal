using System;
using System.Collections.Generic;

namespace ScreenPortal.Editor
{
    /// <summary>
    /// Registro dos comandos disparáveis por arquivo (tools/unity-cmd.ps1). Cada módulo de Editor registra os
    /// seus em [InitializeOnLoadMethod]; o registro é refeito a cada domain reload.
    /// </summary>
    public static class EditorCommands
    {
        private static readonly Dictionary<string, Action<Action<string>>> s_commands =
            new Dictionary<string, Action<Action<string>>>(StringComparer.OrdinalIgnoreCase);

        public static void Register(string name, Func<string> run) => s_commands[name] = done => done(run());

        /// <summary>Para comandos que terminam depois (ex.: testes): chame <c>done(resultado)</c> ao final.</summary>
        public static void RegisterAsync(string name, Action<Action<string>> run) => s_commands[name] = run;

        public static bool TryRun(string name, Action<string> done)
        {
            if (!s_commands.TryGetValue(name, out var run)) return false;
            run(done);
            return true;
        }

        public static IEnumerable<string> Names => s_commands.Keys;
    }
}
