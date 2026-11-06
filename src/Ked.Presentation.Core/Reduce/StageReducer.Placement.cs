namespace Ked.Presentation.Core
{
    /// <summary>
    /// place · size — focus 지점을 기준으로 삼는 배치.
    ///
    /// 둘 다 "노드를 어디로"가 아니라 "focus 지점을 어디로"를 말한다.
    /// 그 역산은 NodeTargets.TryPlaceFocus · TrySetDepth가 하고, 여기는 토큰 해석과 배선만 맡는다.
    /// </summary>
    public static partial class StageReducer
    {
        private static bool ApplyPlace(
            StageState state, in StageCommand cmd, StageReducerTuning tuning,
            string screenPointName, string defaultFocusToken, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!FocusPresetName.TryNormalizeToken(cmd.Arg(1, defaultFocusToken), out string focusName))
            {
                reason = $"focus 프리셋 토큰 '{cmd.Arg(1)}'을 모른다";
                return false;
            }

            state.TryGetCharacter(slotKey, out string characterKey);

            if (!NodeTargets.TryPlaceFocus(
                    state, slotKey, characterKey, focusName, screenPointName,
                    tuning.FocusTuning, out NodeTarget target, out reason))
            {
                return false;
            }

            state.Apply(target);
            return true;
        }

        private static bool ApplySize(
            StageState state, in StageCommand cmd, StageReducerTuning tuning,
            string depthPresetKey, string preserveFocusToken, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            state.TryGetCharacter(slotKey, out string characterKey);

            if (!NodeTargets.TrySetDepth(
                    state, slotKey, characterKey, depthPresetKey, preserveFocusToken,
                    tuning.DepthLevel, tuning.FocusTuning,
                    out NodeTarget depthYTarget, out NodeTarget depthScaleTarget,
                    out reason))
            {
                return false;
            }

            state.Apply(depthYTarget);
            state.Apply(depthScaleTarget);
            return true;
        }
    }
}
