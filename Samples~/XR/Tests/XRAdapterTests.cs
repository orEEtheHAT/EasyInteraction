using System.Collections;
using System.Collections.Generic;
using EasyInteraction.Integrations.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace EasyInteraction.Tests.XR
{
    public sealed class XRAdapterTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private GameObject New(string name)
        {
            var go = new GameObject(name);
            objects.Add(go);
            return go;
        }

        private XRRayInteractor Hand(XRInteractionManager manager, Vector3 position)
        {
            var go = New("XR hand");
            go.transform.position = position;
            var interactor = go.AddComponent<XRRayInteractor>();
            interactor.interactionManager = manager;
            go.AddComponent<XRInteractionActor>();
            return interactor;
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
        public IEnumerator TwoXrInteractorsShareCoreGrabAndRestoreAfterRelease()
        {
            var manager = New("XR manager").AddComponent<XRInteractionManager>();
            var left = Hand(manager, new Vector3(-.2f, 60, 0));
            var right = Hand(manager, new Vector3(.2f, 60, 0));
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(item);
            item.transform.position = new Vector3(0, 60, 1);
            item.transform.localScale = Vector3.one * .3f;
            var grab = item.AddComponent<GrabAction>();
            grab.MaxParticipants = 2;
            var bridge = item.AddComponent<XRInteractionAction>();
            bridge.Action = grab;
            var interactable = item.GetComponent<XRSimpleInteractable>();
            interactable.interactionManager = manager;
            interactable.selectMode = InteractableSelectMode.Multiple;
            Physics.SyncTransforms();
            yield return null;
            manager.SelectEnter((IXRSelectInteractor)left, interactable);
            manager.SelectEnter((IXRSelectInteractor)right, interactable);
            Assert.AreEqual(2, grab.OwnerCount);
            Assert.IsFalse(grab.Body.useGravity);
            manager.SelectExit((IXRSelectInteractor)left, interactable);
            Assert.AreEqual(1, grab.OwnerCount);
            Assert.IsFalse(grab.Body.useGravity);
            manager.SelectExit((IXRSelectInteractor)right, interactable);
            Assert.AreEqual(0, grab.OwnerCount);
            Assert.IsTrue(grab.Body.useGravity);
        }

        [UnityTest]
        public IEnumerator CoreCancellationAlsoEndsXrSelection()
        {
            var manager = New("XR manager").AddComponent<XRInteractionManager>();
            var hand = Hand(manager, new Vector3(0, 60, 0));
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(item);
            item.transform.position = new Vector3(0, 60, 1);
            var grab = item.AddComponent<GrabAction>();
            item.AddComponent<XRInteractionAction>().Action = grab;
            var interactable = item.GetComponent<XRSimpleInteractable>();
            interactable.interactionManager = manager;
            Physics.SyncTransforms();
            yield return null;
            manager.SelectEnter((IXRSelectInteractor)hand, interactable);
            hand.GetComponent<InteractionActor>().Cancel();
            Assert.IsFalse(interactable.isSelected);
            Assert.IsTrue(grab.Body.useGravity);
        }
    }
}
