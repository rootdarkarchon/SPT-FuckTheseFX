using System;
using BepInEx.Configuration;

namespace FuckTheseFX
{
    // ConfigurationManager discovers this metadata through its public field names.
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? IsAdvanced;
        public bool? ShowRangeAsPercent;
        public bool? HideDefaultButton;
        public bool? HideSettingName;
        public Action<ConfigEntryBase> CustomDrawer;
    }
}
