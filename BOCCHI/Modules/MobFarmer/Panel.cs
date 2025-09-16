using System.Linq;
using Dalamud.Bindings.ImGui;
using Ocelot;
using Ocelot.Ui;

namespace BOCCHI.Modules.MobFarmer;

public class Panel
{
    public void Draw(MobFarmerModule module)
    {
        OcelotUI.Title("刷怪:");
        OcelotUI.Indent(() =>
        {
            if (ImGui.Button(module.Farmer.Running ? I18N.T("generic.label.stop") : I18N.T("generic.label.start")))
            {
                module.Farmer.Toggle();
            }

            if (module.Farmer.Running)
            {
                OcelotUI.LabelledValue("状态", module.Farmer.StateMachine.State);
            }

            OcelotUI.LabelledValue("未开怪", module.Scanner.NotInCombat.Count());
            OcelotUI.LabelledValue("已开怪", module.Scanner.InCombat.Count());
        });
    }
}
