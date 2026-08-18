using System.Reflection;
using Microsoft.SemanticKernel;
using VibeDesk.Ai.Plugins;
using Xunit;

namespace VibeDesk.Tests.Assistant;

/// <summary>
/// The shape of the tool schema the model is handed, checked without calling a provider.
/// </summary>
/// <remarks>
/// This exists because of a failure that only showed up against a live model: a parameter declared
/// <c>string?</c> but with no default is <b>required</b> in the generated schema. Semantic Kernel then
/// refuses any call that omits it, and the model has to guess and retry — which is exactly what
/// happened to <c>create_presentation</c>, twice, before the default was added. Nullability reads
/// like optionality but is not it.
/// </remarks>
public class KernelFunctionShapeTests
{
    private static readonly Type[] PluginTypes =
    [
        typeof(WorkspacePlugin),
        typeof(WorkspaceWritePlugin),
        typeof(MathPlugin),
        typeof(TimePlugin),
        typeof(WebPlugin),
    ];

    public static TheoryData<Type, string, string> NullableParameters()
    {
        var data = new TheoryData<Type, string, string>();

        foreach (var type in PluginTypes)
        {
            foreach (var method in KernelMethods(type))
            {
                foreach (var parameter in method.GetParameters())
                {
                    if (parameter.ParameterType == typeof(CancellationToken)) continue;
                    if (!IsNullableReference(parameter)) continue;

                    data.Add(type, method.Name, parameter.Name!);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(NullableParameters))]
    public void ANullableParameterAlsoCarriesADefault(Type plugin, string method, string parameter)
    {
        var target = KernelMethods(plugin).Single(m => m.Name == method);
        var argument = target.GetParameters().Single(p => p.Name == parameter);

        Assert.True(
            argument.HasDefaultValue,
            $"{plugin.Name}.{method}({parameter}) is nullable but has no default, so Semantic Kernel " +
            "marks it required and the model cannot omit it. Add '= null'.");
    }

    [Fact]
    public void EveryKernelFunctionParameterIsDescribed()
    {
        var undescribed = PluginTypes
            .SelectMany(KernelMethods)
            .SelectMany(m => m.GetParameters().Select(p => (Method: m, Parameter: p)))
            .Where(x => x.Parameter.ParameterType != typeof(CancellationToken))
            .Where(x => x.Parameter.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>() is null)
            .Select(x => $"{x.Method.DeclaringType!.Name}.{x.Method.Name}({x.Parameter.Name})")
            .ToList();

        // An undescribed parameter is one the model has to infer from its name alone.
        Assert.Empty(undescribed);
    }

    [Fact]
    public void TheWritePluginOffersNoWayToDeleteAnything()
    {
        var names = KernelMethods(typeof(WorkspaceWritePlugin))
            .Select(m => m.GetCustomAttribute<KernelFunctionAttribute>()!.Name ?? m.Name)
            .ToList();

        Assert.NotEmpty(names);

        foreach (var forbidden in (string[])["delete", "trash", "remove", "purge", "empty"])
        {
            Assert.DoesNotContain(names, n => n.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void TheReadPluginStaysReadOnly()
    {
        var names = KernelMethods(typeof(WorkspacePlugin))
            .Select(m => m.GetCustomAttribute<KernelFunctionAttribute>()!.Name ?? m.Name)
            .ToList();

        // Authoring lives in WorkspaceWritePlugin so a deployment can register the read half alone.
        foreach (var forbidden in (string[])["create", "write", "append", "rename", "move", "replace"])
        {
            Assert.DoesNotContain(names, n => n.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static List<MethodInfo> KernelMethods(Type plugin) =>
        [.. plugin
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<KernelFunctionAttribute>() is not null)];

    /// <summary>
    /// True for a parameter declared as a nullable reference type. Value types are excluded: an
    /// <c>int</c> without a default is genuinely required, and that is fine.
    /// </summary>
    private static bool IsNullableReference(ParameterInfo parameter) =>
        !parameter.ParameterType.IsValueType &&
        new NullabilityInfoContext().Create(parameter).WriteState == NullabilityState.Nullable;
}
