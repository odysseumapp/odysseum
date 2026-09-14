namespace Odysseum.Server.Settings;

/// <summary>Per-project preferences, stored in the project's <c>.writer/project.json</c>.</summary>
public interface IProjectSettings
{
    string Title { get; set; }

    /// <summary>Manuscript word goal shown in the binder footer.</summary>
    int WordGoal { get; set; }

    /// <summary>Word goal given to newly created or newly discovered scenes.</summary>
    int DefaultSceneWordGoal { get; set; }
}
