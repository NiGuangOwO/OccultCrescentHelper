using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.Modules.Automator;
using BOCCHI.Modules.Buff;
using BOCCHI.Modules.Buff.Chains;
using BOCCHI.Modules.Teleporter;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Lumina.Excel.Sheets;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;
using System;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Chains;

public class ReturnChain(TeleporterModule module, ReturnChainConfig config) : ChainFactory
{
    protected override unsafe Chain Create(Chain chain)
    {
        chain.BreakIf(() => Player.IsDead);

        var shouldReturn = GetCostToReturn() < GetCostToWalk();

        if (shouldReturn)
        {
            chain.Then(_ => ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 8) == 0);
            chain = Actions.Return.CastOnChain(chain);
            chain.WaitToCast().WaitToCycleCondition(ConditionFlag.BetweenAreas);
        }

        chain.Then(ChainHelper.TreasureSightChain());
        chain.Then(ApplyBuffs);
        chain.Then(ChangeLowLevelJob);

        if (config.ApproachAetheryte)
        {
            var vnav = module.GetIPCSubscriber<VNavmesh>();
            var lifestream = module.GetIPCSubscriber<Lifestream>();
            var position = GetAetherytePosition();

            chain.Then(PathfindAndMoveToChain.RandomNearby(vnav, position, 3));
            chain.Then(_ => lifestream.GetActiveCustomAetheryte() != 0);
            chain.Then(_ => Svc.Targets.Target = Svc.Objects.FirstOrDefault(o => o.BaseId == AethernetData.GetClosestToPlayer().BaseId));
            chain.Then(_ => vnav.Stop());
        }


        return chain;
    }

    private unsafe Chain ChangeLowLevelJob()
    {
        var auto = module.GetModule<AutomatorModule>();
        var state = PublicContentOccultCrescent.GetState();
        var currentJob = Job.Current;
        var chain = Chain.Create();

        if (!auto.Config.ShouldChangeLowLevelJob)
            return chain;

        // Freelancer's actual level cap is dynamic — it depends on how many other jobs are maxed.
        // The static LevelMax from the Excel sheet is only the theoretical maximum.
        // Dynamic cap formula: 1 + number of maxed-out non-Freelancer jobs.
        var freelancerCap = Svc.Data.GetExcelSheet<MKDSupportJob>()
            .Count(j => j.RowId != 0 && state->SupportJobLevels[(byte)j.RowId] >= j.LevelMax) + 1;

        var currentLevel = state->SupportJobLevels[currentJob.ByteId];
        var jobMaxLevel = currentJob.id == JobId.Freelancer
            ? freelancerCap
            : Svc.Data.GetExcelSheet<MKDSupportJob>().GetRow(currentJob.ByteId).LevelMax;

        if (currentLevel < jobMaxLevel)
            return chain;

        // First pass: try to switch to any non-Freelancer job that still has room to level
        foreach (var job in Svc.Data.GetExcelSheet<MKDSupportJob>())
        {
            if (job.RowId == 0)
                continue;

            var level = state->SupportJobLevels[(byte)job.RowId];
            if (level == 0 || level >= job.LevelMax)
                continue;

            chain.Then(_ => PublicContentOccultCrescent.ChangeSupportJob((byte)job.RowId));
            return chain;
        }

        // Second pass: if no other jobs need leveling, fall back to Freelancer (using dynamic cap)
        var freelancerLevel = state->SupportJobLevels[0];
        if (freelancerLevel > 0 && freelancerLevel < freelancerCap)
        {
            chain.Then(_ => PublicContentOccultCrescent.ChangeSupportJob(0));
            return chain;
        }

        return chain;
    }

    private Chain ApplyBuffs()
    {
        var vnav = module.GetIPCSubscriber<VNavmesh>();
        var buffs = module.GetModule<BuffModule>();

        var closestKnowledgeCrystal = ZoneData.GetNearbyKnowledgeCrystal(60f).FirstOrDefault();

        var chain = Chain.Create();
        chain.BreakIf(() => !buffs.ShouldRefreshBuffs() || !vnav.IsReady() || closestKnowledgeCrystal == null);
        chain.Then(_ => Actions.TryUnmount());

        chain.Then(PathfindAndMoveToChain.RandomNearby(vnav, closestKnowledgeCrystal!.Position, 2));
        chain.WaitUntilNear(vnav, closestKnowledgeCrystal!.Position, 2);
        chain.Then(_ => vnav.Stop());

        chain.Then(new AllBuffsChain(buffs));

        return chain;
    }

    public override TaskManagerConfiguration? Config()
    {
        return new TaskManagerConfiguration { TimeLimitMS = 60000 };
    }

    private Vector3 GetAetherytePosition()
    {
        if (ZoneData.Aetherytes.TryGetValue(Svc.ClientState.TerritoryType, out var position))
        {
            return position;
        }

        throw new Exception("Unable to determine Aetheryte position");
    }

    private float GetCostToReturn()
    {
        if (ZoneData.StartingLocations.TryGetValue(Svc.ClientState.TerritoryType, out var start))
        {
            return Vector3.Distance(start, GetAetherytePosition()) + 75f;
        }


        throw new Exception("Unable to determine Starting position");
    }

    private float GetCostToWalk()
    {
        return Player.DistanceTo(GetAetherytePosition());
    }
}
