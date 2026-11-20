using System.Numerics;
using DesktopFamiliar.Services.Animation;
using SharpGLTF.Runtime;
using SharpGLTF.Schema2;
using SharpGLTF.Transforms;

namespace DesktopFamiliar.Tests;

public class ModelPoseTransitionTests
{
    [Fact]
    public void NestedJointAndFaceBlendFromDisplayedPoseToMovingClip()
    {
        var armature = CreateArmature();
        var hand = armature.LogicalNodes.Single(node => node.Name == "hand");
        var start = hand.LocalMatrix;
        var transition = new ModelPoseTransition(armature);
        var target = Matrix4x4.CreateRotationY(MathF.PI / 2) * Matrix4x4.CreateTranslation(4, 0, 0);
        var expression = SparseWeight8.Create(new float[] { 1 });

        hand.LocalMatrix = target;
        hand.MorphWeights = expression;
        transition.Apply(0);
        Assert.Equal(start, hand.LocalMatrix);
        Assert.Equal(default, hand.MorphWeights);

        hand.LocalMatrix = target;
        hand.MorphWeights = expression;
        transition.Apply(transition.Duration / 2);
        Assert.Equal(2f, hand.LocalMatrix.Translation.X, 4);
        Assert.Equal(MathF.Cos(MathF.PI / 4), hand.LocalMatrix.M11, 4);
        Assert.Equal(0.5f, hand.MorphWeights[0], 4);

        var movingTarget = target * Matrix4x4.CreateTranslation(0.5f, 0, 0);
        hand.LocalMatrix = movingTarget;
        hand.MorphWeights = expression;
        transition.Apply(transition.Duration);
        Assert.Equal(movingTarget, hand.LocalMatrix);
        Assert.Equal(expression, hand.MorphWeights);
    }

    [Fact]
    public void InterruptingTalkStartsTheReturnFromTheVisibleIntermediatePose()
    {
        var armature = CreateArmature();
        var joint = armature.LogicalNodes.Single(node => node.Name == "hand");
        var entry = new ModelPoseTransition(armature);
        joint.LocalMatrix = Matrix4x4.CreateTranslation(4, 0, 0);
        entry.Apply(entry.Duration / 2);
        var displayed = joint.LocalMatrix;
        var exit = new ModelPoseTransition(armature);
        armature.SetPoseTransforms();
        exit.Apply(0);
        Assert.Equal(displayed, joint.LocalMatrix);
        armature.SetPoseTransforms();
        exit.Apply(exit.Duration / 2);
        Assert.Equal(displayed.Translation.X / 2, joint.LocalMatrix.Translation.X, 4);
        armature.SetPoseTransforms();
        exit.Apply(exit.Duration);
        Assert.Equal(Matrix4x4.Identity, joint.LocalMatrix);
    }

    private static ArmatureInstance CreateArmature()
    {
        var scene = ModelRoot.CreateModel().UseScene("pet");
        scene.CreateNode("root").CreateNode("hand");
        return SceneTemplate.Create(scene).CreateInstance().Armature;
    }
}
