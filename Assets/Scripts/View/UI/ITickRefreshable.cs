namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Implemented by UI controllers that display simulation-derived data and need
    /// to stay in sync every tick. Controllers register themselves via
    /// UIManager.RegisterTickRefreshable() in their constructor — no manual wiring
    /// in UIManager.OnSimulationTick() needed.
    ///
    /// Sub-controllers managed by a parent (e.g., ChroniclePanelController inside
    /// LogPanelContainerController) should NOT implement this interface — their
    /// parent handles their refresh timing.
    /// </summary>
    public interface ITickRefreshable
    {
        void Refresh();
    }
}
