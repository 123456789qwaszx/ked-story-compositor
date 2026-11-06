namespace Ked.Presentation.Core
{
    /// <summary>
    /// shot — 카메라 의도(zoom·pan). 노드가 아니라 StageState.Shot 한 장을 갈아끼운다.
    ///
    /// shot_reset은 인자가 없어 디스패치에서 바로 접는다 — 여기 있는 건 인자를 읽는 넷이다.
    /// </summary>
    public static partial class StageReducer
    {
        /// <summary>
        /// shot_focus_to (role, focus="body", screenPoint="center", zoom=2.5, duration).
        ///
        /// 런타임과 같은 목표 계산을 태운다: 무대 좌표의 focus와 그 슬롯이 선 층 d를 ShotIntents.FocusTo에 넘긴다.
        /// 층 반응 곡선은 하네스가 무대 뷰를 판정할 때와 같은 것(ResolveLayerProfileCurve)이다.
        /// </summary>
        private static bool ApplyShotFocusTo(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            // 샷은 본 무대만 움직인다(StageRoles). 덮는 무대의 리그로는 본 무대의 카메라를 풀 수 없다 — 런타임은 경고하고 건너뛴다.
            SlotAttachment attachment = state.GetAttachment(slotKey);

            if (StageRoles.Of(attachment.StageKey) != StageRole.Main)
            {
                reason = $"슬롯 '{slotKey}'은 덮는 무대 '{attachment.StageKey}'에 서 있다 — 샷은 본 무대만 움직인다";
                return false;
            }

            if (!FocusPresetName.TryNormalizeToken(cmd.Arg(1, "body"), out string focusName))
            {
                reason = $"focus 프리셋 토큰 '{cmd.Arg(1)}'을 모른다";
                return false;
            }

            string screenPointName = cmd.Arg(2, "center");

            if (!ScreenPointRatios.TryResolve(state.Nodes.RootSpace.Size, screenPointName, out Vec2 desired))
            {
                reason = $"화면 지점 '{screenPointName}'을 모른다";
                return false;
            }

            if (!NumberToken.TryParseFloat(cmd.Arg(3, "2.5"), out float zoom))
            {
                reason = $"zoom을 읽지 못했다: '{cmd.Arg(3)}'";
                return false;
            }

            state.TryGetCharacter(slotKey, out string characterKey);

            Vec2 focusOffset = FocusOffsetMath.Resolve(tuning.FocusTuning, characterKey, focusName, Vec2.Zero);

            RectNodeState[] chain = state.Nodes.BuildChainTo(
                StageState.NodeKeyOf(slotKey, NodeTargets.FocusMeasureNodeId));

            Vec2 focus = SettledFocusMath.FocusPointInRigSpace(
                chain, state.Nodes.RootSpace, focusOffset);

            state.Shot = ShotIntents.FocusTo(
                zoom,
                focus,
                desired,
                attachment.LayerDepth,
                tuning.ResolveLayerProfileCurve());

            return true;
        }

        private static bool ApplyShotZoom(StageState state, in StageCommand cmd, out string reason)
        {
            // 브리지: shot_zoom(zoom=1f, duration).
            if (!NumberToken.TryParseFloat(cmd.Arg(0, "1"), out float zoom))
            {
                reason = $"zoom을 읽지 못했다: '{cmd.Arg(0)}'";
                return false;
            }

            state.Shot = ShotIntents.Zoom(state.Shot, zoom);
            reason = null;
            return true;
        }

        private static bool ApplyShotTo(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            // 브리지: shot_to(zoom=1f, x="2.5u", y="0u", duration).
            if (!NumberToken.TryParseFloat(cmd.Arg(0, "1"), out float zoom) ||
                !UnitToken.TryParseSignedPixels(cmd.Arg(1, "2.5u"), tuning.ReferenceStageWidth, out float x) ||
                !UnitToken.TryParseSignedPixels(cmd.Arg(2, "0u"), tuning.ReferenceStageWidth, out float y))
            {
                reason = $"shot_to 인자를 읽지 못했다: {cmd}";
                return false;
            }

            state.Shot = ShotIntents.To(state.Shot, zoom, new Vec2(x, y));
            reason = null;
            return true;
        }

        private static bool ApplyShotTrack(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            // 브리지: shot_track(x="2.5u", y="0u", duration).
            if (!UnitToken.TryParseSignedPixels(cmd.Arg(0, "2.5u"), tuning.ReferenceStageWidth, out float x) ||
                !UnitToken.TryParseSignedPixels(cmd.Arg(1, "0u"), tuning.ReferenceStageWidth, out float y))
            {
                reason = $"shot_track 인자를 읽지 못했다: {cmd}";
                return false;
            }

            state.Shot = ShotIntents.Track(state.Shot, new Vec2(x, y));
            reason = null;
            return true;
        }
    }
}
