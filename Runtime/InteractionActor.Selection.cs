using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EasyInteraction
{
    public sealed partial class InteractionActor
    {
        [SerializeField]
        private InteractionSelectionMode selectionMode;
        [SerializeField, Min(.1f)]
        private float range = 3;
        [SerializeField, Range(1, 179)]
        private float focusAngle = 60;
        [SerializeField, Range(0, 1)]
        private float directionWeight = .75f;
        [SerializeField, Range(0, .5f)]
        private float switchThreshold = .08f;
        [SerializeField]
        private LayerMask targetLayers = ~0;
        [SerializeField]
        private LayerMask obstacleLayers = ~0;
        [SerializeField, Min(.001f)]
        private float castRadius = .12f;
        [SerializeField, Min(.01f)]
        private float contactRadius = .5f;
        [SerializeField]
        private Camera cursorCamera;
        [SerializeField]
        private bool includeTriggers;
        [SerializeField]
        private bool requireVisibility = true;
        [SerializeField]
        private bool debugSelection;
        private Collider[] overlaps = new Collider[32];
        private RaycastHit[] hits = new RaycastHit[16];
        private Candidate[] candidates = new Candidate[32];
        private readonly HashSet<InteractionTarget> seen = new HashSet<InteractionTarget>();
        private readonly List<Candidate> cycleCandidates = new List<Candidate>(16);
        private InteractionTarget explicitTarget, lockedTarget;
        private InteractionPoint lockedPoint;
        private Vector2 pointerPosition;
        private bool manualPointer;
        private int candidateCount;
        private RaycastHit selectionHit;
        private struct Candidate
        {
            public InteractionTarget Target;
            public int Point, Priority, Id;
            public float Score;
            public Vector3 Position;
        }

        private static readonly ProfilerMarker selectionMarker = new ProfilerMarker("EasyInteraction.SelectTarget");
        public InteractionSelectionMode SelectionMode
        {
            get => selectionMode;
            set
            {
                selectionMode = value;
                lockedTarget = null;
            }
        }

        public float Range { get => range; set => range = Mathf.Max(.1f, value); }
        public float FocusAngle { get => focusAngle; set => focusAngle = Mathf.Clamp(value, 1, 179); }
        public float DirectionWeight { get => directionWeight; set => directionWeight = Mathf.Clamp01(value); }
        public float SwitchThreshold { get => switchThreshold; set => switchThreshold = Mathf.Clamp(value, 0, .5f); }
        public LayerMask TargetLayers { get => targetLayers; set => targetLayers = value; }
        public LayerMask ObstacleLayers { get => obstacleLayers; set => obstacleLayers = value; }
        public float CastRadius { get => castRadius; set => castRadius = Mathf.Max(.001f, value); }
        public float ContactRadius { get => contactRadius; set => contactRadius = Mathf.Max(.01f, value); }
        public Camera CursorCamera { get => cursorCamera; set => cursorCamera = value; }
        public bool IncludeTriggers { get => includeTriggers; set => includeTriggers = value; }
        public bool RequireVisibility { get => requireVisibility; set => requireVisibility = value; }
        public bool DebugSelection { get => debugSelection; set => debugSelection = value; }
        public int LastCandidateCount { get; private set; }
        public int LastVisibilityChecks { get; private set; }
        public int LastPhysicsQueries { get; private set; }
        public Collider LastOccluder { get; private set; }
        public int OverlapCapacity => overlaps.Length;

        public void SetPointerPosition(Vector2 position)
        {
            pointerPosition = position;
            manualPointer = true;
        }

        public void UseDevicePointer() => manualPointer = false;
        public bool SelectTarget(InteractionTarget target)
        {
            selectionMode = InteractionSelectionMode.Explicit;
            explicitTarget = target;
            lockedTarget = null;
            return RefreshTarget() == target && target;
        }

        public void ReleaseTargetLock()
        {
            lockedTarget = null;
            lockedPoint = null;
        }

        public bool PinTarget()
        {
            if (!CurrentTarget)
                return false;
            lockedTarget = CurrentTarget;
            lockedPoint = CurrentPoint;
            return true;
        }

        public InteractionTarget RefreshTarget()
        {
            using (selectionMarker.Auto())
            {
                LastCandidateCount = LastVisibilityChecks = LastPhysicsQueries = 0;
                LastOccluder = null;
                selectionHit = default;
                if (!isActiveAndEnabled)
                {
                    SetTarget(null, null);
                    return null;
                }

                InteractionTarget next = null;
                InteractionPoint point = null;
                if (lockedTarget && WithinReach(lockedTarget) && InCone(lockedTarget.AimPosition) && (!requireVisibility || Visible(lockedTarget)))
                {
                    next = lockedTarget;
                    point = lockedPoint;
                }
                else
                {
                    lockedTarget = null;
                    lockedPoint = null;
                    switch (selectionMode)
                    {
                        case InteractionSelectionMode.Raycast:
                            next = SelectRay(false, false, out point);
                            break;
                        case InteractionSelectionMode.SphereCast:
                            next = SelectRay(true, false, out point);
                            break;
                        case InteractionSelectionMode.Cursor:
                            next = SelectRay(false, true, out point);
                            break;
                        case InteractionSelectionMode.Explicit:
                            if (explicitTarget && explicitTarget.isActiveAndEnabled && CheckGeometry(explicitTarget, null, true, requireVisibility, false).IsAvailable)
                                next = explicitTarget;
                            break;
                        default:
                            next = SelectArea(out point);
                            break;
                    }
                }

                SetTarget(next, point);
                return CurrentTarget;
            }
        }

        private InteractionTarget SelectRay(bool sphere, bool cursor, out InteractionPoint point)
        {
            point = null;
            var origin = View.position;
            var forward = View.forward;
            if (cursor)
            {
                var camera = cursorCamera ? cursorCamera : View.GetComponent<Camera>();
                if (!camera)
                    return null;
                Vector2 position = manualPointer ? pointerPosition : Pointer.current != null ? Pointer.current.position.ReadValue() : new Vector2(Screen.width * .5f, Screen.height * .5f);
                var ray = camera.ScreenPointToRay(position);
                origin = ray.origin;
                forward = ray.direction;
            }

            if (!NearestHit(origin, forward, range, sphere ? castRadius : 0, out var hit))
                return null;
            var target = InteractionTarget.Resolve(hit.collider);
            if (!target || (targetLayers.value & (1 << hit.collider.gameObject.layer)) == 0)
                return null;
            if (sphere && requireVisibility && !Visible(target))
                return null;
            LastCandidateCount = 1;
            selectionHit = hit;
            point = target.GetPoint(hit.collider);
            return target;
        }

        private void CollectCandidates()
        {
            candidateCount = 0;
            seen.Clear();
            float radius = selectionMode == InteractionSelectionMode.Contact ? contactRadius : range;
            int count;
            do
            {
                LastPhysicsQueries++;
                count = Physics.OverlapSphereNonAlloc(View.position, radius, overlaps, targetLayers, includeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore);
                if (count < overlaps.Length)
                    break;
                Array.Resize(ref overlaps, checked(overlaps.Length * 2));
            }
            while (true);
            var origin = View.position;
            var forward = View.forward;
            var root = PlayerRoot;
            float minimumDot = Mathf.Cos(focusAngle * .5f * Mathf.Deg2Rad);
            bool cone = selectionMode == InteractionSelectionMode.Focus;
            for (int i = 0; i < count; i++)
            {
                var collider = overlaps[i];
                overlaps[i] = null;
                var target = InteractionTarget.Resolve(collider);
                if (!target || collider.transform.IsChildOf(root) || !seen.Add(target))
                    continue;
                for (int p = 0; p < target.FocusPointCount; p++)
                {
                    if (!target.UseFocusPoint(p))
                        continue;
                    var position = target.GetFocusPosition(p);
                    var offset = position - origin;
                    float distance = offset.magnitude;
                    if (distance > radius)
                        continue;
                    float dot = distance < .0001f ? 1 : Vector3.Dot(offset / distance, forward);
                    if (cone && dot < minimumDot)
                        continue;
                    float score = cone ? directionWeight * ((dot - minimumDot) / (1 - minimumDot)) + (1 - directionWeight) * (1 - distance / radius) : 1 - distance / radius;
                    if (target == CurrentTarget)
                        score += switchThreshold;
                    if (candidateCount == candidates.Length)
                        Array.Resize(ref candidates, checked(candidates.Length * 2));
                    candidates[candidateCount++] = new Candidate
                    {
                        Target = target,
                        Point = p,
                        Position = position,
                        Priority = target.FocusPriority,
                        Score = score,
                        Id = target.GetInstanceID()
                    };
                }
            }

            LastCandidateCount = candidateCount;
            // Bottom-up heap construction is O(n). Typical visible selection removes only one candidate.
            for (int i = candidateCount / 2 - 1; i >= 0; i--)
                SiftDown(i);
        }

        private static bool Better(Candidate a, Candidate b)
        {
            if (a.Priority != b.Priority)
                return a.Priority > b.Priority;
            if (a.Score != b.Score)
                return a.Score > b.Score;
            if (a.Id != b.Id)
                return a.Id < b.Id;
            return a.Point < b.Point;
        }

        private void SiftDown(int index)
        {
            while (true)
            {
                int left = index * 2 + 1;
                if (left >= candidateCount)
                    return;
                int right = left + 1;
                int best = right < candidateCount && Better(candidates[right], candidates[left]) ? right : left;
                if (!Better(candidates[best], candidates[index]))
                    return;
                var temporary = candidates[index];
                candidates[index] = candidates[best];
                candidates[best] = temporary;
                index = best;
            }
        }

        private Candidate PopCandidate()
        {
            var result = candidates[0];
            candidateCount--;
            candidates[0] = candidates[candidateCount];
            candidates[candidateCount] = default;
            if (candidateCount > 0)
                SiftDown(0);
            return result;
        }

        private InteractionTarget SelectArea(out InteractionPoint point)
        {
            CollectCandidates();
            InteractionTarget target = null;
            point = null;
            int rejected = 0;
            while (candidateCount > 0)
            {
                var best = PopCandidate();
                if (!requireVisibility || VisiblePoint(best.Target, best.Position))
                {
                    target = best.Target;
                    point = target.GetPoint(best.Point);
                    break;
                }

                if (++rejected == 4 && candidateCount > 0)
                {
                    // Dense occlusion: scan the rest without paying for N heap removals.
                    // A candidate can be skipped only after an actually visible better candidate exists.
                    Candidate visible = default;
                    bool found = false;
                    for (int i = 0; i < candidateCount; i++)
                    {
                        var candidate = candidates[i];
                        if (found && !Better(candidate, visible))
                            continue;
                        if (!VisiblePoint(candidate.Target, candidate.Position))
                            continue;
                        visible = candidate;
                        found = true;
                    }

                    if (found)
                    {
                        target = visible.Target;
                        point = target.GetPoint(visible.Point);
                    }

                    break;
                }
            }

            Array.Clear(candidates, 0, candidateCount);
            candidateCount = 0;
            return target;
        }

        public bool CycleTarget(int direction = 1)
        {
            if (selectionMode != InteractionSelectionMode.Focus && selectionMode != InteractionSelectionMode.Nearest && selectionMode != InteractionSelectionMode.Contact)
                return false;
            CollectCandidates();
            cycleCandidates.Clear();
            seen.Clear();
            while (candidateCount > 0)
            {
                var candidate = PopCandidate();
                if (seen.Contains(candidate.Target))
                    continue;
                if (requireVisibility && !VisiblePoint(candidate.Target, candidate.Position))
                    continue;
                seen.Add(candidate.Target);
                cycleCandidates.Add(candidate);
            }

            if (cycleCandidates.Count == 0)
            {
                SetTarget(null, null);
                return false;
            }

            int index = -1;
            for (int i = 0; i < cycleCandidates.Count; i++)
                if (cycleCandidates[i].Target == CurrentTarget)
                {
                    index = i;
                    break;
                }

            index = (index + (direction < 0 ? -1 : 1) + cycleCandidates.Count) % cycleCandidates.Count;
            var next = cycleCandidates[index];
            lockedTarget = next.Target;
            lockedPoint = next.Target.GetPoint(next.Point);
            SetTarget(lockedTarget, lockedPoint);
            return true;
        }

        private bool InCone(Vector3 position) => selectionMode != InteractionSelectionMode.Focus || Vector3.Angle(View.forward, position - View.position) <= focusAngle * .5f;
        private bool WithinReach(InteractionTarget target)
        {
            float radius = selectionMode == InteractionSelectionMode.Contact ? contactRadius : range;
            for (int i = 0; i < target.FocusPointCount; i++)
                if ((target.GetFocusPosition(i) - View.position).sqrMagnitude <= radius * radius)
                    return true;
            return false;
        }

        public bool IsVisible(InteractionTarget target) => target && Visible(target);
        private bool Visible(InteractionTarget target)
        {
            for (int i = 0; i < target.FocusPointCount; i++)
                if (target.UseFocusPoint(i) && VisiblePoint(target, target.GetFocusPosition(i)))
                    return true;
            return false;
        }

        private InteractionAvailability CheckGeometry(InteractionTarget target, InteractionAction action, bool checkRange, bool checkVisibility, bool selected)
        {
            if (!checkRange && !checkVisibility)
                return InteractionAvailability.Available;
            float radius = selectionMode == InteractionSelectionMode.Contact ? contactRadius : range;
            bool reachable = false;
            if (selected && selectionHit.collider && InteractionTarget.Resolve(selectionHit.collider) == target)
            {
                var position = selectionHit.point;
                if (!checkRange || (position - View.position).sqrMagnitude <= radius * radius + .001f)
                {
                    reachable = true;
                    if (!checkVisibility || VisiblePoint(target, position))
                        return InteractionAvailability.Available;
                }
            }

            for (int i = 0; i < target.FocusPointCount; i++)
            {
                if (!target.UseFocusPoint(i))
                    continue;
                var point = target.GetPoint(i);
                if (action && point && !point.Allows(action))
                    continue;
                var position = target.GetFocusPosition(i);
                if (checkRange && (position - View.position).sqrMagnitude > radius * radius)
                    continue;
                reachable = true;
                if (!checkVisibility || VisiblePoint(target, position))
                    return InteractionAvailability.Available;
            }

            if (!target.FocusPoint && !target.HasSelectionParts && target.ClosestPoint(View.position, targetLayers, includeTriggers, out var closest))
            {
                if (!checkRange || (closest - View.position).sqrMagnitude <= radius * radius)
                {
                    reachable = true;
                    if (!checkVisibility || VisiblePoint(target, closest))
                        return InteractionAvailability.Available;
                }
            }

            return reachable ? Denied(InteractionFailure.Occluded, "The object is blocked.") : Denied(InteractionFailure.OutOfRange, "Move closer.");
        }

        private bool VisiblePoint(InteractionTarget target, Vector3 position)
        {
            LastVisibilityChecks++;
            var offset = position - View.position;
            float distance = offset.magnitude;
            if (distance < .0001f)
                return true;
            if (!NearestHit(View.position, offset / distance, distance + .001f, 0, out var hit))
                return true;
            if (InteractionTarget.Resolve(hit.collider) == target)
                return true;
            LastOccluder = hit.collider;
            return false;
        }

        private bool NearestHit(Vector3 origin, Vector3 forward, float distance, float radius, out RaycastHit nearest)
        {
            int mask = targetLayers.value | obstacleLayers.value;
            LastPhysicsQueries++;
            var query = includeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;
            bool hit = radius > 0 ? Physics.SphereCast(origin, radius, forward, out nearest, distance, mask, query) : Physics.Raycast(origin, forward, out nearest, distance, mask, query);
            if (!hit)
                return false;
            var root = PlayerRoot;
            if (!nearest.collider.transform.IsChildOf(root))
                return true;
            int count;
            do
            {
                LastPhysicsQueries++;
                count = radius > 0 ? Physics.SphereCastNonAlloc(origin, radius, forward, hits, distance, mask, query) : Physics.RaycastNonAlloc(origin, forward, hits, distance, mask, query);
                if (count < hits.Length)
                    break;
                Array.Resize(ref hits, checked(hits.Length * 2));
            }
            while (true);
            nearest = default;
            float closest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var item = hits[i];
                hits[i] = default;
                if (!item.collider || item.collider.transform.IsChildOf(root) || item.distance >= closest)
                    continue;
                nearest = item;
                closest = item.distance;
            }

            return closest < float.PositiveInfinity;
        }

        private void OnDrawGizmosSelected()
        {
            if (!debugSelection)
                return;
            var origin = view ? view : transform;
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(origin.position, origin.forward * range);
            if (CurrentTarget)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(origin.position, CurrentTarget.AimPosition);
            }

            if (LastOccluder)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireCube(LastOccluder.bounds.center, LastOccluder.bounds.size);
            }
        }
    }
}
