namespace Hermes.Core.Models;

public sealed class SessionState
{
    public List<SessionTab> Tabs { get; set; } = [];
    public int ActiveTabIndex { get; set; }
    public Guid? ActiveEnvironmentId { get; set; }
    public double SidebarWidth { get; set; } = 300;
}
