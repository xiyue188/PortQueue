using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace PortQueue.Api.Mobile;

/// <summary>默认移除移动控制器，关闭时路由不存在，不影响原有 /api 接口。</summary>
public sealed class MobileControllerFeatureProvider(IConfiguration configuration)
    : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        if (configuration.GetValue<bool>("Mobile:Enabled")) return;
        foreach (var controller in feature.Controllers
            .Where(type => type.Namespace == typeof(MobileControllerFeatureProvider).Namespace).ToArray())
        {
            feature.Controllers.Remove(controller);
        }
    }
}
