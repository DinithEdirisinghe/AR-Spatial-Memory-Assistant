using System;
using SpatialSense.Interaction;
using UnityEngine;

namespace SpatialSense.RoomScan
{
    // Built at runtime; the existing controller pointer invokes this interface.
    public sealed class ScanPanelButton : MonoBehaviour, IPointerTarget
    {
        private RoomScanMenu menu;
        private Action action;
        public bool CanSelect => isActiveAndEnabled && menu != null && menu.CanClick;
        public void Configure(RoomScanMenu owner, Action callback) { menu = owner; action = callback; }
        public void Select() { if (CanSelect) action?.Invoke(); }
    }
}
