using ScreenPortal.App.Manipulation;
using ScreenPortal.Domain.Spatial;
using UnityEngine;

namespace ScreenPortal.Infrastructure.Unity
{
    /// <summary>
    /// Adapter (brief §8.3): mão rastreada (OVRHand) ou, sem mão, controle Touch → <see cref="ManipulationInput"/>.
    /// Mão: pinça do indicador = selecionar; mão fechada = solto; puxar/empurrar = distância.
    /// Controle: gatilho, grip, analógico Y; B/Y cancela; X/Y (esquerdo) desfaz/refaz.
    /// </summary>
    public sealed class OvrPointerInput : IManipulationInput
    {
        public const float TriggerThreshold = 0.55f;
        public const float StickDeadZone = 0.2f;
        public const float StickSpeedMps = 1.5f;
        public const float HandPushGain = 3f;
        public const float FistStrength = 0.9f;
        public const float OpenPinchStrength = 0.5f;
        /// <summary>Normal da palma no espaço da mão (calibrado na 2B para a esquerda; OpenXR: −Y nas duas mãos).</summary>
        private static readonly Vector3 PalmNormalLocal = Vector3.down;

        private readonly OVRInput.Controller _controller;
        private readonly OVRHand _hand;
        private readonly Transform _trackingSpace;
        private readonly Transform _head;
        private readonly bool _isLeft;
        private float _lastHandDistance = -1f;

        public OvrPointerInput(OVRInput.Controller controller, OVRHand hand, Transform trackingSpace, Transform head, bool isLeft)
        {
            _controller = controller;
            _hand = hand;
            _trackingSpace = trackingSpace;
            _head = head;
            _isLeft = isLeft;
        }

        public bool Read(out ManipulationInput input)
        {
            input = default;
            if (_hand != null && _hand.IsTracked && _hand.IsPointerPoseValid) return ReadHand(ref input);
            _lastHandDistance = -1f;
            if (_trackingSpace == null || !OVRInput.IsControllerConnected(_controller)) return false;

            var position = _trackingSpace.TransformPoint(OVRInput.GetLocalControllerPosition(_controller));
            var rotation = _trackingSpace.rotation * OVRInput.GetLocalControllerRotation(_controller);
            input.HasRay = true;
            input.Origin = ToDomain(position);
            input.Direction = ToDomain(rotation * Vector3.forward);
            input.SelectHeld = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, _controller) > TriggerThreshold;
            input.GripHeld = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, _controller) > TriggerThreshold;
            float stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, _controller).y;
            input.DistanceDeltaM = Mathf.Abs(stick) < StickDeadZone ? 0f : stick * StickSpeedMps * Time.deltaTime;
            input.CancelPressed = OVRInput.GetDown(OVRInput.Button.Two, _controller); // B (direito) / Y (esquerdo)
            if (_isLeft)
            {
                input.UndoPressed = OVRInput.GetDown(OVRInput.Button.One, _controller); // X
                input.RedoPressed = input.CancelPressed;                                  // Y
            }
            return true;
        }

        private bool ReadHand(ref ManipulationInput input)
        {
            var pointer = _hand.PointerPose;
            input.HasRay = true;
            input.Origin = ToDomain(pointer.position);
            input.Direction = ToDomain(pointer.forward);
            input.SelectHeld = _hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            input.GripHeld = _hand.GetFingerPinchStrength(OVRHand.HandFinger.Middle) > FistStrength
                             && _hand.GetFingerPinchStrength(OVRHand.HandFinger.Ring) > FistStrength;
            input.IsHand = true;
            input.PalmPosition = ToDomain(_hand.transform.position);
            input.PalmNormal = ToDomain(_hand.transform.rotation * PalmNormalLocal);
            input.HandOpen = !input.SelectHeld && !input.GripHeld
                             && _hand.GetFingerPinchStrength(OVRHand.HandFinger.Index) < OpenPinchStrength;
            float distance = _head != null ? Vector3.Distance(_hand.transform.position, _head.position) : 0f;
            input.DistanceDeltaM = _lastHandDistance < 0f || !input.SelectHeld ? 0f : (distance - _lastHandDistance) * HandPushGain;
            _lastHandDistance = distance;
            return true;
        }

        private static Vec3 ToDomain(Vector3 v) => new Vec3(v.x, v.y, v.z);
    }
}
