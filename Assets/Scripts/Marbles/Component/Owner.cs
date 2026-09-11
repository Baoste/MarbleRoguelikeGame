using Unity.Entities;

namespace MarblesECS
{
    internal struct Owner : IComponentData
    {
        public Entity Player;
    }
}
