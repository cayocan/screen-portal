using System.Collections.Generic;
using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Cursor;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class WorldCursorTests
    {
        private WorkspaceStore _store;
        private List<CursorEnterMessage> _sent;
        private WorldCursor _cursor;
        private uint _s1, _s2;

        [SetUp]
        public void SetUp()
        {
            _store = new WorkspaceStore();
            // S1 à frente, S2 à direita com um vão (1,28 m a 1,3 m ≈ 52° de largura; 70° entre os centros)
            _store.Dispatch(new CreateScreen(Resolution.Create(2560, 1440), Placement.OnSurface(0f, 0f, 1.3f), 1.28f, 0f, "", ScreenSource.Virtual));
            _store.Dispatch(new CreateScreen(Resolution.Create(2560, 1440), Placement.OnSurface(70f, 0f, 1.3f), 1.28f, 0f, "", ScreenSource.Virtual));
            _s1 = (uint)_store.State.Screens[0].Id.Value;
            _s2 = (uint)_store.State.Screens[1].Id.Value;
            _sent = new List<CursorEnterMessage>();
            var head = new FixedHeadPose(Pose.At(_store.State.Surface.Center));
            _cursor = new WorldCursor(_store, head, m => _sent.Add((CursorEnterMessage)m));
        }

        private MouseState World(uint epoch, uint exitKey, int x, int y, int dx, int dy, MouseMode mode = MouseMode.World) =>
            new MouseState { Mode = mode, Epoch = epoch, ScreenKey = exitKey, X = x, Y = y, Dx = dx, Dy = dy };

        private void Tick(MouseState s) => _cursor.Tick(in s);

        [Test]
        public void LeavingAtTheEdge_DoesNotComeStraightBack()
        {
            for (int i = 0; i < 5; i++) Tick(World(1, _s1, 2559, 720, 0, 0));
            Assert.That(_cursor.Active, Is.True);
            Assert.That(_sent, Is.Empty, "parado logo depois da saída: sem pingue-pongue");
        }

        [Test]
        public void CrossingTheGap_EntersTheNextScreen_AtTheMatchingHeight()
        {
            Tick(World(1, _s1, 2559, 720, 0, 0));
            int dx = 0;
            while (_sent.Count == 0 && dx < 20000) Tick(World(1, _s1, 2559, 720, dx += 20, 0));
            Assert.That(_sent.Count, Is.EqualTo(1));
            Assert.That(_sent[0].ScreenKey, Is.EqualTo(_s2));
            Assert.That(_sent[0].X, Is.LessThan(60), "entra pela borda esquerda da S2");
            Assert.That(_sent[0].Y, Is.InRange(680, 760), "na mesma altura");
            Tick(World(1, _s1, 2559, 720, dx + 20, 0));
            Assert.That(_sent.Count, Is.EqualTo(1), "um pedido por saída");
        }

        [Test]
        public void GoingBack_EntersTheExitScreen_AfterLeavingIt()
        {
            Tick(World(1, _s1, 2559, 720, 0, 0));
            Tick(World(1, _s1, 2559, 720, 40, 0));
            Assert.That(_sent, Is.Empty);
            Tick(World(1, _s1, 2559, 720, -30, 0));
            Assert.That(_sent.Count, Is.EqualTo(1));
            Assert.That(_sent[0].ScreenKey, Is.EqualTo(_s1));
            Assert.That(_sent[0].X, Is.InRange(2500, 2559));
        }

        [Test]
        public void Speed_MatchesTheScreen_OnePixelOfMouseIsOnePixelOfAngle()
        {
            Tick(World(1, _s1, 1280, 720, 0, 0, MouseMode.Edit));
            WorldCursor.AnglesOf(_cursor.Direction, out float az0, out _);
            Tick(World(1, _s1, 1280, 720, 200, 0, MouseMode.Edit));
            WorldCursor.AnglesOf(_cursor.Direction, out float az1, out _);
            float expected = (float)(System.Math.Atan(0.0005 / 1.3) * 180.0 / System.Math.PI) * 200f;
            Assert.That(az1 - az0, Is.EqualTo(expected).Within(0.5f));
        }

        [Test]
        public void EditMode_NeverHandsTheMouseBack()
        {
            Tick(World(1, _s1, 1280, 720, 0, 0, MouseMode.Edit));
            Tick(World(1, _s1, 1280, 720, 100, 0, MouseMode.Edit));
            Assert.That(_sent, Is.Empty);
        }

        [Test]
        public void EditOff_ContinuesWhereTheCursorWas_AndCanEnter()
        {
            Tick(World(1, _s1, 2559, 720, 200, 0, MouseMode.Edit)); // no vão, à direita da S1
            var before = _cursor.Direction;
            Tick(World(2, 0, 0, 0, 0, 0)); // edição desligada: Mundo continuando de onde estava
            Assert.That((_cursor.Direction - before).Length, Is.LessThan(1e-4f));
            Tick(World(2, 0, 0, 0, -400, 0));
            Assert.That(_sent.Count, Is.EqualTo(1));
            Assert.That(_sent[0].ScreenKey, Is.EqualTo(_s1));
        }

        [Test]
        public void FindCursor_PutsItInFrontOfTheEyes_AndMovesKeepWorking()
        {
            Tick(World(1, _s1, 2559, 720, 0, 0));
            Tick(World(1, _s1, 2559, 720, 30000, 9000)); // perdido lá atrás
            _cursor.CenterOnView();
            Tick(World(1, _s1, 2559, 720, 30000, 9000));
            Assert.That(Vec3.Dot(_cursor.Direction, Vec3.Forward), Is.GreaterThan(0.999f), "à frente dos olhos");
            Tick(World(1, _s1, 2559, 720, 30100, 9000));
            WorldCursor.AnglesOf(_cursor.Direction, out float az, out _);
            Assert.That(az, Is.GreaterThan(0.5f), "andar para a direita continua valendo");
        }

        [Test]
        public void ElevationIsLimited()
        {
            Tick(World(1, _s1, 1280, 0, 0, -100000));
            WorldCursor.AnglesOf(_cursor.Direction, out _, out float el);
            Assert.That(el, Is.LessThanOrEqualTo(WorldCursor.MaxElevationDeg + 0.01f));
        }

        [Test]
        public void BackOnScreen_TheCursorIsInactive()
        {
            Tick(World(1, _s1, 2559, 720, 0, 0));
            Tick(new MouseState { Mode = MouseMode.Screen, Epoch = 2, ScreenKey = _s2, X = 3, Y = 700 });
            Assert.That(_cursor.Active, Is.False);
        }
    }
}
