using System;

namespace Project.Core.Integration
{
    public enum ObservationOrigin { StoredAnchor, ManualController, SimulatedVision, Vision }
    public enum ObjectLocationState { NotSaved, Saved, WaitingForRoom, Locating, Ready, Saving, Removing, Unavailable, CleanupPending }
    public enum ApplicationPhase { Starting, LoadingRoom, SelectingObject, AwaitingObservation, LocatingObject, SavingObject, RemovingObject, Ready, Searching, Found, Cancelled, Unavailable }

    public static class ObservationPolicy
    {
        public static readonly System.Collections.Generic.IReadOnlyList<string> ObjectIds =
            Array.AsReadOnly(new[] { "keys", "wallet", "remote" });

        public static string NameFor(string id)
        {
            switch (id)
            {
                case "keys": return "My keys";
                case "wallet": return "My wallet";
                case "remote": return "My remote";
                default: return null;
            }
        }

        public static string IdFor(string name)
        {
            foreach (var id in ObjectIds) if (NameFor(id) == name) return id;
            return null;
        }

        public static bool Validate(string objectId, string observationId, float x, float y, float z,
            float identityConfidence, float positionConfidence, DateTime observedAtUtc, DateTime nowUtc,
            string roomId, string expectedRoomId, string worldFrameId, string expectedFrameId,
            out string reason)
        {
            reason = null;
            if (NameFor(objectId) == null) reason = "Unknown object ID.";
            else if (!Guid.TryParse(observationId, out var observationGuid) || observationGuid == Guid.Empty) reason = "A unique observation UUID is required.";
            else if (!Finite(x) || !Finite(y) || !Finite(z)) reason = "World position is not finite.";
            else if (!Finite(identityConfidence) || identityConfidence < 0f || identityConfidence > 1f
                || !Finite(positionConfidence) || positionConfidence < 0f || positionConfidence > 1f) reason = "Confidence must be between zero and one.";
            else if (identityConfidence < 0.8f || positionConfidence < 0.6f) reason = "Observation confidence is below the demo acceptance threshold.";
            else if (observedAtUtc.Kind != DateTimeKind.Utc || nowUtc.Kind != DateTimeKind.Utc) reason = "Observation time must use UTC.";
            else if ((nowUtc - observedAtUtc).TotalSeconds > 30) reason = "Observation is stale.";
            else if ((observedAtUtc - nowUtc).TotalSeconds > 5) reason = "Observation time is in the future.";
            else if (!Guid.TryParse(roomId, out var room) || room == Guid.Empty || !Guid.TryParse(expectedRoomId, out var expectedRoom) || room != expectedRoom) reason = "Observation belongs to a different room.";
            else if (!Guid.TryParse(worldFrameId, out var frame) || frame == Guid.Empty || !Guid.TryParse(expectedFrameId, out var expectedFrame) || frame != expectedFrame) reason = "Observation belongs to an expired coordinate frame.";
            return reason == null;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
