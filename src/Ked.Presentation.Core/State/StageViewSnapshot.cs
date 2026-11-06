using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 살아 있는 무대에서 읽은 "샷이 쓴 것" — 무대마다 카메라 둘, 층마다 보이는 자리와 배율.
    ///
    /// 정지 프레임(StageState)에는 이 값이 없다. 샷 의도(StageState.Shot)와 층 d만 있으면
    /// ShotLayoutMath로 풀리는 값이라, 접는 쪽은 저장하지 않고 비교할 때 푼다(StageStateComparer.CompareStageView).
    ///
    /// 층은 분해된 트랜스폼(위치 / 배율)이 아니라 **합성된 결과**로 담는다 — 무대 루트에서 본 층 중심과 전체 배율.
    /// 런타임이 값을 어떻게 나눠 쓰든(카메라에 얼마, 층에 얼마) 보이는 것이 같으면 등가다.
    /// </summary>
    public sealed class StageViewSnapshot
    {
        public readonly struct CameraEntry
        {
            public readonly string StageKey;
            public readonly Vec2 Pan;
            public readonly float Scale;

            public CameraEntry(string stageKey, Vec2 pan, float scale)
            {
                StageKey = stageKey;
                Pan = pan;
                Scale = scale;
            }
        }

        public readonly struct LayerEntry
        {
            public readonly string StageKey;
            public readonly float Depth;

            /// <summary>무대 루트(rig space)에서 본 층 중심. = pan + offset.</summary>
            public readonly Vec2 CenterInRigSpace;

            /// <summary>무대 루트에서 본 층의 전체 배율. = cameraScale × layerScale.</summary>
            public readonly Vec2 TotalScale;

            public LayerEntry(string stageKey, float depth, Vec2 centerInRigSpace, Vec2 totalScale)
            {
                StageKey = stageKey;
                Depth = depth;
                CenterInRigSpace = centerInRigSpace;
                TotalScale = totalScale;
            }
        }

        public readonly List<CameraEntry> Cameras = new();
        public readonly List<LayerEntry> Layers = new();
    }
}
