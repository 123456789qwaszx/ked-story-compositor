namespace Ked.Presentation.Core
{
    public readonly struct ShotIntentState
    {
        // 저작된 zoom 의도. 실제 카메라 배율은 ShotIntentMath.EvaluateCameraScale.
        public readonly float Zoom;

        // rig-space 카메라 pan 오프셋.
        public readonly Vec2 PanInRigSpace;

        // rig-space 논리 focus 지점. 프레이밍·대상 산개가 이 점 기준으로 풀린다.
        public readonly Vec2 FocusPointInRigSpace;

        public ShotIntentState(float zoom, Vec2 panInRigSpace, Vec2 focusPointInRigSpace)
        {
            Zoom = zoom;
            PanInRigSpace = panInRigSpace;
            FocusPointInRigSpace = focusPointInRigSpace;
        }

        public static readonly ShotIntentState Default = new(0f, Vec2.Zero, Vec2.Zero);

        public override string ToString()
            => $"zoom={Zoom} pan={PanInRigSpace} focus={FocusPointInRigSpace}";
    }

    public static class ShotIntentMath
    {
        // 규약 상수: zoom 1당 배율 +0.05.
        public const float ZoomToScaleFactor = 0.05f;

        public static float EvaluateCameraScale(float zoom)
            => 1f + zoom * ZoomToScaleFactor;
    }
}