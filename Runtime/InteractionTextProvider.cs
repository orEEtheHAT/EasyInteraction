using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyInteraction
{
    public interface IInteractionTextProvider
    {
        string Translate(string key, string fallback);
    }

    [AddComponentMenu("Easy Interaction/Interaction Text Provider")]
    public sealed class InteractionTextProvider : MonoBehaviour, IInteractionTextProvider
    {
        [Serializable]
        public struct Translation
        {
            public string key;
            [TextArea]
            public string text;
        }

        [SerializeField]
        private Translation[] translations = Array.Empty<Translation>();
        private readonly Dictionary<string, string> lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        private void OnEnable() => Rebuild();
        public void SetTranslations(Translation[] entries)
        {
            translations = entries ?? Array.Empty<Translation>();
            Rebuild();
        }

        private void Rebuild()
        {
            lookup.Clear();
            foreach (var entry in translations)
                if (!string.IsNullOrEmpty(entry.key))
                    lookup[entry.key] = entry.text;
        }

        public string Translate(string key, string fallback) => lookup.TryGetValue(string.IsNullOrEmpty(key) ? fallback ?? "" : key, out var result) ? result : fallback;
    }
}
