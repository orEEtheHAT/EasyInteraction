using System.Collections;
using System.Collections.Generic;
using EasyResources;
using EasyInteraction.Integrations.Resources;
using EasyInteraction.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasyInteraction.Tests.Integrations
{
    public sealed class IntegrationTests
    {
        private readonly List<Object> objects = new List<Object>();
        private InteractionActor actor;
        private ResourceContainer inventory;
        private ResourceDefinition resource;
        private GameObject New(string name)
        {
            var go = new GameObject(name);
            objects.Add(go);
            return go;
        }

        private EventAction Action()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(go);
            go.transform.position = new Vector3(0, 90, 2);
            var action = go.AddComponent<EventAction>();
            action.Execution = InteractionExecutionMode.Timed;
            action.Duration = 4;
            Physics.SyncTransforms();
            return action;
        }

        [SetUp]
        public void SetUp()
        {
            actor = New("Player").AddComponent<InteractionActor>();
            actor.AutomaticUpdate = false;
            actor.transform.position = new Vector3(0, 90, 0);
            inventory = actor.gameObject.AddComponent<ResourceContainer>();
            resource = ResourceDefinition.Create("Test resource");
            objects.Add(resource);
            inventory.Add(resource, 20);
        }

        [TearDown]
        public void TearDown()
        {
            if (actor)
                actor.Cancel();
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i])
                    Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        private ResourceInteractionCost Cost(EventAction action, int amount, int installments = 1)
        {
            var cost = action.gameObject.AddComponent<ResourceInteractionCost>();
            cost.Cost = new ResourceCost(new ResourceAmount(resource, amount));
            cost.Installments = installments;
            return cost;
        }

        [Test]
        public void ResourceCostReservesAndRefundsThroughPublicResourceApi()
        {
            var action = Action();
            Cost(action, 8);
            Assert.IsTrue(actor.TryExecute(action));
            Assert.AreEqual(8, inventory.GetReserved(resource));
            Assert.AreEqual(20, inventory.GetTotal(resource));
            actor.Cancel();
            Assert.AreEqual(0, inventory.GetReserved(resource));
            Assert.AreEqual(20, inventory.GetTotal(resource));
            Assert.IsTrue(actor.TryExecute(action));
            actor.Tick(4);
            Assert.AreEqual(12, inventory.GetTotal(resource));
            Assert.AreEqual(0, inventory.GetReserved(resource));
        }

        [Test]
        public void ProgressivePaymentKeepsOnlyCompletedInstallmentsOnCancel()
        {
            var action = Action();
            Cost(action, 8, 4);
            Assert.IsTrue(actor.TryExecute(action));
            actor.Tick(2);
            Assert.AreEqual(16, inventory.GetTotal(resource));
            Assert.AreEqual(4, inventory.GetReserved(resource));
            actor.Cancel();
            Assert.AreEqual(16, inventory.GetTotal(resource));
            Assert.AreEqual(0, inventory.GetReserved(resource));
        }

        [Test]
        public void DestroyingCostCancelsItsReservationsAndAction()
        {
            var action = Action();
            var cost = Cost(action, 8);
            Assert.IsTrue(actor.TryExecute(action));
            Object.DestroyImmediate(cost);
            Assert.AreEqual(0, inventory.GetReserved(resource));
            Assert.AreEqual(20, inventory.GetTotal(resource));
            Assert.IsNull(actor.ActiveAction);
        }

        [Test]
        public void TwoParticipantsCannotReserveMoreThanTheSharedContainerHas()
        {
            var action = Action();
            action.MaxParticipants = 2;
            var cost = Cost(action, 15);
            cost.Container = inventory;
            var other = New("Other").AddComponent<InteractionActor>();
            other.AutomaticUpdate = false;
            other.transform.position = actor.transform.position;
            Assert.IsTrue(actor.TryExecuteDirect(action));
            Assert.IsFalse(other.TryExecuteDirect(action));
            Assert.AreEqual(15, inventory.GetReserved(resource));
        }

        [Test]
        public void PickupAndDepositKeepStockInEasyResources()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(go);
            go.transform.position = new Vector3(0, 90, 2);
            var pickup = go.AddComponent<ResourcePickupAction>();
            pickup.Resource = resource;
            pickup.Amount = 3;
            var stock = go.GetComponent<ResourceContainer>();
            stock.Add(resource, 5);
            Physics.SyncTransforms();
            Assert.IsTrue(actor.TryExecute(pickup));
            Assert.AreEqual(23, inventory.GetTotal(resource));
            Assert.AreEqual(2, stock.GetTotal(resource));
            pickup.Direction = ResourceTransferDirection.Deposit;
            Assert.IsTrue(actor.TryExecute(pickup));
            Assert.AreEqual(20, inventory.GetTotal(resource));
            Assert.AreEqual(5, stock.GetTotal(resource));
        }

        [Test]
        public void DestinationOverflowRejectsTransferWithoutDebitingStock()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(go);
            go.transform.position = new Vector3(0, 90, 2);
            var pickup = go.AddComponent<ResourcePickupAction>();
            pickup.Resource = resource;
            pickup.Amount = 3;
            var stock = go.GetComponent<ResourceContainer>();
            stock.Add(resource, 5);
            inventory.Add(resource, long.MaxValue - 20);
            Physics.SyncTransforms();
            Assert.IsFalse(actor.TryExecute(pickup));
            Assert.AreEqual(5, stock.GetTotal(resource));
            Assert.AreEqual(0, stock.ActiveReservationCount);
        }

        [UnityTest]
        public IEnumerator HudCreatesListAndRadialWithoutManualCanvasSetup()
        {
            var action = Action();
            action.gameObject.AddComponent<EventAction>().ActionId = "second";
            actor.RefreshTarget();
            var hud = actor.gameObject.AddComponent<InteractionHUD>();
            yield return null;
            hud.SetMenuOpen(true);
            Assert.IsTrue(hud.MenuOpen);
            Assert.AreEqual(2, hud.VisibleActionCount);
            hud.Layout = InteractionMenuLayout.Radial;
            yield return null;
            Assert.AreEqual(2, hud.VisibleActionCount);
            hud.SetMenuOpen(false);
            Object.Destroy(hud);
            yield return null;
        }

        [Test]
        public void CombatDamageCancelsAndDeathDisablesUntilRevive()
        {
            var health = actor.gameObject.AddComponent<EasyCombat.Health>();
            actor.gameObject.AddComponent<EasyInteraction.Integrations.Combat.CombatInteractionGuard>();
            var action = Action();
            Assert.IsTrue(actor.TryExecute(action));
            health.ApplyDamage(new EasyCombat.DamageInfo(1));
            Assert.IsNull(actor.ActiveAction);
            Assert.IsTrue(actor.TryExecute(action));
            health.ApplyDamage(new EasyCombat.DamageInfo(1000));
            Assert.IsFalse(actor.enabled);
            Assert.IsNull(actor.ActiveAction);
            health.ResetToFull();
            Assert.IsTrue(actor.enabled);
            Assert.IsTrue(actor.TryExecute(action));
        }

        [UnityTest]
        public IEnumerator ManyActionsRemainReachableThroughMenuPages()
        {
            var action = Action();
            for (int i = 1; i < 13; i++)
                action.gameObject.AddComponent<EventAction>().ActionId = "action-" + i;
            actor.RefreshTarget();
            var hud = actor.gameObject.AddComponent<InteractionHUD>();
            yield return null;
            hud.SetMenuOpen(true);
            Assert.AreEqual(13, hud.ActionCount);
            hud.SetPage(hud.PageCount - 1);
            Assert.IsTrue(hud.VisibleActionCount > 0);
            Assert.AreEqual("action-10", actor.SelectedAction.ActionId);
            hud.Layout = InteractionMenuLayout.Radial;
            hud.SetPage(hud.PageCount - 1);
            Assert.AreEqual("action-12", actor.SelectedAction.ActionId);
            Object.Destroy(hud);
            yield return null;
        }

        [Test]
        public void BuildingSelectionsBelongToEachActor()
        {
            var system = New("Building world").AddComponent<EasyBuilding.BuildingSystem>();
            var first = ScriptableObject.CreateInstance<EasyBuilding.BuildingDefinition>();
            objects.Add(first);
            var second = ScriptableObject.CreateInstance<EasyBuilding.BuildingDefinition>();
            objects.Add(second);
            var action = Action().gameObject.AddComponent<EasyInteraction.Integrations.Building.BuildingAction>();
            action.ActionId = "build";
            action.operation = EasyInteraction.Integrations.Building.BuildingInteraction.SelectForPlacement;
            action.buildingSystem = system;
            action.definition = first;
            Assert.IsTrue(actor.TryExecuteDirect(action));
            var other = New("Other builder").AddComponent<InteractionActor>();
            other.transform.position = actor.transform.position;
            other.AutomaticUpdate = false;
            action.definition = second;
            Assert.IsTrue(other.TryExecuteDirect(action));
            Assert.AreSame(first, actor.GetComponent<EasyInteraction.Integrations.Building.BuildingInteractionContext>().GetSession(system).Selected);
            Assert.AreSame(second, other.GetComponent<EasyInteraction.Integrations.Building.BuildingInteractionContext>().GetSession(system).Selected);
            Assert.IsNull(system.Selected);
        }

        [Test]
        public void OverlappingApproachAndStationLocksRestoreInputOnlyAfterBothRelease()
        {
            var input = actor.gameObject.AddComponent<EasyMovement.FirstPerson.FirstPersonCharacterControllerInput>();
            var character = input.GetComponent<EasyMovement.FirstPerson.FirstPersonCharacterController>();
            actor.gameObject.AddComponent<EasyInteraction.Integrations.Movement.EasyMovementInteractionLock>();
            var motor = actor.gameObject.AddComponent<EasyInteraction.Integrations.Movement.EasyMovementInteractionMotor>();
            var action = Action();
            action.Channel = "body";
            Assert.IsTrue(actor.TryExecuteDirect(action));
            Assert.IsTrue(input.enabled);
            Assert.IsTrue(character.InputBlocked);
            motor.Begin(actor.transform);
            actor.Cancel();
            Assert.IsTrue(input.enabled);
            Assert.IsTrue(character.InputBlocked);
            motor.Stop();
            Assert.IsTrue(input.enabled);
            Assert.IsFalse(character.InputBlocked);
            input.enabled = false;
            motor.Begin(actor.transform);
            motor.Stop();
            Assert.IsFalse(input.enabled);
        }
    }
}
