using System.Diagnostics;
using System.Numerics;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Animation;
using SharpGLTF.Runtime;
using SharpGLTF.Transforms;
using GltfMaterial = SharpGLTF.Schema2.Material;

namespace DesktopFamiliar.Views.Controls;

/// <summary>Renders an embedded GLB scene in the transparent WPF viewport.</summary>
public sealed class CompanionModelView : Viewport3D
{
    private sealed record Surface(IMeshPrimitiveDecoder Primitive, MeshGeometry3D Geometry);
    private readonly Dictionary<IGeometryTransform, List<Surface>> _surfaces = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IGeometryTransform> _visible = new(ReferenceEqualityComparer.Instance);
    private IMeshDecoder<GltfMaterial>[] _meshes = [];
    private readonly Model3DGroup _content = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1d / 24) };
    private readonly Stopwatch _clock = new();
    private readonly OrthographicCamera _camera = new() { LookDirection = new(0, 0, -1), UpDirection = new(0, 1, 0) };
    private SceneInstance? _scene;
    private ModelPlaybackRig? _rig;
    private string? _path;
    private (long Length, DateTime LastWriteUtc) _version;
    private ModelClipTimeline? _timeline;
    private bool _loop;
    private bool _playing;
    private ModelPoseTransition? _transition;
    public bool IsLooping => _playing && _loop;

    public bool FinishLoop()
    {
        return IsLooping && _timeline!.FinishLoop(_clock.Elapsed.TotalSeconds);
    }
    private Rect3D _restBounds = Rect3D.Empty;
    private Rect? _characterFrame;

    public event EventHandler? Completed;
    internal event Action<bool>? PosePresented;

    internal static BitmapSource CaptureFirstFrame(string path, string? clip)
    {
        var view = new CompanionModelView();
        try
        {
            view.Play(path, clip, loop: true);
            return view.CaptureFrame();
        }
        finally { view.Clear(); }
    }

    internal BitmapSource CaptureFrame()
    {
        const int size = 192;
        // Frame a second camera around the posed geometry without moving the live preview.
        var portrait = new CompanionModelView { _restBounds = _content.Bounds };
        portrait._content.Children.Add(_content);
        portrait.Measure(new Size(size, size));
        portrait.Arrange(new Rect(0, 0, size, size));
        portrait.UpdateLayout();
        portrait.FitCamera();
        var frame = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        frame.Render(portrait);
        frame.Freeze();
        return frame;
    }

    public CompanionModelView()
    {
        Camera = _camera;
        Children.Add(new ModelVisual3D { Content = _content });
        _timer.Tick += (_, _) => Advance(_clock.Elapsed.TotalSeconds);
        SizeChanged += (_, _) => FitCamera();
        IsVisibleChanged += (_, _) => UpdateTimer();
    }

    internal void PrepareClip(string name)
    {
        try { _rig?.GetClip(name); }
        catch (Exception error) when (GlbModelFile.IsLoadError(error))
        {
            DesktopFamiliar.Services.AppLogger.Warning("Could not prepare the upcoming idle clip.", error);
        }
    }

    public void Play(string? path, string? clip, bool loop, bool blendFromCurrent = false) =>
        PlayScene(path, new AnimationScene { ModelClip = clip }, loop, blendFromCurrent);

    public void PlayScene(string? path, AnimationScene scene, bool loop, bool blendFromCurrent = false) =>
        PlayScene(path, scene, loop, blendFromCurrent ? ModelPoseTransition.DefaultDuration : 0);

    internal sealed record PreparedModel(string Path, (long, DateTime) Version, ModelPlaybackRig Rig, IMeshDecoder<GltfMaterial>[] Meshes)
    {
        internal static PreparedModel Read(string? path, CancellationToken cancellation = default)
        {
            var version = FileVersion(path);
            var rig = GlbModelFile.ReadForPlayback(path, cancellation);
            cancellation.ThrowIfCancellationRequested();
            var meshes = rig.Model.LogicalMeshes.Decode();
            if (!meshes.Any(mesh => mesh.Primitives.Any(primitive => primitive.TriangleIndices.Any())))
                throw new InvalidDataException("The model contains no triangles.");
            return new(path!, version, rig, meshes);
        }
    }

    private static (long, DateTime) FileVersion(string? path) =>
        !string.IsNullOrWhiteSpace(path) && new FileInfo(path) is { Exists: true } file
            ? (file.Length, file.LastWriteTimeUtc) : default;

    internal void Attach(PreparedModel model)
    {
        _scene = model.Rig.Scene;
        _meshes = model.Meshes;
        _content.Children.Add(new AmbientLight(Color.FromRgb(190, 190, 190)));
        _content.Children.Add(new DirectionalLight(Colors.White, new Vector3D(0.4, -0.65, -1)));
        Pose();
        _path = model.Path;
        _version = model.Version;
        _rig = model.Rig;
    }

    internal void PlayScene(string? path, AnimationScene scene, bool loop, float blendSeconds)
    {
        var reuse = _rig != null && _path == path && _version == FileVersion(path);
        if (!reuse)
        {
            Clear();
            Attach(PreparedModel.Read(path));
        }

        _timeline = new ModelClipTimeline(scene, name => _rig!.Model.LogicalAnimations
            .FirstOrDefault(animation => animation.Name == name)?.Duration ?? 0, loop);
        var blendTime = loop ? blendSeconds : Math.Min((float)_timeline.Duration, blendSeconds);
        _transition = reuse && blendTime > 0 ? new ModelPoseTransition(_scene!.Armature, blendTime) : null;
        _loop = loop;
        _playing = true;
        _clock.Reset();
        PresentFrame(_timeline.Sample(0), 0, firstFrame: true);
        UpdateTimer();
    }

    private List<Surface> CreateSurfaces(DrawableInstance drawable)
    {
        var surfaces = new List<Surface>();
        foreach (var primitive in _meshes[drawable.Template.LogicalMeshIndex].Primitives)
        {
            var indices = new Int32Collection();
            foreach (var (a, b, c) in primitive.TriangleIndices)
            {
                indices.Add(a);
                indices.Add(b);
                indices.Add(c);
            }
            if (indices.Count == 0) continue;
            indices.Freeze();
            var geometry = new MeshGeometry3D { TriangleIndices = indices };
            var texCoord = primitive.Material?.FindChannel("BaseColor")?.TextureCoordinate ?? 0;
            if (texCoord >= 0 && texCoord < primitive.TexCoordsCount)
            {
                var coords = new PointCollection(primitive.VertexCount);
                for (var i = 0; i < primitive.VertexCount; i++)
                {
                    var uv = primitive.GetTextureCoord(i, texCoord);
                    coords.Add(new Point(uv.X, uv.Y));
                }
                coords.Freeze();
                geometry.TextureCoordinates = coords;
            }
            var material = CreateMaterial(primitive.Material);
            _content.Children.Add(new GeometryModel3D(geometry, material)
            {
                BackMaterial = primitive.Material?.DoubleSided == true ? material : null
            });
            surfaces.Add(new Surface(primitive, geometry));
        }
        return surfaces;
    }

    private static Material CreateMaterial(GltfMaterial? source)
    {
        var channel = source?.FindChannel("BaseColor");
        var color = channel?.Color ?? Vector4.One;
        Brush brush;
        if (channel?.Texture?.PrimaryImage is { } image)
        {
            using var stream = image.Content.Open();
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            brush = new ImageBrush(TintTexture(bitmap, color)) { ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 1, 1), TileMode = TileMode.Tile, Opacity = color.W };
        }
        else
            brush = new SolidColorBrush(Color.FromScRgb(color.W, color.X, color.Y, color.Z));
        var material = new DiffuseMaterial(brush);
        material.Freeze();
        return material;
    }

    internal static BitmapSource TintTexture(BitmapSource bitmap, Vector4 color)
    {
        if (color.X == 1 && color.Y == 1 && color.Z == 1) return bitmap;
        var source = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = checked(source.PixelWidth * 4);
        var pixels = new byte[checked(stride * source.PixelHeight)];
        source.CopyPixels(pixels, stride, 0);
        // glTF multiplies the decoded sRGB texture by a linear base-colour factor.
        var lookup = new Color[256];
        for (int i = 0; i < lookup.Length; i++)
        {
            float linear = Color.FromRgb((byte)i, (byte)i, (byte)i).ScR;
            lookup[i] = Color.FromScRgb(1, color.X * linear, color.Y * linear, color.Z * linear);
        }
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = lookup[pixels[i]].B;
            pixels[i + 1] = lookup[pixels[i + 1]].G;
            pixels[i + 2] = lookup[pixels[i + 2]].R;
        }
        var tinted = BitmapSource.Create(source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY,
            PixelFormats.Bgra32, null, pixels, stride);
        tinted.Freeze();
        return tinted;
    }

    internal void SetCharacterFrame(Rect frame)
    {
        if (_characterFrame == frame) return;
        _characterFrame = frame;
        FitCamera();
    }

    private double UnitsPerDip(Size frame) => 1.1 * Math.Max(0.001 / frame.Width,
        Math.Max(_restBounds.SizeX / frame.Width, _restBounds.SizeY / frame.Height));

    // Pixel bounds relative to the configured character rectangle, independent of the HWND's size.
    internal Rect GetPoseBounds(Size frame)
    {
        var pose = _content.Bounds;
        if (_restBounds.IsEmpty || pose.IsEmpty || frame.Width <= 0 || frame.Height <= 0) return Rect.Empty;
        var units = UnitsPerDip(frame);
        var x = _restBounds.X + _restBounds.SizeX / 2;
        var y = _restBounds.Y + _restBounds.SizeY / 2;
        return new Rect(frame.Width / 2 + (pose.X - x) / units,
            frame.Height / 2 + (y - pose.Y - pose.SizeY) / units, pose.SizeX / units, pose.SizeY / units);
    }

    private void FitCamera()
    {
        if (_restBounds.IsEmpty || ActualHeight <= 0 || ActualWidth <= 0) return;
        var frame = _characterFrame ?? new Rect(RenderSize);
        var units = UnitsPerDip(frame.Size);
        // Compensate only for the drawing surface's origin. Every world point keeps its desktop position.
        var x = _restBounds.X + _restBounds.SizeX / 2 + (ActualWidth / 2 - frame.X - frame.Width / 2) * units;
        var y = _restBounds.Y + _restBounds.SizeY / 2 - (ActualHeight / 2 - frame.Y - frame.Height / 2) * units;
        var pose = _content.Bounds;
        if (pose.IsEmpty) return;
        var distance = Math.Max(1, Math.Max(_restBounds.SizeX, _restBounds.SizeY));
        var restFront = _restBounds.Z + _restBounds.SizeZ;
        var z = restFront + distance;
        _camera.Width = ActualWidth * units;
        _camera.Position = new Point3D(x, y, z);
        _camera.NearPlaneDistance = z - Math.Max(restFront, pose.Z + pose.SizeZ) - distance / 2;
        _camera.FarPlaneDistance = z - Math.Min(_restBounds.Z, pose.Z) + distance;
    }

    private void UpdateTimer()
    {
        if (IsVisible && _playing && (_transition != null || _timeline?.Duration > 0 || !_loop))
        {
            _clock.Start();
            _timer.Start();
        }
        else
        {
            _clock.Stop();
            _timer.Stop();
        }
    }

    private void Advance(double elapsed)
    {
        if (!_playing || _timeline == null) return;
        var frame = _timeline.Sample(elapsed);
        if (_transition is { } blend && (frame.Complete || elapsed >= blend.Duration))
        {
            _transition = null;
            UpdateTimer();
        }
        PresentFrame(frame, (float)elapsed);
        if (frame.Complete)
        {
            _playing = false;
            UpdateTimer();
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal event Action<double>? SourceTimeChanged;

    internal void SeekSource(double seconds)
    {
        if (_timeline == null) return;
        _playing = false;
        _transition = null;
        UpdateTimer();
        PresentFrame(_timeline.SampleSource(seconds), 0);
    }

    private void PresentFrame(ModelClipTimeline.Frame frame, float elapsed, bool firstFrame = false)
    {
        _scene!.Armature.SetPoseTransforms();
        _rig!.GetClip(frame.Clip)?.Apply((float)frame.Time);
        _transition?.Apply(elapsed);
        Pose();
        PosePresented?.Invoke(firstFrame);
        FitCamera();
        SourceTimeChanged?.Invoke(frame.SourceTime);
    }

    private void Pose()
    {
        // SharpGLTF yields only the nodes visible in this pose.
        _visible.Clear();
        foreach (var instance in _scene!)
        {
            if (!instance.Transform.Visible) continue;
            _visible.Add(instance.Transform);
            if (!_surfaces.TryGetValue(instance.Transform, out var surfaces))
                _surfaces.Add(instance.Transform, surfaces = CreateSurfaces(instance));
            foreach (var surface in surfaces)
            {
                var primitive = surface.Primitive;
                var positions = new Point3DCollection(primitive.VertexCount);
                var normals = new Vector3DCollection(primitive.VertexCount);
                for (var i = 0; i < primitive.VertexCount; i++)
                {
                    var p = primitive.GetPosition(i, instance.Transform);
                    var n = primitive.GetNormal(i, instance.Transform);
                    positions.Add(new Point3D(p.X, p.Y, p.Z));
                    normals.Add(new Vector3D(n.X, n.Y, n.Z));
                }
                positions.Freeze();
                normals.Freeze();
                surface.Geometry.Positions = positions;
                surface.Geometry.Normals = normals;
            }
        }
        foreach (var (transform, surfaces) in _surfaces)
            if (!_visible.Contains(transform))
                foreach (var surface in surfaces)
                    surface.Geometry.ClearValue(MeshGeometry3D.PositionsProperty);
        if (_restBounds.IsEmpty) _restBounds = _content.Bounds;
    }

    public void Stop()
    {
        _playing = false;
        _transition = null;
        _timeline = null;
        _clock.Reset();
        UpdateTimer();
    }

    public void Clear()
    {
        Stop();
        _surfaces.Clear();
        _visible.Clear();
        _meshes = [];
        _content.Children.Clear();
        _scene = null;
        _rig = null;
        _path = null;
        _restBounds = Rect3D.Empty;
    }
}
