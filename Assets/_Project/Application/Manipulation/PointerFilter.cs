using System;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Manipulation
{
    /// <summary>
    /// Decorator de um ponteiro (controle ou mão):
    /// - controle parado por <see cref="IdleSeconds"/> (sem mover <see cref="IdleMoveM"/>, sem girar
    ///   <see cref="IdleTurnDeg"/>, sem botão) perde o laser e não interage; mexer ou apertar qualquer botão traz de volta;
    /// - mão: alimenta o detector de tchau (alterna <see cref="HandsMode"/>); com as mãos desligadas o ponteiro some.
    /// </summary>
    public sealed class PointerFilter : IManipulationInput
    {
        public const double IdleSeconds = 3.0;
        public const float IdleMoveM = 0.01f;
        public const float IdleTurnDeg = 2f;
        private static readonly float IdleTurnCos = (float)Math.Cos(IdleTurnDeg * Math.PI / 180.0);

        private readonly IManipulationInput _inner;
        private readonly HandsMode _hands;
        private readonly IHeadPoseSource _head;
        private readonly IClock _clock;
        private readonly WaveDetector _wave = new WaveDetector();
        private bool _hasAnchor;
        private Vec3 _anchorOrigin;
        private Vec3 _anchorDirection;
        private double _lastActive;

        public PointerFilter(IManipulationInput inner, HandsMode hands, IHeadPoseSource head, IClock clock)
        {
            _inner = inner;
            _hands = hands;
            _head = head;
            _clock = clock;
        }

        public bool Read(out ManipulationInput input)
        {
            if (!_inner.Read(out input))
            {
                _hasAnchor = false;
                return false;
            }
            double now = _clock.NowSeconds;

            if (input.IsHand)
            {
                _hasAnchor = false; // ao voltar ao controle, o tempo parado recomeça
                if (_wave.Feed(now, input.PalmPosition, input.PalmNormal, input.HandOpen, _head.HeadPose)) _hands.Toggle();
                return _hands.Enabled;
            }

            if (!input.HasRay) return true;
            bool moved = !_hasAnchor
                         || (input.Origin - _anchorOrigin).Length > IdleMoveM
                         || Vec3.Dot(input.Direction.Normalized, _anchorDirection) < IdleTurnCos;
            if (moved || input.AnyButton)
            {
                _hasAnchor = true;
                _anchorOrigin = input.Origin;
                _anchorDirection = input.Direction.Normalized;
                _lastActive = now;
            }
            if (now - _lastActive > IdleSeconds) input.HasRay = false;
            return true;
        }
    }
}
