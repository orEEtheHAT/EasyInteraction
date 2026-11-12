using System.Collections.Generic;
using EasyCombat;
using EasyInteraction.Integrations.Combat;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests.Integrations
{
    public sealed class CombatRestoreTests
    {
        private readonly List<Object> objects = new List<Object>();
        private InteractionActor actor;
        private Health health;
        private CombatDefinitionCatalog catalog;
        private CombatReferenceRegistry registry;
        [SetUp]
        public void SetUp()
        {
            var root = new GameObject("Guard restore actor");
            objects.Add(root);
            root.transform.position = new Vector3(0, 125, 0);
            actor = root.AddComponent<InteractionActor>();
            actor.AutomaticUpdate = false;
            health = root.AddComponent<Health>();
            root.AddComponent<CombatInteractionGuard>();
            catalog = ScriptableObject.CreateInstance<CombatDefinitionCatalog>();
            objects.Add(catalog);
            registry = new CombatReferenceRegistry();
            registry.RegisterActor("guard", health);
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
        public void WorldRestoreRevivesActorAfterRepeatedDeadRestore()
        {
            Assert.IsTrue(CombatSaveSystem.TryCapture(catalog, registry, out var living, out var error), error);
            health.ApplyDamage(new DamageInfo(health.Maximum + 1));
            Assert.IsFalse(actor.enabled);
            Assert.IsTrue(CombatSaveSystem.TryCapture(catalog, registry, out var dead, out error), error);
            Assert.IsTrue(CombatSaveSystem.TryRestore(dead, catalog, registry, out _, out error), error);
            Assert.IsFalse(actor.enabled);
            Assert.IsTrue(CombatSaveSystem.TryRestore(living, catalog, registry, out _, out error), error);
            Assert.IsTrue(health.IsAlive);
            Assert.IsTrue(actor.enabled);
        }

        [Test]
        public void RestoreAndRevivePreserveAnActorDisabledByAnotherSystem()
        {
            actor.enabled = false;
            Assert.IsTrue(CombatSaveSystem.TryCapture(catalog, registry, out var living, out var error), error);
            health.ApplyDamage(new DamageInfo(health.Maximum + 1));
            Assert.IsFalse(actor.enabled);
            Assert.IsTrue(CombatSaveSystem.TryRestore(living, catalog, registry, out _, out error), error);
            Assert.IsFalse(actor.enabled);
            health.ApplyDamage(new DamageInfo(health.Maximum + 1));
            health.ResetToFull();
            Assert.IsFalse(actor.enabled);
            actor.GetComponent<CombatInteractionGuard>().enabled = false;
            Assert.IsFalse(actor.enabled);
        }

        [Test]
        public void RestoringLivingCombatStateCancelsAnActiveInteraction()
        {
            Assert.IsTrue(CombatSaveSystem.TryCapture(catalog, registry, out var snapshot, out var error), error);
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(target);
            target.transform.position = actor.transform.position + Vector3.forward * 2;
            var action = target.AddComponent<EventAction>();
            action.Execution = InteractionExecutionMode.Timed;
            action.Duration = 10;
            Physics.SyncTransforms();
            Assert.IsTrue(actor.TryBegin(action, out var ticket));
            int cancelled = 0;
            action.OnCancelled.AddListener(() => cancelled++);
            Assert.IsTrue(CombatSaveSystem.TryRestore(snapshot, catalog, registry, out _, out error), error);
            Assert.IsFalse(ticket.IsActive);
            Assert.AreEqual(1, cancelled);
            Assert.IsTrue(actor.enabled);
        }
    }
}
