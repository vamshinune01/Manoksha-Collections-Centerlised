using System.Reflection;
using Manoksha.Hosting;
using NetArchTest.Rules;

namespace Manoksha.ArchitectureTests;

/// <summary>
/// Enforces the modular-monolith boundaries (design §3): modules talk to each other only through their
/// Contracts namespace; building blocks never depend on modules.
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly Assembly[] ModuleAssemblies = ModuleCatalog.All.Select(m => m.GetType().Assembly).Distinct().ToArray();

    private static readonly string[] InternalLayers = ["Domain", "Application", "Infrastructure", "Persistence", "Endpoints"];

    public static TheoryData<string> ModuleNames()
    {
        var data = new TheoryData<string>();
        foreach (var a in ModuleAssemblies)
        {
            data.Add(a.GetName().Name!);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_does_not_depend_on_other_modules_internals(string moduleAssembly)
    {
        var assembly = ModuleAssemblies.Single(a => a.GetName().Name == moduleAssembly);
        var forbidden = ModuleAssemblies
            .Where(a => a != assembly)
            .SelectMany(a => InternalLayers.Select(layer => $"{a.GetName().Name}.{layer}"))
            .ToArray();

        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

        result.IsSuccessful.Should().BeTrue($"{moduleAssembly} must only use other modules' Contracts. Offenders: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Building_blocks_do_not_depend_on_modules()
    {
        var buildingBlocks = new[]
        {
            typeof(Manoksha.SharedKernel.Money).Assembly,
            typeof(Manoksha.Application.Modules.IModule).Assembly,
            typeof(Manoksha.Persistence.ManokshaDbContext).Assembly,
        };
        foreach (var assembly in buildingBlocks)
        {
            var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny("Manoksha.Modules").GetResult();
            result.IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} must not reference modules: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    [Fact]
    public void Shared_kernel_has_no_framework_dependencies()
    {
        var result = Types.InAssembly(typeof(Manoksha.SharedKernel.Money).Assembly)
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Domain_layers_do_not_depend_on_http_or_endpoints()
    {
        foreach (var assembly in ModuleAssemblies)
        {
            var name = assembly.GetName().Name!;
            var result = Types.InAssembly(assembly).That().ResideInNamespace($"{name}.Domain")
                .ShouldNot().HaveDependencyOnAny("Microsoft.AspNetCore", $"{name}.Endpoints", "Microsoft.EntityFrameworkCore")
                .GetResult();
            result.IsSuccessful.Should().BeTrue($"{name}.Domain must be persistence- and transport-agnostic: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    [Fact]
    public void Every_module_is_registered_in_the_catalog() =>
        ModuleCatalog.All.Select(m => m.Name).Should().OnlyHaveUniqueItems();
}
