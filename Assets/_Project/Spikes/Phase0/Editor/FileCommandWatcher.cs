using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ScreenPortal.Spikes.Phase0.Editor
{
    /// <summary>
    /// Gatilho por arquivo para automação sem MCP (o MCP trava quando o Editor abre diálogos modais).
    /// Criar um arquivo vazio em Temp/sp-commands/&lt;comando&gt; executa o comando no próximo tick do
    /// Editor; o resultado vai para Temp/sp-commands/&lt;comando&gt;.done (e para o Editor.log).
    /// Comandos: refresh, build-scenes, boot-video, boot-passthrough, build-apk.
    /// Ver tools/unity-cmd.ps1.
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
                return; // um comando por tick (refresh/compilação podem recarregar o domínio)
            }
        }

        private static void Run(string command)
        {
            Debug.Log("[ScreenPortal] CMD " + command);
            try
            {
                switch (command)
                {
                    case "refresh":
                        Done(command, "OK (refresh solicitado; veja 'Reloading assemblies' no Editor.log)");
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                        break;
                    case "build-scenes":
                        Phase0SceneBuilder.BuildAll();
                        Done(command, "OK");
                        break;
                    case "boot-video":
                        Phase0SceneBuilder.BootVideo();
                        Done(command, "OK");
                        break;
                    case "boot-passthrough":
                        Phase0SceneBuilder.BootPassthrough();
                        Done(command, "OK");
                        break;
                    case "build-apk":
                        Done(command, Phase0SceneBuilder.BuildApkNow());
                        break;
                    default:
                        Done(command, "ERRO: comando desconhecido");
                        break;
                }
            }
            catch (Exception e)
            {
                Done(command, "ERRO: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void Done(string command, string result)
        {
            File.WriteAllText(Path.Combine(Dir, command + ".done"), DateTime.Now.ToString("s") + " " + result);
            Debug.Log("[ScreenPortal] CMD " + command + " → " + result);
        }
    }
}
