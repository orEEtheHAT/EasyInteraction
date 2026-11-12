using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace EasyInteraction.Integrations.Localization
{
    [AddComponentMenu("Easy Interaction/Integrations/Localized Interaction Text")]
    public sealed class LocalizedInteractionText : MonoBehaviour, IInteractionTextProvider
    {
        public TableReference table;
        private StringTable loaded;
        private int generation;
        private void OnEnable()
        {
            LocalizationSettings.SelectedLocaleChanged += LocaleChanged;
            Load();
        }

        private void OnDisable()
        {
            LocalizationSettings.SelectedLocaleChanged -= LocaleChanged;
            generation++;
            loaded = null;
        }

        private void LocaleChanged(Locale _) => Load();
        public void Load()
        {
            int version = ++generation;
            loaded = null;
            var operation = LocalizationSettings.StringDatabase.GetTableAsync(table);
            operation.Completed += result =>
            {
                if (this && isActiveAndEnabled && version == generation)
                    loaded = result.Result;
            };
        }

        public string Translate(string key, string fallback)
        {
            if (!loaded)
                return fallback;
            var entry = loaded.GetEntry(string.IsNullOrEmpty(key) ? fallback : key);
            return entry == null ? fallback : entry.LocalizedValue;
        }
    }
}
