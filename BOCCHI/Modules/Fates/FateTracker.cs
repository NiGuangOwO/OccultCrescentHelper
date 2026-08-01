using ECommons.DalamudServices;
using Ocelot.Modules;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BOCCHI.Modules.Fates;

public class FateTracker
{
    public readonly Dictionary<uint, Fate> Fates = [];

    public event Action<Fate>? OnFateSpawned;

    public event Action<Fate>? OnFateDespawned;


    public void Update(UpdateContext context)
    {
        var currentFates = Svc.Fates.ToDictionary(f => (uint)f.FateId, f => f);

        foreach (var (id, data) in currentFates)
        {
            var fate = new Fate(data);
            if (!Fates.ContainsKey(id))
            {
                OnFateSpawned?.Invoke(fate);
            }

            Fates[id] = fate;
        }

        var despawned = Fates.Keys.Except(currentFates.Keys).ToList();
        foreach (var id in despawned)
        {
            var fate = Fates[id];
            // The game has already freed the native memory backing this FATE.
            // Invalidate first so event handlers (e.g. Alerter reading fate.Name)
            // never touch the dangling pointer.
            fate.Invalidate();
            OnFateDespawned?.Invoke(fate);
            Fates.Remove(id);
        }

        foreach (var fate in Fates.Values)
        {
            fate.Update(context);
        }
    }
}
