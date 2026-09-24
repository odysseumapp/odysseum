namespace Odysseum.Server.Settings;

public interface IProjectSettings
{
    string Title { get; set; }

    int WordGoal { get; set; }

    int DefaultSceneWordGoal { get; set; }
}
