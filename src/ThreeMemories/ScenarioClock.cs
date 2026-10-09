using AgentMemory.Abstractions.Services;

namespace ThreeMemories;

/// <summary>
/// The clock of the script. Three months of conversation are replayed in a few minutes, so "now"
/// is whatever moment the script has reached, not the moment the program happens to run.
/// AgentMemory reads the time from <see cref="IClock"/>, which is what makes this possible.
/// </summary>
public sealed class ScenarioClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}
