using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using ECommons.Automation.NeoTaskManager;
using ECommons.GameHelpers;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;

namespace BOCCHI.Modules.Buff.Chains;

public abstract class BuffChain(Job job, PlayerStatus buff, Action action) : ChainFactory
{
    protected override Chain Create(Chain chain)
    {
        chain.RunIf(ShouldRun).Then(job.ChangeToChain);

        return chain.Then(TryCastBuff());
    }

    private TaskManagerTask TryCastBuff()
    {
        var start = System.DateTime.UtcNow;
        var hasCast = false;

        return new TaskManagerTask(() =>
        {
            if (!hasCast)
            {
                if (!action.CanCast())
                {
                    return false;
                }

                action.Cast();
                hasCast = true;
            }

            var status = Player.Status.Get(buff);
            if (status != null && status.RemainingTime >= 1780)
            {
                return true;
            }

            // Give up after a grace period so the chain can continue and the
            // original support job gets restored instead of timing out mid-chain.
            return (System.DateTime.UtcNow - start).TotalSeconds >= 10;
        }, new TaskManagerConfiguration { TimeLimitMS = 30000, ShowError = false });
    }

    public override TaskManagerConfiguration? Config()
    {
        // Keep the chain alive even if a buff step times out, so the final
        // "restore original job" step always runs.
        return new TaskManagerConfiguration { TimeLimitMS = 30000, AbortOnTimeout = false };
    }

    protected abstract bool ShouldRun();
}
