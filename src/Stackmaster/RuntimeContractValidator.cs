#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Stackmaster
{
    /// <summary>
    /// Exact reflection checks used by the runtime compatibility gate and the
    /// Harmony patch-plan preflight. Identity (version, hash, or MVID) is not a
    /// compatibility contract: only the members Stackmaster actually consumes are.
    /// </summary>
    internal static class RuntimeContractValidator
    {
        private const BindingFlags AllMembers = BindingFlags.Public | BindingFlags.NonPublic |
                                                BindingFlags.Instance | BindingFlags.Static;
        private const BindingFlags DeclaredMembers = AllMembers | BindingFlags.DeclaredOnly;

        internal static void RequireType(ICollection<string> failures, Type type)
        {
            if (type == null || string.IsNullOrEmpty(type.FullName))
            {
                failures.Add("required runtime type missing");
            }
        }

        internal static void RequireMethod(
            ICollection<string> failures,
            Type type,
            string name,
            bool mustBeStatic,
            bool declaredOnly,
            params Type[] parameters)
        {
            var matches = FindMethods(type, name, mustBeStatic, declaredOnly, parameters);
            if (matches.Length == 1) return;

            failures.Add(Describe(type, name, parameters) +
                         (matches.Length == 0 ? " missing" : " ambiguous (" + matches.Length + " exact matches)"));
        }

        internal static MethodInfo ResolveExactMethod(
            Type type,
            string name,
            bool mustBeStatic,
            bool declaredOnly,
            params Type[] parameters)
        {
            var matches = FindMethods(type, name, mustBeStatic, declaredOnly, parameters);
            if (matches.Length == 0) throw new MissingMethodException(type.FullName, Describe(type, name, parameters));
            if (matches.Length > 1) throw new AmbiguousMatchException(Describe(type, name, parameters));
            return matches[0];
        }

        internal static MethodInfo ResolveUniqueNamedMethod(Type type, string name, bool mustBeStatic)
        {
            var matches = type.GetMethods(DeclaredMembers)
                .Where(method => method.Name == name && method.IsStatic == mustBeStatic)
                .ToArray();
            if (matches.Length == 0) throw new MissingMethodException(type.FullName, name);
            if (matches.Length > 1) throw new AmbiguousMatchException(type.Name + "." + name);
            return matches[0];
        }

        internal static void RequireConstructor(ICollection<string> failures, Type type, params Type[] parameters)
        {
            var matches = type.GetConstructors(DeclaredMembers)
                .Where(constructor => ParametersEqual(constructor.GetParameters(), parameters))
                .ToArray();
            if (matches.Length == 1) return;

            failures.Add(Describe(type, ".ctor", parameters) +
                         (matches.Length == 0 ? " missing" : " ambiguous (" + matches.Length + " exact matches)"));
        }

        internal static void RequireField(ICollection<string> failures, Type type, string name)
        {
            var matches = type.GetFields(AllMembers).Where(field => field.Name == name).ToArray();
            if (matches.Length == 1) return;

            failures.Add(type.Name + "." + name +
                         (matches.Length == 0 ? " missing" : " ambiguous (" + matches.Length + " matches)"));
        }

        internal static void RequireProperty(ICollection<string> failures, Type type, string name)
        {
            var matches = type.GetProperties(AllMembers).Where(property => property.Name == name).ToArray();
            if (matches.Length == 1) return;

            failures.Add(type.Name + "." + name +
                         (matches.Length == 0 ? " missing" : " ambiguous (" + matches.Length + " matches)"));
        }

        private static MethodInfo[] FindMethods(
            Type type,
            string name,
            bool mustBeStatic,
            bool declaredOnly,
            Type[] parameters)
        {
            var flags = declaredOnly ? DeclaredMembers : AllMembers;
            return type.GetMethods(flags)
                .Where(method => method.Name == name)
                .Where(method => method.IsStatic == mustBeStatic)
                .Where(method => ParametersEqual(method.GetParameters(), parameters))
                .ToArray();
        }

        private static bool ParametersEqual(ParameterInfo[] observed, Type[] expected)
        {
            if (observed.Length != expected.Length) return false;
            for (var index = 0; index < observed.Length; index++)
            {
                if (observed[index].ParameterType != expected[index]) return false;
            }
            return true;
        }

        private static string Describe(Type type, string name, Type[] parameters)
        {
            return type.Name + "." + name + "(" +
                   string.Join(",", parameters.Select(parameter => parameter.Name).ToArray()) + ")";
        }
    }
}
