using EasyBuilding;
using UnityEngine;

namespace EasyInteraction.Integrations.Building
{
    [DisallowMultipleComponent, RequireComponent(typeof(BuildingActor)), AddComponentMenu("Easy Interaction/Integrations/Building Interaction Context")]
    public sealed class BuildingInteractionContext : MonoBehaviour, IBuildingSessionProvider
    {
        public BuildingActor Actor => GetComponent<BuildingActor>();

        /// <summary>Borrow the canonical actor/world session. This context never disposes another component's selection.</summary>
        public BuildingSession GetSession(BuildingSystem system) => system ? ((IBuildingSessionProvider)Actor).GetSession(system) : null;
    }
}
