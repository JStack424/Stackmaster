using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace Stackmaster.Compatibility.Tests
{
    internal static class Program
    {
        private const string ExpectedSha256 = "e5af0669755ed3b098f71b4dd0753f8a997761b99bca1e8dac3d5ca4c706a0be";
        private static readonly Guid ExpectedMvid = new Guid("a63433e8-968e-407a-918a-9f9fe7e7ba9a");
        private static int _passed;

        private static int Main(string[] args)
        {
            if (args.Length != 1) throw new ArgumentException("Expected path to assembly_valheim.dll.");
            var path = Path.GetFullPath(args[0]);
            Equal(ExpectedSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), "assembly SHA-256");

            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var reader = pe.GetMetadataReader();
            Equal(ExpectedMvid, reader.GetGuid(reader.GetModuleDefinition().Mvid), "assembly MVID");
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
            contract.CurrentGameVersion(1, 0, 14);

            Console.WriteLine(_passed + " compatibility contract checks passed");
            return 0;
        }
        private static void Equal<T>(T expected, T actual, string label) where T : notnull
        {
            if (!expected.Equals(actual)) throw new InvalidOperationException(label + " mismatch: " + actual);
            _passed++;
            Console.WriteLine("PASS " + label);
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

            internal void CurrentGameVersion(int major, int minor, int patch)
            {
                if (major < 0 || major > 8 || minor < 0 || minor > 8 || patch < sbyte.MinValue || patch > sbyte.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(patch), "Version contract helper requires compact integer opcodes.");

                var initializer = FindMethodHandle("Version", ".cctor", "System.Void", Array.Empty<string>());
                var constructor = FindMethodHandle("GameVersion", ".ctor", "System.Void",
                    new[] { "System.Int32", "System.Int32", "System.Int32" });
                var currentVersion = FindFieldHandle("Version", "<CurrentVersion>k__BackingField", "GameVersion");
                var il = MethodIl(initializer);
                var pattern = new List<byte>();
                AppendCompactInteger(pattern, major);
                AppendCompactInteger(pattern, minor);
                AppendCompactInteger(pattern, patch);
                pattern.Add(0x73); // newobj
                pattern.AddRange(BitConverter.GetBytes(MetadataTokens.GetToken(constructor)));
                pattern.Add(0x80); // stsfld
                pattern.AddRange(BitConverter.GetBytes(MetadataTokens.GetToken(currentVersion)));

                var matches = 0;
                for (var index = 0; index <= il.Length - pattern.Count; index++)
                {
                    if (il.AsSpan(index, pattern.Count).SequenceEqual(pattern.ToArray())) matches++;
                }
                if (matches != 1)
                    throw new InvalidOperationException("Version.CurrentVersion initializer mismatch: " + matches);
                _passed++;
                Console.WriteLine("PASS IL Version.CurrentVersion = " + major + "." + minor + "." + patch);
            }

            private static void AppendCompactInteger(ICollection<byte> output, int value)
            {
                if (value >= 0 && value <= 8)
                {
                    output.Add((byte)(0x16 + value)); // ldc.i4.0 through ldc.i4.8
                    return;
                }
                output.Add(0x1f); // ldc.i4.s
                output.Add(unchecked((byte)(sbyte)value));
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
