using System;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Cursor
{
    /// <summary>
    /// Cursor 3D fora das telas (spec 3B §4): uma direção (azimute/elevação, mundo) em volta da cabeça. Nasce no ponto de
    /// saída da tela (1,5 px para fora da borda) e anda pelos deltas acumulados do host × graus por pixel da tela de onde
    /// saiu (a velocidade sentida não muda). No Mundo, quando o raio cabeça → cursor toca o conteúdo de uma tela com vídeo,
    /// manda <see cref="CursorEnterMessage"/> com o pixel do toque. A tela de saída só aceita a volta depois que o cursor
    /// esteve fora dela (sem pingue-pongue). Puro, sem alocação por quadro.
    /// </summary>
    public sealed class WorldCursor
    {
        public const float MaxElevationDeg = 85f;
        public const float EdgeNudgePx = 1.5f;

        private readonly WorkspaceStore _store;
        private readonly IHeadPoseSource _head;
        private readonly Action<ControlMessage> _send;

        private uint _epoch;
        private bool _started;
        private float _az0, _el0;
        private float _az, _el;
        private float _degPerPx = 0.03f;
        private float _distance = 1.3f;
        private float _metersPerPixel = 0.0005f;
        private uint _exitScreen;
        private bool _leftExitScreen;
        private bool _entered;

        public WorldCursor(WorkspaceStore store, IHeadPoseSource head, Action<ControlMessage> send)
        {
            _store = store;
            _head = head;
            _send = send;
        }

        /// <summary>Mundo ou Edição neste quadro (o cursor 3D aparece).</summary>
        public bool Active { get; private set; }
        public Vec3 Origin { get; private set; }
        public Vec3 Direction { get; private set; }
        /// <summary>Ponto do cursor 3D (na distância da tela de onde saiu).</summary>
        public Vec3 Point => Origin + Direction * _distance;
        /// <summary>Metros por pixel da forma: o cursor 3D tem o tamanho angular que tinha na tela.</summary>
        public float MetersPerPixel => _metersPerPixel;

        /// <summary>Cursor perdido (Ctrl+Alt+Home): no próximo quadro ele fica à frente dos olhos, na distância de antes.</summary>
        public void CenterOnView() => _center = true;

        private bool _center;

        public void Tick(in MouseState s)
        {
            Active = s.Mode == MouseMode.World || s.Mode == MouseMode.Edit;
            if (!Active)
            {
                _started = false;
                _center = false;
                return;
            }
            var head = _head.HeadPose.Position;
            if (!_started || s.Epoch != _epoch) Begin(in s, head);
            if (_center)
            {
                _center = false;
                AnglesOf(_head.HeadPose.Rotation * Vec3.Forward, out float az, out float el);
                _az0 = az - s.Dx * _degPerPx; // a partir daqui os deltas continuam valendo
                _el0 = el + s.Dy * _degPerPx;
                _leftExitScreen = true;
            }
            _az = _az0 + s.Dx * _degPerPx;
            _el = Clamp(_el0 - s.Dy * _degPerPx, -MaxElevationDeg, MaxElevationDeg);
            Origin = head;
            Direction = DirectionOf(_az, _el);
            if (s.Mode == MouseMode.World && !_entered) TryEnter(head);
        }

        private void Begin(in MouseState s, Vec3 head)
        {
            bool had = _started && Direction.Length > 1e-4f;
            _epoch = s.Epoch;
            _started = true;
            _entered = false;
            _exitScreen = 0;
            _leftExitScreen = true;
            var ws = _store.State;
            if (s.ScreenKey != 0 && s.ScreenKey <= int.MaxValue && ws.TryGetScreen(new ScreenId((int)s.ScreenKey), out var screen))
            {
                float px = s.X + 0.5f, py = s.Y + 0.5f;
                int w = screen.Resolution.Width, h = screen.Resolution.Height;
                if (s.X <= 0) px = -EdgeNudgePx;
                else if (s.X >= w - 1) px = w + EdgeNudgePx;
                if (s.Y <= 0) py = -EdgeNudgePx;
                else if (s.Y >= h - 1) py = h + EdgeNudgePx;
                CursorPlacement.PixelToLocal(screen, px, py, out float lx, out float ly);
                var point = CursorPlacement.LocalPose(screen, ws.Surface, lx, ly, 0f).Position;
                var toPoint = point - head;
                _distance = Math.Max(0.3f, toPoint.Length);
                _metersPerPixel = CursorPlacement.MetersPerPixel(screen);
                _degPerPx = (float)(Math.Atan(_metersPerPixel / _distance) * Angles.RadToDeg);
                AnglesOf(toPoint, out _az0, out _el0);
                _exitScreen = s.ScreenKey;
                _leftExitScreen = false;
            }
            else if (had)
            {
                _az0 = _az; // continua de onde o cursor 3D estava (edição ↔ mundo)
                _el0 = _el;
            }
            else AnglesOf(_head.HeadPose.Rotation * Vec3.Forward, out _az0, out _el0); // sem histórico: à frente da cabeça
            _az = _az0;
            _el = _el0;
        }

        private void TryEnter(Vec3 head)
        {
            var ws = _store.State;
            var hit = ScreenHitTest.Pick(ws, head, Direction);
            VirtualScreen screen = null;
            bool onContent = hit.Region == HitRegion.Content && ws.TryGetScreen(hit.Screen, out screen) && screen.Source.HasVideo;
            uint key = onContent ? (uint)hit.Screen.Value : 0;
            if (!_leftExitScreen)
            {
                if (key == _exitScreen) return;
                _leftExitScreen = true;
            }
            if (!onContent) return;
            CursorPlacement.LocalToPixel(screen, hit.LocalX, hit.LocalY, out int px, out int py);
            _entered = true; // um pedido por saída; o host responde mudando a epoch
            _send(new CursorEnterMessage(key, px, py));
        }

        public static Vec3 DirectionOf(float azDeg, float elDeg)
        {
            double az = azDeg * Angles.DegToRad, el = elDeg * Angles.DegToRad;
            return new Vec3((float)(Math.Sin(az) * Math.Cos(el)), (float)Math.Sin(el), (float)(Math.Cos(az) * Math.Cos(el)));
        }

        public static void AnglesOf(Vec3 v, out float azDeg, out float elDeg)
        {
            float len = v.Length;
            if (len < 1e-6f)
            {
                azDeg = elDeg = 0f;
                return;
            }
            azDeg = (float)(Math.Atan2(v.X, v.Z) * Angles.RadToDeg);
            elDeg = (float)(Math.Asin(Clamp(v.Y / len, -1f, 1f)) * Angles.RadToDeg);
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
