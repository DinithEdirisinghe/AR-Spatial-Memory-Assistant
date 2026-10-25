using UnityEngine;

namespace SpatialSense.RoomDemo
{
    // Keep the rendered outline aligned when MRUK refines the anchor's pose.
    public sealed class RoomOutlineAttachment : MonoBehaviour
    {
        private Transform anchor;
        public void Configure(Transform source)
        {
            anchor = source;
            FollowAnchor();
        }

        private void LateUpdate() => FollowAnchor();

        private void FollowAnchor()
        {
            if (anchor == null)
            {
                gameObject.SetActive(false);
                return;
            }
            transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            transform.localScale = anchor.lossyScale;
        }
    }
}
