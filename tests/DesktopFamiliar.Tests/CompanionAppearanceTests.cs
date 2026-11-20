using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Runtime.ExceptionServices;
using System.Numerics;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;
using DesktopFamiliar.Views.Controls;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Animation;
using DesktopFamiliar.Services.Storage;
using DesktopFamiliar.Services.Validation;
using SharpGLTF.Schema2;
using SharpGLTF.Transforms;

namespace DesktopFamiliar.Tests;

public class CompanionAppearanceTests
{
    [Fact]
    public void SchemaElevenAddsAnEmptyModelAndPreservesSpriteRolesAndIdentity()
    {
        using var dir = new TemporaryDirectory();
        var storage = new CompanionStorage(dir.Path);
        var scene = new AnimationScene();
        var pet = new CompanionDefinition { Animations = [scene], IdleDefaultAnimationId = scene.Id };
        storage.Save(new DesktopCompanionData { Companions = [pet] });
        var json = JsonNode.Parse(File.ReadAllText(storage.DataFilePath))!;
        json["SchemaVersion"] = 11;
        var legacyPet = json["Companions"]![0]!.AsObject();
        legacyPet.Remove("Appearance");
        legacyPet.Remove("Model3D");
        File.WriteAllText(storage.DataFilePath, json.ToJsonString());

        var loaded = storage.Load();
        var migrated = Assert.Single(loaded.Companions);
        Assert.Equal(pet.Id, migrated.Id);
        Assert.Equal(CompanionAppearance.Images2D, migrated.Appearance);
        Assert.Equal(scene.Id, migrated.IdleDefaultAnimation!.Id);
        Assert.Null(migrated.Model3D.FilePath);
        Assert.Null(migrated.ChatReplyAnimationId);
        Assert.Null(migrated.Model3D.ChatReplyAnimationId);
        Assert.Equal(json.ToJsonString(), File.ReadAllText(storage.DataFilePath));
        storage.Save(loaded);
        Assert.Contains($"\"SchemaVersion\": {DesktopCompanionData.CurrentSchemaVersion}", File.ReadAllText(storage.DataFilePath));
    }

    [Theory]
    [InlineData(CompanionAppearance.Model3D, true)]
    [InlineData(CompanionAppearance.Images2D, true)]
    [InlineData(CompanionAppearance.Model3D, false)]
    [InlineData(CompanionAppearance.Images2D, false)]
    public void BothSetupsSurviveSaveAndPackAndFollowTheChosenDeletionPolicy(CompanionAppearance appearance, bool wipe)
    {
        using var dir = new TemporaryDirectory();
        var storage = new CompanionStorage(dir.Path);
        var manager = new CompanionManager(storage);
        var model = CreateTriangle(dir.Path);
        var image = Path.Combine(dir.Path, "frame.png");
        File.WriteAllBytes(image, [1]);
        var idle = new AnimationScene { AnimationPath = image };
        idle.DialogLines.Add(new() { Text = "Hello from 2D" });
        var source = new CompanionDefinition { Name = "Triangle", Appearance = appearance,
            Description = "Keeper of the library", Personality = "Quietly curious",
            Animations = [idle], IdleDefaultAnimationId = idle.Id, Model3D = new() { FilePath = model } };
        source.Model3D.CreateInitialScenes(null, null);
        var motion = source.Model3D.IdleDefaultAnimation!;
        var voice = Path.Combine(dir.Path, "dialogue.wav");
        File.WriteAllBytes(voice, [7]);
        motion.DialogLines.Add(new() { Text = "Hello from 3D", VoicePath = voice });
        motion.DialogStartSeconds = 0.4;
        source.Model3D.ReminderAnimationId = motion.Id;
        source.ChatReplyAnimationId = idle.Id;
        source.Model3D.ChatReplyAnimationId = motion.Id;
        source.ChatExpressions["happy"] = idle.Id;
        source.Model3D.ChatExpressions["happy"] = motion.Id;
        source.Model3D.ChatExpressions["angry"] = null;
        var pack = Path.Combine(dir.Path, "triangle.companionpack");
        storage.ExportCompanion(source, pack);
        var pet = storage.ImportCompanionBundle(pack).Companion;
        manager.SaveCompanion(pet);
        pet.Appearance = appearance == CompanionAppearance.Model3D ? CompanionAppearance.Images2D : CompanionAppearance.Model3D;
        manager.SaveCompanion(pet);
        pet.Appearance = appearance;
        manager.SaveCompanion(pet);
        var stored = Assert.Single(storage.Load().Companions);
        Assert.Equal(appearance, stored.Appearance);
        Assert.Equal(source.Description, stored.Description);
        Assert.Equal(source.Personality, stored.Personality);
        Assert.Equal(idle.Id, stored.ChatReplyAnimationId);
        Assert.Equal(motion.Id, stored.Model3D.ChatReplyAnimationId);
        Assert.Equal(idle.Id, stored.ChatExpressions["happy"]);
        Assert.Equal(motion.Id, stored.Model3D.ChatExpressions["happy"]);
        Assert.Null(stored.Model3D.ChatExpressions["angry"]);
        Assert.Equal(appearance == CompanionAppearance.Model3D ? motion.Id : idle.Id,
            stored.Clone().ActiveScenes.ChatReplyAnimationId);
        Assert.Equal(motion.Id, stored.Model3D.ReminderAnimation!.Id);
        Assert.Equal(0.4, stored.Model3D.ReminderAnimation.DialogStartSeconds);
        var savedLine = Assert.Single(stored.Model3D.ReminderAnimation.DialogLines);
        Assert.Equal("Hello from 3D", savedLine.Text);
        Assert.NotEqual(voice, savedLine.VoicePath);
        Assert.True(File.Exists(savedLine.VoicePath));
        Assert.Equal([appearance == CompanionAppearance.Model3D ? savedLine.Text : "Hello from 2D"], stored.GetDialogueExamples());
        Assert.Equal(File.ReadAllBytes(model), File.ReadAllBytes(stored.Model3D.FilePath!));
        Assert.Equal(idle.Id, stored.IdleDefaultAnimation!.Id);
        Assert.NotEqual(image, stored.IdleDefaultAnimation.AnimationPath);
        var history = new ChatHistoryStore(dir.Path);
        history.Append(pet.Id, pet.Name, new("Hello", "Hi", DateTimeOffset.Now), "First pet");
        Assert.True(new CompanionDeletionService(manager, storage, history).Delete(pet.Id, wipe));
        Assert.Empty(storage.Load().Companions);
        Assert.Equal(!wipe, File.Exists(savedLine.VoicePath));
        Assert.Equal(!wipe, File.Exists(stored.Model3D.FilePath));
        Assert.Equal(!wipe, File.Exists(stored.IdleDefaultAnimation.AnimationPath));
        Assert.Equal(!wipe, history.ForPet(pet.Id).Notes.Any());
        Assert.Equal(!wipe, history.ForPet(pet.Id).Turns.Any());
        var imported = storage.ImportCompanionBundle(pack).Companion;
        Assert.NotEqual(stored.Id, imported.Id);
        Assert.Empty(history.ForPet(imported.Id).Notes);
        Assert.Empty(history.ForPet(imported.Id).Turns);
    }

    [Fact]
    public void ModelOnlyPetCanUseRestPoseAndRejectsUnknownClips()
    {
        using var dir = new TemporaryDirectory();
        var pet = new CompanionDefinition { Appearance = CompanionAppearance.Model3D,
            Model3D = new() { FilePath = CreateTriangle(dir.Path) } };
        pet.Model3D.CreateInitialScenes(null, null);
        Assert.Empty(CompanionValidator.Validate(pet));
        pet.Model3D.IdleDefaultAnimation!.ModelClip = "Unavailable";
        Assert.NotEmpty(CompanionValidator.Validate(pet));
        Assert.Empty(pet.Animations);
    }

    [Fact]
    public void ModelScenesUseTheModelAssignmentsExclusivelyAndDoNotMutateThePet()
    {
        var sprite = new AnimationScene { Name = "Sprite idle" };
        var pet = new CompanionDefinition { Appearance = CompanionAppearance.Model3D, Animations = [sprite],
            IdleDefaultAnimationId = sprite.Id, ExitAnimationId = sprite.Id,
            Model3D = new() { FilePath = "pet.glb" } };
        pet.Model3D.CreateInitialScenes("Breathe", "Wave");
        var playback = pet.Clone().ActiveScenes;
        Assert.Equal("Breathe", playback.IdleDefaultAnimation!.Name);
        Assert.Equal("Wave", playback.StartAnimation!.Name);
        Assert.Equal(playback.StartAnimationId, Assert.Single(playback.InteractionStates.ClickAreas).AnimationId);
        Assert.Null(playback.ExitAnimation);
        playback.IdleDefaultAnimation!.ModelClip = "Changed";
        Assert.Equal("Breathe", pet.Model3D.IdleDefaultAnimation!.FirstModelClip);
        pet.Appearance = CompanionAppearance.Images2D;
        Assert.Same(pet, pet.ActiveScenes);
        Assert.Equal(sprite.Id, pet.IdleDefaultAnimation!.Id);
    }

    [Fact]
    public void SchemaTwelveMigratesEmbeddedClipsToStableScenesWithoutTouchingSprites()
    {
        using var dir = new TemporaryDirectory();
        var storage = new CompanionStorage(dir.Path);
        var sprite = new AnimationScene();
        var source = new CompanionDefinition { Appearance = CompanionAppearance.Model3D, Animations = [sprite],
            IdleDefaultAnimationId = sprite.Id };
        storage.Save(new DesktopCompanionData { Companions = [source] });
        var json = JsonNode.Parse(File.ReadAllText(storage.DataFilePath))!;
        json["SchemaVersion"] = 12;
        json["Companions"]![0]!["Model3D"] = new JsonObject { ["FilePath"] = "pet.glb", ["IdleClip"] = "Rest", ["GreetingClip"] = "Bow" };
        File.WriteAllText(storage.DataFilePath, json.ToJsonString());
        var loaded = storage.Load();
        var migrated = loaded.Companions.Single();
        Assert.Equal(source.Id, migrated.Id);
        Assert.Equal(sprite.Id, migrated.IdleDefaultAnimationId);
        Assert.Equal("Rest", migrated.Model3D.IdleDefaultAnimation!.FirstModelClip);
        Assert.Equal("Bow", migrated.Model3D.StartAnimation!.FirstModelClip);
        Assert.Equal(migrated.Model3D.StartAnimationId, Assert.Single(migrated.Model3D.InteractionStates.ClickAreas).AnimationId);
        Assert.Equal(json.ToJsonString(), File.ReadAllText(storage.DataFilePath));
        storage.Save(loaded);
        Assert.Equal(migrated.Model3D.StartAnimationId, storage.Load().Companions.Single().Model3D.StartAnimationId);
        Assert.DoesNotContain("GreetingClip", File.ReadAllText(storage.DataFilePath));
    }

    [Fact]
    public void VersionTwoPackWithInactiveModelGetsAnIndependentRestScene()
    {
        using var dir = new TemporaryDirectory();
        var storage = new CompanionStorage(dir.Path);
        var frame = Path.Combine(dir.Path, "sprite.png");
        File.WriteAllBytes(frame, [1]);
        var idle = new AnimationScene { AnimationPath = frame };
        var source = new CompanionDefinition { Animations = [idle], IdleDefaultAnimationId = idle.Id,
            Model3D = new() { FilePath = CreateTriangle(dir.Path) } };
        var pack = Path.Combine(dir.Path, "v2.companionpack");
        storage.ExportCompanion(source, pack);
        using (var zip = ZipFile.Open(pack, ZipArchiveMode.Update))
        {
            var manifest = zip.GetEntry("manifest.json")!;
            JsonNode json;
            using (var input = manifest.Open()) json = JsonNode.Parse(input)!;
            json["formatVersion"] = 2;
            var companion = json["companion"]!;
            var modelPath = (string?)companion["model3D"]!["filePath"];
            companion["model3D"] = new JsonObject { ["filePath"] = modelPath, ["idleClip"] = null, ["greetingClip"] = null };
            manifest.Delete();
            using var output = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            output.Write(json.ToJsonString());
        }
        var migrated = storage.ImportCompanionBundle(pack).Companion;
        Assert.Equal(CompanionAppearance.Images2D, migrated.Appearance);
        Assert.Equal(idle.Id, migrated.IdleDefaultAnimationId);
        var rest = Assert.Single(migrated.Model3D.Animations);
        Assert.Equal(rest.Id, migrated.Model3D.IdleDefaultAnimationId);
        Assert.Null(rest.FirstModelClip);
        Assert.Null(migrated.Model3D.StartAnimationId);
    }

    [Theory]
    [InlineData(AnimationSceneRole.Click, false, true)]
    [InlineData(AnimationSceneRole.DragHold, false, true)]
    [InlineData(AnimationSceneRole.Click, true, false)]
    [InlineData(AnimationSceneRole.Reminder, true, true)]
    [InlineData(AnimationSceneRole.ReminderFallback, false, false)]
    [InlineData(AnimationSceneRole.ChatReply, false, false)]
    public void ModelDialogueHonorsTheTriggerAndFocusMode(AnimationSceneRole role, bool focus, bool expected) =>
        Assert.Equal(expected, ScenePlaybackPolicy.ShouldPresentSceneDialog(focus, role, CompanionAppearance.Model3D));

    [Fact]
    public void TwoScenesCanShareAClipWithIndependentDialogueTimingAndRoles()
    {
        var pet = new CompanionDefinition { Appearance = CompanionAppearance.Model3D };
        pet.Model3D.CreateInitialScenes("Idle", "Bow");
        var welcome = pet.Model3D.StartAnimation!;
        welcome.DialogLines.Add(new() { Text = "Welcome" });
        var tap = new AnimationScene { Name = "Click", ModelClip = "Bow", DialogStartSeconds = 0.4,
            DialogLines = [new() { Text = "Hello" }] };
        pet.Model3D.Animations.Add(tap);
        pet.Model3D.InteractionStates.ClickAreas.Single().AnimationId = tap.Id;
        var active = pet.Clone().ActiveScenes;
        var click = active.FindAnimation(active.InteractionStates.ClickAreas.Single().AnimationId)!;
        Assert.Equal(welcome.FirstModelClip, click.FirstModelClip);
        Assert.Equal("Welcome", active.StartAnimation!.DialogLines.Single().Text);
        Assert.Equal(0.4, click.DialogStartSeconds);
        Assert.Equal("Hello", click.DialogLines.Single().Text);
        click.DialogLines.Clear();
        Assert.Single(tap.DialogLines);
        Assert.Equal(new[] { "Welcome", "Hello" }, pet.GetDialogueExamples());
    }

    [Fact]
    public void LegacyVersionOneImageBundleStillImports()
    {
        using var dir = new TemporaryDirectory();
        var storage = new CompanionStorage(dir.Path);
        var image = Path.Combine(dir.Path, "frame.png");
        File.WriteAllBytes(image, [1]);
        var scene = new AnimationScene { AnimationPath = image };
        var pet = new CompanionDefinition { Animations = [scene], IdleDefaultAnimationId = scene.Id };
        var pack = Path.Combine(dir.Path, "legacy.companionpack");
        storage.ExportCompanion(pet, pack);
        using (var zip = ZipFile.Open(pack, ZipArchiveMode.Update))
        {
            var manifest = zip.GetEntry("manifest.json")!;
            JsonNode json;
            using (var input = manifest.Open()) json = JsonNode.Parse(input)!;
            Assert.Equal(1, (int)json["formatVersion"]!);
            var companion = json["companion"]!.AsObject();
            companion.Remove("appearance");
            companion.Remove("model3D");
            manifest.Delete();
            using var output = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            output.Write(json.ToJsonString());
        }
        var imported = storage.ImportCompanionBundle(pack).Companion;
        Assert.Equal(CompanionAppearance.Images2D, imported.Appearance);
        Assert.Equal(scene.Id, imported.IdleDefaultAnimation!.Id);
        Assert.Null(imported.Model3D.FilePath);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("familiar-appearance-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    [Fact]
    public void SchemaThirteenAndFormatThreeKeepOldScenesAndNewSequencesRoundTrip()
    {
        using var directory = new TemporaryDirectory();
        var storage = new CompanionStorage(directory.Path);
        var model = ModelRoot.Load(CreateTriangle(directory.Path));
        foreach (var name in new[] { "intro", "loop" })
            model.CreateAnimation(name).CreateTranslationChannel(model.LogicalNodes[0],
                new Dictionary<float, Vector3> { [0] = Vector3.Zero, [1] = Vector3.UnitY });
        var path = Path.Combine(directory.Path, "model.glb");
        model.SaveGLB(path);
        var pet = new CompanionDefinition { Appearance = CompanionAppearance.Model3D, Model3D = new() { FilePath = path } };
        pet.Model3D.CreateInitialScenes("intro", null);
        var scene = pet.Model3D.IdleDefaultAnimation!;
        scene.DialogStartSeconds = 0.5;
        scene.DialogLines.Add(new() { Text = "hello" });
        storage.Save(new() { Companions = [pet] });
        var data = JsonNode.Parse(File.ReadAllText(storage.DataFilePath))!;
        data["SchemaVersion"] = 13;
        var old = data["Companions"]![0]!["Model3D"]!["Animations"]![0]!.AsObject();
        old.Remove("ModelLoop");
        old["ModelClip"] = "intro";
        File.WriteAllText(storage.DataFilePath, data.ToJsonString());
        var upgraded = storage.Load().Companions.Single().Model3D.IdleDefaultAnimation!;
        Assert.Equal(scene.Id, upgraded.Id);
        Assert.Equal("intro", upgraded.ModelClip);
        Assert.Null(upgraded.ModelLoop);
        Assert.Equal([(0, 0.5)], ModelDialogueTiming.Cues(upgraded).ToArray());
        scene.ModelClip = null;
        scene.ModelParts = ["intro", "loop"];
        scene.ModelLoop = new() { Mode = ModelLoopMode.FixedDuration, StartSeconds = 0.5, EndSeconds = 1.25, DurationSeconds = 4.25 };
        scene.DialogLines.Add(new() { Text = "second", StartSeconds = 2.5 });
        var pack = Path.Combine(directory.Path, "motion.companionpack");
        storage.ExportCompanion(pet, pack);
        var imported = storage.ImportCompanionBundle(pack).Companion;
        new CompanionManager(storage).SaveCompanion(imported);
        var saved = storage.Load().Companions.Single(item => item.Id == imported.Id).Model3D.IdleDefaultAnimation!;
        Assert.Equal(["intro", "loop"], saved.ModelParts);
        Assert.Equal(4.25, saved.ModelLoop!.DurationSeconds);
        Assert.Equal(1.25, saved.ModelLoop.EndSeconds);
        Assert.Equal(2.5, saved.DialogLines[1].StartSeconds);
        Assert.Equal("intro", CompanionPortrait.From(imported).ModelClip);

        using (var archive = ZipFile.Open(pack, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("manifest.json")!;
            JsonNode manifest;
            using (var input = entry.Open()) manifest = JsonNode.Parse(input)!;
            Assert.Equal(4, (int)manifest["formatVersion"]!);
            manifest["formatVersion"] = 3;
            var oldScene = manifest["companion"]!["model3D"]!["animations"]![0]!.AsObject();
            oldScene.Remove("modelParts");
            oldScene.Remove("modelLoop");
            oldScene["modelClip"] = "loop";
            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
            writer.Write(manifest.ToJsonString());
        }
        Assert.Equal("loop", storage.ImportCompanionBundle(pack).Companion.Model3D.IdleDefaultAnimation!.FirstModelClip);
    }

    [Fact]
    public void RigPreparesOnlyThePlayedClipAndPreservesJointAndMorphTracks()
    {
        using var directory = new TemporaryDirectory();
        var source = ModelRoot.Load(CreateTriangle(directory.Path));
        var body = source.LogicalNodes.Single();
        body.Name = "joint";
        var child = body.CreateNode("joint");
        var primitive = body.Mesh.Primitives.Single();
        primitive.SetMorphTargetAccessors(0, primitive.VertexAccessors);
        var entrance = source.CreateAnimation("Entrance");
        entrance.CreateTranslationChannel(child, new Dictionary<float, Vector3>
            { [0] = Vector3.Zero, [1] = new(0, 4, 0) });
        entrance.CreateMorphChannel(body, new Dictionary<float, SparseWeight8>
            { [0] = default, [1] = SparseWeight8.Create(new float[] { 1 }) }, 1);
        source.CreateAnimation("Idle").CreateTranslationChannel(body, new Dictionary<float, Vector3>
            { [0] = Vector3.Zero, [1] = new(2, 0, 0) });
        var path = Path.Combine(directory.Path, "animated.glb");
        source.SaveGLB(path);

        var rig = GlbModelFile.ReadForPlayback(path);
        Assert.Empty(rig.Scene.Armature.AnimationTracks);
        var nodes = rig.Scene.Armature.LogicalNodes;
        var parent = nodes.Single(node => node.VisualParent == null);
        var joint = nodes.Single(node => node.VisualParent != null);
        var first = rig.GetClip("Entrance")!;
        first.Apply(0.5f);
        Assert.Equal(new Vector3(0, 2, 0), joint.LocalMatrix.Translation);
        Assert.Equal(0.5f, parent.MorphWeights[0], 4);
        rig.Scene.Armature.SetPoseTransforms();
        rig.GetClip("Idle")!.Apply(0.5f);
        Assert.Equal(Vector3.UnitX, parent.LocalMatrix.Translation);
        Assert.Equal(Vector3.Zero, joint.LocalMatrix.Translation);
        Assert.Equal(0f, parent.MorphWeights[0]);
        Assert.Same(first, rig.GetClip("Entrance"));
        Assert.Null(rig.GetClip(null));
    }

    [Fact]
    public void InitiallyHiddenPropAppearsAndDisappearsDuringPlayback()
    {
        using var directory = new TemporaryDirectory();
        var source = ModelRoot.Load(CreateTriangle(directory.Path));
        var body = source.LogicalNodes.Single();
        var prop = source.DefaultScene.CreateNode("Prop");
        prop.Mesh = body.Mesh;
        prop.LocalMatrix = Matrix4x4.CreateTranslation(3, 0, 0);
        prop.SetVisibility(false);
        source.CreateAnimation("Show prop").CreateVisibilityChannel(prop, new Dictionary<float, bool>
            { [0] = false, [1] = true, [2] = false });
        var path = Path.Combine(directory.Path, "prop.glb");
        source.SaveGLB(path);

        RunOnSta(() =>
        {
            var view = new CompanionModelView();
            view.Play(path, "Show prop", loop: false);
            var content = ((ModelVisual3D)view.Children[0]).Content;
            Assert.Equal(1, content.Bounds.SizeX);
            view.SeekSource(1.5);
            Assert.Equal(4, content.Bounds.SizeX);
            view.SeekSource(2);
            Assert.Equal(1, content.Bounds.SizeX);
            view.Clear();
        });
    }

    [Fact]
    public void BaseColourTextureUsesLinearRgbTintAndPreservesPixelAlpha()
    {
        RunOnSta(() =>
        {
            var source = BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32,
                null, new byte[] { 255, 128, 64, 128 }, 4);
            Assert.Same(source, CompanionModelView.TintTexture(source, Vector4.One));
            var tinted = CompanionModelView.TintTexture(source, new Vector4(1, 0, 0.5f, 1));
            var bytes = new byte[4];
            tinted.CopyPixels(bytes, 4, 0);
            Assert.Equal(new byte[] { 188, 0, 64, 128 }, bytes);
        });
    }

    [Fact]
    public void StoppedModelReplaysWithoutReopeningFileAndReloadsOnReplacement()
    {
        using var directory = new TemporaryDirectory();
        var path = CreateTriangle(directory.Path);
        RunOnSta(() =>
        {
            var view = new CompanionModelView();
            var content = (Model3DGroup)((ModelVisual3D)view.Children[0]).Content;
            view.Play(path, null, true);
            var first = content.Children.OfType<GeometryModel3D>().Single();
            view.Stop();
            Assert.False(view.IsLooping);
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                view.Play(path, null, true);
            Assert.Same(first, content.Children.OfType<GeometryModel3D>().Single());
            view.Stop();
            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(10));
            view.Play(path, null, true);
            Assert.NotSame(first, content.Children.OfType<GeometryModel3D>().Single());
            view.Clear();
            Assert.Empty(content.Children);
        });
    }

    [Theory]
    [InlineData(120, 360)]
    [InlineData(480, 120)]
    public void TravellingModelKeepsItsAuthoredScreenMotionAndScaleAcrossWindowGrowthAndResize(int width, int height)
    {
        using var directory = new TemporaryDirectory();
        var source = ModelRoot.Load(CreateTriangle(directory.Path));
        var node = source.LogicalNodes.Single();
        var dance = source.CreateAnimation("Travel");
        dance.CreateTranslationChannel(node, new Dictionary<float, Vector3> {
            [0] = Vector3.Zero, [1] = new(2, -1.5f, 80), [2] = new(-2, 2, -90), [3] = Vector3.Zero });
        dance.CreateScaleChannel(node, new Dictionary<float, Vector3> {
            [0] = Vector3.One, [1] = new(3, 4, 1), [2] = new(5, 2, 1), [3] = Vector3.One });
        var path = Path.Combine(directory.Path, "travel.glb");
        source.SaveGLB(path);

        RunOnSta(() =>
        {
            var view = new CompanionModelView();
            view.Play(path, "Travel", loop: false);
            void VerifyProjection(int w, int h)
            {
                var pet = new System.Drawing.RectangleF(400, 300, w, h);
                var extent = new System.Drawing.RectangleF(0, 0, w, h);
                var desktop = new System.Drawing.RectangleF(-2000, -2000, 4000, 4000);
                var pixelsPerUnit = Math.Min(w, h) / 1.1; // The rest triangle occupies [0,1] on both axes.
                foreach (var time in new[] { 0, 0.5, 1, 1.5, 2, 3 })
                {
                    view.SeekSource(time);
                    var pose = view.GetPoseBounds(new(w, h));
                    extent = OverlayLayoutPolicy.ExpandModelExtent(extent,
                        new((float)pose.X, (float)pose.Y, (float)pose.Width, (float)pose.Height));
                    var window = OverlayLayoutPolicy.GetDrawingWindow(pet, 96, extent, desktop);
                    view.SetCharacterFrame(new(pet.X - window.X, pet.Y - window.Y, w, h));
                    view.Measure(new(window.Width, window.Height));
                    view.Arrange(new(0, 0, window.Width, window.Height));
                    view.UpdateLayout();

                    var camera = (OrthographicCamera)view.Camera;
                    var bounds = ((ModelVisual3D)view.Children[0]).Content.Bounds;
                    var scale = window.Width / camera.Width;
                    var x = window.X + window.Width / 2 + (bounds.X - camera.Position.X) * scale;
                    var y = window.Y + window.Height / 2 - (bounds.Y + bounds.SizeY - camera.Position.Y) * scale;
                    Assert.Equal(pixelsPerUnit, scale, 5);
                    Assert.Equal(pet.X + w / 2d + (bounds.X - 0.5) * pixelsPerUnit, x, 5);
                    Assert.Equal(pet.Y + h / 2d - (bounds.Y + bounds.SizeY - 0.5) * pixelsPerUnit, y, 5);
                    Assert.InRange(x, window.Left, window.Right - bounds.SizeX * scale);
                    Assert.InRange(y, window.Top, window.Bottom - bounds.SizeY * scale);
                    Assert.Equal(1, camera.Position.Z);
                    Assert.True(camera.Position.Z - bounds.Z - bounds.SizeZ > camera.NearPlaneDistance);
                    Assert.True(camera.Position.Z - bounds.Z < camera.FarPlaneDistance);
                    if (time == 2)
                        Assert.True(x < pet.Left && y < pet.Top - 96);
                    if (time == 1 || time == 2)
                    {
                        // Sample a painted pixel during travel both in front of and behind the resting plane.
                        var bitmap = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96,
                            System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(view);
                        var sampleX = (int)(x - window.X + bounds.SizeX * scale / 4);
                        var sampleY = (int)(y - window.Y + bounds.SizeY * scale * 0.75);
                        var pixel = new byte[4];
                        bitmap.CopyPixels(new System.Windows.Int32Rect(sampleX, sampleY, 1, 1), pixel, 4, 0);
                        Assert.True(pixel[3] > 0);
                    }
                }
            }
            VerifyProjection(width, height);
            VerifyProjection(height, width);
            view.Clear();
        });
    }

    [Fact]
    public void ModelPortraitUsesIdleSnapshotAndAnImportedPictureTakesPriority()
    {
        using var directory = new TemporaryDirectory();
        var pet = new CompanionDefinition { Appearance = CompanionAppearance.Model3D,
            Model3D = new() { FilePath = CreateTriangle(directory.Path) } };
        pet.Model3D.CreateInitialScenes(null, null);
        RunOnSta(() =>
        {
            var image = Assert.IsAssignableFrom<BitmapSource>(CompanionPictureLoader.Load(CompanionPortrait.From(pet)));
            var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
            image.CopyPixels(pixels, image.PixelWidth * 4, 0);
            Assert.Contains(pixels, value => value != 0);
            Assert.True(image.IsFrozen);
            Assert.Null(pet.ProfilePicturePath);

            var custom = Path.Combine(directory.Path, "custom.png");
            var pixel = BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null,
                new byte[] { 0, 0, 255, 255 }, 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(pixel));
            using (var stream = File.Create(custom)) encoder.Save(stream);
            pet.ProfilePicturePath = custom;
            Assert.Equal(1, ((BitmapSource)CompanionPictureLoader.Load(CompanionPortrait.From(pet))!).PixelWidth);
            File.Delete(custom);
            Assert.Same(image, CompanionPictureLoader.Load(CompanionPortrait.From(pet)));
            pet.Appearance = CompanionAppearance.Images2D;
            Assert.Null(CompanionPictureLoader.Load(CompanionPortrait.From(pet)));
        });
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private static string CreateTriangle(string directory)
    {
        const string json = """
            {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0}],
             "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],"buffers":[{"byteLength":36}],
             "bufferViews":[{"buffer":0,"byteLength":36}],"accessors":[{"bufferView":0,"componentType":5126,
             "count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]}]}
            """;
        var bytes = Encoding.UTF8.GetBytes(json.PadRight((json.Length + 3) / 4 * 4));
        var path = Path.Combine(directory, "triangle.glb");
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(0x46546C67);
        writer.Write(2);
        writer.Write(28 + bytes.Length + 36);
        writer.Write(bytes.Length);
        writer.Write(0x4E4F534A);
        writer.Write(bytes);
        writer.Write(36);
        writer.Write(0x004E4942);
        foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }) writer.Write(value);
        return path;
    }
}
