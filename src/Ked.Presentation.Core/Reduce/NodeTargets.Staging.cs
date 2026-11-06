namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // NodeTargets — 커맨드가 끝나면 노드가 어떤 값이어야 하는가(목표 계산).
    // 메서드 이름은 대본 커맨드 이름을 따른다: move_by → MoveBy.
    //
    // 규약:
    // - 커맨드당 메서드 하나, 파싱된 인자는 XxxArgs 구조체.
    // - 순수 함수다: 시간·랜덤·IO·전역 상태 없음.
    //   현재 상태가 필요하면 인자로 명시해서 받는다(트랜스폼을 읽지 않는다).
    // - 출력은 NodeTarget — 호스트는 게시·트윈 종점으로 쓰고, 리듀서는 트리에 접는다.
    // - 무대 상태를 읽고 실패 사유를 낼 수 있는 것은 Try로 시작한다(TryPlaceFocus 등).
    //
    // 경계(호스트에 남는 것): CommandBase(코루틴 호스트)·ResolveRefs·DOKill·
    // 트윈 실행·Commit의 트랜스폼 쓰기·HasClaimedTarget 수명 관리.
    // 전문: Documentation~/target-boundary.md
    // ─────────────────────────────────────────────────────────────────

    public static partial class NodeTargets
    {
        // ── move_by ──────────────────────────────────────────────────

        public readonly struct MoveByArgs
        {
            public readonly bool UseAbsolutePosition;
            public readonly Vec2 Delta;

            public MoveByArgs(bool useAbsolutePosition, Vec2 delta)
            {
                UseAbsolutePosition = useAbsolutePosition;
                Delta = delta;
            }
        }

        public static NodeTarget MoveBy(
            string nodeKey,
            in MoveByArgs args,
            Vec2 currentAnchoredPosition)
        {
            Vec2 destination = args.UseAbsolutePosition
                ? args.Delta
                : currentAnchoredPosition + args.Delta;

            return NodeTarget.AnchoredPosition(nodeKey, destination);
        }

        // ── rotate_to ────────────────────────────────────────────────

        public readonly struct RotateToArgs
        {
            public readonly bool RelativeToCurrent;
            public readonly Vec3 ToEuler;

            public RotateToArgs(bool relativeToCurrent, Vec3 toEuler)
            {
                RelativeToCurrent = relativeToCurrent;
                ToEuler = toEuler;
            }
        }

        public static NodeTarget RotateTo(
            string nodeKey,
            in RotateToArgs args,
            Vec3 currentLocalEuler)
        {
            Vec3 target = args.RelativeToCurrent
                ? currentLocalEuler + args.ToEuler
                : args.ToEuler;

            return NodeTarget.LocalEuler(nodeKey, target);
        }

        // ── scale_to ─────────────────────────────────────────────────

        public readonly struct ScaleToArgs
        {
            public readonly bool RelativeToCurrent;
            public readonly Vec2 ToScale;

            public ScaleToArgs(bool relativeToCurrent, Vec2 toScale)
            {
                RelativeToCurrent = relativeToCurrent;
                ToScale = toScale;
            }
        }

        public static NodeTarget ScaleTo(
            string nodeKey,
            in ScaleToArgs args,
            Vec2 currentLocalScaleXY)
        {
            Vec2 target = args.RelativeToCurrent
                ? Vec2.Scale(currentLocalScaleXY, args.ToScale)
                : args.ToScale;

            return NodeTarget.LocalScaleXY(nodeKey, target);
        }
    }
}