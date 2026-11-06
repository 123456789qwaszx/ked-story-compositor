using System;
using NUnit.Framework;
using Ked.Presentation.Core;

namespace Ked.Presentation.Tests
{
    /// <summary>
    /// 샷 식의 순수 산수. 기대값은 종전 PresentationResponseMath · PresentationCameraRootApplier의
    /// 식을 손으로 계산한 값이다 — 이관은 자리만 바꾸고 값을 바꾸지 않는다.
    /// 런타임이 같은 값을 쓰는지는 UnityParity/ShotLayoutUnityParityTests가 본다.
    /// </summary>
    public sealed class ShotLayoutMathTests
    {
        private const float Eps = 1e-4f;

        private static readonly LayerProfileCurve Curve = LayerProfileCurve.Default;

        private static void AssertVec2(Vec2 actual, Vec2 expected, string what)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(Eps), $"{what} X — actual={actual}");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Eps), $"{what} Y — actual={actual}");
        }

        // ── 중립 ─────────────────────────────────────────────────────

        [Test]
        public void Default_의도는_무대_전체가_항등이다()
        {
            // 중립의 정의: 식에 Default를 넣은 값. 카메라 배율 1 · 팬 0 · 층 오프셋 0 · 층 배율 1.
            ShotLayout layout = ShotLayoutMath.Solve(ShotIntentState.Default, StageLayerLabels.Depths, Curve);

            Assert.That(layout.CameraScale, Is.EqualTo(1f).Within(Eps));
            AssertVec2(layout.Pan, Vec2.Zero, "pan");
            Assert.That(layout.Layers.Length, Is.EqualTo(5));

            for (int i = 0; i < layout.Layers.Length; i++)
            {
                AssertVec2(layout.Layers[i].OffsetInRigSpace, Vec2.Zero, $"{StageLayerLabels.Names[i]} 오프셋");
                AssertVec2(layout.Layers[i].Scale, Vec2.One, $"{StageLayerLabels.Names[i]} 배율");
            }
        }

        // ── 카메라 루트 ───────────────────────────────────────────────

        [Test]
        public void 카메라는_배율과_팬_둘이다()
        {
            ShotIntentState intent = new(6f, new Vec2(250f, -30f), Vec2.Zero);

            ShotLayoutMath.SolveCamera(in intent, out float cameraScale, out Vec2 pan);

            Assert.That(cameraScale, Is.EqualTo(1.3f).Within(Eps), "1 + 0.05 × 6");
            AssertVec2(pan, new Vec2(250f, -30f), "팬은 의도 그대로");
        }

        // ── 층 반응 ───────────────────────────────────────────────────

        [Test]
        public void Mid_층은_어떤_샷에도_카메라_그대로다()
        {
            ShotIntentState intent = new(6f, new Vec2(250f, 0f), new Vec2(100f, 40f));

            LayerResponse mid = ShotLayoutMath.SolveLayer(in intent, StageLayerLabels.Mid, Curve);

            AssertVec2(mid.OffsetInRigSpace, Vec2.Zero, "Mid 오프셋");
            AssertVec2(mid.Scale, Vec2.One, "Mid 배율");
        }

        [Test]
        public void 팬은_층마다_다른_비율로_따라간다()
        {
            // 초점이 0이면 산개 항은 0이라 팬 항만 남는다. Close +0.2 · Far −0.04 (종전 프로필).
            ShotIntentState intent = new(0f, new Vec2(250f, 0f), Vec2.Zero);

            LayerResponse close = ShotLayoutMath.SolveLayer(in intent, StageLayerLabels.Close, Curve);
            LayerResponse far = ShotLayoutMath.SolveLayer(in intent, StageLayerLabels.Far, Curve);

            AssertVec2(close.OffsetInRigSpace, new Vec2(50f, 0f), "Close = 250 × 0.2");
            AssertVec2(far.OffsetInRigSpace, new Vec2(-10f, 0f), "Far = 250 × −0.04");
        }

        [Test]
        public void 줌은_층_배율을_거듭제곱으로_상쇄하거나_강조한다()
        {
            ShotIntentState intent = new(6f, Vec2.Zero, Vec2.Zero);   // cameraScale 1.3

            LayerResponse close = ShotLayoutMath.SolveLayer(in intent, StageLayerLabels.Close, Curve);
            LayerResponse far = ShotLayoutMath.SolveLayer(in intent, StageLayerLabels.Far, Curve);

            float expectedClose = (float)Math.Pow(1.3, 0.16);
            float expectedFar = (float)Math.Pow(1.3, -0.15);

            AssertVec2(close.Scale, new Vec2(expectedClose, expectedClose), "Close = 1.3^0.16");
            AssertVec2(far.Scale, new Vec2(expectedFar, expectedFar), "Far = 1.3^−0.15");
        }

        [Test]
        public void 초점_산개는_초점의_반대쪽_부호로_줌_변화량에_비례한다()
        {
            // 층 기준위치가 0이므로 부호 = sign(0 − focus). 초점이 오른쪽(+100)이면 전부 왼쪽(−)으로.
            ShotIntentState intent = new(6f, Vec2.Zero, new Vec2(100f, 0f));   // zoomAmount 0.3

            LayerResponse close = ShotLayoutMath.SolveLayer(in intent, StageLayerLabels.Close, Curve);
            LayerResponse far = ShotLayoutMath.SolveLayer(in intent, StageLayerLabels.Far, Curve);

            AssertVec2(close.OffsetInRigSpace, new Vec2(-1f * 16f * 0.3f, 0f), "Close = −1 × 16 × 0.3");
            AssertVec2(far.OffsetInRigSpace, new Vec2(-1f * -8f * 0.3f, 0f), "Far = −1 × −8 × 0.3");
        }

        [Test]
        public void 초점이_정확히_0인_축은_산개하지_않는다()
        {
            // 종전 Mathf.Approximately(fromFocus, 0) → 0. Y 산개 계수가 0이라 Y는 어차피 0이지만 X로 본다.
            ShotIntentState intent = new(6f, Vec2.Zero, new Vec2(0f, 100f));

            LayerResponse close = ShotLayoutMath.SolveLayer(in intent, StageLayerLabels.Close, Curve);

            AssertVec2(close.OffsetInRigSpace, Vec2.Zero, "X 초점 0 → X 산개 0, Y 계수 0 → Y 산개 0");
        }

        [Test]
        public void 세_항은_더해진다()
        {
            // 줌 6 · 팬 250 · 초점 +100 — Close: 산개 −4.8 + 팬 50 = 45.2.
            ShotIntentState intent = new(6f, new Vec2(250f, 0f), new Vec2(100f, 0f));

            LayerResponse close = ShotLayoutMath.SolveLayer(in intent, StageLayerLabels.Close, Curve);

            AssertVec2(close.OffsetInRigSpace, new Vec2(45.2f, 0f), "Close");
        }

        // ── 합성 — 층 안의 점이 무대 루트의 어디에 보이나 ─────────────

        [Test]
        public void 층_안의_점은_팬_더하기_오프셋_더하기_두_배율의_곱이다()
        {
            // 줌 6(1.3) · 팬 (250, −30) · 초점 +100 — Close: 오프셋 (45.2, 0), 배율 1.3^0.16.
            ShotIntentState intent = new(6f, new Vec2(250f, -30f), new Vec2(100f, 0f));
            LayerResponse close = ShotLayoutMath.SolveLayer(in intent, StageLayerLabels.Close, Curve);
            ShotLayoutMath.SolveCamera(in intent, out float cameraScale, out Vec2 pan);

            Vec2 p = new(100f, 200f);
            Vec2 actual = ShotLayoutMath.ProjectToRigSpace(p, cameraScale, pan, in close);

            float k = (float)Math.Pow(1.3, 0.16);
            AssertVec2(actual, new Vec2(250f + 45.2f + 1.3f * k * 100f, -30f + 0f + 1.3f * k * 200f), "Close의 (100, 200)");

            // 중립에서는 항등이다.
            ShotLayoutMath.SolveCamera(ShotIntentState.Default, out float s0, out Vec2 pan0);
            LayerResponse mid0 = ShotLayoutMath.SolveLayer(ShotIntentState.Default, StageLayerLabels.Mid, Curve);
            AssertVec2(ShotLayoutMath.ProjectToRigSpace(p, s0, pan0, in mid0), p, "중립");
        }

        // ── 역 — 그 점을 화면 지점에 놓는 팬 ───────────────────────────

        [Test]
        public void 팬_역산은_합성식의_역이다()
        {
            // 두 축 모두 반응이 있는 곡선이어야 축별 독립까지 본다. 기본 곡선은 y 반응이 0이다.
            LayerProfileCurve both = new(
                new[] { 0f },
                new[] { new LayerProfile(new Vec2(6f, 3f), new Vec2(0.3f, -0.5f), 0.1f) });

            Vec2 focus = new(-180f, 420f);
            Vec2 desired = new(300f, -150f);

            foreach (LayerProfileCurve curve in new[] { Curve, both })
            foreach (float depth in new[] { StageLayerLabels.Far, StageLayerLabels.Mid, StageLayerLabels.Close, 0.25f })
            foreach (float zoom in new[] { 0f, 2.5f, -4f })
            {
                Vec2 pan = ShotLayoutMath.SolvePanToPlace(zoom, focus, focus, desired, depth, curve);

                ShotIntentState placed = new(zoom, pan, focus);
                ShotLayoutMath.SolveCamera(in placed, out float cameraScale, out Vec2 cameraPan);
                LayerResponse layer = ShotLayoutMath.SolveLayer(in placed, depth, curve);

                Vec2 seen = ShotLayoutMath.ProjectToRigSpace(focus, cameraScale, cameraPan, in layer);

                Assert.That(seen.X, Is.EqualTo(desired.X).Within(1e-3f), $"d={depth} zoom={zoom} X — seen={seen}");
                Assert.That(seen.Y, Is.EqualTo(desired.Y).Within(1e-3f), $"d={depth} zoom={zoom} Y — seen={seen}");
            }
        }

        // ── 곡선 ─────────────────────────────────────────────────────

        [Test]
        public void 눈금_위에서는_종전_다섯_층_값_그대로다()
        {
            LayerProfile far = Curve.Evaluate(StageLayerLabels.Far);
            LayerProfile close = Curve.Evaluate(StageLayerLabels.Close);

            AssertVec2(far.FocusSpreadPerZoom, new Vec2(-8f, 0f), "Far spread");
            AssertVec2(far.PanResponse, new Vec2(-0.04f, 0f), "Far pan");
            Assert.That(far.ZoomScaleResponse, Is.EqualTo(-0.15f).Within(Eps));

            AssertVec2(close.FocusSpreadPerZoom, new Vec2(16f, 0f), "Close spread");
            AssertVec2(close.PanResponse, new Vec2(0.2f, 0f), "Close pan");
            Assert.That(close.ZoomScaleResponse, Is.EqualTo(0.16f).Within(Eps));
        }

        [Test]
        public void 눈금_사이는_선형이고_바깥은_끝값이다()
        {
            // Mid(0) = 0 · Front(0.5) = (4, 0.02, 0.04) → d = 0.25는 그 절반.
            LayerProfile quarter = Curve.Evaluate(0.25f);

            AssertVec2(quarter.FocusSpreadPerZoom, new Vec2(2f, 0f), "spread");
            AssertVec2(quarter.PanResponse, new Vec2(0.01f, 0f), "pan");
            Assert.That(quarter.ZoomScaleResponse, Is.EqualTo(0.02f).Within(Eps));

            LayerProfile beyond = Curve.Evaluate(3f);
            AssertVec2(beyond.FocusSpreadPerZoom, new Vec2(16f, 0f), "d > Close → Close");

            LayerProfile before = Curve.Evaluate(-3f);
            AssertVec2(before.FocusSpreadPerZoom, new Vec2(-8f, 0f), "d < Far → Far");
        }

        [Test]
        public void 표본은_d_오름차순이어야_한다()
        {
            Assert.Throws<ArgumentException>(() => new LayerProfileCurve(
                new[] { 0f, -1f },
                new[] { LayerProfile.Identity, LayerProfile.Identity }));

            Assert.Throws<ArgumentException>(() => new LayerProfileCurve(
                new[] { 0f, 0f },
                new[] { LayerProfile.Identity, LayerProfile.Identity }));
        }

        [Test]
        public void panResponse는_축마다_마이너스_1보다_커야_한다()
        {
            // −1이면 그 층은 팬을 전혀 따라가지 않는다 — 팬으로 그 층의 점을 화면 지점에 놓을 수 없다.
            Assert.Throws<ArgumentException>(() => new LayerProfileCurve(
                new[] { 0f },
                new[] { new LayerProfile(Vec2.Zero, new Vec2(-1f, 0f), 0f) }));

            Assert.Throws<ArgumentException>(() => new LayerProfileCurve(
                new[] { 0f },
                new[] { new LayerProfile(Vec2.Zero, new Vec2(0f, -1.5f), 0f) }));

            Assert.DoesNotThrow(() => new LayerProfileCurve(
                new[] { 0f },
                new[] { new LayerProfile(Vec2.Zero, new Vec2(-0.99f, 0f), 0f) }));
        }

        [Test]
        public void DTO_왕복은_같은_곡선이다()
        {
            LayerProfileCurve round = LayerProfileCurve.FromDto(Curve.ToDto());

            Assert.That(round.SampleCount, Is.EqualTo(Curve.SampleCount));

            foreach (float d in new[] { -1f, -0.75f, -0.5f, 0f, 0.25f, 0.5f, 1f })
            {
                LayerProfile a = Curve.Evaluate(d);
                LayerProfile b = round.Evaluate(d);

                AssertVec2(b.FocusSpreadPerZoom, a.FocusSpreadPerZoom, $"d={d} spread");
                AssertVec2(b.PanResponse, a.PanResponse, $"d={d} pan");
                Assert.That(b.ZoomScaleResponse, Is.EqualTo(a.ZoomScaleResponse).Within(Eps), $"d={d} zsr");
            }
        }

        // ── 라벨 ─────────────────────────────────────────────────────

        [Test]
        public void 라벨은_정본_다섯뿐이고_눈금은_멀리서_가까이로_오른다()
        {
            Assert.That(StageLayerLabels.TryGetDepth("far", out float far) && far == StageLayerLabels.Far);
            Assert.That(StageLayerLabels.TryGetDepth("close", out float close) && close == StageLayerLabels.Close);
            Assert.That(StageLayerLabels.TryGetDepth("near", out _), Is.False, "별칭은 두지 않는다");
            Assert.That(StageLayerLabels.TryGetDepth("4", out _), Is.False, "숫자 색인도 두지 않는다");

            for (int i = 1; i < StageLayerLabels.Depths.Length; i++)
                Assert.That(StageLayerLabels.Depths[i], Is.GreaterThan(StageLayerLabels.Depths[i - 1]));

            Assert.That(StageLayerLabels.NameOf(StageLayerLabels.Mid), Is.EqualTo("mid"));
            Assert.That(StageLayerLabels.NameOf(0.25f), Is.EqualTo("0.25"));
        }
    }
}
