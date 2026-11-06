using System;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 무대의 역할. 같은 세계의 원근은 한 무대의 층(d)이 맡고, 무대를 나누는 것은 다른 화면이라는 뜻이다(2026-11-06).
    ///
    ///   Main     본 무대. 장면이 사는 곳이고 샷(카메라)이 움직인다. 하나뿐이다
    ///   Overlay  덮는 무대. 컷인 · 인서트 · 커튼. 카메라는 중립에 고정 — 내용은 그 무대에 세운 리그가 만든다
    ///
    /// 겹쳐 그려지는 순서(조립이 세운 순서, 뒤가 위)와 tx(마스크 — 무대를 어디까지 보여 줄지)는 역할과 따로다.
    /// 역할은 무대 키로 정한다. 대본에서 무대를 생략하면 본 무대다(slot · bg_spawn).
    /// 런타임 샷 시스템과 코어 하네스가 이 한 곳을 본다.
    /// </summary>
    public enum StageRole
    {
        Main,
        Overlay,
    }

    public static class StageRoles
    {
        public const string MainStageKey = "stage00";

        public static StageRole Of(string stageKey)
            => string.Equals(stageKey, MainStageKey, StringComparison.Ordinal) ? StageRole.Main : StageRole.Overlay;
    }
}
