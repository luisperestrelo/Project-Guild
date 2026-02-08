namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// Interface for save/load operations. The simulation layer defines what needs
    /// to be saved (GameState), but the actual serialization is handled by the
    /// Bridge layer which has access to JSON libraries.
    ///
    /// All simulation classes that need to be saved are marked [Serializable].
    /// </summary>
    public interface ISaveSystem
    {
        void Save(GameState state, string slotName);
        GameState Load(string slotName);
        bool SaveExists(string slotName);
        void DeleteSave(string slotName);
    }
}
