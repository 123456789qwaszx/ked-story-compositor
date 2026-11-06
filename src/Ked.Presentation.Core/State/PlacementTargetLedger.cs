using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// "트윈이 다 끝났다면 어디에 있을 것인가"의 예약 장부.
    /// </summary>
    public sealed class PlacementTargetLedger
    {
        private struct Slots
        {
            public Vec2? AnchoredPosition;
            public Vec2? LocalScaleXY;   // z는 라이브 값을 보존.
            public Vec3? LocalEuler;

            public bool IsEmpty =>
                !AnchoredPosition.HasValue && !LocalScaleXY.HasValue && !LocalEuler.HasValue;
        }

        private readonly Dictionary<string, Slots> _targets = new(StringComparer.Ordinal);

        public bool IsEmpty => _targets.Count == 0;

        public int Count => _targets.Count;

        public void PublishAnchoredPosition(string key, Vec2 target)
        {
            Slots slots = GetOrDefault(key);
            slots.AnchoredPosition = target;
            _targets[key] = slots;
        }

        public void PublishLocalScale(string key, Vec2 targetXY)
        {
            Slots slots = GetOrDefault(key);
            slots.LocalScaleXY = targetXY;
            _targets[key] = slots;
        }

        public void PublishLocalEuler(string key, Vec3 target)
        {
            Slots slots = GetOrDefault(key);
            slots.LocalEuler = target;
            _targets[key] = slots;
        }

        /// <summary>
        /// 목표 계산 출력을 그대로 예약으로 받는다 — 타깃이 흐르는 세 갈래 중 하나.
        /// 트윈 종점·상태 폴드와 같은 값을 보게 하는 것이 요점이다.
        /// </summary>
        public void Publish(in NodeTarget target)
        {
            switch (target.Kind)
            {
                case NodeTargetKind.AnchoredPosition:
                    PublishAnchoredPosition(target.NodeKey, target.Value.XY);
                    break;

                case NodeTargetKind.LocalScaleXY:
                    PublishLocalScale(target.NodeKey, target.Value.XY);
                    break;

                case NodeTargetKind.LocalEulerAngles:
                    PublishLocalEuler(target.NodeKey, target.Value);
                    break;

                default:
                    // SizeDelta(즉시 쓰기) · CanvasAlpha(가시성 축)는 장부 대상이 아니다 — 모르는 게 아니라 받지 않는 것.
                    throw new ArgumentException($"장부 대상이 아닌 타깃 종류: {target.Kind}", nameof(target));
            }
        }

        public bool HasTargets(string key)
            => key != null && _targets.ContainsKey(key);

        public void Clear(string key)
        {
            if (key != null)
                _targets.Remove(key);
        }

        public void ClearAll() => _targets.Clear();

        /// <summary>
        /// 라이브 상태에 예약된 target을 입힌다. 예약이 없으면 그대로 반환.
        /// 이 결과를 RectChainMath에 넘기는 동작만 수행.
        /// </summary>
        public RectNodeState ApplyTo(string key, in RectNodeState live)
        {
            if (key == null || !_targets.TryGetValue(key, out Slots slots) || slots.IsEmpty)
                return live;

            RectNodeState result = live;

            if (slots.AnchoredPosition.HasValue)
                result = result.WithAnchoredPosition(slots.AnchoredPosition.Value);

            if (slots.LocalScaleXY.HasValue)
            {
                // z는 라이브값을 그대로 반환.
                Vec2 xy = slots.LocalScaleXY.Value;
                result = result.WithLocalScale(new Vec3(xy.X, xy.Y, live.LocalScale.Z));
            }

            if (slots.LocalEuler.HasValue)
                result = result.WithLocalEuler(slots.LocalEuler.Value);

            return result;
        }

        private Slots GetOrDefault(string key)
        {
            Require(key);
            return _targets.TryGetValue(key, out Slots slots) ? slots : default;
        }

        private static void Require(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("장부 키가 비어 있다.", nameof(key));
        }
    }
}