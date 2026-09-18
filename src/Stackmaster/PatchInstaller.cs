#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Stackmaster
{
    internal static class PatchInstaller
    {
        internal static IReadOnlyList<PatchSpec> Prepare()
        {
            // CompatibilityGate and installation consume this same resolved manifest.
            // Every target and patch entrypoint is resolved before the first Harmony write.
            return HarmonyTargetManifest.ResolveAll();
        }

        internal static IReadOnlyList<PatchSpec> PrepareCleanupSafety()
        {
            // This is a filtered view of the same canonical manifest used at startup.
            return HarmonyTargetManifest.ResolveCleanupSafety();
        }

        internal static void Install(Harmony harmony, IReadOnlyList<PatchSpec> patches)
        {
            if (harmony == null) throw new ArgumentNullException(nameof(harmony));
            if (patches == null) throw new ArgumentNullException(nameof(patches));

            foreach (var patch in patches)
            {
                harmony.Patch(
                    patch.Original,
                    prefix: Wrap(patch.Prefix),
                    postfix: Wrap(patch.Postfix),
                    transpiler: null,
                    finalizer: Wrap(patch.Finalizer),
                    ilmanipulator: null);
            }
        }

        private static HarmonyMethod Wrap(MethodInfo method)
        {
            return method == null ? null : new HarmonyMethod(method);
        }

        internal sealed class PatchSpec
        {
            internal PatchSpec(MethodInfo original, MethodInfo prefix, MethodInfo postfix, MethodInfo finalizer)
            {
                Original = original;
                Prefix = prefix;
                Postfix = postfix;
                Finalizer = finalizer;
            }

            internal MethodInfo Original { get; }
            internal MethodInfo Prefix { get; }
            internal MethodInfo Postfix { get; }
            internal MethodInfo Finalizer { get; }
        }
    }
}
