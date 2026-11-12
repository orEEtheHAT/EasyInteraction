using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace EasyInteraction.Verification
{
    /// <summary>Standalone development-player benchmark. Copy this folder to a test project's Assets.</summary>
    public sealed class InteractionBenchmark : MonoBehaviour
    {
        [Serializable]
        public sealed class Report
        {
            public int schemaVersion = 2;
            public string unity, platform, cpu, gpu, os, backend, dateUtc;
            public bool development, passed;
            public int cpuCount, systemMemoryMB;
            public string allocationMeter = "Main-thread GC.Alloc ProfilerRecorder (event count)";
            public bool allocationMeasurementAvailable;
            public long allocationCalibrationEvents, allocationEmptyControlEvents;
            public string allocationMeasurementUnavailableReason;
            public List<SelectionRow> selection = new List<SelectionRow>();
            public List<HoldRow> hold = new List<HoldRow>();
            public List<ApiRow> api = new List<ApiRow>();
        }

        [Serializable]
        public sealed class ApiRow
        {
            public string operation;
            public int calls, failures;
            public double meanUs, p95Us;
            public bool allocationMeasurementAvailable;
            public long allocationEvents = -1, allocatedBytes = -1;
        }

        [Serializable]
        public sealed class SelectionRow
        {
            public string mode;
            public int objects, samples, candidates, capacity, targetChanges, mismatches;
            public double meanMs, p95Ms, maximumMs, referenceMeanMs, physicsQueries, visibilityChecks;
            public bool allocationMeasurementAvailable, referenceAllocationMeasurementAvailable;
            public long allocationEvents = -1, referenceAllocationEvents = -1;
            public long allocatedBytes = -1, referenceAllocatedBytes = -1;
            public long managedBytes, unityAllocatedBytes, unityReservedBytes;
        }

        [Serializable]
        public sealed class HoldRow
        {
            public int targetFPS, frames, fixedSteps;
            public float fixedDelta, measuredFPS, finalError, maximumError, maximumSpeed, maximumWallZ, movingMaximumError;
            public bool movingStable;
            public double holdMeanUs;
            public long holdAllocatedBytes;
            public bool acquired, held, restored, wallBlocked, finite;
        }

        private InteractionActor actor;
        private GameObject population;
        private readonly Dictionary<Collider, InteractionTarget> lookup = new Dictionary<Collider, InteractionTarget>();
        private readonly HashSet<InteractionTarget> seen = new HashSet<InteractionTarget>();
        private Collider[] referenceOverlap;
        private RaycastHit[] referenceHits;
        private int targetChanges, fixedSteps;
        private Vector3 switchLeft, switchRight;
        private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        // These scopes count events, not bytes. No scope crosses a yield/frame boundary.
        private sealed class AllocationMeter : IDisposable
        {
            private const ProfilerRecorderOptions Options = ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
            private readonly bool previousProfilerEnabled = Profiler.enabled;
            public bool Available { get; private set; }
            public long CalibrationEvents { get; private set; } = -1;
            public long EmptyControlEvents { get; private set; } = -1;
            public string UnavailableReason { get; private set; } = "";

            public AllocationMeter()
            {
                Profiler.enabled = true;
                using (var control = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, Options))
                {
                    if (!control.Valid || !Profiler.enabled)
                    {
                        Unavailable("The main-thread GC.Alloc recorder is not active on this platform/build.");
                        return;
                    }
                    GC.KeepAlive(new byte[512]);
                    control.Stop();
                    CalibrationEvents = control.Count == 0 ? 0 : control.GetSample(0).Count;
                }
                if (CalibrationEvents <= 0)
                {
                    Unavailable("GC.Alloc missed the 512-byte positive control.");
                    return;
                }
                Available = true;
                // Warm the wrapper/JIT before checking its empty measurement overhead.
                using (var warm = Begin())
                    End(warm);
                // Also reject a noisy measurement scope before interpreting zero as evidence.
                using (var empty = Begin())
                    EmptyControlEvents = End(empty);
                if (EmptyControlEvents != 0)
                    Unavailable("The warmed empty GC.Alloc measurement scope did not report zero events.");
            }

            public ProfilerRecorder Begin() => Available
                ? ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, Options)
                : default;

            public long End(ProfilerRecorder recorder)
            {
                if (!Available)
                    return -1;
                if (!recorder.Valid || !Profiler.enabled)
                {
                    Unavailable("GC.Alloc became unavailable during the benchmark.");
                    return -1;
                }
                recorder.Stop();
                return recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
            }

            private void Unavailable(string reason)
            {
                Available = false;
                UnavailableReason = reason;
            }

            public void Dispose() => Profiler.enabled = previousProfilerEnabled;
        }

        private static long AddEvents(long total, long sample) => total < 0 || sample < 0 ? -1 : total + sample;
        // GC.Alloc sample.Value is not an allocation size. Only zero events prove zero bytes.
        private static long KnownBytes(long events) => events == 0 ? 0 : -1;

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            var report = new Report
            {
                unity = Application.unityVersion,
                platform = Application.platform.ToString(),
                cpu = SystemInfo.processorType,
                gpu = SystemInfo.graphicsDeviceName,
                os = SystemInfo.operatingSystem,
                cpuCount = SystemInfo.processorCount,
                systemMemoryMB = SystemInfo.systemMemorySize,
                development = Debug.isDebugBuild,
                backend = "Mono",
                dateUtc = DateTime.UtcNow.ToString("o")
            };
            using var allocationMeter = new AllocationMeter();
            actor = new GameObject("Benchmark actor").AddComponent<InteractionActor>();
            actor.Range = 15;
            actor.FocusAngle = 100;
            actor.SwitchThreshold = .08f;
            actor.TargetChanged += _ => targetChanges++;
            // Small/large loads, steady and frequently changing targets, plus deliberately worst-case occlusion.
            foreach (int count in new[]
            {
                16,
                128,
                1024,
                4096
            }

            )
            {
                CreatePopulation(count);
                yield return null;
                foreach (string mode in new[]
                {
                    "Focus steady",
                    "Focus switching",
                    "Focus occluded",
                    "Raycast switching"
                }

                )
                {
                    actor.SelectionMode = mode.StartsWith("Raycast") ? InteractionSelectionMode.Raycast : InteractionSelectionMode.Focus;
                    var wall = new GameObject("Occluder");
                    var wallCollider = wall.AddComponent<BoxCollider>();
                    wall.transform.position = new Vector3(0, 0, 4);
                    wall.transform.localScale = new Vector3(20, 20, .2f);
                    wall.SetActive(mode.Contains("occluded"));
                    Physics.SyncTransforms();
                    for (int i = 0; i < 64; i++)
                        actor.RefreshTarget();
                    ReferenceSelect(actor.CurrentTarget); // JIT and container warmup outside timed region
                    const int samples = 160;
                    var times = new double[samples];
                    var row = new SelectionRow
                    {
                        mode = mode,
                        objects = count,
                        samples = samples,
                        allocationEvents = allocationMeter.Available ? 0 : -1,
                        referenceAllocationEvents = allocationMeter.Available && actor.SelectionMode == InteractionSelectionMode.Focus ? 0 : -1
                    };
                    targetChanges = 0;
                    for (int i = 0; i < samples; i++)
                    {
                        if (mode.Contains("switching"))
                            actor.transform.rotation = Quaternion.LookRotation((i % 2 == 0 ? switchLeft : switchRight) - actor.View.position);
                        else
                            actor.transform.rotation = Quaternion.identity;
                        var previous = actor.CurrentTarget;
                        long started, stopped;
                        InteractionTarget selected;
                        using (var recorder = allocationMeter.Begin())
                        {
                            started = Stopwatch.GetTimestamp();
                            selected = actor.RefreshTarget();
                            stopped = Stopwatch.GetTimestamp();
                            row.allocationEvents = AddEvents(row.allocationEvents, allocationMeter.End(recorder));
                        }
                        times[i] = Milliseconds(stopped - started);
                        row.meanMs += times[i];
                        row.maximumMs = Math.Max(row.maximumMs, times[i]);
                        row.candidates = Math.Max(row.candidates, actor.LastCandidateCount);
                        row.physicsQueries += actor.LastPhysicsQueries;
                        row.visibilityChecks += actor.LastVisibilityChecks;
                        if (actor.SelectionMode == InteractionSelectionMode.Focus)
                        {
                            InteractionTarget reference;
                            using (var recorder = allocationMeter.Begin())
                            {
                                started = Stopwatch.GetTimestamp();
                                reference = ReferenceSelect(previous);
                                stopped = Stopwatch.GetTimestamp();
                                row.referenceAllocationEvents = AddEvents(row.referenceAllocationEvents, allocationMeter.End(recorder));
                            }
                            row.referenceMeanMs += Milliseconds(stopped - started);
                            if (reference != selected)
                                row.mismatches++;
                        }

                        if (i % 8 == 0)
                            yield return null;
                    }

                    Array.Sort(times);
                    row.p95Ms = times[(int)(samples * .95f)];
                    row.meanMs /= samples;
                    row.referenceMeanMs /= samples;
                    row.physicsQueries /= samples;
                    row.visibilityChecks /= samples;
                    row.targetChanges = targetChanges;
                    row.capacity = actor.OverlapCapacity;
                    row.managedBytes = Profiler.GetMonoUsedSizeLong();
                    row.unityAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong();
                    row.unityReservedBytes = Profiler.GetTotalReservedMemoryLong();
                    row.allocationMeasurementAvailable = row.allocationEvents >= 0;
                    row.referenceAllocationMeasurementAvailable = row.referenceAllocationEvents >= 0;
                    row.allocatedBytes = KnownBytes(row.allocationEvents);
                    row.referenceAllocatedBytes = KnownBytes(row.referenceAllocationEvents);
                    report.selection.Add(row);
                    Debug.Log("EASYINTERACTION_SELECTION " + JsonUtility.ToJson(row));
                    Object.Destroy(wall);
                    yield return null;
                }

                Object.Destroy(population);
                lookup.Clear();
                yield return null;
            }

            actor.SelectionMode = InteractionSelectionMode.Raycast;
            actor.transform.rotation = Quaternion.identity;
            yield return MeasureApi(report, allocationMeter);
            foreach (int fps in new[]
            {
                30,
                60,
                144
            }

            )
                foreach (float step in new[]
                {
                    1f / 30f,
                    .02f,
                    .01f
                }

                )
                    yield return MeasureHold(report, fps, step);
            report.allocationMeasurementAvailable = allocationMeter.Available;
            report.allocationCalibrationEvents = allocationMeter.CalibrationEvents;
            report.allocationEmptyControlEvents = allocationMeter.EmptyControlEvents;
            report.allocationMeasurementUnavailableReason = allocationMeter.UnavailableReason;
            report.passed = report.allocationMeasurementAvailable;
            foreach (var row in report.selection)
                report.passed &= row.mismatches == 0 && row.allocationMeasurementAvailable && row.allocationEvents == 0 && (!row.mode.Contains("switching") || row.targetChanges >= row.samples - 1);
            foreach (var row in report.api)
                report.passed &= row.failures == 0 && row.allocationMeasurementAvailable && row.allocationEvents == 0;
            foreach (var row in report.hold)
                report.passed &= row.acquired && row.held && row.restored && row.wallBlocked && row.finite && row.movingStable;
            string output = Path.Combine(Application.persistentDataPath, "EasyInteraction-benchmark-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json");
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--report")
                    output = args[i + 1];
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            // Preserve previous raw reports, including an explicitly supplied --report path.
            using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream))
                writer.Write(JsonUtility.ToJson(report, true));
            Debug.Log("EASYINTERACTION_REPORT " + output);
            if (!Application.isEditor)
                Application.Quit(report.passed ? 0 : 1);
        }

        private void CreatePopulation(int count)
        {
            population = new GameObject("Population " + count);
            referenceOverlap = new Collider[Mathf.NextPowerOfTwo(count + 2)];
            referenceHits = new RaycastHit[Mathf.NextPowerOfTwo(count + 2)];
            int side = Mathf.CeilToInt(Mathf.Sqrt(count));
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Target");
                go.transform.SetParent(population.transform);
                float x = ((i % side) / (float)Mathf.Max(1, side - 1) - .5f) * 8;
                float y = ((i / side) / (float)Mathf.Max(1, side - 1) - .5f) * 6;
                go.transform.position = new Vector3(x, y, 8);
                if (i == side * (side / 2))
                    switchLeft = go.transform.position;
                if (i == side * (side / 2) + side - 1)
                    switchRight = go.transform.position;
                var collider = go.AddComponent<BoxCollider>();
                collider.size = Vector3.one * .025f;
                var action = go.AddComponent<EventAction>();
                lookup.Add(collider, action.Target);
            }

            Physics.SyncTransforms();
        }

        // Exhaustive reference: same scores/occlusion/tie break, checks visibility of EVERY candidate before ranking.
        // No hard truncation or per-call allocations. This is a verification baseline, not a second shipping API.
        private InteractionTarget ReferenceSelect(InteractionTarget previous)
        {
            int count = Physics.OverlapSphereNonAlloc(actor.View.position, actor.Range, referenceOverlap, ~0, QueryTriggerInteraction.Ignore);
            seen.Clear();
            InteractionTarget best = null;
            float bestScore = float.NegativeInfinity;
            int bestId = int.MaxValue;
            float minimumDot = Mathf.Cos(actor.FocusAngle * .5f * Mathf.Deg2Rad);
            for (int i = 0; i < count; i++)
            {
                if (!lookup.TryGetValue(referenceOverlap[i], out var target) || !seen.Add(target))
                    continue;
                Vector3 offset = target.AimPosition - actor.View.position;
                float distance = offset.magnitude;
                if (distance > actor.Range || distance < .0001f)
                    continue;
                float dot = Vector3.Dot(offset / distance, actor.View.forward);
                if (dot < minimumDot)
                    continue;
                int n = Physics.RaycastNonAlloc(actor.View.position, offset / distance, referenceHits, distance + .001f, ~0, QueryTriggerInteraction.Ignore);
                Collider nearest = null;
                float closest = float.PositiveInfinity;
                for (int h = 0; h < n; h++)
                    if (referenceHits[h].distance < closest)
                    {
                        nearest = referenceHits[h].collider;
                        closest = referenceHits[h].distance;
                    }

                if (nearest && (!lookup.TryGetValue(nearest, out var owner) || owner != target))
                    continue;
                float score = actor.DirectionWeight * ((dot - minimumDot) / (1 - minimumDot)) + (1 - actor.DirectionWeight) * (1 - distance / actor.Range);
                if (target == previous)
                    score += actor.SwitchThreshold;
                int id = target.GetInstanceID();
                if (score > bestScore || (score == bestScore && id < bestId))
                {
                    best = target;
                    bestScore = score;
                    bestId = id;
                }
            }

            return best;
        }

        private void FixedUpdate() => fixedSteps++;
        private IEnumerator MeasureApi(Report report, AllocationMeter allocationMeter)
        {
            var go = new GameObject("API target");
            go.transform.position = new Vector3(0, 0, 2);
            go.AddComponent<BoxCollider>();
            var action = go.AddComponent<EventAction>();
            action.ActionId = "use";
            action.Target.PersistentId = "benchmark.api";
            Physics.SyncTransforms();
            actor.AutomaticUpdate = false;
            actor.SelectionMode = InteractionSelectionMode.Raycast;
            var authority = new InteractionAuthority
            {
                MaxRequestsPerSecond = int.MaxValue
            };
            ulong sequence = 0;
            foreach (string operation in new[]
            {
                "Instant action",
                "Timed tick",
                "Authoritative request"
            }

            )
            {
                action.Execution = operation == "Timed tick" ? InteractionExecutionMode.Timed : InteractionExecutionMode.Automatic;
                action.Duration = 1000;
                if (operation == "Timed tick")
                    actor.TryExecuteDirect(action);
                for (int i = 0; i < 128; i++)
                    CallApi(operation, action, authority, ref sequence);
                var row = new ApiRow
                {
                    operation = operation,
                    calls = 512,
                    allocationEvents = allocationMeter.Available ? 0 : -1
                };
                var times = new double[row.calls];
                for (int i = 0; i < row.calls; i++)
                {
                    long start, stop;
                    bool success;
                    using (var recorder = allocationMeter.Begin())
                    {
                        start = Stopwatch.GetTimestamp();
                        success = CallApi(operation, action, authority, ref sequence);
                        stop = Stopwatch.GetTimestamp();
                        row.allocationEvents = AddEvents(row.allocationEvents, allocationMeter.End(recorder));
                    }
                    if (!success)
                        row.failures++;
                    times[i] = Milliseconds(stop - start) * 1000;
                    row.meanUs += times[i];
                    if (i % 64 == 0)
                        yield return null;
                }

                Array.Sort(times);
                row.p95Us = times[(int)(row.calls * .95f)];
                row.meanUs /= row.calls;
                row.allocationMeasurementAvailable = row.allocationEvents >= 0;
                row.allocatedBytes = KnownBytes(row.allocationEvents);
                report.api.Add(row);
                actor.Cancel();
                Debug.Log("EASYINTERACTION_API " + JsonUtility.ToJson(row));
            }

            authority.Dispose();
            actor.AutomaticUpdate = true;
            Object.Destroy(go);
            yield return null;
        }

        private bool CallApi(string operation, EventAction action, InteractionAuthority authority, ref ulong sequence)
        {
            if (operation == "Timed tick")
            {
                actor.Tick(.001f);
                return actor.GetTicket(action).IsActive;
            }

            if (operation == "Authoritative request")
                return authority.Process(actor, new InteractionRequest(++sequence, "benchmark.api", "use")).Accepted;
            return actor.TryExecuteDirect(action);
        }

        private IEnumerator MeasureHold(Report report, int fps, float step)
        {
            Application.targetFrameRate = fps;
            Time.fixedDeltaTime = step;
            var go = new GameObject("Physical crate");
            go.transform.position = new Vector3(0, 0, 2.6f);
            go.AddComponent<BoxCollider>().size = Vector3.one * .5f;
            var body = go.AddComponent<Rigidbody>();
            body.mass = 2;
            var grab = go.AddComponent<GrabAction>();
            Physics.SyncTransforms();
            var row = new HoldRow
            {
                targetFPS = fps,
                fixedDelta = step,
                acquired = actor.TryExecute(grab),
                finite = true
            };
            fixedSteps = 0;
            float start = Time.realtimeSinceStartup;
            using (var recorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Scripts, "EasyInteraction.HoldPhysics", 1))
            using (var gcRecorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame", 1))
            {
                // Settle for 0.5 seconds before scoring error; report total process GC separately from selection GC.
                double recorderSum = 0;
                int recorderFrames = 0;
                while (Time.realtimeSinceStartup - start < 1.5f)
                {
                    yield return null;
                    row.frames++;
                    float error = Vector3.Distance(grab.GripPosition, actor.View.position + actor.View.forward * grab.HoldDistance);
                    if (Time.realtimeSinceStartup - start > .5f)
                        row.maximumError = Mathf.Max(row.maximumError, error);
                    row.maximumSpeed = Mathf.Max(row.maximumSpeed, body.linearVelocity.magnitude);
                    row.finite &= !float.IsNaN(error) && !float.IsInfinity(error);
                    if (recorder.Valid)
                    {
                        recorderSum += recorder.LastValue;
                        recorderFrames++;
                    }

                    if (gcRecorder.Valid)
                        row.holdAllocatedBytes += gcRecorder.LastValue;
                }

                row.holdMeanUs = recorderFrames > 0 ? recorderSum / recorderFrames / 1000.0 : -1;
            }

            row.measuredFPS = row.frames / (Time.realtimeSinceStartup - start);
            row.fixedSteps = fixedSteps;
            row.finalError = Vector3.Distance(grab.GripPosition, actor.View.position + actor.View.forward * grab.HoldDistance);
            row.held = grab.IsHeld;
            start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 1.5f)
            {
                float elapsed = Time.realtimeSinceStartup - start;
                actor.transform.position = new Vector3(Mathf.Sin(elapsed * 2) * .45f, 0, 0);
                actor.transform.rotation = Quaternion.Euler(0, Mathf.Sin(elapsed * 1.5f) * 20, 0);
                yield return null;
                float error = Vector3.Distance(grab.GripPosition, actor.View.position + actor.View.forward * grab.HoldDistance);
                row.movingMaximumError = Mathf.Max(row.movingMaximumError, error);
                row.finite &= !float.IsNaN(error) && !float.IsInfinity(error);
            }

            row.movingStable = grab.IsHeld && row.movingMaximumError < .5f;
            actor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < .5f)
                yield return null;
            var wall = new GameObject("Hold wall");
            wall.AddComponent<BoxCollider>();
            wall.transform.position = new Vector3(0, 0, 2.8f);
            wall.transform.localScale = new Vector3(6, 6, .2f);
            Physics.SyncTransforms();
            grab.HoldDistance = 4;
            start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < .8f)
            {
                yield return null;
                row.maximumWallZ = Mathf.Max(row.maximumWallZ, body.position.z);
            }

            row.wallBlocked = row.maximumWallZ < 2.6f && grab.IsHeld;
            actor.enabled = false;
            row.restored = !grab.IsHeld && body.useGravity && body.interpolation == RigidbodyInterpolation.None && body.collisionDetectionMode == CollisionDetectionMode.Discrete;
            actor.enabled = true;
            report.hold.Add(row);
            Debug.Log("EASYINTERACTION_HOLD " + JsonUtility.ToJson(row));
            Object.Destroy(go);
            Object.Destroy(wall);
            yield return null;
        }
    }
}
