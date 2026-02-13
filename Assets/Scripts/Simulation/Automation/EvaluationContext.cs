using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// Everything a condition needs to evaluate against.
    /// Created once per runner per evaluation, not stored long-term.
    /// </summary>
    public struct EvaluationContext
    {
        public Runner Runner;
        public GameState GameState;
        public SimulationConfig Config;

        public EvaluationContext(Runner runner, GameState gameState, SimulationConfig config)
        {
            Runner = runner;
            GameState = gameState;
            Config = config;
        }
    }
}
