using System;
using NUnit.Framework;
using Ked.Presentation.Core;

namespace Ked.Presentation.Tests
{
    /// <summary>
    /// 샷 목표 계산. 기대값은 종전 BuildTargetState 본문에서 온다.
    ///
    /// zoom_focus는 값보다 **명중**이 정의다: 접은 샷을 정방향 식(SolveLayer → ProjectToRigSpace)에 넣어
    /// 그 점이 원하는 화면 지점에 보이는지 본다. 층마다 반응이 달라도 명중해야 한다.
    /// </summary>
    public sealed class ShotIntentTests
    {
        private const float Eps = 1e-4f;

        private static readonly ShotIntentState From =
            new(2f, new Vec2(30f, -10f), new Vec2(100f, 200f));

        private static readonly LayerProfileCurve Curve = LayerProfileCurve.Default;

        private static void AssertVec2(Vec2 actual, Vec2 expected, string what, float eps = Eps)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(eps), $"{what} X — actual={actual}");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(eps), $"{what} Y — actual={actual}");
        }

        // ── 규약 상수 ────────────────────────────────────────────────

        [Test]
        public void zoom은_1당_배율_0_05다()
        {
            // 커맨드(목표 계산)와 적용측(카메라 루트·response)이 같이 보는 규약이다.
            Assert.That(ShotIntentMath.ZoomToScaleFactor, Is.EqualTo(0.05f));

            Assert.That(ShotIntentMath.EvaluateCameraScale(0f), Is.EqualTo(1f).Within(Eps));
            Assert.That(ShotIntentMath.EvaluateCameraScale(2f), Is.EqualTo(1.1f).Within(Eps));
            Assert.That(ShotIntentMath.EvaluateCameraScale(-4f), Is.EqualTo(0.8f).Within(Eps));
        }

        // ── 단순 목표 계산 4종 ──────────────────────────────────────────

        [Test]
        public void shot_zoom은_zoom만_바꾼다()
        {
            ShotIntentState to = ShotIntents.Zoom(From, 5f);

            Assert.That(to.Zoom, Is.EqualTo(5f));
            AssertVec2(to.PanInRigSpace, From.PanInRigSpace, "pan 유지");
            AssertVec2(to.FocusPointInRigSpace, From.FocusPointInRigSpace, "focus 유지");
        }

        [Test]
        public void shot_track은_현재_pan에_더한다()
        {
            ShotIntentState to = ShotIntents.Track(From, new Vec2(20f, 5f));

            Assert.That(to.Zoom, Is.EqualTo(From.Zoom));
            AssertVec2(to.PanInRigSpace, new Vec2(50f, -5f), "pan 가산");
            AssertVec2(to.FocusPointInRigSpace, From.FocusPointInRigSpace, "focus 유지");
        }

        [Test]
        public void shot_to는_zoom과_pan을_절대값으로_바꾼다()
        {
            ShotIntentState to = ShotIntents.To(From, 4f, new Vec2(-80f, 60f));

            Assert.That(to.Zoom, Is.EqualTo(4f));
            AssertVec2(to.PanInRigSpace, new Vec2(-80f, 60f), "pan 절대");

            // focus는 건드리지 않는다 — 논리 focus는 zoom_focus만 바꾼다.
            AssertVec2(to.FocusPointInRigSpace, From.FocusPointInRigSpace, "focus 유지");
        }

        [Test]
        public void shot_reset은_기본_샷이다()
        {
            ShotIntentState to = ShotIntents.Reset();

            Assert.That(to.Zoom, Is.EqualTo(0f));
            AssertVec2(to.PanInRigSpace, Vec2.Zero, "pan 0");
            AssertVec2(to.FocusPointInRigSpace, Vec2.Zero, "focus 0");
        }

        // ── shot_focus_to: 명중 ──────────────────────────────────────

        // 접은 샷에서 층 d 안의 점이 보이는 자리 — 런타임 계층(Pan → Zoom → Layer)과 같은 정방향 식.
        private static Vec2 SeenAt(in ShotIntentState shot, Vec2 pointInLayer, float depth)
        {
            ShotLayoutMath.SolveCamera(in shot, out float cameraScale, out Vec2 pan);
            LayerResponse layer = ShotLayoutMath.SolveLayer(in shot, depth, Curve);
            return ShotLayoutMath.ProjectToRigSpace(pointInLayer, cameraScale, pan, in layer);
        }

        private static readonly float[] Depths =
        {
            StageLayerLabels.Far, StageLayerLabels.Back, StageLayerLabels.Mid,
            StageLayerLabels.Front, StageLayerLabels.Close, 0.25f, -0.8f,
        };

        [Test]
        public void 접은_샷에서_그_점은_어느_층에_서도_원하는_지점에_보인다()
        {
            // 이것이 zoom_focus의 정의다. 초점이 왼쪽 · 오른쪽 · 정확히 0인 축을 모두 지나게 한다(산개 부호).
            Vec2[] focuses = { new(-460.8f, 300f), new(250f, -380f), new(0f, 820f) };
            Vec2[] desireds = { Vec2.Zero, new(-460.8f, 0f), new(300f, 200f) };

            foreach (float depth in Depths)
            foreach (Vec2 focus in focuses)
            foreach (Vec2 desired in desireds)
            foreach (float zoom in new[] { 2.5f, 6f, -4f })
            {
                ShotIntentState to = ShotIntents.FocusTo(zoom, focus, desired, depth, Curve);

                // 값이 수백 px이라 float 왕복 오차를 1e-3까지 둔다.
                AssertVec2(SeenAt(in to, focus, depth), desired, $"d={depth} focus={focus} desired={desired} zoom={zoom}", 1e-3f);
                Assert.That(to.Zoom, Is.EqualTo(zoom));
            }
        }

        [Test]
        public void 논리_focus는_무대_위_자리_그대로다()
        {
            // 층 산개의 기준이 되는 점이다. 카메라를 벗길 것이 없다 — 입력이 이미 무대 좌표다.
            Vec2 focus = new(250f, 380f);

            ShotIntentState to = ShotIntents.FocusTo(6f, focus, Vec2.Zero, StageLayerLabels.Close, Curve);

            AssertVec2(to.FocusPointInRigSpace, focus, "논리 focus");
        }

        [Test]
        public void mid에서는_종전_식_desired_빼기_focus_곱하기_배율이다()
        {
            // mid의 반응은 항등이라 카메라만 남는다 — 종전 shot_focus_to와 같은 팬.
            Vec2 focus = new(250f, 380f);
            Vec2 desired = new(-460.8f, 0f);
            float scale = ShotIntentMath.EvaluateCameraScale(6f);

            ShotIntentState to = ShotIntents.FocusTo(6f, focus, desired, StageLayerLabels.Mid, Curve);

            AssertVec2(to.PanInRigSpace, desired - focus * scale, "mid 팬");
        }

        [Test]
        public void mid가_아닌_층에서는_카메라만으로_푼_팬과_다르다()
        {
            // 층 반응을 빼고 풀면 빗나간다 — 이 목표 계산이 층을 보는 이유.
            Vec2 focus = new(-460.8f, 300f);
            float scale = ShotIntentMath.EvaluateCameraScale(2.5f);
            Vec2 cameraOnly = Vec2.Zero - focus * scale;

            ShotIntentState to = ShotIntents.FocusTo(2.5f, focus, Vec2.Zero, StageLayerLabels.Close, Curve);

            Assert.That(Math.Abs(to.PanInRigSpace.X - cameraOnly.X), Is.GreaterThan(1f), $"close 팬={to.PanInRigSpace} 카메라만={cameraOnly}");
        }

        [Test]
        public void 같은_지점을_다시_요청하면_같은_결과다()
        {
            // 순수 함수 — 같은 입력은 언제나 같은 출력.
            Vec2 focus = new(120f, 90f);
            Vec2 desired = new(50f, -20f);

            ShotIntentState a = ShotIntents.FocusTo(3f, focus, desired, StageLayerLabels.Front, Curve);
            ShotIntentState b = ShotIntents.FocusTo(3f, focus, desired, StageLayerLabels.Front, Curve);

            Assert.That(b.Zoom, Is.EqualTo(a.Zoom));
            AssertVec2(b.PanInRigSpace, a.PanInRigSpace, "pan");
            AssertVec2(b.FocusPointInRigSpace, a.FocusPointInRigSpace, "focus");
        }
    }
}
