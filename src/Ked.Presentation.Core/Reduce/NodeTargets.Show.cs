using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    public static partial class NodeTargets
    {
        // ── set_anchor / show ────────────────────────────────────────
        //
        // 캐릭터를 역할 기본 자리에 세우고 연기 축들을 초기화한다.
        // 리셋 대상 노드 목록은 리그 스키마 지식이므로 코드에 박지 않고 인자로 받는다 —
        // 호스트는 refs에서, 리듀서는 tuning의 리그 스키마에서 만든다.

        /// <summary>role-anchor 튜닝 값 (RoleAnchorTuningDBSO 엔트리의 데이터 부분).</summary>
        public readonly struct RoleAnchorTuning
        {
            public readonly Vec2 Offset;
            public readonly float VisualScale;

            public RoleAnchorTuning(Vec2 offset, float visualScale)
            {
                Offset = offset;
                VisualScale = visualScale;
            }

            /// <summary>엔트리가 없는 캐릭터의 기본값: 오프셋 0, 배율 1.</summary>
            public static readonly RoleAnchorTuning Default = new(Vec2.Zero, 1f);
        }

        /// <summary>종전 규약: visualScale 하한 (Mathf.Max(0.0001f, v)).</summary>
        public const float MinVisualScale = 0.0001f;

        /// <summary>
        /// 타깃 순서: 위치 리셋들 → 회전 리셋들 → 스케일 리셋들 → 앵커 위치 → 앵커 스케일.
        /// 호스트 어댑터는 같은 순서로 rect 목록을 만들어 zip으로 적용한다 —
        /// 순서를 바꾸면 조용히 엉뚱한 노드에 값이 들어간다.
        /// </summary>
        public static NodeTarget[] SetAnchor(
            string anchorNodeKey,
            in RoleAnchorTuning tuning,
            IReadOnlyList<string> resetPositionKeys,
            IReadOnlyList<string> resetEulerKeys,
            IReadOnlyList<string> resetScaleKeys)
        {
            if (string.IsNullOrEmpty(anchorNodeKey))
                throw new ArgumentException("앵커 노드 키가 비어 있다.", nameof(anchorNodeKey));

            resetPositionKeys ??= Array.Empty<string>();
            resetEulerKeys ??= Array.Empty<string>();
            resetScaleKeys ??= Array.Empty<string>();

            float visualScale = Math.Max(MinVisualScale, tuning.VisualScale);

            NodeTarget[] targets = new NodeTarget[
                resetPositionKeys.Count + resetEulerKeys.Count + resetScaleKeys.Count + 2];

            int i = 0;

            for (int k = 0; k < resetPositionKeys.Count; k++)
                targets[i++] = NodeTarget.AnchoredPosition(resetPositionKeys[k], Vec2.Zero);

            for (int k = 0; k < resetEulerKeys.Count; k++)
                targets[i++] = NodeTarget.LocalEuler(resetEulerKeys[k], Vec3.Zero);

            for (int k = 0; k < resetScaleKeys.Count; k++)
                targets[i++] = NodeTarget.LocalScaleXY(resetScaleKeys[k], Vec2.One);

            targets[i++] = NodeTarget.AnchoredPosition(anchorNodeKey, tuning.Offset);
            targets[i] = NodeTarget.LocalScaleXY(anchorNodeKey, new Vec2(visualScale, visualScale));

            return targets;
        }

        // ── fade_in / fade_out / fade ────────────────────────────────

        public const float FadeInAlpha = 1f;
        public const float FadeOutAlpha = 0f;

        public static NodeTarget FadeIn(string nodeKey)
            => NodeTarget.CanvasAlpha(nodeKey, FadeInAlpha);

        public static NodeTarget FadeOut(string nodeKey)
            => NodeTarget.CanvasAlpha(nodeKey, FadeOutAlpha);

        /// <summary>
        /// fade — 목표 alpha를 정하는 정본. fade_in(1) · fade_out(0)은 이것의 두 끝이다.
        /// [0,1] 밖은 잘라 낸다 — 재생(파서)과 폴드가 같은 규칙으로 같은 값을 낸다.
        /// </summary>
        public static float FadeAlpha(float alpha)
            => alpha < 0f ? 0f : alpha > 1f ? 1f : alpha;

        public static NodeTarget Fade(string nodeKey, float alpha)
            => NodeTarget.CanvasAlpha(nodeKey, FadeAlpha(alpha));
    }
}