using System.Collections.Generic;
using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Manipulation;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class ScreenManipulationControllerTests
    {
        private static readonly Vec3 Eye = LayoutSurface.DefaultCenter;

        private sealed class Recorder : ICommandObserver
        {
            public readonly List<ICommand> Undoable = new List<ICommand>();
            public void OnCommandProcessed(ICommand command, CommandResult result)
            {
                if (result.Accepted && command.IsUndoable) Undoable.Add(command);
            }
        }

        private sealed class Rig
        {
            public readonly WorkspaceStore Store = new WorkspaceStore();
            public readonly Recorder Recorder = new Recorder();
            public readonly CommandBus Bus;
            public readonly ScreenManipulationController Controller;

            public Rig(params Placement[] screens)
            {
                foreach (var p in screens) Store.Dispatch(new CreateScreen(Resolution.Create(1920, 1080), p, 1f));
                Bus = new CommandBus(Store, new ICommandObserver[] { Recorder });
                Controller = new ScreenManipulationController(Store, Bus);
            }

            public Pose PoseOf(int index) => Store.State.Surface.ToPose(Store.State.Screens[index].Placement);

            /// <summary>Direção do olho até um ponto local da tela (metros a partir do centro).</summary>
            public Vec3 Toward(int index, float x, float y)
            {
                var pose = PoseOf(index);
                return (pose.Position + pose.Rotation * new Vec3(x, y, 0f) - Eye).Normalized;
            }

            public void Right(Vec3 dir, bool select, bool grip = false, float delta = 0f, bool cancel = false, ManipulationInput left = default) =>
                Controller.Update(Ray(dir, select, grip, delta, cancel), left);
        }

        private static ManipulationInput Ray(Vec3 dir, bool select, bool grip = false, float delta = 0f, bool cancel = false) =>
            new ManipulationInput { HasRay = true, Origin = Eye, Direction = dir, SelectHeld = select, GripHeld = grip, DistanceDeltaM = delta, CancelPressed = cancel };

        private static Vec3 Yawed(Vec3 dir, float degrees) => Quat.AngleAxis(degrees, Vec3.Up) * dir;

        private const float FrameX = 0.515f; // faixa da moldura à direita

        [Test]
        public void PointingAtTheFrame_Hovers_ContentDoesNot()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            r.Right(r.Toward(0, FrameX, 0f), false);
            Assert.That(r.Controller.State, Is.EqualTo(ManipulationState.Hover));
            Assert.That(r.Controller.HoverHit.Region, Is.EqualTo(HitRegion.Frame));
            r.Right(r.Toward(0, 0f, 0f), false);
            Assert.That(r.Controller.State, Is.EqualTo(ManipulationState.Idle));
            r.Right(r.Toward(0, 0f, 0f), true);
            Assert.That(r.Controller.IsDragging, Is.False, "clicar no meio não pega a tela");
        }

        [Test]
        public void DragFrame_MovesOnTheCylinder_OneCommandOnRelease_UndoRestores()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            var start = r.Store.State;
            var grab = r.Toward(0, FrameX, 0f);
            r.Right(grab, false);
            r.Right(grab, true);
            Assert.That(r.Controller.State, Is.EqualTo(ManipulationState.Moving));
            r.Right(Yawed(grab, 10f), true);
            Assert.That(r.Controller.Preview.Active, Is.True);
            Assert.That(r.Controller.Preview.Placement.AzimuthDeg, Is.EqualTo(10f).Within(0.05f));
            Assert.That(r.Recorder.Undoable, Is.Empty, "nada muda durante o arraste");

            r.Right(Yawed(grab, 10f), false);
            Assert.That(r.Recorder.Undoable.Count, Is.EqualTo(1));
            Assert.That(r.Recorder.Undoable[0], Is.TypeOf<MoveScreenOnSurface>());
            var p = r.Store.State.Screens[0].Placement;
            Assert.That(p.AzimuthDeg, Is.EqualTo(10f).Within(0.05f));
            Assert.That(p.ElevationDeg, Is.EqualTo(0f).Within(0.05f));
            Assert.That(p.DistanceM, Is.EqualTo(1.3f).Within(0.001f));
            Assert.That(r.Controller.Preview.Active, Is.False);

            r.Bus.Undo();
            Assert.That(r.Store.State.Screens[0].Placement, Is.EqualTo(start.Screens[0].Placement));
        }

        [Test]
        public void DistanceDelta_ChangesTheRadius_InTheSameCommand()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            var grab = r.Toward(0, FrameX, 0f);
            r.Right(grab, false);
            r.Right(grab, true);
            r.Right(grab, true, delta: 0.2f);
            r.Right(grab, false);
            Assert.That(r.Recorder.Undoable.Count, Is.EqualTo(1));
            Assert.That(r.Store.State.Screens[0].Placement.DistanceM, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(r.Store.State.Screens[0].Placement.AzimuthDeg, Is.EqualTo(0f).Within(0.05f));
        }

        [Test]
        public void Cancel_RestoresWithoutCommands()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            var grab = r.Toward(0, FrameX, 0f);
            r.Right(grab, false);
            r.Right(grab, true);
            r.Right(Yawed(grab, 15f), true);
            r.Right(Yawed(grab, 15f), true, cancel: true);
            Assert.That(r.Controller.IsDragging, Is.False);
            Assert.That(r.Controller.Preview.Active, Is.False);
            r.Right(Yawed(grab, 15f), false);
            Assert.That(r.Recorder.Undoable, Is.Empty);
            Assert.That(r.Store.State.Screens[0].Placement.AzimuthDeg, Is.EqualTo(0f));
        }

        [Test]
        public void Handle_ResizesProportionally_ResolutionNeverChanges()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            float hh = 0.5f / (16f / 9f) * 1f; // metade da altura (0,28125)
            var corner = r.Toward(0, 0.5f, hh);
            r.Right(corner, false);
            Assert.That(r.Controller.HoverHit.Region, Is.EqualTo(HitRegion.Handle));
            r.Right(corner, true);
            Assert.That(r.Controller.State, Is.EqualTo(ManipulationState.Resizing));
            r.Right(r.Toward(0, 0.6f, hh * 1.2f), true);
            r.Right(r.Toward(0, 0.6f, hh * 1.2f), false);
            Assert.That(r.Recorder.Undoable.Count, Is.EqualTo(1));
            Assert.That(r.Recorder.Undoable[0], Is.TypeOf<ResizeScreen>());
            Assert.That(r.Store.State.Screens[0].WidthMeters, Is.EqualTo(1.2f).Within(0.002f));
            Assert.That(r.Store.State.Screens[0].Resolution, Is.EqualTo(Resolution.Create(1920, 1080)));
        }

        [Test]
        public void Grip_MakesTheScreenLoose()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            var grab = r.Toward(0, FrameX, 0f);
            r.Right(grab, false);
            r.Right(grab, true);
            r.Right(Yawed(grab, 10f), true, grip: true);
            Assert.That(r.Controller.Preview.IsLoose, Is.True);
            r.Right(Yawed(grab, 10f), false, grip: true);
            Assert.That(r.Recorder.Undoable.Count, Is.EqualTo(1));
            Assert.That(r.Recorder.Undoable[0], Is.TypeOf<SetScreenLoose>());
            Assert.That(r.Store.State.Screens[0].Placement.IsLoose, Is.True);
        }

        [Test]
        public void Grid_SnapsWhileDragging_OtherHandSelectTurnsSnappingOff()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            r.Store.Dispatch(new SetSnapOptions(new SnapOptions(false, true, 5f)));
            var grab = r.Toward(0, FrameX, 0f);
            r.Right(grab, false);
            r.Right(grab, true);
            r.Right(Yawed(grab, 9f), true);
            Assert.That(r.Controller.Preview.Placement.AzimuthDeg, Is.EqualTo(10f).Within(0.01f), "gruda na grade");
            Assert.That(r.Controller.Preview.HasGuideAzimuth, Is.True);
            var otherSelect = new ManipulationInput { SelectHeld = true };
            r.Right(Yawed(grab, 9f), true, left: otherSelect);
            Assert.That(r.Controller.Preview.Placement.AzimuthDeg, Is.EqualTo(9f).Within(0.05f), "sem encaixe");
        }

        private static float TopY => 0.5f / (16f / 9f) + 0.015f; // faixa de cima da moldura

        private static ManipulationInput RayFrom(Vec3 origin, Vec3 dir, bool select) =>
            new ManipulationInput { HasRay = true, Origin = origin, Direction = dir, SelectHeld = select };

        [Test]
        public void CurvatureHandle_PushingCurves_PreviewOnly_OneCommandOnRelease_UndoRestores()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            var grab = r.Toward(0, 0f, TopY);
            r.Right(grab, false);
            Assert.That(r.Controller.HoverHit.Region, Is.EqualTo(HitRegion.CurvatureHandle));
            r.Right(grab, true);
            Assert.That(r.Controller.State, Is.EqualTo(ManipulationState.Curving));
            Assert.That(r.Controller.IsDragging, Is.True);

            var pushed = Eye + new Vec3(0f, 0f, 0.15f); // 15 cm para a frente (normal da tela = +Z)
            r.Controller.Update(RayFrom(pushed, grab, true), default);
            Assert.That(r.Controller.Preview.Curvature, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(r.Recorder.Undoable, Is.Empty, "nada muda durante o arraste");
            Assert.That(r.Store.State.Screens[0].Curvature, Is.EqualTo(0f));

            r.Controller.Update(RayFrom(pushed, grab, false), default);
            Assert.That(r.Recorder.Undoable.Count, Is.EqualTo(1));
            Assert.That(r.Recorder.Undoable[0], Is.TypeOf<SetScreenCurvature>());
            var screen = r.Store.State.Screens[0];
            Assert.That(screen.Curvature, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(screen.WidthMeters, Is.EqualTo(1f), "curvar não muda a largura");
            Assert.That(screen.Resolution, Is.EqualTo(Resolution.Create(1920, 1080)));
            Assert.That(r.Controller.Preview.Active, Is.False);

            r.Bus.Undo();
            Assert.That(r.Store.State.Screens[0].Curvature, Is.EqualTo(0f));
        }

        [Test]
        public void CurvatureHandle_PullingFlattens_ClampedTo01()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            r.Store.Dispatch(new SetScreenCurvature(r.Store.State.Screens[0].Id, 0.3f));
            var grab = r.Toward(0, 0f, TopY);
            r.Right(grab, false);
            r.Right(grab, true);
            Assert.That(r.Controller.State, Is.EqualTo(ManipulationState.Curving));
            r.Controller.Update(RayFrom(Eye + new Vec3(0f, 0f, -0.5f), grab, true), default);
            Assert.That(r.Controller.Preview.Curvature, Is.EqualTo(0f));
            r.Controller.Update(RayFrom(Eye + new Vec3(0f, 0f, 0.5f), grab, true), default);
            Assert.That(r.Controller.Preview.Curvature, Is.EqualTo(1f));
            r.Controller.Update(RayFrom(Eye + new Vec3(0f, 0f, 0.5f), grab, false), default);
            Assert.That(r.Store.State.Screens[0].Curvature, Is.EqualTo(1f));
        }

        [Test]
        public void CurvatureHandle_Unchanged_OrCancelled_SendsNothing()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            var grab = r.Toward(0, 0f, TopY);
            r.Right(grab, false);
            r.Right(grab, true);
            r.Right(grab, false);
            Assert.That(r.Recorder.Undoable, Is.Empty, "soltar sem mexer");

            r.Right(grab, true);
            Assert.That(r.Controller.State, Is.EqualTo(ManipulationState.Curving));
            r.Controller.Update(RayFrom(Eye + new Vec3(0f, 0f, 0.1f), grab, true), default);
            r.Right(grab, true, cancel: true);
            Assert.That(r.Controller.IsDragging, Is.False);
            r.Right(grab, false);
            Assert.That(r.Recorder.Undoable, Is.Empty);
            Assert.That(r.Store.State.Screens[0].Curvature, Is.EqualTo(0f));
        }

        [Test]
        public void Preview_CarriesTheScreenCurvature_WhileMoving()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            r.Store.Dispatch(new SetScreenCurvature(r.Store.State.Screens[0].Id, 0.6f));
            var ws = r.Store.State;
            var pose = r.PoseOf(0);
            var grab = (CurvedGeometry.SurfacePoint(pose, CurvedGeometry.Of(ws.Screens[0], ws.Surface).RadiusM, FrameX, 0f) - Eye).Normalized;
            r.Right(grab, false);
            r.Right(grab, true);
            Assert.That(r.Controller.State, Is.EqualTo(ManipulationState.Moving));
            Assert.That(r.Controller.Preview.Curvature, Is.EqualTo(0.6f));
        }

        [Test]
        public void Handle_OnACurvedScreen_ResizesByTheArc()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f));
            r.Store.Dispatch(new SetScreenCurvature(r.Store.State.Screens[0].Id, 0.6f));
            var ws = r.Store.State;
            var pose = r.PoseOf(0);
            float radius = CurvedGeometry.Of(ws.Screens[0], ws.Surface).RadiusM;
            float hh = 0.5f / (16f / 9f);
            var corner = (CurvedGeometry.SurfacePoint(pose, radius, 0.5f, hh) - Eye).Normalized;
            r.Right(corner, false);
            Assert.That(r.Controller.HoverHit.Region, Is.EqualTo(HitRegion.Handle));
            r.Right(corner, true);
            var farther = (CurvedGeometry.SurfacePoint(pose, radius, 0.6f, hh * 1.2f) - Eye).Normalized;
            r.Right(farther, true);
            r.Right(farther, false);
            Assert.That(r.Store.State.Screens[0].WidthMeters, Is.EqualTo(1.2f).Within(0.002f));
            Assert.That(r.Store.State.Screens[0].Curvature, Is.EqualTo(0.6f), "redimensionar não muda a curvatura");
        }

        [Test]
        public void OnlyOneManipulationAtATime()
        {
            var r = new Rig(Placement.OnSurface(0f, 0f, 1.3f), Placement.OnSurface(-60f, 0f, 1.3f));
            var grabA = r.Toward(0, FrameX, 0f);
            var leftAtB = Ray(r.Toward(1, FrameX, 0f), false);
            r.Right(grabA, false, left: leftAtB);
            r.Right(grabA, true, left: leftAtB);
            var leftPress = Ray(r.Toward(1, FrameX, 0f), true);
            r.Right(Yawed(grabA, 5f), true, left: leftPress);
            Assert.That(r.Controller.ActivePointer, Is.EqualTo(0));
            Assert.That(r.Controller.Preview.Screen, Is.EqualTo(r.Store.State.Screens[0].Id));
            r.Right(Yawed(grabA, 5f), false, left: leftPress);
            Assert.That(r.Recorder.Undoable.Count, Is.EqualTo(1));
            Assert.That(r.Store.State.Screens[1].Placement.AzimuthDeg, Is.EqualTo(-60f).Within(0.01f));
        }
    }
}
