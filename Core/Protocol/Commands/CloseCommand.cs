﻿using System.Diagnostics;
using Vint.Core.Server;

namespace Vint.Core.Protocol.Commands;

public class CloseCommand(
    string reason
) : ICommand {
    public string Reason => reason;

    public void Execute(IPlayerConnection connection) => throw new UnreachableException();

    public override string ToString() => $"Close command {{ Reason: '{Reason}' }}";
}