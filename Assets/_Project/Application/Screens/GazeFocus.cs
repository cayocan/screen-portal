using System;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Screens
{
    /// <summary>
    /// Tela em foco pelo olhar (spec 2F §2) até o cursor espacial da Fase 3: a tela com vídeo mais perto do centro da
    /// visão, dentro de <see cref="MaxAngleDeg"/>. Histerese: vira foco depois de <see cref="AcquireSeconds"/> sendo a
    /// candidata; sem candidata (ou com outra), o foco atual cai depois de <see cref="ReleaseSeconds"/>. Sem alocação.
    /// </summary>
    public sealed class GazeFocus
    {
        public const float MaxAngleDeg = 25f;
        public const double AcquireSeconds = 0.3;
        public const double ReleaseSeconds = 1.0;
        private static readonly float MinCos = (float)Math.Cos(MaxAngleDeg * Math.PI / 180.0);

        private ScreenId _focus = ScreenId.None;
        private ScreenId _candidate = ScreenId.None;
        private double _candidateSince;
        private double _focusLastSeen;

        public ScreenId Focus => _focus;

        public ScreenId Tick(double now, Pose head, Workspace ws)
        {
            var candidate = Closest(head, ws);
            if (!candidate.Equals(_candidate))
            {
                _candidate = candidate;
                _candidateSince = now;
            }
            if (!_focus.IsNone && candidate.Equals(_focus)) _focusLastSeen = now;

            if (!candidate.IsNone && !candidate.Equals(_focus) && now - _candidateSince >= AcquireSeconds - 1e-9)
            {
                _focus = candidate;
                _focusLastSeen = now;
            }
            else if (!_focus.IsNone && (!ws.Contains(_focus) || candidate.IsNone && now - _focusLastSeen > ReleaseSeconds))
            {
                _focus = ScreenId.None;
            }
            return _focus;
        }

        private static ScreenId Closest(Pose head, Workspace ws)
        {
            var forward = (head.Rotation * Vec3.Forward).Normalized;
            var best = ScreenId.None;
            float bestCos = MinCos;
            var screens = ws.Screens;
            for (int i = 0; i < screens.Count; i++)
            {
                var screen = screens[i];
                if (!screen.Source.HasVideo) continue;
                var center = screen.Placement.IsLoose ? screen.Placement.LoosePose.Position : ws.Surface.ToPose(screen.Placement).Position;
                var to = center - head.Position;
                if (to.Length < 1e-4f) continue;
                float cos = Vec3.Dot(forward, to.Normalized);
                if (cos < bestCos) continue;
                bestCos = cos;
                best = screen.Id;
            }
            return best;
        }
    }
}
