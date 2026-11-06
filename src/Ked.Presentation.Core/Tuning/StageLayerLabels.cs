namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // 무대 깊이 층 라벨 → 깊이 값 d.
    //
    // far·back·mid·front·close는 독립된 다섯 층이 아니다. 층의 진실은 깊이 값 d 하나이고,
    // 샷에 대한 층의 반응(초점 산개·팬 따라가기·줌 배율)은 d의 곡선(LayerProfileCurve)이다.
    // 라벨은 그 곡선 위의 눈금이다. 눈금을 옮기고 싶으면 여기 숫자 하나만 고친다.
    //
    // 캐릭터 깊이(DepthLevelLabels, 리그 배율)와는 **별도의 축**이다 — 같은 낱말을 쓰지만
    // 층은 카메라 반응 계수이고 캐릭터는 배율이라 뜻이 다르다. 두 표를 합치지 않는다 (2026-11-05).
    //
    // 별칭은 두지 않는다. 대본은 compositor가 내고 정본 다섯만 쓴다.
    // ─────────────────────────────────────────────────────────────────
    public static class StageLayerLabels
    {
        public const float Far = -1f;
        public const float Back = -0.5f;
        public const float Mid = 0f;
        public const float Front = 0.5f;
        public const float Close = 1f;

        /// <summary>정본 라벨 순서. 멀리서 가까이로.</summary>
        public static readonly string[] Names = { "far", "back", "mid", "front", "close" };

        /// <summary>Names와 같은 순서의 깊이 값.</summary>
        public static readonly float[] Depths = { Far, Back, Mid, Front, Close };

        /// <summary>라벨 → d. 모르는 라벨이면 false — 비슷한 이름으로 잇지 않는다.</summary>
        public static bool TryGetDepth(string token, out float depth)
        {
            switch ((token ?? "").Trim().ToLowerInvariant())
            {
                case "far": depth = Far; return true;
                case "back": depth = Back; return true;
                case "mid": depth = Mid; return true;
                case "front": depth = Front; return true;
                case "close": depth = Close; return true;
                default: depth = Mid; return false;
            }
        }

        /// <summary>d → 정본 라벨. 눈금에 없는 d는 숫자 문자열("0.25") — 노드 이름·진단용.</summary>
        public static string NameOf(float depth)
        {
            for (int i = 0; i < Depths.Length; i++)
                if (Depths[i] == depth)
                    return Names[i];

            return depth.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
