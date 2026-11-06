using System;

namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // presets/layer-profile.json의 전송 타입 — 깊이 값 d에 대한 층 반응 곡선의 표본.
    // 표본은 d 오름차순이어야 한다. LayerProfileCurve.FromDto가 검증한다.
    // ─────────────────────────────────────────────────────────────────

    [Serializable]
    public sealed class LayerProfileFileDto
    {
        public LayerProfileSampleDto[] samples;
    }

    [Serializable]
    public sealed class LayerProfileSampleDto
    {
        public float depth;
        public Float2Dto focusSpreadPerZoom;
        public Float2Dto panResponse;
        public float zoomScaleResponse;
    }
}
