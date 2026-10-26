using System;
using SpatialSense.ManualDemo;
using UnityEngine;

namespace Project.Core.Integration
{
    public sealed class SimulatedVisionObservationSource : MonoBehaviour, IObjectObservationSource
    {
        [SerializeField] private SpatialApplicationHub hub;
        [SerializeField] private ManualObjectDemo flow;
        [SerializeField] private OVRCameraRig cameraRig;
        private ObservationContext armedContext;
        private string objectId;
        public bool IsArmed { get; private set; }
        public event Action<ObjectObservation> ObservationReceived;
        public string Prompt => "SIMULATED VISION TEST: hold the right controller beside the selected object and squeeze the RIGHT GRIP once.";

        public void Configure(SpatialApplicationHub application, ManualObjectDemo objectFlow, OVRCameraRig rig)
        {
            hub = application; flow = objectFlow; cameraRig = rig;
        }

        public bool Arm(string objectName)
        {
            if (hub == null || !hub.isActiveAndEnabled || ObservationReceived == null || !hub.TryGetObservationContext(out armedContext)) return false;
            objectId = ObservationPolicy.IdFor(objectName);
            IsArmed = objectId != null;
            return IsArmed;
        }

        public void Cancel() => IsArmed = false;

        private void Update()
        {
            if (!IsArmed || hub == null || flow == null) return;
            if (flow.IsMenuOpen || !hub.TryGetObservationContext(out var current)
                || current.WorldFrameId != armedContext.WorldFrameId || current.RoomId != armedContext.RoomId)
            {
                Cancel();
                flow.SetInputFeedback("Simulation cancelled because the menu or room changed. Arm the test again.");
                return;
            }
            if (!OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch)) return;
            if (cameraRig == null || cameraRig.rightControllerAnchor == null
                || !OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch)
                || !OVRInput.GetControllerOrientationTracked(OVRInput.Controller.RTouch))
            {
                flow.SetInputFeedback("Right controller tracking is unavailable. Keep it in view and squeeze the grip again.");
                return;
            }
            IsArmed = false;
            ObservationReceived?.Invoke(new ObjectObservation
            {
                ObservationId = Guid.NewGuid().ToString("D"), ObjectId = objectId,
                WorldPosition = cameraRig.rightControllerAnchor.position, ObservedAtUtc = DateTime.UtcNow,
                IdentityConfidence = 0.95f, PositionConfidence = 0.9f, Origin = ObservationOrigin.SimulatedVision,
                RoomId = current.RoomId, WorldFrameId = current.WorldFrameId
            });
        }
    }
}
