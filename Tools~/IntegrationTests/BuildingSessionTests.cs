using System.Collections.Generic;
using EasyBuilding;
using EasyInteraction.Integrations.Building;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests.Integrations
{
    public sealed class BuildingSessionTests
    {
        private readonly List<Object> objects = new List<Object>();
        private GameObject New(string name)
        {
            var go = new GameObject(name);
            objects.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i])
                    Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        [Test]
        public void InteractionAndPlacerBorrowTheSameActorSessionAcrossReconnects()
        {
            var player = New("Shared building player");
            var context = player.AddComponent<BuildingInteractionContext>();
            var world = New("Building world");
            world.SetActive(false);
            var system = world.AddComponent<BuildingSystem>();
            var placer = world.AddComponent<BuildingPlacer>();
            placer.Actor = context.Actor;
            placer.ShowHelp = false;
            placer.ExternalTargeting = true;
            world.SetActive(true);
            var definition = ScriptableObject.CreateInstance<BuildingDefinition>();
            objects.Add(definition);
            var session = context.GetSession(system);
            Assert.AreSame(session, placer.Session);
            Assert.IsTrue(session.Select(definition));
            Assert.AreSame(definition, placer.Session.Selected);
            context.enabled = false;
            Assert.IsFalse(session.IsDisposed);
            Assert.AreSame(definition, placer.Session.Selected);
            context.enabled = true;
            placer.enabled = false;
            Assert.IsFalse(session.IsDisposed);
            placer.enabled = true;
            Assert.AreSame(session, placer.Session);
            Object.DestroyImmediate(context);
            Assert.IsFalse(session.IsDisposed);
            context = player.AddComponent<BuildingInteractionContext>();
            Assert.AreSame(session, context.GetSession(system));
            using var independent = system.CreateSession(context.Actor);
            Assert.AreNotSame(session, independent);
            Assert.IsNull(independent.Selected);
        }

        [Test]
        public void InteractionSelectionImmediatelyReachesTheStockPlacer()
        {
            var player = New("Builder");
            player.transform.position = new Vector3(0, 140, 0);
            var actor = player.AddComponent<InteractionActor>();
            actor.AutomaticUpdate = false;
            var context = player.AddComponent<BuildingInteractionContext>();
            var world = New("World");
            world.SetActive(false);
            var system = world.AddComponent<BuildingSystem>();
            var placer = world.AddComponent<BuildingPlacer>();
            placer.Actor = context.Actor;
            placer.ShowHelp = false;
            placer.ExternalTargeting = true;
            world.SetActive(true);
            var definition = ScriptableObject.CreateInstance<BuildingDefinition>();
            objects.Add(definition);
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(target);
            target.transform.position = player.transform.position + Vector3.forward * 2;
            var action = target.AddComponent<EasyInteraction.Integrations.Building.BuildingAction>();
            action.operation = BuildingInteraction.SelectForPlacement;
            action.buildingSystem = system;
            action.definition = definition;
            Physics.SyncTransforms();
            Assert.IsTrue(actor.TryExecuteDirect(action));
            Assert.AreSame(definition, placer.Session.Selected);
            Assert.AreSame(context.GetSession(system), placer.Session);
        }
    }
}
