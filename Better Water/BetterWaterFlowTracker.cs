using BlockEntities;
using BlockTypes;
using Pipliz;

namespace BetterWater
{
    [BlockEntityAutoLoader(200f)]
    public sealed class BetterWaterFlowTracker :
        ISingleBlockEntityMapping,
        IEntityManager,
        IUpdatedAdjacentType,
        IChangedWithType
    {
        public ItemTypes.ItemType TypeToRegister
        {
            get { return BuiltinBlocks.Types.water; }
        }

        public void OnUpdateAdjacent(AdjacentUpdateData data)
        {
            if (data.UpdatePositionType == BuiltinBlocks.Types.water)
            {
                BetterWaterModEntry.OnWaterAdjacent(data.UpdatePosition);
            }
        }

        public void OnChangedWithType(
            Chunk chunk,
            BlockChangeRequestOrigin requestOrigin,
            Vector3Int blockPosition,
            ItemTypes.ItemType typeOld,
            ItemTypes.ItemType typeNew)
        {
            BetterWaterModEntry.OnWaterChanged(blockPosition, typeOld, typeNew);
        }
    }
}
