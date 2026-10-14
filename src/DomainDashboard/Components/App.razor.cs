using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace DomainDashboard.Components;

public partial class App
{
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = null!;

    private IComponentRenderMode? PageRenderMode =>
        HttpContext.AcceptsInteractiveRouting() ? InteractiveServer : null;
}
