using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests.Integrations
{
    public sealed class TestInteractionInputSource : MonoBehaviour, IInteractionInputSource
    {
        private string label = "Custom";
        private Sprite icon;
        private bool toggle;
        private Action listeners;
        public int Subscriptions { get; private set; }
        public int ReadsWhileDisabled { get; private set; }

        private void Read()
        {
            if (!isActiveAndEnabled)
                ReadsWhileDisabled++;
        }

        public string BindingLabel
        {
            get
            {
                Read();
                return label;
            }
        }

        public Sprite BindingIcon
        {
            get
            {
                Read();
                return icon;
            }
        }

        public bool ToggleInsteadOfHold
        {
            get
            {
                Read();
                return toggle;
            }
        }

        public event Action PresentationChanged
        {
            add
            {
                listeners += value;
                Subscriptions++;
            }

            remove
            {
                listeners -= value;
                Subscriptions--;
            }
        }

        public void Present(string text, Sprite picture = null, bool holdToggle = false)
        {
            label = text;
            icon = picture;
            toggle = holdToggle;
            listeners?.Invoke();
        }
    }

    public sealed class PresentationTests
    {
        private readonly List<Object> objects = new List<Object>();
        private InteractionActor actor;
        private InteractionPresenter presenter;
        private EventAction action;
        [SetUp]
        public void SetUp()
        {
            var player = new GameObject("Presentation test actor");
            objects.Add(player);
            actor = player.AddComponent<InteractionActor>();
            actor.AutomaticUpdate = false;
            player.transform.position = new Vector3(0, 120, 0);
            presenter = player.AddComponent<InteractionPresenter>();
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(target);
            target.transform.position = new Vector3(0, 120, 2);
            action = target.AddComponent<EventAction>();
            action.Execution = InteractionExecutionMode.Timed;
            action.Duration = 10;
            Physics.SyncTransforms();
            actor.RefreshTarget();
            presenter.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i])
                    Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        [UnityTest]
        public IEnumerator PresentationChangesRefreshTextAndIconWithoutLegacyInput()
        {
            var source = actor.gameObject.AddComponent<TestInteractionInputSource>();
            presenter.Refresh();
            var icon = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            objects.Add(icon);
            source.Present("K", icon, true);
            yield return null;
            Assert.AreEqual("K", presenter.Prompt.Binding);
            Assert.AreSame(icon, presenter.Prompt.BindingIcon);
            Assert.IsTrue(presenter.ToggleInsteadOfHold);
            source.Present("K", null, true);
            yield return null;
            Assert.IsNull(presenter.Prompt.BindingIcon);
        }

        [Test]
        public void ActiveAdapterWinsAndDisabledSourcesAreNeverRead()
        {
            var legacy = actor.gameObject.AddComponent<InteractionInput>();
            var source = actor.gameObject.AddComponent<TestInteractionInputSource>();
            source.Present("Adapter");
            presenter.Refresh();
            Assert.AreSame(source, presenter.ActiveInputSource);
            Assert.AreEqual("Adapter", presenter.Prompt.Binding);
            source.enabled = false;
            presenter.Refresh();
            Assert.AreSame(legacy, presenter.ActiveInputSource);
            legacy.enabled = false;
            presenter.Refresh();
            Assert.IsNull(presenter.ActiveInputSource);
            Assert.AreEqual("", presenter.Prompt.Binding);
            Assert.AreEqual(0, source.ReadsWhileDisabled);
        }

        [Test]
        public void ExplicitSourceWinsAndUnsubscribesOnReplaceDisableAndDestroy()
        {
            var first = actor.gameObject.AddComponent<TestInteractionInputSource>();
            var second = actor.gameObject.AddComponent<TestInteractionInputSource>();
            second.Present("Explicit");
            presenter.InputSource = second;
            Assert.AreEqual("Explicit", presenter.Prompt.Binding);
            Assert.AreEqual(0, first.Subscriptions);
            second.enabled = false;
            presenter.Refresh();
            Assert.IsNull(presenter.ActiveInputSource);
            Assert.AreEqual(0, second.ReadsWhileDisabled);
            Assert.AreEqual(0, second.Subscriptions);
            presenter.InputSource = first;
            Assert.AreEqual(1, first.Subscriptions);
            presenter.enabled = false;
            Assert.AreEqual(0, first.Subscriptions);
            presenter.enabled = true;
            presenter.Refresh();
            Assert.AreEqual(1, first.Subscriptions);
            Object.DestroyImmediate(first);
            presenter.Refresh();
            Assert.IsNull(presenter.ActiveInputSource);
        }

        [Test]
        public void UiReleaseUsesTheActiveSourcesHoldToggleSetting()
        {
            var legacy = actor.gameObject.AddComponent<InteractionInput>();
            legacy.enabled = false;
            legacy.ToggleInsteadOfHold = false;
            var source = actor.gameObject.AddComponent<TestInteractionInputSource>();
            source.Present("K", holdToggle: true);
            action.InputGesture = InteractionInputGesture.HoldWhileActive;
            Assert.IsTrue(presenter.Activate(action));
            presenter.Release(action);
            Assert.IsTrue(actor.GetTicket(action).IsActive);
            source.Present("K", holdToggle: false);
            presenter.Release(action);
            Assert.IsFalse(actor.GetTicket(action).IsActive);
        }

        [Test]
        public void UiAnalogRoutesToTheOperationWithoutLegacyInput()
        {
            var mechanical = action.gameObject.AddComponent<MechanicalAction>();
            mechanical.ActionId = "mechanical";
            Assert.IsTrue(presenter.Activate(mechanical));
            Assert.AreSame(mechanical, actor.GetActive(InteractionInputGesture.Analog));
            Assert.IsTrue(presenter.SetValue(mechanical, 1));
            actor.Tick(.2f);
            Assert.Greater(mechanical.Value, 0);
        }

        [Test]
        public void WarmPresenterSourceResolutionDoesNotAllocate()
        {
            var source = actor.gameObject.AddComponent<TestInteractionInputSource>();
            bool previous = Profiler.enabled;
            try
            {
                Profiler.enabled = true;
                var options = ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
                long calibration;
                using (var control = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, options))
                {
                    if (!control.Valid || !Profiler.enabled)
                        Assert.Inconclusive("GC.Alloc measurement unavailable: the main-thread recorder could not start.");
                    GC.KeepAlive(new byte[512]);
                    control.Stop();
                    if (!control.Valid || !Profiler.enabled)
                        Assert.Inconclusive("GC.Alloc measurement unavailable: the main-thread recorder is not active.");
                    calibration = control.Count == 0 ? 0 : control.GetSample(0).Count;
                }

                if (calibration <= 0)
                    Assert.Inconclusive("GC.Alloc measurement unavailable: the recorder missed the 512-byte positive control.");
                for (int i = 0; i < 128; i++)
                    presenter.Refresh();
                long allocations;
                using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, options))
                {
                    if (!recorder.Valid)
                        Assert.Inconclusive("GC.Alloc measurement unavailable: the measured recorder could not start.");
                    for (int i = 0; i < 128; i++)
                        presenter.Refresh();
                    recorder.Stop();
                    if (!recorder.Valid || !Profiler.enabled)
                        Assert.Inconclusive("GC.Alloc measurement became unavailable during presenter refresh.");
                    allocations = recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
                }

                Assert.AreSame(source, presenter.ActiveInputSource);
                Assert.AreEqual("Custom", presenter.Prompt.Binding);
                TestContext.WriteLine($"GC.Alloc: control={calibration} events; 128 presenter refreshes={allocations} main-thread events.");
                Assert.That(allocations, Is.Zero, "GC.Alloc events in warmed presenter source resolution.");
            }
            finally
            {
                Profiler.enabled = previous;
            }
        }

        [Test]
        public void LegacyPromptConstructorRemainsAvailable()
        {
            var prompt = new InteractionPrompt(action.Target, action, "Target", "Use", "", "E", true, false, 0);
            Assert.AreEqual("E", prompt.Binding);
            Assert.IsNull(prompt.BindingIcon);
        }
    }
}
