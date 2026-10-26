using SpatialSense.Interaction;
using UnityEngine;

namespace SpatialSense.ObjectMenu
{
    public enum ObjectMenuAction { SelectObject, Register, Find, Forget, Close, SimulateVision }

    public sealed class ObjectMenuButton : MonoBehaviour, IPointerTarget
    {
        private MultiObjectMenu menu;
        private ObjectMenuAction action;
        private int slot;
        public bool CanSelect => isActiveAndEnabled && menu != null && menu.CanUse(action, slot);

        public void Configure(MultiObjectMenu owner, ObjectMenuAction command, int objectSlot = -1)
        {
            menu = owner;
            action = command;
            slot = objectSlot;
        }

        public void Select()
        {
            if (CanSelect) menu.Execute(action, slot);
        }
    }
}
