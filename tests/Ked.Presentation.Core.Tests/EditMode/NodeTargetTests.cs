using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ked.Presentation.Core;

namespace Ked.Presentation.Tests
{
    /// <summary>
    /// 목표 계산 규약과 본보기 둘.
    ///
    /// 기대값은 전부 "종전 식"에서 온다 — 새 기대값을 만들면 동작 불변을 주장할 수 없다.
    ///   MoveBy   : useAbsolutePosition ? delta : startPos + delta
    ///   ScaleTo  : relativeToCurrent ? current * toScale : toScale
    /// </summary>
    public sealed class NodeTargetTests
    {
        // ── NodeTargets.MoveBy ──────────────────────────────────────────

        [Test]
        public void MoveBy_상대는_현재_위치에_더한다()
        {
            NodeTarget target = NodeTargets.MoveBy(
                "track",
                new NodeTargets.MoveByArgs(false, new Vec2(30f, -10f)),
                new Vec2(100f, 50f));

            Assert.That(target.NodeKey, Is.EqualTo("track"));
            Assert.That(target.Kind, Is.EqualTo(NodeTargetKind.AnchoredPosition));
            Assert.That(target.Value.XY, Is.EqualTo(new Vec2(130f, 40f)));
        }

        [Test]
        public void MoveBy_절대는_현재_위치를_무시한다()
        {
            NodeTarget target = NodeTargets.MoveBy(
                "track",
                new NodeTargets.MoveByArgs(true, new Vec2(30f, -10f)),
                new Vec2(100f, 50f));

            Assert.That(target.Value.XY, Is.EqualTo(new Vec2(30f, -10f)));
        }

        [Test]
        public void MoveBy는_순수하다()
        {
            // 같은 입력은 언제나 같은 출력. 호출 순서·횟수에 의존하지 않는다.
            NodeTargets.MoveByArgs args = new(false, new Vec2(5f, 5f));

            Vec2 first = NodeTargets.MoveBy("n", args, new Vec2(1f, 1f)).Value.XY;
            Vec2 second = NodeTargets.MoveBy("n", args, new Vec2(1f, 1f)).Value.XY;

            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Is.EqualTo(new Vec2(6f, 6f)));
        }

        // ── NodeTargets.ScaleTo ─────────────────────────────────────────

        [Test]
        public void ScaleTo_절대는_스펙_값_그대로다()
        {
            NodeTarget target = NodeTargets.ScaleTo(
                "scale",
                new NodeTargets.ScaleToArgs(false, new Vec2(1.5f, 2f)),
                new Vec2(3f, 3f));

            Assert.That(target.Kind, Is.EqualTo(NodeTargetKind.LocalScaleXY));
            Assert.That(target.Value.XY, Is.EqualTo(new Vec2(1.5f, 2f)));
        }

        [Test]
        public void ScaleTo_상대는_현재에_곱한다()
        {
            // 성분별 곱이다 — 종전 ResolveTargetScale과 같은 식.
            NodeTarget target = NodeTargets.ScaleTo(
                "scale",
                new NodeTargets.ScaleToArgs(true, new Vec2(2f, 0.5f)),
                new Vec2(3f, 4f));

            Assert.That(target.Value.XY, Is.EqualTo(new Vec2(6f, 2f)));
        }

        // ── 타깃 적용 ──────────────────────────────────────────────

        [Test]
        public void 스케일_타깃은_z를_보존한다()
        {
            // Ledger와 같은 규약. 게시·트윈·폴드가 같은 규약을 봐야 한다.
            RectNodeState state = RectNodeState.StretchFull.WithLocalScale(new Vec3(1f, 1f, 7f));

            NodeTarget target = NodeTarget.LocalScaleXY("n", new Vec2(2f, 3f));

            Assert.That(target.ApplyTo(state).LocalScale, Is.EqualTo(new Vec3(2f, 3f, 7f)));
        }

        [Test]
        public void 각_타깃은_해당_성분만_바꾼다()
        {
            RectNodeState state = RectNodeState.StretchFull
                .WithAnchoredPosition(new Vec2(1f, 2f))
                .WithLocalScale(new Vec3(3f, 4f, 5f))
                .WithLocalEuler(new Vec3(0f, 0f, 30f));

            RectNodeState moved = NodeTarget.AnchoredPosition("n", new Vec2(9f, 9f)).ApplyTo(state);
            Assert.That(moved.AnchoredPosition, Is.EqualTo(new Vec2(9f, 9f)));
            Assert.That(moved.LocalScale, Is.EqualTo(new Vec3(3f, 4f, 5f)));
            Assert.That(moved.LocalEulerAngles, Is.EqualTo(new Vec3(0f, 0f, 30f)));

            RectNodeState turned = NodeTarget.LocalEuler("n", new Vec3(0f, 0f, 90f)).ApplyTo(state);
            Assert.That(turned.LocalEulerAngles, Is.EqualTo(new Vec3(0f, 0f, 90f)));
            Assert.That(turned.AnchoredPosition, Is.EqualTo(new Vec2(1f, 2f)));
        }

        [Test]
        public void 빈_노드_키는_거부한다()
        {
            Assert.Throws<ArgumentException>(() => NodeTarget.AnchoredPosition(null, Vec2.Zero));
            Assert.Throws<ArgumentException>(() => NodeTarget.LocalScaleXY("", Vec2.One));
            Assert.Throws<ArgumentException>(() => NodeTarget.LocalEuler("", Vec3.Zero));
        }

        // ── 타깃이 흐르는 세 갈래 ──────────────────────────────────

        [Test]
        public void 트리_폴드는_없는_노드에_침묵하지_않는다()
        {
            RectNodeTree tree = new(RectSpace.Centered(1000f, 500f));
            tree.Add("a", null, RectNodeState.StretchFull);

            NodeTarget.AnchoredPosition("a", new Vec2(7f, 8f)).ApplyTo(tree);
            Assert.That(tree.GetState("a").AnchoredPosition, Is.EqualTo(new Vec2(7f, 8f)));

            Assert.Throws<KeyNotFoundException>(
                () => NodeTarget.AnchoredPosition("없음", Vec2.Zero).ApplyTo(tree));

            Assert.Throws<ArgumentNullException>(
                () => NodeTarget.AnchoredPosition("a", Vec2.Zero).ApplyTo((RectNodeTree)null));
        }

        [Test]
        public void 장부_게시는_종류별_슬롯으로_들어간다()
        {
            PlacementTargetLedger ledger = new();

            ledger.Publish(NodeTarget.AnchoredPosition("n", new Vec2(10f, 20f)));
            ledger.Publish(NodeTarget.LocalScaleXY("n", new Vec2(2f, 2f)));
            ledger.Publish(NodeTarget.LocalEuler("n", new Vec3(0f, 0f, 45f)));

            RectNodeState settled = ledger.ApplyTo("n", RectNodeState.StretchFull.WithLocalScale(new Vec3(1f, 1f, 9f)));

            Assert.That(settled.AnchoredPosition, Is.EqualTo(new Vec2(10f, 20f)));
            Assert.That(settled.LocalScale, Is.EqualTo(new Vec3(2f, 2f, 9f)));
            Assert.That(settled.LocalEulerAngles, Is.EqualTo(new Vec3(0f, 0f, 45f)));
        }

        [Test]
        public void 세_갈래가_같은_값을_본다()
        {
            // 이 단계의 존재 이유. 목표 계산 하나의 출력이
            //   장부 게시 · 트윈 종점 · 트리 폴드
            // 셋 모두에서 같은 값이어야 "재생 = 정착 예약 = 정지 프레임"이 성립한다.
            NodeTarget target = NodeTargets.MoveBy(
                "track",
                new NodeTargets.MoveByArgs(false, new Vec2(40f, 0f)),
                new Vec2(60f, 10f));

            Vec2 expected = new(100f, 10f);

            // ① 트윈 종점 — 호스트가 그대로 쓰는 값
            Assert.That(target.Value.XY, Is.EqualTo(expected));

            // ② 장부 게시
            PlacementTargetLedger ledger = new();
            ledger.Publish(target);
            RectNodeState live = RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(60f, 10f));
            Assert.That(ledger.ApplyTo("track", live).AnchoredPosition, Is.EqualTo(expected));

            // ③ 트리 폴드
            RectNodeTree tree = new(RectSpace.Centered(1000f, 500f));
            tree.Add("track", null, live);
            target.ApplyTo(tree);
            Assert.That(tree.GetState("track").AnchoredPosition, Is.EqualTo(expected));
        }
    }
}
