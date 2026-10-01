using System;
using System.IO;
using ScreenPortal.App.Session;
using UnityEngine;

namespace ScreenPortal.Infrastructure.Unity
{
    /// <summary>Última sessão num arquivo do app (persistentDataPath). Grava num temporário e troca: nunca meio arquivo.</summary>
    public sealed class FileSessionStore : ISessionStore
    {
        private readonly string _path;

        public FileSessionStore(string path) => _path = path;

        public string Load()
        {
            try
            {
                return File.Exists(_path) ? File.ReadAllText(_path) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ScreenPortal] sessão ilegível ({e.Message}); abrindo vazio.");
                return null;
            }
        }

        public void Save(string text)
        {
            try
            {
                string temp = _path + ".tmp";
                File.WriteAllText(temp, text);
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(temp, _path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ScreenPortal] falha ao salvar a sessão: {e.Message}");
            }
        }
    }
}
