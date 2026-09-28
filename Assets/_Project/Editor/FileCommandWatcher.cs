using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ScreenPortal.Editor
{
    /// <summary>
    /// Gatilho por arquivo para automação sem MCP (o MCP trava com diálogos modais do Editor).
    /// Arquivo vazio em Temp/sp-commands/&lt;comando&gt; → executa → resultado em &lt;comando&gt;.done.
    /// </summary>
    [InitializeOnLoad]
    internal static class FileCommandWatcher
    {
        public const string Dir = "Temp/sp-commands";
        private static double s_nextPoll;

        static FileCommandWatcher()
        {
            Directory.CreateDirectory(Dir);
            EditorApplication.update += Poll;
            EditorCommands.Register("refresh", () =>
            {
                EditorApplication.delayCall += () => AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                return "OK (refresh agendado; veja 'Reloading assemblies' no Editor.log)";
            });
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < s_nextPoll) return;
            s_nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer) return;
            if (!Directory.Exists(Dir)) return;

            foreach (var path in Directory.GetFiles(Dir))
            {
                if (Path.GetExtension(path) != string.Empty) continue; // ignora .done
                string command = Path.GetFileName(path);
                File.Delete(path);
                Run(command);
                return; // um por tick: comandos podem recarregar o domínio
            }
        }

        private static void Run(string command)
        {
            Debug.Log("[ScreenPortal] CMD " + command);
            try
            {
                if (!EditorCommands.TryRun(command, result => Done(command, result)))
                    Done(command, "ERRO: comando desconhecido. Disponíveis: " + string.Join(", ", EditorCommands.Names));
            }
            catch (Exception e)
            {
                Done(command, "ERRO: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void Done(string command, string result)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, command + ".done"), DateTime.Now.ToString("s") + " " + result);
            Debug.Log("[ScreenPortal] CMD " + command + " → " + result);
        }
    }
}
