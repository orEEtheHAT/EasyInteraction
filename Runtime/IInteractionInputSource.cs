using System;
using UnityEngine;

namespace EasyInteraction
{
    /// <summary>Presentation and accessibility settings of one actor's input. Implement on a MonoBehaviour.</summary>
    public interface IInteractionInputSource
    {
        string BindingLabel { get; }

        Sprite BindingIcon { get; }

        bool ToggleInsteadOfHold { get; }

        event Action PresentationChanged;
    }
}
