using AIDrivenFW.Config;
using UnityEngine;

namespace AIDrivenFW.Core
{
    internal static class GenAIConfigLifecycle
    {
        internal static GenAIConfig CreateOwned()
        {
            return ScriptableObject.CreateInstance<GenAIConfig>();
        }

        internal static void DestroyOwned(ref GenAIConfig config)
        {
            if (config == null)
                return;

            GenAIConfig ownedConfig = config;
            config = null;

            if (Application.isPlaying)
                Object.Destroy(ownedConfig);
            else
                Object.DestroyImmediate(ownedConfig);
        }
    }
}
