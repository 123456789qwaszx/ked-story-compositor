using System;

namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // 샷 = 숫자 셋(zoom · pan · focus)이고, 무대의 트랜스폼은 전부 그 셋의 함수다.
    // 이 파일이 그 함수의 유일한 자리다 — 런타임은 출력을 노드에 쓰고, 폴드는 상태에 접고,
    // 중립은 Solve(Default)다. 식은 종전 PresentationResponseMath · PresentationCameraRootApplier에서
    // 글자 그대로 옮겼다(2026-11-05). 기대값은 종전 식에서 온다.
    //
    // 이 런타임에 카메라는 없다. 카메라 루트 둘(팬 · 줌)이 무대를 밀고 키워 카메라처럼 보이게 하고,
    // 깊이 층은 그 카메라에 d만큼 다르게 반응해서 시차를 만든다.
    //
    // 층의 기준값은 항등이다(위치 0 · 배율 1). 무대 리그 빌더가 보장하므로 입력에 없다.
    // ─────────────────────────────────────────────────────────────────

    /// <summary>한 깊이에서의 층 반응 계수. d의 곡선(LayerProfileCurve) 위의 한 점.</summary>
    public readonly struct LayerProfile
    {
        /// <summary>카메라 배율 변화량 1.0당, 초점에서 멀어지는 방향으로 밀리는 거리(px). 축별 독립.</summary>
        public readonly Vec2 FocusSpreadPerZoom;

        /// <summary>팬(px)을 따라가는 비율. 축별 독립.</summary>
        public readonly Vec2 PanResponse;

        /// <summary>카메라 배율을 얼마나 상쇄/강조할지. 층 배율 = cameraScale ^ 이 값.</summary>
        public readonly float ZoomScaleResponse;

        public LayerProfile(Vec2 focusSpreadPerZoom, Vec2 panResponse, float zoomScaleResponse)
        {
            FocusSpreadPerZoom = focusSpreadPerZoom;
            PanResponse = panResponse;
            ZoomScaleResponse = zoomScaleResponse;
        }

        public static readonly LayerProfile Identity = new(Vec2.Zero, Vec2.Zero, 0f);

        public static LayerProfile Lerp(in LayerProfile a, in LayerProfile b, float t)
            => new(
                a.FocusSpreadPerZoom + (b.FocusSpreadPerZoom - a.FocusSpreadPerZoom) * t,
                a.PanResponse + (b.PanResponse - a.PanResponse) * t,
                a.ZoomScaleResponse + (b.ZoomScaleResponse - a.ZoomScaleResponse) * t);
    }

    /// <summary>
    /// d → LayerProfile. 표본 사이는 선형, 바깥은 끝 표본으로 고정.
    /// 기본 표본 다섯(Default)은 종전 PresentationResponseProfile의 Far…Close 값 그대로라,
    /// 눈금 위에서는 종전과 정확히 같은 값이 나온다.
    ///
    /// panResponse는 축마다 −1보다 커야 한다. 층이 화면에서 움직이는 양은 pan × (1 + panResponse)라,
    /// −1이면 팬을 전혀 따라가지 않고(무한히 먼 층) 그보다 작으면 거꾸로 간다. 그런 층의 점은 팬으로
    /// 화면 지점에 놓을 수 없다(SolvePanToPlace). 표본이 모두 −1보다 크면 선형 보간도 그렇다.
    /// </summary>
    public sealed class LayerProfileCurve
    {
        private readonly float[] _depths;
        private readonly LayerProfile[] _profiles;

        public int SampleCount => _depths.Length;

        public LayerProfileCurve(float[] depths, LayerProfile[] profiles)
        {
            if (depths == null || profiles == null || depths.Length == 0 || depths.Length != profiles.Length)
                throw new ArgumentException("층 프로필 표본이 비었거나 길이가 맞지 않는다.");

            for (int i = 1; i < depths.Length; i++)
                if (!(depths[i] > depths[i - 1]))
                    throw new ArgumentException($"층 프로필 표본은 d 오름차순이어야 한다 — [{i - 1}]={depths[i - 1]}, [{i}]={depths[i]}");

            for (int i = 0; i < profiles.Length; i++)
                if (!(profiles[i].PanResponse.X > -1f) || !(profiles[i].PanResponse.Y > -1f))
                    throw new ArgumentException($"층 프로필 panResponse는 축마다 −1보다 커야 한다 — [{i}] d={depths[i]} panResponse={profiles[i].PanResponse}");

            _depths = depths;
            _profiles = profiles;
        }

        /// <summary>종전 다섯 층의 값. Far(-1) · Back(-0.5) · Mid(0) · Front(0.5) · Close(1).</summary>
        public static LayerProfileCurve Default { get; } = new(
            new[] { StageLayerLabels.Far, StageLayerLabels.Back, StageLayerLabels.Mid, StageLayerLabels.Front, StageLayerLabels.Close },
            new[]
            {
                new LayerProfile(new Vec2(-8f, 0f), new Vec2(-0.04f, 0f), -0.15f),
                new LayerProfile(new Vec2(-4f, 0f), new Vec2(-0.02f, 0f), -0.08f),
                LayerProfile.Identity,
                new LayerProfile(new Vec2(4f, 0f), new Vec2(0.02f, 0f), 0.04f),
                new LayerProfile(new Vec2(16f, 0f), new Vec2(0.2f, 0f), 0.16f),
            });

        public LayerProfile Evaluate(float depth)
        {
            if (depth <= _depths[0])
                return _profiles[0];

            int last = _depths.Length - 1;

            if (depth >= _depths[last])
                return _profiles[last];

            int hi = 1;
            while (_depths[hi] < depth)
                hi++;

            float t = (depth - _depths[hi - 1]) / (_depths[hi] - _depths[hi - 1]);

            return LayerProfile.Lerp(_profiles[hi - 1], _profiles[hi], t);
        }

        public static LayerProfileCurve FromDto(LayerProfileFileDto dto)
        {
            if (dto?.samples == null)
                throw new ArgumentException("layer-profile.json에 samples가 없다.");

            float[] depths = new float[dto.samples.Length];
            LayerProfile[] profiles = new LayerProfile[dto.samples.Length];

            for (int i = 0; i < dto.samples.Length; i++)
            {
                LayerProfileSampleDto s = dto.samples[i];
                depths[i] = s.depth;
                profiles[i] = new LayerProfile(s.focusSpreadPerZoom.ToVec2(), s.panResponse.ToVec2(), s.zoomScaleResponse);
            }

            return new LayerProfileCurve(depths, profiles);
        }

        public LayerProfileFileDto ToDto()
        {
            LayerProfileSampleDto[] samples = new LayerProfileSampleDto[_depths.Length];

            for (int i = 0; i < _depths.Length; i++)
            {
                samples[i] = new LayerProfileSampleDto
                {
                    depth = _depths[i],
                    focusSpreadPerZoom = new Float2Dto { x = _profiles[i].FocusSpreadPerZoom.X, y = _profiles[i].FocusSpreadPerZoom.Y },
                    panResponse = new Float2Dto { x = _profiles[i].PanResponse.X, y = _profiles[i].PanResponse.Y },
                    zoomScaleResponse = _profiles[i].ZoomScaleResponse,
                };
            }

            return new LayerProfileFileDto { samples = samples };
        }
    }

    /// <summary>한 층이 샷에 반응한 결과. 기준(항등)에서의 편차다 — 오프셋은 rig space 벡터, 배율은 절대값.</summary>
    public readonly struct LayerResponse
    {
        public readonly Vec2 OffsetInRigSpace;
        public readonly Vec2 Scale;

        public LayerResponse(Vec2 offsetInRigSpace, Vec2 scale)
        {
            OffsetInRigSpace = offsetInRigSpace;
            Scale = scale;
        }

        public static readonly LayerResponse Identity = new(Vec2.Zero, Vec2.One);
    }

    /// <summary>샷 하나가 무대에 쓰는 값 전부. 카메라 루트 둘 + 층마다 하나.</summary>
    public readonly struct ShotLayout
    {
        public readonly float CameraScale;
        public readonly Vec2 Pan;
        public readonly LayerResponse[] Layers;

        public ShotLayout(float cameraScale, Vec2 pan, LayerResponse[] layers)
        {
            CameraScale = cameraScale;
            Pan = pan;
            Layers = layers;
        }
    }

    public static class ShotLayoutMath
    {
        /// <summary>카메라 루트 둘. Zoom_Root.localScale = (CameraScale, CameraScale, 1), Pan_Root.anchoredPosition = Pan.</summary>
        public static void SolveCamera(in ShotIntentState intent, out float cameraScale, out Vec2 pan)
        {
            cameraScale = ShotIntentMath.EvaluateCameraScale(intent.Zoom);
            pan = intent.PanInRigSpace;
        }

        /// <summary>
        /// 깊이 d인 층 하나의 반응.
        ///
        /// 위치 = sign(0 − focus) × focusSpread × (cameraScale − 1) + pan × panResponse
        /// 배율 = cameraScale ^ zoomScaleResponse
        ///
        /// 초점 산개의 부호는 층 기준위치(항등 = 0)와 초점의 차이에서 온다. 축별로 독립이고,
        /// 차이가 정확히 0이면 0이다(종전 Mathf.Approximately(x, 0)과 같다).
        /// </summary>
        public static LayerResponse SolveLayer(in ShotIntentState intent, float depth, LayerProfileCurve curve)
        {
            LayerProfile profile = curve.Evaluate(depth);

            float cameraScale = ShotIntentMath.EvaluateCameraScale(intent.Zoom);
            float zoomAmount = cameraScale - 1f;

            Vec2 fromFocus = Vec2.Zero - intent.FocusPointInRigSpace;

            float signX = fromFocus.X == 0f ? 0f : Math.Sign(fromFocus.X);
            float signY = fromFocus.Y == 0f ? 0f : Math.Sign(fromFocus.Y);

            Vec2 focusSpread = new(
                signX * profile.FocusSpreadPerZoom.X * zoomAmount,
                signY * profile.FocusSpreadPerZoom.Y * zoomAmount);

            Vec2 panFollow = Vec2.Scale(intent.PanInRigSpace, profile.PanResponse);

            float scale = (float)Math.Pow(cameraScale, profile.ZoomScaleResponse);

            return new LayerResponse(focusSpread + panFollow, new Vec2(scale, scale));
        }

        /// <summary>
        /// 층 안의 한 점이 무대 루트(rig space)의 어디에 보이는가 — 계층 Pan → Zoom → Layer를 합성한 결과.
        ///
        /// 화면 = pan + offset + cameraScale × layerScale × p
        ///
        /// 무대 노드가 전부 stretch · 피벗 중앙이라 각 노드의 원점이 부모의 중심이고, 층 위치는 부모(줌) 공간에
        /// offset / cameraScale로 쓰이므로 줌을 지나면 offset이 된다. 정지 프레임을 그리는 쪽(툴 프리뷰)과
        /// 하네스가 이 한 줄을 공유한다.
        /// </summary>
        public static Vec2 ProjectToRigSpace(Vec2 pointInLayer, float cameraScale, Vec2 pan, in LayerResponse layer)
            => pan + layer.OffsetInRigSpace + Vec2.Scale(pointInLayer, layer.Scale) * cameraScale;

        /// <summary>
        /// 층 d 안의 점이 화면(rig space)의 desired에 보이게 하는 팬 — ProjectToRigSpace의 팬에 대한 역.
        ///
        /// 줌과 초점이 정해지면 층 반응이 팬에 기대는 항은 pan × panResponse 하나라, 보이는 자리는 팬에 대해 선형이다.
        ///
        /// 보이는 자리 = pan × (1 + panResponse) + [팬 0일 때 보이는 자리]
        ///
        /// 그래서 축별로 닫힌 꼴로 풀린다. 산개 · 배율 항은 정방향 식(SolveLayer)을 팬 0으로 불러 얻는다 — 식을 다시 적지 않는다.
        /// mid(반응 항등)에서는 desired − p × cameraScale이 된다(종전 shot_focus_to의 식).
        /// </summary>
        public static Vec2 SolvePanToPlace(
            float zoom,
            Vec2 focusPointInRigSpace,
            Vec2 pointInLayer,
            Vec2 desiredInRigSpace,
            float depth,
            LayerProfileCurve curve)
        {
            ShotIntentState unpanned = new(zoom, Vec2.Zero, focusPointInRigSpace);

            SolveCamera(in unpanned, out float cameraScale, out _);
            LayerResponse layer = SolveLayer(in unpanned, depth, curve);

            Vec2 seenUnpanned = ProjectToRigSpace(pointInLayer, cameraScale, Vec2.Zero, in layer);
            Vec2 follow = Vec2.One + curve.Evaluate(depth).PanResponse;
            Vec2 remaining = desiredInRigSpace - seenUnpanned;

            return new Vec2(remaining.X / follow.X, remaining.Y / follow.Y);
        }

        /// <summary>무대 전체. 층 배열은 depths와 같은 순서.</summary>
        public static ShotLayout Solve(in ShotIntentState intent, ReadOnlySpan<float> depths, LayerProfileCurve curve)
        {
            SolveCamera(in intent, out float cameraScale, out Vec2 pan);

            LayerResponse[] layers = new LayerResponse[depths.Length];

            for (int i = 0; i < depths.Length; i++)
                layers[i] = SolveLayer(in intent, depths[i], curve);

            return new ShotLayout(cameraScale, pan, layers);
        }
    }
}
