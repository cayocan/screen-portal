using NUnit.Framework;
using ScreenPortal.Presentation.Screens;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace ScreenPortal.Tests
{
    public class CurvedQuadMeshTests
    {
        [Test]
        public void Flat_IsOneQuadOfTheRightSize()
        {
            using (var mesh = new CurvedQuadMesh("t"))
            {
                mesh.Set(1f, 0.5f, 0f);
                Assert.That(mesh.Segments, NUnit.Framework.Is.EqualTo(1));
                Assert.That(mesh.Mesh.vertexCount, NUnit.Framework.Is.EqualTo(4));
                Assert.That(mesh.Mesh.bounds.size.x, NUnit.Framework.Is.EqualTo(1f).Within(1e-5f));
                Assert.That(mesh.Mesh.bounds.size.y, NUnit.Framework.Is.EqualTo(0.5f).Within(1e-5f));
                Assert.That(mesh.Mesh.bounds.size.z, NUnit.Framework.Is.EqualTo(0f).Within(1e-6f));
            }
        }

        [TestCase(1f, 2.1667f, 9)]   // arco 26,44° → ceil(8,8)
        [TestCase(0.05f, 5f, 2)]     // arco 0,57°: mínimo 2
        [TestCase(2.72f, 1.3f, 40)]  // 119,9° → ceil(39,96)
        public void Segments_OneEvery3Degrees_AtLeast2(float width, float radius, int expected) =>
            Assert.That(CurvedQuadMesh.SegmentsFor(width, radius), NUnit.Framework.Is.EqualTo(expected));

        [Test]
        public void Curved_VerticesLieOnTheArc()
        {
            using (var mesh = new CurvedQuadMesh("t"))
            {
                const float r = 1.3f;
                mesh.Set(1f, 0.5f, r);
                var vertices = mesh.Mesh.vertices; // teste: pode alocar
                var axis = new Vector3(0f, 0f, -r);
                foreach (var v in vertices)
                {
                    var flat = new Vector3(v.x, 0f, v.z) - axis;
                    Assert.That(flat.magnitude, NUnit.Framework.Is.EqualTo(r).Within(1e-4f));
                }
                Assert.That(vertices[0].x, NUnit.Framework.Is.EqualTo(r * Mathf.Sin(-0.5f / r)).Within(1e-4f), "borda esquerda no arco");
            }
        }

        [Test]
        public void SameSize_DoesNotRegenerate_NorAllocate()
        {
            using (var mesh = new CurvedQuadMesh("t"))
            {
                Assert.That(mesh.Set(1f, 0.5f, 2f), NUnit.Framework.Is.True);
                Assert.That(mesh.Set(1f, 0.5f, 2f), NUnit.Framework.Is.False);
                Assert.That(() => { mesh.Set(1f, 0.5f, 2f); }, Is.Not.AllocatingGCMemory());
            }
        }

        [Test]
        public void Regenerating_DoesNotAllocate()
        {
            using (var mesh = new CurvedQuadMesh("t"))
            {
                mesh.Set(1f, 0.5f, 2f);
                float r = 2f;
                Assert.That(() => { r += 0.01f; mesh.Set(1f, 0.5f, r); }, Is.Not.AllocatingGCMemory());
            }
        }
    }
}
