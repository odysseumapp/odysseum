using System.ComponentModel.DataAnnotations;

namespace Odysseum.Server.Settings;

public class ProjectSettings : IProjectSettings
{
    [Required(ErrorMessage = "A title is required.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Use a title between 1 and 200 characters.")]
    public string Title { get; set; } = "Untitled manuscript";

    [Range(0, 10000000, ErrorMessage = "Invalid word goal.")]
    public int WordGoal { get; set; } = 50000;

    [Range(0, 10000000, ErrorMessage = "Invalid scene word goal.")]
    public int DefaultSceneWordGoal { get; set; } = 1000;

    public ProjectSettings Clone() => new() { Title = Title, WordGoal = WordGoal, DefaultSceneWordGoal = DefaultSceneWordGoal };

    /// <summary>A trimmed, validated copy of any implementation, or the first validation message.</summary>
    public static ProjectSettings From(IProjectSettings settings, out string? error)
    {
        var copy = new ProjectSettings
        {
            Title = (settings.Title ?? "").Trim(),
            WordGoal = settings.WordGoal,
            DefaultSceneWordGoal = settings.DefaultSceneWordGoal,
        };
        TryValidate(copy, out error);
        return copy;
    }

    public static bool TryValidate(IProjectSettings settings, out string? error)
    {
        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);
        error = valid ? null : results[0].ErrorMessage;
        return valid;
    }
}
