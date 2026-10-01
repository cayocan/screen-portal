using TMPro;
using UnityEngine;

namespace ScreenPortal.Presentation
{
    /// <summary>
    /// Texto 3D da interface em TextMeshPro (SDF): nítido em qualquer tamanho e distância, nos dois olhos (o shader
    /// "TextMeshPro/Distance Field" tem stereo instancing). Substitui o TextMesh legado, cuja fonte bitmap encolhida
    /// sem mipmaps ficava serrilhada no Quest (ADR 0013).
    /// </summary>
    public static class UiText
    {
        /// <summary>Altura de "Hg" por unidade de fontSize (medido no Editor: 0,1117 m), igual ao TextMesh com
        /// fontSize × characterSize = 1. Os tamanhos antigos passam como <c>fontSize × characterSize</c>.</summary>
        public static TextMeshPro Create(string name, Transform parent, float size, TextAlignmentOptions alignment, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshPro>();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.richText = false;
            text.rectTransform.sizeDelta = Vector2.zero; // sem caixa: o texto se alinha ao pivô
            return text;
        }
    }
}
