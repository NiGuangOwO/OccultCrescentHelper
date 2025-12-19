using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using BOCCHI.Modules.CriticalEncounters;
using BOCCHI.Modules.StateManager;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Chain;
using Ocelot.IPC;
using System;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Modules.Automator;

public class CriticalEncounter : Activity
{
    private readonly CriticalEncountersModule source;

    private DynamicEvent Encounter
    {
        get => source.CriticalEncounters[data.Id];
    }

    private bool finalDestination = false;

    public CriticalEncounter(EventData data, Lifestream lifestream, VNavmesh vnav, AutomatorModule module, CriticalEncountersModule source)
        : base(data, lifestream, vnav, module)
    {
        this.source = source;

        handlers.Add(ActivityState.WaitingToStartCriticalEncounter, GetWaitingToStartCriticalEncounterChain);
    }

    protected override TaskManagerTask GetPathfindingWatcher(StateManagerModule states)
    {
        return new TaskManagerTask(() =>
        {
            if (!IsValid())
            {
                throw new Exception("Activity is no longer valid.");
            }

            if (!finalDestination && IsCloseToZone())
            {
                // Get all players in the zone
                var playersInZone = Svc.Objects
                    .Where(o => o.ObjectKind == ObjectKind.Player)
                    .Where(o => Vector3.Distance(o.Position, GetPosition()) <= (data.Radius ?? GetRadius()))
                    .ToList();

                if (playersInZone.Count > 0)
                {
                    var minX = playersInZone.Min(p => p.Position.X);
                    var maxX = playersInZone.Max(p => p.Position.X);
                    var minY = playersInZone.Min(p => p.Position.Z);
                    var maxY = playersInZone.Max(p => p.Position.Z);
                    var rand = module.GetModule<AutomatorModule>().random;

                    // 处理所有玩家位置相同的情况
                    if (minX == maxX)
                    {
                        var randomOffset = (float)(rand.NextDouble() * 2.0);
                        maxX += randomOffset;
                    }

                    if (minY == maxY)
                    {
                        var randomOffset = (float)(rand.NextDouble() * 2.0);
                        maxY += randomOffset;
                    }

                    // Choose a random point within the bounding box of players
                    var randX = (float)(minX + rand.NextDouble() * (maxX - minX));
                    var randY = (float)(minY + rand.NextDouble() * (maxY - minY));
                    var randomPoint = new Vector3(randX, GetPosition().Y, randY);

                    module.Debug($"Pathfinding to random point: {randomPoint} (MinX: {minX}, MaxX: {maxX}, MinY: {minY}, MaxY: {maxY})");

                    vnav.PathfindAndMoveTo(randomPoint, false);
                    finalDestination = true;
                }
            }

            if (!finalDestination && IsInZone())
            {
                if (vnav.IsRunning())
                {
                    vnav.Stop();
                }

                return true;
            }

            var critical = module.GetModule<CriticalEncountersModule>();
            var encounter = critical.CriticalEncounters[data.Id];

            if (encounter.State != DynamicEventState.Register)
            {
                throw new Exception("This event started without you");
            }

            if (finalDestination)
            {
                return !vnav.IsRunning();
            }

            if (!vnav.IsRunning())
            {
                throw new VnavmeshStoppedException();
            }

            return false;
        }, new TaskManagerConfiguration { TimeLimitMS = 180000, ShowError = false });
    }


    private Func<Chain> GetWaitingToStartCriticalEncounterChain(StateManagerModule states)
    {
        return () =>
        {
            return Chain.Create("Illegal:WaitingToStartCriticalEncounter")
                .Then(new TaskManagerTask(() =>
                    {
                        if (!IsValid())
                        {
                            throw new Exception("The critical encounter appears to have started without you.");
                        }

                        var critical = module.GetModule<CriticalEncountersModule>();
                        var encounter = critical.CriticalEncounters[data.Id];

                        if (encounter.State == DynamicEventState.Battle &&
                            states.GetState() != State.InCriticalEncounter)
                        {
                            throw new Exception("The critical encounter appears to have started without you.");
                        }

                        if (!vnav.IsRunning() && states.GetState() == State.InCombat)
                        {
                            Actions.TryUnmount();

                            if (module.Config.ShouldToggleAiProvider)
                            {
                                module.Config.AiProvider.On();
                            }

                            if (Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "AEAssistV3" && p.IsLoaded))
                            {
                                Chat.ExecuteCommand("/aeTargetSelector off");
                                Chat.ExecuteCommand("/aepull on");
                            }
                        }

                        return states.GetState() == State.InCriticalEncounter;
                    },
                    new TaskManagerConfiguration
                    {
                        TimeLimitMS = 180000,
                    }))
                .Then(_ => state = ActivityState.Participating);
        };
    }

    public override unsafe bool IsValid()
    {
        if (Encounter.State == DynamicEventState.Register)
        {
            return true;
        }

        var dec = DynamicEventContainer.GetInstance();
        return dec != null && Encounter.DynamicEventId == dec->CurrentEventId;
    }

    protected override float GetRadius()
    {
        // This is kind of an assumption, but it seems accurate enough for most encounters.
        // return Encounter.Unknown4;
        return 30f;
    }

    protected override Vector3 GetPosition()
    {
        return Encounter.MapMarker.Position;
    }

    public override string GetName()
    {
        return Encounter.Name.ToString();
    }

    private bool IsCloseToZone(float radius = 50f)
    {
        return Player.DistanceTo(GetPosition()) <= radius;
    }


    protected override unsafe bool IsActivityTarget(IBattleNpc obj)
    {
        try
        {
            var battleChara = (BattleChara*)obj.Address;

            var isRelatedToCurrentEvent = battleChara->EventId.EntryId == Player.BattleChara->EventId.EntryId;

            return obj.SubKind == (byte)BattleNpcSubKind.Enemy && isRelatedToCurrentEvent;
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex.Message);
            return false;
        }
    }

    protected override ActivityState GetPostPathfindingState()
    {
        return ActivityState.WaitingToStartCriticalEncounter;
    }
}
