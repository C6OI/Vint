﻿using Vint.Core.ECS.Components;
using Vint.Core.Protocol.Attributes;

namespace Vint.Core.ECS.Templates.Leagues;

[ProtocolId(1502713060357)]
public class LeagueConfigComponent(int leagueIndex, double reputationToEnter) : IComponent {
    public int LeagueIndex { get; private set; } = leagueIndex;

    public double ReputationToEnter { get; private set; } = reputationToEnter;
}