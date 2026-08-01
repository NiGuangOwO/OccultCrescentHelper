using BOCCHI.Data;
using BOCCHI.Enums;
using Dalamud.Game.ClientState.Fates;
using ECommons;
using Ocelot.Modules;
using System;
using System.Numerics;

namespace BOCCHI.Modules.Fates;

public class Fate(IFate fate)
{
    // Eagerly snapshot values that never change for a FATE's lifetime.
    // The native memory backing `fate` is guaranteed valid at construction time
    // (the FATE was just observed in this frame), but is freed by the game as
    // soon as the FATE despawns — reading it afterwards raises an
    // AccessViolationException that cannot be reliably caught.
    private readonly uint fateId = fate.FateId;
    private readonly string name = fate.Name.GetText();
    private readonly float radius = fate.Radius;
    private readonly Vector3 position = fate.Position;

    private bool valid = true;

    public readonly EventData Data = EventData.Fates[fate.FateId];

    public bool IsValid => valid;

    /// <summary>
    /// Marks this FATE as despawned. After this is called every property returns
    /// a safe default without touching the (already freed) native memory.
    /// </summary>
    public void Invalidate()
    {
        valid = false;
    }

    public uint Id => valid ? fateId : 0;

    public string Name => valid ? name : "Unknown Fate";

    public float Radius => valid ? Data.Radius ?? radius : 0f;

    public Vector3 StartPosition => valid ? Data.StartPosition ?? position : Vector3.Zero;

    public readonly EventProgress Progress = new();

    public byte CurrentProgress
    {
        get
        {
            if (!valid)
            {
                return 100;
            }

            try
            {
                return fate.Progress;
            }
            catch (AccessViolationException)
            {
                return 100;
            }
        }
    }

    public void Update(UpdateContext context)
    {
        if (!valid || CurrentProgress <= 0)
        {
            return;
        }

        if (Progress.Count == 0 || Progress.Latest != CurrentProgress)
        {
            Progress.Add(CurrentProgress);
        }
    }

    public bool IsPotFate()
    {
        return Data.Note == MonsterNote.PersistentPots;
    }

    public Aethernet GetAethernet()
    {
        return Data.Aethernet ?? ZoneData.GetClosestAethernetShard(StartPosition);
    }
}
