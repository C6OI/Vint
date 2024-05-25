using Vint.Core.ECS.Components.Quest.Type;
using Vint.Core.ECS.Entities;
using Vint.Core.ECS.Enums;
using Vint.Core.Protocol.Attributes;

namespace Vint.Core.ECS.Templates.Quests;

[ProtocolId(1493731191898)]
public class SupplyQuestTemplate : QuestTemplate {
    public override IEntity Create(
        IEntity user,
        int index,
        float currentValue,
        float targetValue,
        IEntity reward,
        int rewardAmount,
        QuestConditionType? conditionType,
        long conditionValue,
        DateTimeOffset expireDate,
        QuestRarityType rarityType,
        bool isCompleted) {
        IEntity entity = base.Create(user,
            "quests/daily/supply",
            index,
            currentValue,
            targetValue,
            reward,
            rewardAmount,
            conditionType,
            conditionValue,
            expireDate,
            rarityType,
            isCompleted);

        entity.AddComponent<SupplyQuestComponent>();
        return entity;
    }
}