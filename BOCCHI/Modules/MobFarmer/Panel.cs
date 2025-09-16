using Dalamud.Bindings.ImGui;
using Ocelot;
using Ocelot.Ui;
using System.Linq;

namespace BOCCHI.Modules.MobFarmer;

public class Panel
{
    public void Draw(MobFarmerModule module)
    {
        OcelotUi.Title("刷怪:");
        OcelotUi.Indent(() =>
        {
            if (ImGui.Button(module.Farmer.Running ? I18N.T("generic.label.stop") : I18N.T("generic.label.start")))
            {
                module.Farmer.Toggle();
            }

            if (module.Farmer.Running)
            {
                OcelotUi.LabelledValue("状态", module.Farmer.StateMachine.State);
            }

            OcelotUi.LabelledValue("未开怪", module.Scanner.NotInCombat.Count());
            OcelotUi.LabelledValue("已开怪", module.Scanner.InCombat.Count());
        });
    }
}
