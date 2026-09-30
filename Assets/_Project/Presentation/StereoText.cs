using UnityEngine;

namespace ScreenPortal.Presentation
{
    /// <summary>
    /// Material de TextMesh que aparece nos dois olhos. O material padrão da fonte ("GUI/Text Shader") sai num olho só
    /// e com a projeção errada em Single Pass Instanced / multiview (visto no Simulator na verificação da 2B).
    /// </summary>
    public static class StereoText
    {
        private const string ShaderName = "ScreenPortal/StereoText"; // Resources/StereoText.shader

        /// <summary>Material novo (o chamador o destrói) com a textura da fonte; sem o shader, cópia do da fonte.</summary>
        public static Material MaterialFor(Font font)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) return new Material(font.material);
            return new Material(shader) { name = "StereoText", mainTexture = font.material.mainTexture };
        }
    }
}
