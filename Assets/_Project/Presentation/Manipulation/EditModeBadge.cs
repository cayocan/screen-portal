using System;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using UnityEngine;

namespace ScreenPortal.Presentation.Manipulation
{
    /// <summary>
    /// Selo "Modo edição" na parte de baixo da visão (spec 3B §4): segue a cabeça com atraso suave (não gruda no rosto) e
    /// some fora da edição. Texto fixo, sem alocação por quadro.
    /// </summary>
    public sealed class EditModeBadge : IDisposable
    {
        private const string Label = "Modo edição · Esc para sair";
        private const string TemporaryLabel = "Modo edição (Ctrl+Alt)";
        private static readonly Vector3 Offset = new Vector3(0f, -0.3f, 1.1f);
        private const float Follow = 4f; // 1/s

        private readonly EditMode _edit;
        private readonly IHeadPoseSource _head;
        private readonly TMPro.TextMeshPro _text;
        private bool _placed;

        public EditModeBadge(Transform parent, EditMode edit, IHeadPoseSource head)
        {
            _edit = edit;
            _head = head;
            _text = UiText.Create("EditModeBadge", parent, 0.5f, TMPro.TextAlignmentOptions.Center, new Color(1f, 0.78f, 0.25f));
            _text.text = Label;
            _text.gameObject.SetActive(false);
        }

        public void Tick(float deltaTime)
        {
            if (!_edit.IsOn)
            {
                if (_text.gameObject.activeSelf) _text.gameObject.SetActive(false);
                _placed = false;
                return;
            }
            string label = _edit.Enabled ? Label : TemporaryLabel;
            if (!ReferenceEquals(_text.text, label)) _text.text = label;
            var pose = _head.HeadPose;
            var rotation = pose.Rotation.ToUnity();
            var target = pose.Position.ToUnity() + rotation * Offset;
            var t = _text.transform;
            if (!_placed)
            {
                t.localPosition = target;
                _placed = true;
            }
            else t.localPosition = Vector3.Lerp(t.localPosition, target, 1f - Mathf.Exp(-Follow * deltaTime));
            var toText = t.localPosition - pose.Position.ToUnity();
            if (toText.sqrMagnitude > 1e-6f) t.localRotation = Quaternion.LookRotation(toText, Vector3.up);
            if (!_text.gameObject.activeSelf) _text.gameObject.SetActive(true);
        }

        public void Dispose()
        {
            if (_text == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(_text.gameObject);
            else UnityEngine.Object.DestroyImmediate(_text.gameObject);
        }
    }
}
