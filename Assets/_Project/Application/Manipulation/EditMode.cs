using System;
using ScreenPortal.App.Connection;

namespace ScreenPortal.App.Manipulation
{
    /// <summary>
    /// Modo edição global (spec 3B §4, ADR 0017): só nele as telas são manipuladas (mouse, controle ou mão). Ligado pelo
    /// menu, por <c>Ctrl+Alt+E</c> (alterna) ou temporário com <c>Ctrl+Alt</c> segurado (vem no <see cref="MouseState"/>);
    /// <c>Esc</c> desliga. Toda mudança de <see cref="IsOn"/> vai ao host (<see cref="SetEditModeMessage"/>: engolir o
    /// mouse). Começa desligado a cada conexão.
    /// </summary>
    public sealed class EditMode : IDisposable
    {
        private readonly ConnectionStateMachine _connection;
        private bool _sentOn;

        public EditMode(ConnectionStateMachine connection)
        {
            _connection = connection;
            if (_connection != null) _connection.Welcomed += OnWelcomed;
        }

        /// <summary>Ligado de propósito (menu, atalho).</summary>
        public bool Enabled { get; private set; }
        /// <summary>Ligado só enquanto <c>Ctrl+Alt</c> está segurado.</summary>
        public bool Temporary { get; private set; }
        /// <summary>Menu do app aberto pelo mouse: segura a edição (o mouse vira o cursor 3D que escolhe os itens).</summary>
        public bool MenuHold { get; private set; }
        public bool IsOn => Enabled || Temporary || MenuHold;

        public void SetMenuHold(bool hold)
        {
            if (MenuHold == hold) return;
            MenuHold = hold;
            Publish();
        }

        public event Action Changed;

        private bool _cancel;

        /// <summary>Esc com um arraste em andamento: o ponteiro do mouse cancela no próximo quadro.</summary>
        public void RequestCancel() => _cancel = true;

        public bool ConsumeCancel()
        {
            bool c = _cancel;
            _cancel = false;
            return c;
        }

        public void Toggle() => Set(!Enabled);

        public void Set(bool enabled)
        {
            if (Enabled == enabled) return;
            Enabled = enabled;
            Publish();
        }

        /// <summary>1× por quadro com o estado do mouse: <c>Ctrl+Alt</c> segurado liga a edição temporária.</summary>
        public void Tick(in MouseState mouse)
        {
            bool temporary = mouse.Mode != MouseMode.Outside && mouse.CtrlAlt;
            if (temporary == Temporary) return;
            Temporary = temporary;
            Publish();
        }

        private void OnWelcomed(WelcomeMessage _)
        {
            Enabled = false;
            Temporary = false;
            MenuHold = false;
            _sentOn = false; // o host novo começa com o mouse livre
            Changed?.Invoke();
        }

        private void Publish()
        {
            bool on = IsOn;
            if (on != _sentOn && _connection != null && _connection.IsLinked)
            {
                _connection.Send(new SetEditModeMessage(on));
                _sentOn = on;
            }
            Changed?.Invoke();
        }

        public void Dispose()
        {
            if (_connection != null) _connection.Welcomed -= OnWelcomed;
        }
    }
}
