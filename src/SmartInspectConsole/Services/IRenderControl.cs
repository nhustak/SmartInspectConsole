using SmartInspectConsole.Contracts;

namespace SmartInspectConsole.Services;

public interface IRenderControl
{
    RenderStateDto GetRenderState();
    RenderStateDto SetRenderPaused(bool paused, bool automatic = false);
}
