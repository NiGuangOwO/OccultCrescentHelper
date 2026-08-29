using BOCCHI.Data;
using BOCCHI.Enums;
using ECommons.DalamudServices;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.Automator;

public enum NavigationType
{
    Walk,
    ReturnWalk,
    ReturnTeleportWalk,
    WalkTeleportWalk,
}

public static class SmartNavigation
{
    private const float RETURN_BASE_COST = 75f;

    public static NavigationType Decide(Vector3 playerPosition, Vector3 destination, AethernetData closestToDestination)
    {
        var closestToPlayer = AethernetData.GetClosestToPlayer();

        var costToWalkToNearestShard = Vector3.Distance(playerPosition, closestToPlayer.Position);
        var costToWalkFromEventShardToEvent = Vector3.Distance(closestToDestination.Position, destination);
        var costToWalkToEventDirectly = Vector3.Distance(playerPosition, destination);

        // Return teleports you to the starting location of the CURRENT zone, not a fixed BaseCamp.
        // Using the southern BaseCamp position here undercosts ReturnWalk for north-horn encounters
        // (e.g. the Pallid Demon CE), making the bot walk across the whole map from the camp
        // instead of teleporting to the nearest aetheryte first.
        var returnPosition = ZoneData.StartingLocations.TryGetValue(Svc.ClientState.TerritoryType, out var start)
            ? start
            : Aethernet.BaseCamp.GetData().Position;
        var costToReturnThenWalk = RETURN_BASE_COST + Vector3.Distance(returnPosition, destination);
        var costToReturnTeleportThenWalk = RETURN_BASE_COST + costToWalkFromEventShardToEvent;
        var costToWalkToShardThenEvent = costToWalkToNearestShard + costToWalkFromEventShardToEvent;

        var costs = new Dictionary<NavigationType, float>
        {
            { NavigationType.Walk, costToWalkToEventDirectly },
            { NavigationType.ReturnWalk, costToReturnThenWalk },
            { NavigationType.ReturnTeleportWalk, costToReturnTeleportThenWalk },
            { NavigationType.WalkTeleportWalk, costToWalkToShardThenEvent },
        };

        Svc.Log.Debug("Closest Aethernet: " + closestToDestination.Aethernet.ToFriendlyString());
        foreach (var (type, cost) in costs)
        {
            Svc.Log.Debug($"{type} - {cost:f2}");
        }

        return costs.OrderBy(kv => kv.Value).First().Key;
    }
}
