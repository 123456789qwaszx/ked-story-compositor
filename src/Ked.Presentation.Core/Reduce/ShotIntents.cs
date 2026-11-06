namespace Ked.Presentation.Core
{
    // ShotIntents — 샷 커맨드가 끝나면 샷 의도가 어떤 값이어야 하는가(목표 계산).
    // 샷 축의 끝값은 노드 타깃이 아니라 ShotIntentState 그 자체.
    // 의도를 실제 트랜스폼으로 푸는 것(카메라 루트·response 바인딩)은 적용측의 일.
    // 메서드 이름은 대본 커맨드 이름을 따른다: shot_zoom → Zoom.
    public static class ShotIntents
    {
        // shot_zoom - zoom만 바꾼다.
        public static ShotIntentState Zoom(in ShotIntentState from, float zoom)
            => new(zoom, from.PanInRigSpace, from.FocusPointInRigSpace);

        // shot_track - 현재 pan에 더한다.
        public static ShotIntentState Track(in ShotIntentState from, Vec2 panDelta)
            => new(from.Zoom, from.PanInRigSpace + panDelta, from.FocusPointInRigSpace);

        // shot_to - zoom과 pan을 절대값으로.
        public static ShotIntentState To(in ShotIntentState from, float zoom, Vec2 pan)
            => new(zoom, pan, from.FocusPointInRigSpace);

        // shot_reset - 기본 샷으로.
        public static ShotIntentState Reset()
            => ShotIntentState.Default;

        // shot_focus_to - 캐릭터 focus 지점이 화면의 지정 지점에 오는 zoom/pan.
        //
        // focus는 무대 좌표다(카메라 중립). 런타임은 정착 사슬이 샷 노드를 중립으로 재고, 폴드는 상태가 곧 무대 좌표다.
        // 그 점이 선 층(d)의 반응까지 넣어, 목표 배율에서 원하는 지점에 보이는 팬을 푼다.
        // 논리 focus는 그 점 자체다 — 층 산개의 기준이 된다.
        //
        // 지금 카메라는 입력이 아니다. 결과는 무대 위 자리와 층만으로 정해진다.
        public static ShotIntentState FocusTo(
            float targetZoom,
            Vec2 focusInStageSpace,
            Vec2 desiredPointInRigSpace,
            float layerDepth,
            LayerProfileCurve curve)
        {
            Vec2 pan = ShotLayoutMath.SolvePanToPlace(
                targetZoom,
                focusInStageSpace,
                focusInStageSpace,
                desiredPointInRigSpace,
                layerDepth,
                curve);

            return new ShotIntentState(targetZoom, pan, focusInStageSpace);
        }
    }
}