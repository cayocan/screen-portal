using System;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Manipulation
{
    /// <summary>
    /// Tchau: palma aberta virada para fora (para longe da cabeça), balançando de um lado para o outro. Dispara com
    /// <see cref="Reversals"/> inversões de sentido de pelo menos <see cref="MinSwingM"/> dentro de <see cref="WindowS"/>;
    /// depois espera <see cref="CooldownS"/>. Mão fechada, pinça ou palma virada para o usuário zeram a contagem.
    /// Puro e sem alocação (um por mão).
    /// </summary>
    public sealed class WaveDetector
    {
        public const float MinSwingM = 0.05f;
        public const int Reversals = 3;
        public const double WindowS = 1.5;
        public const double CooldownS = 1.5;
        public const float FacingAwayDot = 0.5f;

        private readonly double[] _reversalTimes = new double[Reversals];
        private int _count;
        private bool _started;
        private float _extreme;
        private int _direction; // 0 = ainda sem sentido; +1 / −1
        private double _cooldownUntil = double.MinValue;

        /// <returns>true no quadro em que o tchau se completa.</returns>
        public bool Feed(double now, Vec3 palm, Vec3 palmNormal, bool open, Pose head)
        {
            var forward = head.Rotation * Vec3.Forward;
            if (now < _cooldownUntil || !open || Vec3.Dot(palmNormal.Normalized, forward) < FacingAwayDot)
            {
                Reset();
                return false;
            }

            var right = head.Rotation * Vec3.Right;
            float lateral = Vec3.Dot(palm - head.Position, right);
            if (!_started)
            {
                _started = true;
                _extreme = lateral;
                return false;
            }

            if (_direction == 0)
            {
                if (Math.Abs(lateral - _extreme) >= MinSwingM)
                {
                    _direction = Math.Sign(lateral - _extreme);
                    _extreme = lateral;
                }
                return false;
            }

            if ((lateral - _extreme) * _direction > 0f)
            {
                _extreme = lateral; // ainda indo no mesmo sentido
                return false;
            }
            if (Math.Abs(lateral - _extreme) < MinSwingM) return false;

            // inverteu o sentido com amplitude suficiente
            _direction = -_direction;
            _extreme = lateral;
            int kept = 0;
            for (int i = 0; i < _count; i++)
                if (now - _reversalTimes[i] <= WindowS) _reversalTimes[kept++] = _reversalTimes[i];
            _count = kept;
            if (_count < Reversals) _reversalTimes[_count++] = now;
            if (_count < Reversals) return false;

            Reset();
            _cooldownUntil = now + CooldownS;
            return true;
        }

        private void Reset()
        {
            _count = 0;
            _started = false;
            _direction = 0;
        }
    }
}
