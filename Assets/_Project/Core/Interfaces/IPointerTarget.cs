namespace SpatialSense.Interaction
{
    public interface IPointerTarget
    {
        bool CanSelect { get; }
        void Select();
    }
}
