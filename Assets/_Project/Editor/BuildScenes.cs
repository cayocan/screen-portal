using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ScreenPortal.Editor
{
    public static class BuildScenes
    {
        /// <summary>Põe <paramref name="scenePath"/> como cena inicial do build, mantendo as demais.</summary>
        public static void SetFirst(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(scenePath, true) };
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.path != scenePath) scenes.Add(scene);
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[ScreenPortal] Cena inicial do build: " + scenePath);
        }
    }
}
