using SharpGLTF.Schema2;

namespace DesktopFamiliar.Services.Animation;

internal static class GlbModelFile
{
    private static FileStream Open(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetExtension(path), ".glb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Expected a binary glTF model.");
        return File.OpenRead(path);
    }

    private static ModelRoot Read(Stream input, JsonFilterCallback? filter = null)
    {
        if (input.Length > 64L * 1024 * 1024)
            throw new InvalidDataException("The model exceeds 64 MiB.");
        // A companion package supplies one self-contained GLB. Never resolve sidecar paths or URLs.
        var context = ReadContext.Create(_ => throw new InvalidDataException("External model assets are unsupported."));
        context.JsonPreprocessor = filter;
        var model = context.ReadBinarySchema2(input);
        if (model.DefaultScene is null || model.DefaultScene.VisualChildren.Count() == 0 || model.LogicalMeshes.Count == 0)
            throw new InvalidDataException("A model must contain a renderable scene.");
        return model;
    }

    public static Dictionary<string, double> ReadClips(string? path)
    {
        using var input = Open(path);
        return Read(input).LogicalAnimations.Where(animation => !string.IsNullOrWhiteSpace(animation.Name))
            .GroupBy(animation => animation.Name!, StringComparer.Ordinal).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => (double)group.First().Duration, StringComparer.Ordinal);
    }

    public static ModelPlaybackRig ReadForPlayback(string? path)
    {
        // Keep a read handle across both passes. The schema reader closes each input it consumes.
        using var input = Open(path);
        using var source = Open(input.Name);
        var model = Read(source);
        if (model.LogicalAnimations.Count == 0) return new ModelPlaybackRig(model, model);

        // SharpGLTF builds samplers for every track when creating a SceneTemplate.
        // Give it the same scene without tracks, then bind tracks from the validated model on demand.
        var rig = Read(input, json =>
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            root.Remove("animations");
            return root.ToJsonString();
        });
        return new ModelPlaybackRig(model, rig);
    }

    public static bool IsLoadError(Exception error) => error is IOException or UnauthorizedAccessException
        or InvalidDataException or FormatException or ArgumentException or NotSupportedException
        or InvalidOperationException or OverflowException or SharpGLTF.Validation.ModelException
        or System.Text.Json.JsonException;
}
