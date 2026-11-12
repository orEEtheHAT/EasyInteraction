using System.Collections;
using EasyInteraction.Samples.Gallery;
using EasyInteraction.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EasyInteraction.Tests.Gallery
{
    public sealed class GalleryFirstRunTests
    {
        [UnityTest]
        public IEnumerator ImportedGallerySupportsKeyboardMenuAndAnimationWithStandaloneInput()
        {
            string path=null;
            for(int i=0;i<SceneManager.sceneCountInBuildSettings;i++)
            {
                var candidate=SceneUtility.GetScenePathByBuildIndex(i);
                if(candidate.EndsWith("/InteractionGallery.unity",System.StringComparison.Ordinal)){path=candidate;break;}
            }
            Assert.IsNotNull(path,"Add the imported InteractionGallery.unity to the validation project's build scenes.");
            yield return SceneManager.LoadSceneAsync(path,LoadSceneMode.Single);
            yield return null;
            var gallery=Object.FindFirstObjectByType<InteractionGalleryControls>();Assert.IsNotNull(gallery);
            var actor=gallery.actor;var presenter=actor.GetComponent<InteractionPresenter>();
            var oldUpdate=InputSystem.settings.updateMode;var oldBackground=InputSystem.settings.backgroundBehavior;
#if UNITY_EDITOR
            var oldEditor=InputSystem.settings.editorInputBehaviorInPlayMode;
#endif
            Keyboard keyboard=null;
            try
            {
                InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsManually;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
                InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
                keyboard=InputSystem.AddDevice<Keyboard>();
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();
                Assert.IsTrue(actor.SelectTarget(gallery.item.Target));Assert.IsTrue(actor.Check(gallery.item).IsAvailable);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.E));InputSystem.Update();Assert.IsTrue(gallery.item.IsHeld,"E must grab the available scene item.");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Q));InputSystem.Update();Assert.IsFalse(gallery.item.IsHeld);Assert.IsTrue(gallery.item.Body.useGravity);
                presenter.Refresh();Assert.IsInstanceOf<InteractionInput>(presenter.ActiveInputSource);Assert.IsNotEmpty(presenter.Prompt.Binding);
                gallery.hud.SetMenuOpen(true);Assert.AreEqual(3,gallery.hud.ActionCount);gallery.hud.Layout=InteractionMenuLayout.Radial;Assert.AreEqual(3,gallery.hud.VisibleActionCount);gallery.hud.SetMenuOpen(false);
                Assert.IsTrue(actor.TryExecuteDirect(gallery.door));Assert.IsTrue(gallery.door.IsOpen);
                gallery.GoToStation(1);var animation=(EventAction)InteractionWorld.Find("gallery.panel").FindAction("animate");int effects=0;animation.OnExecuteEvent.AddListener(()=>effects++);
                Assert.IsTrue(actor.TryExecuteDirect(animation));yield return new WaitForSecondsRealtime(1);
                Assert.AreEqual(1,effects);Assert.AreEqual(0,actor.ActiveCount);
            }
            finally
            {
                if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.updateMode=oldUpdate;InputSystem.settings.backgroundBehavior=oldBackground;
#if UNITY_EDITOR
                InputSystem.settings.editorInputBehaviorInPlayMode=oldEditor;
#endif
            }
        }
    }
}
