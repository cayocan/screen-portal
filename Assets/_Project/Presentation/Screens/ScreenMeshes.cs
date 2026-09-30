using UnityEngine;

namespace ScreenPortal.Presentation.Screens
{
    /// <summary>Objetos de malha das telas (spec 2E §4): a malha já tem o tamanho, então a escala fica 1.</summary>
    public static class ScreenMeshes
    {
        public static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            if (material != null) renderer.sharedMaterial = material;
            return go;
        }
    }
}
