using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Animation;

namespace DesktopFamiliar.Views;

public partial class CompanionEditorWindow
{
    private sealed record ExpressionChoice(string Label, Guid? SceneId = null, bool Automatic = false);

    private sealed class ExpressionRow(CompanionScenes scenes, string key, IReadOnlyList<ExpressionChoice> choices)
    {
        public string Label => LocalizationService.Get("Editor.Expression." + key);
        public IReadOnlyList<ExpressionChoice> Choices => choices;
        public ExpressionChoice Selected
        {
            get => scenes.ChatExpressions.TryGetValue(key, out var id)
                ? Choices.First(choice => !choice.Automatic && choice.SceneId == id) : Choices[0];
            set
            {
                if (value == null) return;
                if (value.Automatic) scenes.ChatExpressions.Remove(key);
                else scenes.ChatExpressions[key] = value.SceneId;
            }
        }
    }

    private void RefreshExpressionAssignments()
    {
        var scenes = EditingScenes;
        var visible = VisibleScenes().ToList();
        ExpressionAssignments.ItemsSource = ChatExpressionPolicy.Keys.Select(key =>
        {
            var match = ChatExpressionPolicy.MatchName(scenes, key);
            var automatic = match == null ? LocalizationService.Get("Editor.Expression.NoMatch")
                : LocalizationService.Format("Editor.Expression.Match", match.DisplayName);
            var choices = new List<ExpressionChoice> { new(automatic, Automatic: true), new(LocalizationService.Get("Editor.StateAssignment.None")) };
            var selected = scenes.ChatExpressions.GetValueOrDefault(key);
            foreach (var scene in visible.Concat(scenes.Animations.Where(scene => scene.Id == selected)).Distinct())
                choices.Add(new(scene.DisplayName, scene.Id));
            return new ExpressionRow(scenes, key, choices);
        }).ToList();
    }
}
