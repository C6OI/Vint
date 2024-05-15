﻿using LinqToDB.Mapping;

namespace Vint.Core.Database.Models;

[Table("HullSkins")]
public class HullSkin {
    [NotColumn] readonly Player _player = null!;
    [PrimaryKey(2)] public required long Id { get; init; }

    [PrimaryKey(1)] public required long HullId { get; init; }

    [Association(ThisKey = nameof(PlayerId), OtherKey = nameof(Player.Id))]
    public required Player Player {
        get => _player;
        init {
            _player = value;
            PlayerId = value.Id;
        }
    }

    [PrimaryKey(0)] public long PlayerId { get; private set; }
}
