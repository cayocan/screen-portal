using ScreenPortal.App.Connection;
using UnityEngine;

namespace ScreenPortal.Presentation.Connection
{
    /// <summary>Painel de texto com o status da conexão (acaba a tela preta). "Conectado" some após 2 s.</summary>
    public sealed class ConnectionStatusView : MonoBehaviour
    {
        [SerializeField] private TextMesh label;
        private float _hideAt = -1f;

        public void Bind(ConnectionStateMachine machine)
        {
            machine.StatusChanged += Show;
            Show(machine.Status);
        }

        private void Show(ConnectionStatus status)
        {
            label.text = ConnectionStatusText.Format(status);
            label.gameObject.SetActive(label.text.Length > 0);
            _hideAt = status.Kind == ConnectionStatusKind.Connected ? Time.realtimeSinceStartup + 2f : -1f;
        }

        private void Update()
        {
            if (_hideAt > 0f && Time.realtimeSinceStartup >= _hideAt)
            {
                _hideAt = -1f;
                label.gameObject.SetActive(false);
            }
        }
    }
}
