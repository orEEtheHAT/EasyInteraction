using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyInteraction
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Easy Interaction/Interaction Target")]
    public sealed partial class InteractionTarget : MonoBehaviour
    {
        [SerializeField]
        private string displayName;
        [SerializeField]
        private Transform focusPoint;
        [SerializeField, Tooltip("Higher priority wins before direction/distance scoring.")]
        private int focusPriority;
        [SerializeField]
        private Transform[] extraFocusPoints = Array.Empty<Transform>();
        [SerializeField, Tooltip("Stable identity for saving/networking. Assign unique IDs to runtime spawns from their world data.")]
        private string persistentId;
        private static readonly Dictionary<Collider, InteractionTarget> owners = new Dictionary<Collider, InteractionTarget>();
        private readonly List<Collider> colliders = new List<Collider>();
        private readonly List<InteractionAction> actions = new List<InteractionAction>();
        private readonly List<InteractionCondition> conditions = new List<InteractionCondition>();
        private readonly List<InteractionCost> costs = new List<InteractionCost>();
        private readonly List<InteractionPoint> points = new List<InteractionPoint>();
        private readonly List<Claim> claims = new List<Claim>(2);
        private readonly List<QueueEntry> queue = new List<QueueEntry>(4);
        private System.Collections.ObjectModel.ReadOnlyCollection<InteractionAction> actionView;
        private string cachedObjectName;
        private struct Claim
        {
            public InteractionActor Actor;
            public InteractionAction Action;
            public InteractionPoint Place;
        }

        private struct QueueEntry
        {
            public InteractionActor Actor;
            public InteractionAction Action;
        }

        public string DisplayName { get => string.IsNullOrEmpty(displayName) ? cachedObjectName ?? (cachedObjectName = gameObject.name) : displayName; set => displayName = value; }

        public string PersistentId
        {
            get => persistentId;
            set
            {
                InteractionWorld.Unregister(this);
                persistentId = value;
                if (isActiveAndEnabled)
                    InteractionWorld.Register(this);
            }
        }

        public Transform FocusPoint { get => focusPoint; set => focusPoint = value; }
        public int FocusPriority { get => focusPriority; set => focusPriority = value; }
        public Transform[] ExtraFocusPoints { get => extraFocusPoints; set => extraFocusPoints = value ?? Array.Empty<Transform>(); }
        public IReadOnlyList<InteractionAction> Actions => actionView ?? (actionView = actions.AsReadOnly());
        internal List<InteractionCondition> Conditions => conditions;
        internal List<InteractionCost> Costs => costs;
        public int ParticipantCount => claims.Count;
        public int QueueCount => queue.Count;
        public int FocusPointCount => 1 + points.Count + extraFocusPoints.Length;

        public Vector3 AimPosition
        {
            get
            {
                if (focusPoint)
                    return focusPoint.position;
                for (int i = 0; i < colliders.Count; i++)
                    if (colliders[i] && colliders[i].enabled && colliders[i].gameObject.activeInHierarchy)
                        return colliders[i].bounds.center;
                return transform.position;
            }
        }

        public Vector3 GetFocusPosition(int index)
        {
            if (index <= 0)
                return AimPosition;
            index--;
            if (index < points.Count)
                return points[index] ? points[index].transform.position : AimPosition;
            index -= points.Count;
            return index < extraFocusPoints.Length && extraFocusPoints[index] ? extraFocusPoints[index].position : AimPosition;
        }

        public InteractionPoint GetPoint(int index) => index > 0 && index <= points.Count ? points[index - 1] : null;
        internal bool UseFocusPoint(int index)
        {
            if (index == 0)
            {
                if (focusPoint)
                    return true;
                for (int i = 0; i < points.Count; i++)
                    if (!points[i].participantPosition)
                        return false;
                return true;
            }

            if (index <= points.Count)
                return points[index - 1] && !points[index - 1].participantPosition;
            return extraFocusPoints[index - points.Count - 1];
        }

        internal bool HasSelectionParts
        {
            get
            {
                for (int i = 0; i < points.Count; i++)
                    if (!points[i].participantPosition)
                        return true;
                return false;
            }
        }

        internal bool ClosestPoint(Vector3 origin, LayerMask layers, bool triggers, out Vector3 point)
        {
            point = default;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < colliders.Count; i++)
            {
                var collider = colliders[i];
                if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy || (!triggers && collider.isTrigger) || (layers.value & (1 << collider.gameObject.layer)) == 0)
                    continue;
                var candidate = collider.ClosestPoint(origin);
                float squared = (candidate - origin).sqrMagnitude;
                if (squared < distance)
                {
                    distance = squared;
                    point = candidate;
                }
            }

            return distance < float.PositiveInfinity;
        }

        public InteractionPoint GetPoint(Collider collider)
        {
            var at = collider ? collider.transform : null;
            while (at && at != transform)
            {
                if (at.TryGetComponent<InteractionPoint>(out var point))
                    return point;
                at = at.parent;
            }

            return null;
        }

        public InteractionAction FindAction(string id)
        {
            InteractionAction result = null;
            for (int i = 0; i < actions.Count; i++)
                if (actions[i] && actions[i].ActionId == id)
                {
                    if (result)
                        return null;
                    result = actions[i];
                }

            return result;
        }

        private void Reset()
        {
            if (string.IsNullOrEmpty(persistentId))
                persistentId = Guid.NewGuid().ToString("N");
        }

        private void OnEnable()
        {
            Refresh();
            InteractionWorld.Register(this);
        }

        private void OnDisable()
        {
            InteractionWorld.Unregister(this);
            Unregister();
            for (int i = 0; i < actions.Count; i++)
                if (actions[i])
                    actions[i].CancelOwner("Target disabled.");
            for (int i = queue.Count - 1; i >= 0; i--)
                if (queue[i].Actor)
                    queue[i].Actor.ClearQueue("Target disabled.");
            queue.Clear();
        }

        private void OnTransformChildrenChanged()
        {
            if (isActiveAndEnabled)
                Refresh();
        }

        public void Refresh()
        {
            cachedObjectName = gameObject.name;
            Unregister();
            GetComponents(actions);
            GetComponents(conditions);
            GetComponents(costs);
            GetComponentsInChildren(false, colliders);
            GetComponentsInChildren(false, points);
            for (int i = points.Count - 1; i >= 0; i--)
                if (!points[i].isActiveAndEnabled || points[i].GetComponentInParent<InteractionTarget>() != this)
                    points.RemoveAt(i);
            if (!isActiveAndEnabled)
                return;
            for (int i = colliders.Count - 1; i >= 0; i--)
            {
                if (colliders[i].GetComponentInParent<InteractionTarget>() != this)
                {
                    colliders.RemoveAt(i);
                    continue;
                }

                owners[colliders[i]] = this;
            }
        }

        private void Unregister()
        {
            for (int i = 0; i < colliders.Count; i++)
                if (!ReferenceEquals(colliders[i], null) && owners.TryGetValue(colliders[i], out var owner) && owner == this)
                    owners.Remove(colliders[i]);
        }

        internal static InteractionTarget Resolve(Collider collider) => collider && owners.TryGetValue(collider, out var target) && target && target.isActiveAndEnabled ? target : null;
        internal InteractionAvailability CheckClaim(InteractionActor actor, InteractionAction action)
        {
            if (queue.Count > 0 && queue[0].Actor != actor)
                return Busy();
            if (action.OwnerCount >= action.MaxParticipants)
                return Busy();
            for (int i = 0; i < claims.Count; i++)
            {
                var existing = claims[i].Action;
                if (existing == action)
                    continue;
                if (action.UseScope == InteractionUseScope.Target || existing.UseScope == InteractionUseScope.Target)
                    return Busy();
                if (action.UseScope == InteractionUseScope.Group && existing.UseScope == InteractionUseScope.Group && action.UseGroup == existing.UseGroup)
                    return Busy();
            }

            bool hasPlaces = false;
            for (int i = 0; i < points.Count; i++)
                if (points[i].participantPosition && points[i].Allows(action))
                {
                    hasPlaces = true;
                    if (!PlaceOccupied(points[i]))
                        return InteractionAvailability.Available;
                }

            return hasPlaces ? Busy() : InteractionAvailability.Available;
        }

        private static InteractionAvailability Busy() => InteractionAvailability.Unavailable(InteractionFailure.Busy, "Object is in use.", "interaction.busy");
        private bool PlaceOccupied(InteractionPoint point)
        {
            for (int i = 0; i < claims.Count; i++)
                if (claims[i].Place == point)
                    return true;
            return false;
        }

        internal Transform Acquire(InteractionActor actor, InteractionAction action)
        {
            InteractionPoint place = null;
            float closest = float.PositiveInfinity;
            for (int i = 0; i < points.Count; i++)
                if (points[i].participantPosition && points[i].Allows(action) && !PlaceOccupied(points[i]))
                {
                    float distance = (points[i].transform.position - actor.PlayerRoot.position).sqrMagnitude;
                    if (distance < closest)
                    {
                        place = points[i];
                        closest = distance;
                    }
                }

            claims.Add(new Claim { Actor = actor, Action = action, Place = place });
            action.Acquire(actor);
            return place ? place.transform : action.ApproachPoint;
        }

        internal void Release(InteractionActor actor, InteractionAction action)
        {
            for (int i = claims.Count - 1; i >= 0; i--)
                if (claims[i].Actor == actor && claims[i].Action == action)
                    claims.RemoveAt(i);
        }

        internal void Enqueue(InteractionActor actor, InteractionAction action)
        {
            RemoveQueue(actor);
            queue.Add(new QueueEntry { Actor = actor, Action = action });
        }

        internal void RemoveQueue(InteractionActor actor)
        {
            for (int i = queue.Count - 1; i >= 0; i--)
                if (queue[i].Actor == actor)
                    queue.RemoveAt(i);
        }

        private void Update()
        {
            if (queue.Count == 0)
                return;
            var first = queue[0];
            if (!first.Actor || !first.Action)
            {
                queue.RemoveAt(0);
                return;
            }

            var check = first.Actor.CheckDirect(first.Action);
            if (check.Code == InteractionFailure.Busy)
                return;
            // Keep our place while executing so later callers cannot jump ahead.
            if (check.IsAvailable)
                first.Actor.TryExecuteDirect(first.Action);
            first.Actor.ClearQueue(check.IsAvailable ? "Queue completed." : check.Reason);
        }
    }
}
