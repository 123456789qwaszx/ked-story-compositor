using System;
using NUnit.Framework;
using Ked.Presentation.Core;

namespace Ked.Presentation.Tests
{
    /// <summary>
    /// placement 묶음의 목표 계산. 기대값은 종전 SetAnchorCommandCharR.Apply에서 온다:
    ///   리셋 = 위치 0 / 오일러 0 / 스케일 1
    ///   앵커 = entry.offset, Mathf.Max(0.0001f, entry.visualScale)
    /// </summary>
    public sealed class PlacementBundleTargetTests
    {
        private const float Eps = 1e-4f;

        // 실제 캐릭터 리그의 리셋 목록 (SetAnchorCommandCharR의 두 플래그가 모두 켜진 경우).
        private static readonly string[] PositionKeys =
        {
            "CharSlot_Track", "CharSlot_Track_X", "CharSlot_Track_Y",
            "CharacterPortrait_Track", "CharacterPortrait_Track_Move",
            "CharacterPortrait_Track_Move_X", "CharacterPortrait_Track_Move_Y",
            "CharacterPortrait_SwayPivot", "CharacterPortrait_Shake",
        };

        private static readonly string[] EulerKeys =
        {
            "CharSlot_Rotation",
            "CharacterPortrait_Rotation", "CharacterPortrait_SwayPivot", "CharacterPortrait_Shake",
        };

        private static readonly string[] ScaleKeys =
        {
            "CharSlot_Scale",
            "CharacterPortrait_SwayPivot", "CharacterPortrait_Shake",
            "CharacterPortrait_ActingScale", "CharacterPortrait_ActingScale_X", "CharacterPortrait_ActingScale_Y",
        };

        private const string AnchorKey = "CharacterPortrait_VisualOffset";

        private static NodeTarget[] Reduce(NodeTargets.RoleAnchorTuning tuning)
            => NodeTargets.SetAnchor(AnchorKey, tuning, PositionKeys, EulerKeys, ScaleKeys);

        // ── 타깃 구성 ──────────────────────────────────────────────

        [Test]
        public void 타깃_순서는_위치_회전_스케일_앵커다()
        {
            // 호스트 어댑터가 같은 순서로 rect 목록을 만들어 zip으로 적용한다.
            // 순서가 어긋나면 조용히 엉뚱한 노드에 값이 들어간다.
            NodeTarget[] targets = Reduce(NodeTargets.RoleAnchorTuning.Default);

            Assert.That(targets.Length, Is.EqualTo(9 + 4 + 6 + 2));

            int i = 0;

            foreach (string key in PositionKeys)
            {
                Assert.That(targets[i].NodeKey, Is.EqualTo(key));
                Assert.That(targets[i].Kind, Is.EqualTo(NodeTargetKind.AnchoredPosition));
                Assert.That(targets[i].Value.XY, Is.EqualTo(Vec2.Zero));
                i++;
            }

            foreach (string key in EulerKeys)
            {
                Assert.That(targets[i].NodeKey, Is.EqualTo(key));
                Assert.That(targets[i].Kind, Is.EqualTo(NodeTargetKind.LocalEulerAngles));
                Assert.That(targets[i].Value, Is.EqualTo(Vec3.Zero));
                i++;
            }

            foreach (string key in ScaleKeys)
            {
                Assert.That(targets[i].NodeKey, Is.EqualTo(key));
                Assert.That(targets[i].Kind, Is.EqualTo(NodeTargetKind.LocalScaleXY));
                Assert.That(targets[i].Value.XY, Is.EqualTo(Vec2.One));
                i++;
            }

            // 앵커는 마지막 두 장 — 위치 다음 스케일.
            Assert.That(targets[i].NodeKey, Is.EqualTo(AnchorKey));
            Assert.That(targets[i].Kind, Is.EqualTo(NodeTargetKind.AnchoredPosition));

            Assert.That(targets[i + 1].NodeKey, Is.EqualTo(AnchorKey));
            Assert.That(targets[i + 1].Kind, Is.EqualTo(NodeTargetKind.LocalScaleXY));
        }

        [Test]
        public void 엔트리가_없으면_오프셋_0_배율_1이다()
        {
            NodeTarget[] targets = Reduce(NodeTargets.RoleAnchorTuning.Default);

            Assert.That(targets[^2].Value.XY, Is.EqualTo(Vec2.Zero));
            Assert.That(targets[^1].Value.XY, Is.EqualTo(Vec2.One));
        }

        [Test]
        public void 엔트리_값이_앵커_타깃에_실린다()
        {
            // 실제 role-anchor 덤프의 비기본값 엔트리(tyrant).
            NodeTargets.RoleAnchorTuning tuning = new(new Vec2(-30f, -800f), 5f);

            NodeTarget[] targets = Reduce(tuning);

            Assert.That(targets[^2].Value.XY, Is.EqualTo(new Vec2(-30f, -800f)));
            Assert.That(targets[^1].Value.XY, Is.EqualTo(new Vec2(5f, 5f)));
        }

        [Test]
        public void visualScale은_하한으로_클램프된다()
        {
            // 종전 규약: Mathf.Max(0.0001f, entry.visualScale).
            Assert.That(
                Reduce(new NodeTargets.RoleAnchorTuning(Vec2.Zero, 0f))[^1].Value.X,
                Is.EqualTo(NodeTargets.MinVisualScale).Within(1e-9f));

            Assert.That(
                Reduce(new NodeTargets.RoleAnchorTuning(Vec2.Zero, -3f))[^1].Value.X,
                Is.EqualTo(NodeTargets.MinVisualScale).Within(1e-9f));

            // 하한 위의 값은 그대로.
            Assert.That(
                Reduce(new NodeTargets.RoleAnchorTuning(Vec2.Zero, 0.5f))[^1].Value.X,
                Is.EqualTo(0.5f).Within(1e-9f));
        }

        [Test]
        public void 빈_리셋_목록도_앵커_두_장은_낸다()
        {
            // 호스트의 resetSlotPos / resetCharacterPos 플래그가 모두 꺼진 경우.
            NodeTarget[] targets = NodeTargets.SetAnchor(
                AnchorKey, NodeTargets.RoleAnchorTuning.Default, null, null, null);

            Assert.That(targets.Length, Is.EqualTo(2));
            Assert.That(targets[0].Kind, Is.EqualTo(NodeTargetKind.AnchoredPosition));
            Assert.That(targets[1].Kind, Is.EqualTo(NodeTargetKind.LocalScaleXY));
        }

        [Test]
        public void 빈_앵커_키는_거부한다()
        {
            Assert.Throws<ArgumentException>(
                () => NodeTargets.SetAnchor(null, NodeTargets.RoleAnchorTuning.Default, null, null, null));
        }

        // ── 골든: 어질러진 리그를 접으면 기본 자세로 ─────────────────

        [Test]
        public void 어질러진_리그에_set_anchor를_접으면_기본_자세로_돌아온다()
        {
            // 바닥 pivot 사슬의 정지 프레임 좌표까지 확인한다 —
            // 리셋 값만 보면 "0을 넣었다"까지고, 좌표까지 봐야 사슬이 맞는지 안다.
            RectNodeTree tree = new(RectSpace.Centered(1920f, 1080f));

            tree.Add("CharSlot_Scale", null, RectNodeState.StretchFull
                .WithPivot(new Vec2(0.5f, 0f))
                .WithLocalScale(new Vec3(3f, 3f, 1f)));          // 어질러 둔다

            tree.Add(AnchorKey, "CharSlot_Scale", RectNodeState.StretchFull
                .WithPivot(new Vec2(0.5f, 0f))
                .WithAnchoredPosition(new Vec2(777f, 555f)));    // 어질러 둔다

            NodeTargets.RoleAnchorTuning tuning = new(new Vec2(0f, 12f), 1f);

            NodeTarget[] targets = NodeTargets.SetAnchor(
                AnchorKey, tuning,
                resetPositionKeys: null,
                resetEulerKeys: null,
                resetScaleKeys: new[] { "CharSlot_Scale" });

            foreach (NodeTarget target in targets)
                target.ApplyTo(tree);

            // CharSlot_Scale: 스케일 1로 복귀, 바닥 pivot이라 로컬 원점이 부모 바닥(-540).
            // 앵커 노드: 오프셋 (0, 12) → -540 + 12 = -528.
            Vec3 p = tree.TransformPoint(AnchorKey, Vec3.Zero);

            Assert.That(p.X, Is.EqualTo(0f).Within(Eps));
            Assert.That(p.Y, Is.EqualTo(-528f).Within(Eps));

            Assert.That(tree.GetState("CharSlot_Scale").LocalScale.XY, Is.EqualTo(Vec2.One));
        }

        // ── 가시성 축 ────────────────────────────────────────────────

        [Test]
        public void fade_in_out_타깃은_alpha_1과_0이다()
        {
            NodeTarget fadeIn = NodeTargets.FadeIn("CharacterPortraitSprite_Root");
            NodeTarget fadeOut = NodeTargets.FadeOut("CharacterPortraitSprite_Root");

            Assert.That(fadeIn.Kind, Is.EqualTo(NodeTargetKind.CanvasAlpha));
            Assert.That(fadeIn.Value.X, Is.EqualTo(1f));

            Assert.That(fadeOut.Kind, Is.EqualTo(NodeTargetKind.CanvasAlpha));
            Assert.That(fadeOut.Value.X, Is.EqualTo(0f));

            Assert.That(NodeTargets.FadeInAlpha, Is.EqualTo(1f));
            Assert.That(NodeTargets.FadeOutAlpha, Is.EqualTo(0f));
        }

        [Test]
        public void alpha_타깃은_좌표_상태에_적용되지_않는다()
        {
            // alpha는 RectNodeState에 살지 않는다. 조용히 무시하면
            // "접었는데 아무 일도 안 일어난" 상태가 되므로 예외다.
            NodeTarget target = NodeTargets.FadeIn("n");

            Assert.Throws<InvalidOperationException>(
                () => target.ApplyTo(RectNodeState.StretchFull));

            RectNodeTree tree = new(RectSpace.Centered(1000f, 500f));
            tree.Add("n", null, RectNodeState.StretchFull);

            Assert.Throws<InvalidOperationException>(() => target.ApplyTo(tree));
        }

        [Test]
        public void alpha_타깃은_장부에도_실리지_않는다()
        {
            // 장부는 트랜스폼 예약만 담는다. 가시성은 다른 축이다.
            PlacementTargetLedger ledger = new();

            Assert.Throws<ArgumentException>(
                () => ledger.Publish(NodeTargets.FadeOut("n")));
        }
    }
}
