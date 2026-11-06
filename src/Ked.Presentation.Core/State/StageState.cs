using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 리듀서가 접는 커맨드 하나. 이름 + 문자열 인자 + 출처.
    /// 런타임은 Yarn에서, 저작 도구는 발행된 출력에서 만든다 
    /// </summary>
    public readonly struct StageCommand
    {
        public readonly string Name;
        public readonly IReadOnlyList<string> Args;

        /// <summary>어디서 온 커맨드인가 (파일:행). Unhandled 보고에 그대로 실린다.</summary>
        public readonly string Source;

        public StageCommand(string name, IReadOnlyList<string> args, string source = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Args = args ?? Array.Empty<string>();
            Source = source;
        }

        /// <summary>인자가 없거나 빈 문자열이면 fallback. yarn의 생략 인자가 이 모양으로 온다.</summary>
        public string Arg(int index, string fallback = null)
            => index >= 0 && index < Args.Count && !string.IsNullOrEmpty(Args[index])
                ? Args[index]
                : fallback;

        public override string ToString()
            => $"<<{Name} {string.Join(" ", Args)}>>{(Source != null ? $" @ {Source}" : "")}";
    }

    /// 접지 못한 커맨드의 기록. 조용히 버리지 않음.
    /// 이 목록이 "아직 마이그레이션 끝나지 않은 작업 목록".
    public readonly struct UnhandledCommand
    {
        public readonly StageCommand Command;
        public readonly string Reason;

        public UnhandledCommand(in StageCommand command, string reason)
        {
            Command = command;
            Reason = reason;
        }

        public override string ToString() => $"{Command} — {Reason}";
    }

    // 슬롯 리그의 구조 부착 상태 — 어느 무대의 어느 깊이 층에 섰나. 스폰 때 한 번 정해진다 —
    // 리그는 무대 · 층을 옮기지 않는다(char_to는 2026-11-06에 걷었다. 다른 자리에 서려면 fade_out 뒤 새 slot).
    // 무대 키는 게임 데이터라 문자열로 담는다(어떤 무대가 있는지는 호스트가 정한다).
    // 층은 깊이 값 d다. 라벨 → d는 StageLayerLabels의 눈금이고, 모르는 라벨은 mid로 간다 —
    // 런타임 StageKeyParser와 같다(그쪽은 경고한다). 원문 라벨은 진단용으로 남긴다.
    public readonly struct SlotAttachment
    {
        public readonly string StageKey;
        public readonly string LayerKey;
        public readonly float LayerDepth;

        public SlotAttachment(string stageKey, string layerKey)
        {
            string stage = (stageKey ?? "").Trim().ToLowerInvariant();

            StageKey = stage.Length == 0 ? StageRoles.MainStageKey : stage;
            LayerKey = layerKey;
            LayerDepth = StageLayerLabels.TryGetDepth(layerKey, out float depth) ? depth : StageLayerLabels.Mid;
        }

        public override string ToString() => $"{StageKey}/{LayerKey}";
    }

    /// <summary>
    /// 무대 확정 상태 - 커맨드 열을 접은 결과이자 정지 프레임의 데이터.
    ///
    /// 축: 노드 트리(좌표) - 가시성(alpha) - 샷 의도 - 슬롯 부착(구조) - 배역/별칭 - Unhandled.
    /// 배경/박스/이펙트 축은 아직 없다 - 그 커맨드가 오면 Unhandled에 남음.
    /// </summary>
    public sealed class StageState
    {
        public RectNodeTree Nodes { get; }

        public ShotIntentState Shot { get; set; }

        private readonly Dictionary<string, float> _alphas;
        private readonly Dictionary<string, SlotAttachment> _attachments;
        private readonly List<UnhandledCommand> _unhandled;

        /// <summary>스폰된 슬롯의 존재 기록. 리그 노드 키의 prefix가 된다.</summary>
        private readonly HashSet<string> _slots;

        // 배역 축: cast가 캐릭터를 슬롯에 앉힌다. 커맨드 대상 해석에 필요한
        // "누가 어느 슬롯인가"만 담는다.
        private readonly Dictionary<string, string> _slotByCharacter;
        private readonly Dictionary<string, string> _characterBySlot;

        // 별칭 축: actor가 "@3" 같은 기호를 캐릭터/슬롯 키에 잇는다.
        private readonly Dictionary<string, string> _aliases;

        // 초상 축: pose가 슬롯의 현재 변형을 정한다. 표정은 상태가 아니다 —
        // 런타임 CastBinding도 변형만 들고, 표정은 커맨드마다 인자로 온다.
        private readonly Dictionary<string, string> _variantBySlot;

        public IReadOnlyList<UnhandledCommand> Unhandled => _unhandled;

        public IReadOnlyCollection<string> Slots => _slots;

        public StageState(RectSpace rootSpace)
        {
            Nodes = new RectNodeTree(rootSpace);
            Shot = ShotIntentState.Default;

            _alphas = new Dictionary<string, float>(StringComparer.Ordinal);
            _attachments = new Dictionary<string, SlotAttachment>(StringComparer.Ordinal);
            _unhandled = new List<UnhandledCommand>();
            _slots = new HashSet<string>(StringComparer.Ordinal);
            _slotByCharacter = new Dictionary<string, string>(StringComparer.Ordinal);
            _characterBySlot = new Dictionary<string, string>(StringComparer.Ordinal);
            _aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            _variantBySlot = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private StageState(StageState source)
        {
            Nodes = source.Nodes.Clone();
            Shot = source.Shot;

            _alphas = new Dictionary<string, float>(source._alphas, StringComparer.Ordinal);
            _attachments = new Dictionary<string, SlotAttachment>(source._attachments, StringComparer.Ordinal);
            _unhandled = new List<UnhandledCommand>(source._unhandled);
            _slots = new HashSet<string>(source._slots, StringComparer.Ordinal);
            _slotByCharacter = new Dictionary<string, string>(source._slotByCharacter, StringComparer.Ordinal);
            _characterBySlot = new Dictionary<string, string>(source._characterBySlot, StringComparer.Ordinal);
            _aliases = new Dictionary<string, string>(source._aliases, StringComparer.Ordinal);
            _variantBySlot = new Dictionary<string, string>(source._variantBySlot, StringComparer.Ordinal);
        }

        /// <summary>깊은 복제. 리듀서의 순수성(원본 불변)이 이것 위에 선다.</summary>
        public StageState Clone() => new(this);

        // ── 슬롯 ─────────────────────────────────────────────────────

        /// <summary>슬롯 리그의 노드 키. 예: ("c1", "CharSlot_Track") → "c1/CharSlot_Track".</summary>
        public static string NodeKeyOf(string slotKey, string nodeId) => $"{slotKey}/{nodeId}";

        public bool HasSlot(string slotKey) => slotKey != null && _slots.Contains(slotKey);

        public void RegisterSlot(string slotKey)
        {
            if (string.IsNullOrEmpty(slotKey))
                throw new ArgumentException("슬롯 키가 비어 있다.", nameof(slotKey));

            _slots.Add(slotKey);
        }

        // ── 배역·별칭 ────────────────────────────────────────────────

        /// <summary>cast — 캐릭터를 슬롯에 앉힌다. 재배역은 이전 관계를 정리하고 갈아탄다.</summary>
        public void SetCast(string slotKey, string characterKey)
        {
            if (string.IsNullOrEmpty(slotKey))
                throw new ArgumentException("슬롯 키가 비어 있다.", nameof(slotKey));

            if (string.IsNullOrEmpty(characterKey))
                throw new ArgumentException("캐릭터 키가 비어 있다.", nameof(characterKey));

            // 슬롯 재사용과 캐릭터 이동 양쪽을 정리해야 두 맵이 어긋나지 않는다.
            if (_characterBySlot.TryGetValue(slotKey, out string previousCharacter))
                _slotByCharacter.Remove(previousCharacter);

            if (_slotByCharacter.TryGetValue(characterKey, out string previousSlot))
                _characterBySlot.Remove(previousSlot);

            _characterBySlot[slotKey] = characterKey;
            _slotByCharacter[characterKey] = slotKey;

            // 재배역은 변형을 비운다 — 런타임 CastRegistry.CastCharRig이 새 바인딩을
            // 빈 변형으로 만드는 것과 같다. 뒤따르는 pose가 다시 채운다.
            _variantBySlot.Remove(slotKey);
        }

        /// <summary>
        /// 슬롯의 배역을 푼다 — 런타임 CastRegistry.UncastCharRig과 같다(두 맵과 변형을 함께 비운다).
        /// 배경 스폰이 배역 있는 슬롯을 덮을 때 쓴다(StageReducer.Background.cs).
        /// </summary>
        public bool ClearCast(string slotKey)
        {
            if (slotKey == null || !_characterBySlot.TryGetValue(slotKey, out string characterKey))
                return false;

            _characterBySlot.Remove(slotKey);
            _slotByCharacter.Remove(characterKey);
            _variantBySlot.Remove(slotKey);
            return true;
        }

        /// <summary>pose — 슬롯의 현재 초상 변형. 빈 값이면 기본 변형으로 되돌린다.</summary>
        public void SetVariant(string slotKey, string variantKey)
        {
            if (string.IsNullOrEmpty(slotKey))
                throw new ArgumentException("슬롯 키가 비어 있다.", nameof(slotKey));

            if (string.IsNullOrEmpty(variantKey))
                _variantBySlot.Remove(slotKey);
            else
                _variantBySlot[slotKey] = variantKey;
        }

        /// <summary>슬롯의 현재 변형. 없으면 빈 문자열 — 조회 쪽이 기본 변형으로 물러선다.</summary>
        public string GetVariant(string slotKey)
            => slotKey != null && _variantBySlot.TryGetValue(slotKey, out string variantKey)
                ? variantKey
                : "";

        /// <summary>actor — 별칭 기호("@3")를 캐릭터/슬롯 키에 잇는다.</summary>
        public void SetAlias(string aliasSymbol, string roleKey)
        {
            if (string.IsNullOrEmpty(aliasSymbol))
                throw new ArgumentException("별칭 기호가 비어 있다.", nameof(aliasSymbol));

            if (string.IsNullOrEmpty(roleKey))
                throw new ArgumentException("별칭 대상이 비어 있다.", nameof(roleKey));

            _aliases[aliasSymbol] = roleKey;
        }

        /// <summary>슬롯에 앉은 캐릭터 키. 배역이 없으면 false.</summary>
        public bool TryGetCharacter(string slotKey, out string characterKey)
        {
            if (slotKey != null)
                return _characterBySlot.TryGetValue(slotKey, out characterKey);

            characterKey = null;
            return false;
        }

        /// <summary>
        /// 커맨드 대상 키 → 슬롯 키.
        ///
        /// 해석 순서(런타임 CharacterRigResolver.ResolveSlotKey와 같다): 별칭을 풀고 →
        /// 캐릭터 키인가(배역 맵) → 슬롯 키인가. 이걸로 "@3"·"parkeunseol"·"c1"이 모두 풀린다.
        /// 한 키가 슬롯 키이면서 캐스팅된 캐릭터 키이면 **캐릭터 쪽**이 이긴다 — 런타임이 그렇다.
        /// </summary>
        public bool TryResolveSlot(string roleKey, out string slotKey)
        {
            slotKey = null;

            if (string.IsNullOrEmpty(roleKey))
                return false;

            if (_aliases.TryGetValue(roleKey, out string aliased))
                roleKey = aliased;

            if (_slotByCharacter.TryGetValue(roleKey, out string bySlot) && _slots.Contains(bySlot))
            {
                slotKey = bySlot;
                return true;
            }

            if (_slots.Contains(roleKey))
            {
                slotKey = roleKey;
                return true;
            }

            return false;
        }

        // ── 가시성 축 ────────────────────────────────────────────────

        // 기록이 없으면 1(보임).
        public float GetAlpha(string nodeKey)
            => nodeKey != null && _alphas.TryGetValue(nodeKey, out float alpha) ? alpha : 1f;

        public void SetAlpha(string nodeKey, float alpha)
        {
            if (string.IsNullOrEmpty(nodeKey))
                throw new ArgumentException("노드 키가 비어 있다.", nameof(nodeKey));

            _alphas[nodeKey] = alpha;
        }

        // ── 구조 축 ──────────────────────────────────────────────────

        public bool TryGetAttachment(string slotKey, out SlotAttachment attachment)
        {
            if (slotKey != null)
                return _attachments.TryGetValue(slotKey, out attachment);

            attachment = default;
            return false;
        }

        /// <summary>스폰된 슬롯의 부착. SpawnSlot이 언제나 남기므로, 없으면 폴드 배선의 잘못이다.</summary>
        public SlotAttachment GetAttachment(string slotKey)
            => _attachments.TryGetValue(slotKey, out SlotAttachment attachment)
                ? attachment
                : throw new InvalidOperationException($"슬롯 '{slotKey}'의 부착이 없다 — 스폰(SpawnSlot)을 거치지 않은 슬롯이다.");

        public void SetAttachment(string slotKey, in SlotAttachment attachment)
        {
            if (string.IsNullOrEmpty(slotKey))
                throw new ArgumentException("슬롯 키가 비어 있다.", nameof(slotKey));

            _attachments[slotKey] = attachment;
        }

        /// <summary>리그가 하나라도 선 무대 키. 순서는 사전순(결정적).</summary>
        public List<string> AttachedStages()
        {
            SortedSet<string> stages = new(StringComparer.Ordinal);

            foreach (SlotAttachment attachment in _attachments.Values)
                stages.Add(attachment.StageKey);

            return new List<string>(stages);
        }

        /// <summary>이 무대에서 리그가 선 깊이 층의 d. 오름차순(먼 층부터) — 무대 리그의 형제 순서와 같다.</summary>
        public List<float> AttachedLayerDepths(string stageKey)
        {
            SortedSet<float> depths = new();

            foreach (SlotAttachment attachment in _attachments.Values)
            {
                if (string.Equals(attachment.StageKey, stageKey, StringComparison.Ordinal))
                    depths.Add(attachment.LayerDepth);
            }

            return new List<float>(depths);
        }

        // ── 타깃 라우팅 ────────────────────────────────────────────

        /// <summary>
        /// 목표 계산 출력을 상태에 접는다. 트랜스폼은 트리로, alpha는 가시성 축으로.
        /// 타깃이 흐르는 세 갈래 중 "상태 폴드"가 여기다.
        /// </summary>
        public void Apply(in NodeTarget target)
        {
            if (target.Kind == NodeTargetKind.CanvasAlpha)
            {
                SetAlpha(target.NodeKey, target.Value.X);
                return;
            }

            target.ApplyTo(Nodes);
        }

        // ── Unhandled ────────────────────────────────────────────────

        public void AddUnhandled(in StageCommand command, string reason)
            => _unhandled.Add(new UnhandledCommand(command, reason));
    }
}