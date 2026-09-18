using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

namespace Stackmaster.Compatibility.Tests
{
    internal static class Program
    {
        private static int _passed;

        private static int Main(string[] args)
        {
            if (args.Length != 4)
                throw new ArgumentException("Expected paths to assembly_valheim.dll, assembly_utils.dll, Stackmaster.dll, and the complete Valheim Managed directory.");
            RuntimeContractGateBehavior();
            HarmonyTargetManifestReleaseGate(args[2], args[3], Path.GetDirectoryName(Path.GetFullPath(args[0]))!);

            var path = Path.GetFullPath(args[0]);
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var reader = pe.GetMetadataReader();
            Console.WriteLine("INFO reference identity is provenance only: MVID " +
                              reader.GetGuid(reader.GetModuleDefinition().Mvid));
            var contract = new Contract(reader, pe);

            var methods = new (string Type, string Name, string[] Parameters)[]
            {
                ("InventoryGui", "Awake", Array.Empty<string>()),
                ("InventoryGui", "Hide", Array.Empty<string>()),
                ("InventoryGui", "Show", new[] { "Container", "System.Int32" }),
                ("InventoryGui", "Update", Array.Empty<string>()),
                ("InventoryGui", "IsContainerOpen", Array.Empty<string>()),
                ("InventoryGui", "OnSelectedItem", new[] { "InventoryGrid", "ItemData", "Vector2i", "Modifier" }),
                ("InventoryGui", "OnRightClickItem", new[] { "InventoryGrid", "ItemData", "Vector2i" }),
                ("InventoryGui", "OnDropOutside", Array.Empty<string>()),
                ("InventoryGui", "OnCraftPressed", Array.Empty<string>()),
                ("InventoryGui", "OnCraftCancelPressed", Array.Empty<string>()),
                ("InventoryGui", "OnTabCraftPressed", Array.Empty<string>()),
                ("InventoryGui", "OnTabUpgradePressed", Array.Empty<string>()),
                ("InventoryGui", "OnSelectedRecipe", new[] { "UnityEngine.GameObject" }),
                ("InventoryGui", "UpdateRecipe", new[] { "Player", "System.Single" }),
                ("InventoryGui", "DoCrafting", new[] { "Player" }),
                ("InventoryGui", "SetupRequirement", new[] { "UnityEngine.Transform", "Requirement", "Player", "System.Boolean", "System.Int32", "System.Int32" }),
                ("InventoryGui", "get_instance", Array.Empty<string>()),
                ("Hud", "SetupPieceInfo", new[] { "Piece" }),
                ("BuildUi", "OnSelectPiece", new[] { "Piece" }),
                ("InventoryGrid", "UpdateInventory", new[] { "Inventory", "Player", "ItemData" }),
                ("InventoryGrid", "OnLeftDown", new[] { "UIInputHandler" }),
                ("InventoryGrid", "OnRightDown", new[] { "UIInputHandler" }),
                ("Container", "CheckAccess", new[] { "System.Int64" }),
                ("Container", "CheckForChanges", Array.Empty<string>()),
                ("Container", "GetInventory", Array.Empty<string>()),
                ("Container", "IsOwner", Array.Empty<string>()),
                ("Container", "IsInUse", Array.Empty<string>()),
                ("Container", "SetInUse", new[] { "System.Boolean" }),
                ("Container", "Interact", new[] { "Humanoid", "System.Boolean", "System.Boolean" }),
                ("Container", "GetHoverText", Array.Empty<string>()),
                ("Container", "StackAll", Array.Empty<string>()),
                ("Container", "RPC_RequestOpen", new[] { "System.Int64", "System.Int64" }),
                ("Container", "RPC_RequestStack", new[] { "System.Int64", "System.Int64" }),
                ("Container", "RPC_StackResponse", new[] { "System.Int64", "System.Boolean" }),
                ("Game", "Shutdown", new[] { "System.Boolean" }),
                ("ZNet", "Shutdown", new[] { "System.Boolean" }),
                ("ZNet", "ShutdownWithoutSave", new[] { "System.Boolean" }),
                ("ZNet", "Update", Array.Empty<string>()),
                ("Player", "HaveRequirementItems", new[] { "Recipe", "System.Boolean", "System.Int32", "System.Int32" }),
                ("Player", "HaveRequirements", new[] { "Piece", "RequirementMode" }),
                ("Player", "GetFirstRequiredItem", new[] { "Inventory", "Recipe", "System.Int32", "System.Int32&", "System.Int32&", "System.Int32" }),
                ("Player", "UpdatePlacement", new[] { "System.Boolean", "System.Single" }),
                ("Player", "TryPlacePiece", new[] { "Piece" }),
                ("Inventory", "RemoveItem", new[] { "System.String", "System.Int32", "System.Int32", "System.Boolean" }),
                ("Inventory", "MoveItemToThis", new[] { "Inventory", "ItemData", "System.Int32", "System.Int32", "System.Int32" }),
                ("Inventory", "CountItems", new[] { "System.String", "System.Int32", "System.Boolean" }),
                ("CraftingStation", "GetStationBuildRange", Array.Empty<string>()),
                ("CraftingStation", "GetLevel", new[] { "System.Boolean" }),
                ("CraftingStation", "CheckUsable", new[] { "Player", "System.Boolean" }),
                ("ZNetScene", "GetPrefab", new[] { "System.String" }),
                ("ZNetView", "GetZDO", Array.Empty<string>()),
                ("ZDO", "GetPrefab", Array.Empty<string>())
            };
            foreach (var method in methods) contract.Method(method.Type, method.Name, method.Parameters);

            contract.Method("ZDOMan", "GetSessionID", "System.Int64", Array.Empty<string>(), MethodAttributes.Public | MethodAttributes.Static);
            contract.Method("GameCamera", "InFreeFly", "System.Boolean", Array.Empty<string>(), MethodAttributes.Public | MethodAttributes.Static);
            contract.Method("PrivateArea", "CheckAccess", "System.Boolean",
                new[] { "UnityEngine.Vector3", "System.Single", "System.Boolean", "System.Boolean" },
                MethodAttributes.Public | MethodAttributes.Static);

            var fields = new (string Type, string Name)[]
            {
                ("InventoryGui", "m_pvp"), ("InventoryGui", "m_container"),
                ("InventoryGui", "m_currentContainer"), ("InventoryGui", "m_craftTimer"),
                ("InventoryGui", "m_craftRecipe"), ("InventoryGui", "m_selectedRecipe"),
                ("InventoryGui", "m_reqList"), ("InventoryGui", "m_craftUpgradeItem"),
                ("InventoryGui", "m_selectedVariant"), ("InventoryGui", "m_craftVariant"),
                ("InventoryGui", "m_touchMultiCrafting"), ("InventoryGui", "m_multiCrafting"),
                ("InventoryGui", "m_multiCraftAmount"), ("InventoryGui", "m_dragItem"),
                ("InventoryGui", "m_dragInventory"), ("Hud", "m_requirementItems"),
                ("Container", "m_nview"), ("Container", "m_wagon"),
                ("Inventory", "m_onChanged"), ("Inventory", "m_inventory"),
                ("Player", "m_customData"), ("Player", "m_noPlacementCost"),
                ("ItemDrop", "m_itemData")
            };
            foreach (var field in fields) contract.Field(field.Type, field.Name);

            contract.Field("InventoryGrid", "m_onSelected");
            contract.Field("InventoryGrid", "m_onRightClick");
            contract.MethodReadsFieldByName("InventoryGrid", "OnLeftDown", new[] { "UIInputHandler" }, "InventoryGrid", "m_onSelected");
            contract.MethodDoesNotReadFieldByName("InventoryGrid", "OnLeftDown", new[] { "UIInputHandler" }, "InventoryGrid", "m_onRightClick");
            contract.MethodReadsFieldByName("InventoryGrid", "OnRightDown", new[] { "UIInputHandler" }, "InventoryGrid", "m_onRightClick");
            contract.MethodDoesNotReadFieldByName("InventoryGrid", "OnRightDown", new[] { "UIInputHandler" }, "InventoryGrid", "m_onSelected");

            using var utilsStream = File.OpenRead(Path.GetFullPath(args[1]));
            using var utilsPe = new PEReader(utilsStream);
            var utilsContract = new Contract(utilsPe.GetMetadataReader(), utilsPe);
            utilsContract.Method("ZInput", "ResetButtonStatus", "System.Void", new[] { "System.String" },
                MethodAttributes.Public | MethodAttributes.Static);

            Console.WriteLine(_passed + " compatibility contract checks passed");
            return 0;
        }
        private static void Equal<T>(T expected, T actual, string label) where T : notnull
        {
            if (!expected.Equals(actual)) throw new InvalidOperationException(label + " mismatch: " + actual);
            _passed++;
            Console.WriteLine("PASS " + label);
        }

        private static void HarmonyTargetManifestReleaseGate(string pluginPath, string managedDirectory, string referenceDirectory)
        {
            var plugin = Path.GetFullPath(pluginPath);
            var managed = Path.GetFullPath(managedDirectory);
            if (!File.Exists(plugin)) throw new FileNotFoundException("Compiled Stackmaster.dll missing", plugin);
            if (!Directory.Exists(managed)) throw new DirectoryNotFoundException("Complete Valheim Managed directory missing: " + managed);

            var searchDirectories = new[] { Path.GetDirectoryName(plugin)!, referenceDirectory, managed };
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                foreach (var directory in searchDirectories)
                {
                    var candidate = Path.Combine(directory, name.Name + ".dll");
                    if (File.Exists(candidate)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
                }
                return null;
            };

            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(plugin);
            var manifestType = assembly.GetType("Stackmaster.HarmonyTargetManifest", true)!;
            var installerType = assembly.GetType("Stackmaster.PatchInstaller", true)!;
            var descriptors = ((System.Collections.IEnumerable)manifestType
                .GetProperty("Descriptors", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null)!).Cast<object>().ToArray();
            var prepared = ((System.Collections.IEnumerable)installerType
                .GetMethod("Prepare", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, null)!).Cast<object>().ToArray();
            var cleanupSafety = ((System.Collections.IEnumerable)installerType
                .GetMethod("PrepareCleanupSafety", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, null)!).Cast<object>().ToArray();

            Equal(32, descriptors.Length, "HarmonyTargetManifest contains every declared patch operation");
            Equal(descriptors.Length, prepared.Length, "PatchInstaller.Prepare returns every manifest operation");
            Equal(2, cleanupSafety.Length, "cleanup safety installation filters the canonical manifest");

            var setupRequirementObserved = false;
            object? setupRequirementDescriptor = null;
            for (var index = 0; index < descriptors.Length; index++)
            {
                var descriptor = descriptors[index];
                var descriptorType = descriptor.GetType();
                var targetType = (Type)ReadProperty(descriptor, "TargetType")!;
                var targetName = (string)ReadProperty(descriptor, "TargetName")!;
                var isStatic = (bool)ReadProperty(descriptor, "IsStatic")!;
                var returnType = (Type)ReadProperty(descriptor, "ReturnType")!;
                var parameters = (Type[])ReadProperty(descriptor, "Parameters")!;
                var patchType = (Type)ReadProperty(descriptor, "PatchType")!;
                var identity = (string)ReadProperty(descriptor, "Identity")!;

                var matches = targetType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                    BindingFlags.Instance | BindingFlags.Static |
                                                    BindingFlags.DeclaredOnly)
                    .Where(method => method.Name == targetName)
                    .Where(method => method.IsStatic == isStatic)
                    .Where(method => method.ReturnType == returnType)
                    .Where(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                        .SequenceEqual(parameters))
                    .ToArray();
                Equal(1, matches.Length, "release target exact declaring/static/return/parameter contract " + identity);

                var resolved = descriptorType.GetMethod("Resolve", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(descriptor, null)!;
                var preparedSpec = prepared[index];
                var expectedOriginal = (MethodInfo)ReadProperty(resolved, "Original")!;
                var actualOriginal = (MethodInfo)ReadProperty(preparedSpec, "Original")!;
                Equal(expectedOriginal, actualOriginal, "Prepare exact MethodInfo " + identity);

                foreach (var role in new[] { "Prefix", "Postfix", "Finalizer" })
                {
                    var patchMethod = (MethodInfo?)ReadProperty(preparedSpec, role);
                    if (patchMethod == null) continue;
                    Equal(true, patchMethod.IsStatic, identity + " " + role.ToLowerInvariant() + " is a unique static patch entrypoint");
                    Equal(patchType, patchMethod.DeclaringType!, identity + " " + role.ToLowerInvariant() + " declaring type");
                }

                if (targetType.Name == "InventoryGui" && targetName == "SetupRequirement")
                {
                    setupRequirementDescriptor = descriptor;
                    setupRequirementObserved = isStatic && returnType == typeof(bool) &&
                                               actualOriginal.IsStatic && actualOriginal.ReturnType == typeof(bool);
                }
            }

            var cleanupOriginals = cleanupSafety
                .Select(spec => (MethodInfo)ReadProperty(spec, "Original")!)
                .ToArray();
            var expectedCleanupOriginals = descriptors
                .Where(descriptor => (bool)ReadProperty(descriptor, "PreserveDuringCleanup")!)
                .Select(descriptor => (MethodInfo)ReadProperty(
                    descriptor.GetType().GetMethod("Resolve", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(descriptor, null)!, "Original")!)
                .ToArray();
            Equal(true, cleanupOriginals.SequenceEqual(expectedCleanupOriginals),
                "cleanup safety Prepare returns exact canonical manifest MethodInfo objects");

            Equal(true, setupRequirementObserved,
                "live regression InventoryGui.SetupRequirement resolves as static bool in Prepare");
            Equal(true, BrokenDescriptorFailsClosed(setupRequirementDescriptor!, isStatic: false, returnType: typeof(bool)),
                "negative regression rejects SetupRequirement declared as an instance method");
            Equal(true, BrokenDescriptorFailsClosed(setupRequirementDescriptor!, isStatic: true, returnType: typeof(void)),
                "negative regression rejects SetupRequirement declared with the wrong return type");
            Console.WriteLine("PASS HarmonyTargetManifestReleaseGate validates target uniqueness, shape, patch compatibility, and Prepare MethodInfo identity");
            _passed++;
        }

        private static bool BrokenDescriptorFailsClosed(object source, bool isStatic, Type returnType)
        {
            var descriptorType = source.GetType();
            var constructor = descriptorType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var broken = constructor.Invoke(new object?[]
            {
                ReadProperty(source, "TargetType"),
                ReadProperty(source, "TargetName"),
                isStatic,
                returnType,
                ReadProperty(source, "Parameters"),
                ReadProperty(source, "PatchType"),
                ReadProperty(source, "PrefixName"),
                ReadProperty(source, "PostfixName"),
                ReadProperty(source, "FinalizerName"),
                false
            });
            try
            {
                descriptorType.GetMethod("Resolve", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(broken, null);
                return false;
            }
            catch (TargetInvocationException exception) when (exception.InnerException is MissingMethodException)
            {
                return true;
            }
        }

        private static object? ReadProperty(object value, string name)
        {
            return value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
        }

        private static void RuntimeContractGateBehavior()
        {
            Equal(0, EvaluateSyntheticIdentity("client-build-a", Guid.Parse("11111111-1111-1111-1111-111111111111"), "identity-a"),
                "first arbitrary runtime identity passes an unchanged member contract");
            Equal(0, EvaluateSyntheticIdentity("client-build-b", Guid.Parse("22222222-2222-2222-2222-222222222222"), "identity-b"),
                "different arbitrary runtime identity passes the same member contract");

            var failures = new List<string>();
            global::Stackmaster.RuntimeContractValidator.RequireMethod(
                failures, typeof(ValidRuntime), "Target", false, true, typeof(string));
            Equal(1, failures.Count, "missing required overload fails closed");

            failures.Clear();
            global::Stackmaster.RuntimeContractValidator.RequireMethod(
                failures, typeof(ValidRuntime), "StaticHook", false, true);
            Equal(1, failures.Count, "wrong method staticness fails closed");

            failures.Clear();
            global::Stackmaster.RuntimeContractValidator.RequireField(failures, typeof(ValidRuntime), "MissingField");
            Equal(1, failures.Count, "missing required field fails closed");

            failures.Clear();
            global::Stackmaster.RuntimeContractValidator.RequireProperty(failures, typeof(ValidRuntime), "MissingProperty");
            Equal(1, failures.Count, "missing required property fails closed");

            var ambiguousFailed = false;
            try
            {
                global::Stackmaster.RuntimeContractValidator.ResolveUniqueNamedMethod(
                    typeof(AmbiguousPatch), "Patch", true);
            }
            catch (AmbiguousMatchException)
            {
                ambiguousFailed = true;
            }
            Equal(true, ambiguousFailed, "ambiguous Harmony patch entrypoint fails closed");

            failures.Clear();
            RequireSyntheticClientUtilityContracts(failures, typeof(StaticClientUtilities));
            Equal(0, failures.Count, "static client utility contracts pass with their real shape");

            failures.Clear();
            RequireSyntheticClientUtilityContracts(failures, typeof(InstanceClientUtilities));
            Equal(4, failures.Count, "instance-shaped client utility lookalikes fail closed");

            foreach (var missingName in new[] { "GetSessionID", "InFreeFly", "ResetButtonStatus", "CheckAccess" })
            {
                failures.Clear();
                RequireSyntheticClientUtilityContracts(failures, typeof(MissingClientUtilities), missingName);
                Equal(1, failures.Count, "missing " + missingName + " fails closed");
            }
        }

        private static void RequireSyntheticClientUtilityContracts(
            ICollection<string> failures,
            Type type,
            string? onlyName = null)
        {
            if (onlyName == null || onlyName == "GetSessionID")
                global::Stackmaster.RuntimeContractValidator.RequireMethod(
                    failures, type, "GetSessionID", true, true);
            if (onlyName == null || onlyName == "InFreeFly")
                global::Stackmaster.RuntimeContractValidator.RequireMethod(
                    failures, type, "InFreeFly", true, true);
            if (onlyName == null || onlyName == "ResetButtonStatus")
                global::Stackmaster.RuntimeContractValidator.RequireMethod(
                    failures, type, "ResetButtonStatus", true, true, typeof(string));
            if (onlyName == null || onlyName == "CheckAccess")
                global::Stackmaster.RuntimeContractValidator.RequireMethod(
                    failures, type, "CheckAccess", true, true,
                    typeof(SyntheticVector3), typeof(float), typeof(bool), typeof(bool));
        }

        private sealed class SyntheticVector3 { }

        private static class StaticClientUtilities
        {
            public static long GetSessionID() => 1L;
            public static bool InFreeFly() => false;
            public static void ResetButtonStatus(string name) => _ = name;
            public static bool CheckAccess(SyntheticVector3 point, float radius, bool flash, bool wardCheck)
            {
                _ = point;
                _ = radius;
                _ = flash;
                _ = wardCheck;
                return true;
            }
        }

        private sealed class InstanceClientUtilities
        {
            public long GetSessionID() => 1L;
            public bool InFreeFly() => false;
            public void ResetButtonStatus(string name) => _ = name;
            public bool CheckAccess(SyntheticVector3 point, float radius, bool flash, bool wardCheck)
            {
                _ = point;
                _ = radius;
                _ = flash;
                _ = wardCheck;
                return true;
            }
        }

        private static class MissingClientUtilities { }

        private static int EvaluateSyntheticIdentity(string gameLabel, Guid mvid, string sha256)
        {
            // These values are intentionally observed but never compared. They prove
            // that runtime identity differences cannot reject an intact ABI contract.
            _ = gameLabel;
            _ = mvid;
            _ = sha256;
            var failures = new List<string>();
            global::Stackmaster.RuntimeContractValidator.RequireType(failures, typeof(ValidRuntime));
            global::Stackmaster.RuntimeContractValidator.RequireMethod(
                failures, typeof(ValidRuntime), "Target", false, true, typeof(int));
            global::Stackmaster.RuntimeContractValidator.RequireMethod(
                failures, typeof(ValidRuntime), "StaticHook", true, true);
            global::Stackmaster.RuntimeContractValidator.RequireField(failures, typeof(ValidRuntime), "Field");
            global::Stackmaster.RuntimeContractValidator.RequireProperty(failures, typeof(ValidRuntime), "Property");
            return failures.Count;
        }

        private sealed class ValidRuntime
        {
            public int Field;
            public string Property { get; } = string.Empty;
            public void Target(int value) => Field = value;
            public static void StaticHook() { }
        }

        private static class AmbiguousPatch
        {
            public static void Patch() { }
            public static void Patch(int value) => _ = value;
        }

        private sealed class Contract
        {
            private readonly MetadataReader _reader;
            private readonly PEReader _pe;
            private readonly TypeNameProvider _provider;

            internal Contract(MetadataReader reader, PEReader pe)
            {
                _reader = reader;
                _pe = pe;
                _provider = new TypeNameProvider();
            }

            internal void Method(string typeName, string methodName, string[] parameters)
            {
                var type = FindTopLevel(typeName);
                var matches = type.GetMethods()
                    .Select(handle => _reader.GetMethodDefinition(handle))
                    .Where(method => _reader.GetString(method.Name) == methodName)
                    .Where(method => method.DecodeSignature(_provider, null).ParameterTypes.SequenceEqual(parameters))
                    .ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException(typeName + "." + methodName + " parameter contract mismatch: " + matches.Length);
                _passed++;
                Console.WriteLine("PASS method " + typeName + "." + methodName);
            }

            internal void Field(string typeName, string fieldName)
            {
                var type = FindTopLevel(typeName);
                var matches = type.GetFields()
                    .Select(handle => _reader.GetFieldDefinition(handle))
                    .Where(field => _reader.GetString(field.Name) == fieldName)
                    .ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException(typeName + "." + fieldName + " field contract mismatch: " + matches.Length);
                _passed++;
                Console.WriteLine("PASS field " + typeName + "." + fieldName);
            }

            internal void Method(string typeName, string methodName, string returnType, string[] parameters, MethodAttributes required)
            {
                var type = FindTopLevel(typeName);
                var matches = type.GetMethods()
                    .Select(handle => _reader.GetMethodDefinition(handle))
                    .Where(method => _reader.GetString(method.Name) == methodName)
                    .Where(method =>
                    {
                        var signature = method.DecodeSignature(_provider, null);
                        return signature.ReturnType == returnType && signature.ParameterTypes.SequenceEqual(parameters);
                    }).ToArray();
                if (matches.Length != 1 || (matches[0].Attributes & required) != required)
                    throw new InvalidOperationException(typeName + "." + methodName + " signature/attributes mismatch");
                _passed++;
                Console.WriteLine("PASS method " + typeName + "." + methodName);
            }

            internal void HaveRequirementsStationCallSequence()
            {
                var haveRequirementsHandle = FindMethodHandle(
                    "Player", "HaveRequirements", "System.Boolean", new[] { "Piece", "RequirementMode" });
                var stationMethodHandle = FindMethodHandle(
                    "CraftingStation", "HaveBuildStationInRange", "CraftingStation",
                    new[] { "System.String", "UnityEngine.Vector3" });
                var implicitHandle = _reader.MemberReferences.Single(handle =>
                {
                    var member = _reader.GetMemberReference(handle);
                    if (_reader.GetString(member.Name) != "op_Implicit" || member.Parent.Kind != HandleKind.TypeReference)
                        return false;
                    var parent = _reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                    if (_reader.GetString(parent.Namespace) != "UnityEngine" || _reader.GetString(parent.Name) != "Object")
                        return false;
                    var signature = member.DecodeMethodSignature(_provider, null);
                    return signature.ReturnType == "System.Boolean" &&
                           signature.ParameterTypes.SequenceEqual(new[] { "UnityEngine.Object" });
                });

                var firstToken = MetadataTokens.GetToken(stationMethodHandle);
                var secondToken = MetadataTokens.GetToken(implicitHandle);
                var pattern = new byte[10];
                pattern[0] = 0x28;
                BitConverter.GetBytes(firstToken).CopyTo(pattern, 1);
                pattern[5] = 0x28;
                BitConverter.GetBytes(secondToken).CopyTo(pattern, 6);

                var il = MethodIl(haveRequirementsHandle);
                var matches = 0;
                for (var index = 0; index <= il.Length - pattern.Length; index++)
                {
                    if (il.AsSpan(index, pattern.Length).SequenceEqual(pattern)) matches++;
                }
                if (matches != 1)
                    throw new InvalidOperationException("Player.HaveRequirements station call sequence mismatch: " + matches);
                _passed++;
                Console.WriteLine("PASS IL Player.HaveRequirements station call sequence");
            }

            internal void HaveRequirementsStationFailureBranch()
            {
                var haveRequirementsHandle = FindMethodHandle(
                    "Player", "HaveRequirements", "System.Boolean", new[] { "Piece", "RequirementMode" });
                var stationHandle = FindMethodHandle(
                    "CraftingStation", "HaveBuildStationInRange", "CraftingStation",
                    new[] { "System.String", "UnityEngine.Vector3" });
                var zoneInstanceHandle = FindMethodHandle("ZoneSystem", "get_instance", "ZoneSystem", Array.Empty<string>());
                var globalKeyHandle = FindMethodHandle(
                    "ZoneSystem", "GetGlobalKey", "System.Boolean", new[] { "GlobalKeys" });
                var implicitHandle = _reader.MemberReferences.Single(handle =>
                {
                    var member = _reader.GetMemberReference(handle);
                    if (_reader.GetString(member.Name) != "op_Implicit" || member.Parent.Kind != HandleKind.TypeReference)
                        return false;
                    var parent = _reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                    if (_reader.GetString(parent.Namespace) != "UnityEngine" || _reader.GetString(parent.Name) != "Object")
                        return false;
                    var signature = member.DecodeMethodSignature(_provider, null);
                    return signature.ReturnType == "System.Boolean" &&
                           signature.ParameterTypes.SequenceEqual(new[] { "UnityEngine.Object" });
                });

                var il = MethodIl(haveRequirementsHandle);
                var stationCall = FindCallOffsets(il, MetadataTokens.GetToken(stationHandle)).Single();
                if (stationCall + 28 >= il.Length ||
                    il[stationCall] != 0x28 ||
                    ReadToken(il, stationCall + 1) != MetadataTokens.GetToken(stationHandle) ||
                    il[stationCall + 5] != 0x28 ||
                    ReadToken(il, stationCall + 6) != MetadataTokens.GetToken(implicitHandle) ||
                    il[stationCall + 10] != 0x2d ||
                    il[stationCall + 12] != 0x28 ||
                    ReadToken(il, stationCall + 13) != MetadataTokens.GetToken(zoneInstanceHandle) ||
                    il[stationCall + 17] != 0x1f || il[stationCall + 18] != 27 ||
                    il[stationCall + 19] != 0x6f ||
                    ReadToken(il, stationCall + 20) != MetadataTokens.GetToken(globalKeyHandle) ||
                    il[stationCall + 24] != 0x2d ||
                    il[stationCall + 26] != 0x16 || il[stationCall + 27] != 0x2a)
                {
                    throw new InvalidOperationException("Player.HaveRequirements station-failure branch shape mismatch");
                }

                var stationSuccessTarget = stationCall + 12 + unchecked((sbyte)il[stationCall + 11]);
                var freeBuildSuccessTarget = stationCall + 26 + unchecked((sbyte)il[stationCall + 25]);
                if (stationSuccessTarget != stationCall + 28 || freeBuildSuccessTarget != stationCall + 28)
                    throw new InvalidOperationException("Player.HaveRequirements station-failure branch target mismatch");

                _passed++;
                Console.WriteLine("PASS IL null station bypasses the complete station-failure branch");
            }

            internal void RequirementCallSite(
                string typeName,
                string methodName,
                string[] parameters,
                int expectedMode,
                bool requireTryPlaceAfter)
            {
                var methodHandle = FindMethodHandle(typeName, methodName, parameters);
                var haveRequirementsHandle = FindMethodHandle(
                    "Player", "HaveRequirements", "System.Boolean", new[] { "Piece", "RequirementMode" });
                var il = MethodIl(methodHandle);
                var calls = FindCallOffsets(il, MetadataTokens.GetToken(haveRequirementsHandle));
                if (calls.Count != 1)
                    throw new InvalidOperationException(typeName + "." + methodName + " requirement call count mismatch: " + calls.Count);

                var expectedModeOpcode = expectedMode switch
                {
                    0 => (byte)0x16,
                    1 => (byte)0x17,
                    2 => (byte)0x18,
                    _ => throw new ArgumentOutOfRangeException(nameof(expectedMode))
                };
                if (calls[0] == 0 || il[calls[0] - 1] != expectedModeOpcode)
                    throw new InvalidOperationException(typeName + "." + methodName + " requirement mode mismatch");

                if (requireTryPlaceAfter)
                {
                    var tryPlaceHandle = FindMethodHandle("Player", "TryPlacePiece", new[] { "Piece" });
                    var tryPlaceCalls = FindCallOffsets(il, MetadataTokens.GetToken(tryPlaceHandle));
                    if (tryPlaceCalls.Count != 1 || tryPlaceCalls[0] <= calls[0])
                        throw new InvalidOperationException(typeName + "." + methodName + " placement call ordering mismatch");
                }

                _passed++;
                Console.WriteLine("PASS IL " + typeName + "." + methodName + " requirement path");
            }

            internal void Field(string typeName, string fieldName, string fieldType, FieldAttributes required)
            {
                var handle = FindFieldHandle(typeName, fieldName, fieldType);
                var field = _reader.GetFieldDefinition(handle);
                if ((field.Attributes & required) != required)
                    throw new InvalidOperationException(typeName + "." + fieldName + " signature/attributes mismatch");
                _passed++;
                Console.WriteLine("PASS field " + typeName + "." + fieldName);
            }

            internal void NestedField(
                string outerName, string nestedName, string fieldName, string fieldType, FieldAttributes required)
            {
                var outer = _reader.GetTypeDefinition(FindTopLevelHandle(outerName));
                var nested = outer.GetNestedTypes()
                    .Select(handle => _reader.GetTypeDefinition(handle))
                    .Single(type => _reader.GetString(type.Name) == nestedName);
                var field = nested.GetFields()
                    .Select(handle => _reader.GetFieldDefinition(handle))
                    .Single(value => _reader.GetString(value.Name) == fieldName &&
                                     value.DecodeSignature(_provider, null) == fieldType);
                if ((field.Attributes & required) != required)
                    throw new InvalidOperationException(outerName + "." + nestedName + "." + fieldName + " signature/attributes mismatch");
                _passed++;
                Console.WriteLine("PASS field " + outerName + "." + nestedName + "." + fieldName);
            }

            internal void GenericMethodCall(
                string sourceType, string sourceName, string sourceReturn, string[] sourceParameters,
                string targetType, string targetName, string genericArgument)
            {
                var source = FindMethodHandle(sourceType, sourceName, sourceReturn, sourceParameters);
                var matches = Enumerable.Range(1, _reader.GetTableRowCount(TableIndex.MethodSpec))
                    .Select(MetadataTokens.MethodSpecificationHandle)
                    .Where(handle =>
                {
                    var specification = _reader.GetMethodSpecification(handle);
                    if (specification.Method.Kind != HandleKind.MemberReference) return false;
                    var member = _reader.GetMemberReference((MemberReferenceHandle)specification.Method);
                    if (_reader.GetString(member.Name) != targetName || member.Parent.Kind != HandleKind.TypeReference) return false;
                    var parent = _reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                    var parentNamespace = _reader.GetString(parent.Namespace);
                    var parentName = (string.IsNullOrEmpty(parentNamespace) ? "" : parentNamespace + ".") + _reader.GetString(parent.Name);
                    var arguments = specification.DecodeSignature(_provider, null);
                    return parentName == targetType && arguments.SequenceEqual(new[] { genericArgument });
                }).ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException(sourceType + "." + sourceName + " generic target mismatch: " + matches.Length);
                RequireInstructionToken(source, new byte[] { 0x28, 0x6f }, MetadataTokens.GetToken(matches[0]),
                    sourceType + "." + sourceName + " calls " + targetType + "." + targetName + "<" + genericArgument + ">");
            }

            internal void MethodReadsFieldByName(
                string sourceType, string sourceName, string[] sourceParameters,
                string fieldType, string fieldName)
            {
                var source = FindMethodHandle(sourceType, sourceName, sourceParameters);
                var field = FindFieldHandle(fieldType, fieldName);
                RequireInstructionToken(source, new byte[] { 0x7b }, MetadataTokens.GetToken(field),
                    sourceType + "." + sourceName + " reads " + fieldType + "." + fieldName);
            }

            internal void MethodDoesNotReadFieldByName(
                string sourceType, string sourceName, string[] sourceParameters,
                string fieldType, string fieldName)
            {
                var source = FindMethodHandle(sourceType, sourceName, sourceParameters);
                var field = FindFieldHandle(fieldType, fieldName);
                var method = _reader.GetMethodDefinition(source);
                var bytes = _pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()
                    ?? throw new InvalidOperationException(sourceType + "." + sourceName + " has no IL body");
                var token = MetadataTokens.GetToken(field);
                for (var index = 0; index + 4 < bytes.Length; index++)
                {
                    if (bytes[index] != 0x7b) continue;
                    var observed = bytes[index + 1] |
                                   (bytes[index + 2] << 8) |
                                   (bytes[index + 3] << 16) |
                                   (bytes[index + 4] << 24);
                    if (observed == token)
                        throw new InvalidOperationException(sourceType + "." + sourceName + " unexpectedly reads " + fieldType + "." + fieldName);
                }
                _passed++;
                Console.WriteLine("PASS IL " + sourceType + "." + sourceName + " excludes " + fieldType + "." + fieldName);
            }

            internal void MethodReadsField(
                string sourceType, string sourceName, string sourceReturn, string[] sourceParameters,
                string fieldType, string fieldName)
            {
                var source = FindMethodHandle(sourceType, sourceName, sourceReturn, sourceParameters);
                var field = FindFieldHandle(fieldType, fieldName, "CraftingStation");
                RequireInstructionToken(source, new byte[] { 0x7b }, MetadataTokens.GetToken(field),
                    sourceType + "." + sourceName + " reads " + fieldType + "." + fieldName);
            }

            internal void MethodCalls(
                string sourceType, string sourceName, string sourceReturn, string[] sourceParameters,
                string targetType, string targetName, string targetReturn, string[] targetParameters)
            {
                var source = FindMethodHandle(sourceType, sourceName, sourceReturn, sourceParameters);
                var target = FindMethodHandle(targetType, targetName, targetReturn, targetParameters);
                RequireInstructionToken(source, new byte[] { 0x28, 0x6f }, MetadataTokens.GetToken(target),
                    sourceType + "." + sourceName + " calls " + targetType + "." + targetName);
            }

            private void RequireInstructionToken(MethodDefinitionHandle source, byte[] opcodes, int token, string label)
            {
                var method = _reader.GetMethodDefinition(source);
                var bytes = _pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()
                    ?? throw new InvalidOperationException(label + " has no IL body");
                var found = false;
                for (var index = 0; index + 4 < bytes.Length && !found; index++)
                {
                    if (!opcodes.Contains(bytes[index])) continue;
                    var observed = bytes[index + 1] |
                                   (bytes[index + 2] << 8) |
                                   (bytes[index + 3] << 16) |
                                   (bytes[index + 4] << 24);
                    found = observed == token;
                }
                if (!found) throw new InvalidOperationException(label + " IL contract mismatch");
                _passed++;
                Console.WriteLine("PASS IL " + label);
            }

            private MethodDefinitionHandle FindMethodHandle(string typeName, string methodName, string returnType, string[] parameters)
            {
                var type = FindTopLevel(typeName);
                return type.GetMethods().Single(handle =>
                {
                    var method = _reader.GetMethodDefinition(handle);
                    if (_reader.GetString(method.Name) != methodName) return false;
                    var signature = method.DecodeSignature(_provider, null);
                    return signature.ReturnType == returnType && signature.ParameterTypes.SequenceEqual(parameters);
                });
            }

            private MethodDefinitionHandle FindMethodHandle(string typeName, string methodName, string[] parameters)
            {
                var type = FindTopLevel(typeName);
                return type.GetMethods().Single(handle =>
                {
                    var method = _reader.GetMethodDefinition(handle);
                    var signature = method.DecodeSignature(_provider, null);
                    return _reader.GetString(method.Name) == methodName && signature.ParameterTypes.SequenceEqual(parameters);
                });
            }

            private byte[] MethodIl(MethodDefinitionHandle handle)
            {
                var definition = _reader.GetMethodDefinition(handle);
                return _pe.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes()
                    ?? throw new InvalidOperationException("Method has no IL body");
            }

            private static List<int> FindCallOffsets(byte[] il, int token)
            {
                var offsets = new List<int>();
                for (var index = 0; index <= il.Length - 5; index++)
                {
                    if ((il[index] == 0x28 || il[index] == 0x6f) && ReadToken(il, index + 1) == token)
                        offsets.Add(index);
                }
                return offsets;
            }

            private static int ReadToken(byte[] il, int offset) => BitConverter.ToInt32(il, offset);

            private FieldDefinitionHandle FindFieldHandle(string typeName, string fieldName)
            {
                var type = FindTopLevel(typeName);
                return type.GetFields().Single(handle =>
                    _reader.GetString(_reader.GetFieldDefinition(handle).Name) == fieldName);
            }

            private FieldDefinitionHandle FindFieldHandle(string typeName, string fieldName, string fieldType)
            {
                var type = FindTopLevel(typeName);
                return type.GetFields().Single(handle =>
                {
                    var field = _reader.GetFieldDefinition(handle);
                    return _reader.GetString(field.Name) == fieldName && field.DecodeSignature(_provider, null) == fieldType;
                });
            }

            internal void EnumValues(string outerName, string nestedName, IReadOnlyDictionary<string, int> expected)
            {
                var outer = _reader.GetTypeDefinition(FindTopLevelHandle(outerName));
                var nested = outer.GetNestedTypes()
                    .Select(handle => _reader.GetTypeDefinition(handle))
                    .Single(value => _reader.GetString(value.Name) == nestedName);
                foreach (var pair in expected)
                {
                    var field = nested.GetFields().Select(handle => _reader.GetFieldDefinition(handle))
                        .Single(value => _reader.GetString(value.Name) == pair.Key);
                    var constant = _reader.GetConstant(field.GetDefaultValue());
                    var value = BitConverter.ToInt32(_reader.GetBlobBytes(constant.Value), 0);
                    if (value != pair.Value) throw new InvalidOperationException(outerName + "." + nestedName + "." + pair.Key + " mismatch");
                }
                _passed++;
                Console.WriteLine("PASS enum " + outerName + "." + nestedName);
            }

            private TypeDefinition FindTopLevel(string name) => _reader.GetTypeDefinition(FindTopLevelHandle(name));

            private TypeDefinitionHandle FindTopLevelHandle(string name)
                => _reader.TypeDefinitions.Single(handle =>
                {
                    var type = _reader.GetTypeDefinition(handle);
                    return type.GetDeclaringType().IsNil && _reader.GetString(type.Name) == name;
                });
        }

        private sealed class TypeNameProvider : ISignatureTypeProvider<string, object?>
        {
            public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
            public string GetByReferenceType(string elementType) => elementType + "&";
            public string GetFunctionPointerType(MethodSignature<string> signature) => "methodptr";
            public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
            public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
            public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
            public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
            public string GetPinnedType(string elementType) => elementType;
            public string GetPointerType(string elementType) => elementType + "*";
            public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
            {
                PrimitiveTypeCode.Boolean => "System.Boolean",
                PrimitiveTypeCode.Byte => "System.Byte",
                PrimitiveTypeCode.Char => "System.Char",
                PrimitiveTypeCode.Double => "System.Double",
                PrimitiveTypeCode.Int16 => "System.Int16",
                PrimitiveTypeCode.Int32 => "System.Int32",
                PrimitiveTypeCode.Int64 => "System.Int64",
                PrimitiveTypeCode.IntPtr => "System.IntPtr",
                PrimitiveTypeCode.Object => "System.Object",
                PrimitiveTypeCode.SByte => "System.SByte",
                PrimitiveTypeCode.Single => "System.Single",
                PrimitiveTypeCode.String => "System.String",
                PrimitiveTypeCode.TypedReference => "System.TypedReference",
                PrimitiveTypeCode.UInt16 => "System.UInt16",
                PrimitiveTypeCode.UInt32 => "System.UInt32",
                PrimitiveTypeCode.UInt64 => "System.UInt64",
                PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
                PrimitiveTypeCode.Void => "System.Void",
                _ => typeCode.ToString()
            };
            public string GetSZArrayType(string elementType) => elementType + "[]";
            public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            {
                var type = reader.GetTypeDefinition(handle);
                var ns = reader.GetString(type.Namespace);
                return string.IsNullOrEmpty(ns) ? reader.GetString(type.Name) : ns + "." + reader.GetString(type.Name);
            }
            public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            {
                var type = reader.GetTypeReference(handle);
                var ns = reader.GetString(type.Namespace);
                return string.IsNullOrEmpty(ns) ? reader.GetString(type.Name) : ns + "." + reader.GetString(type.Name);
            }
            public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
                => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }
    }
}
