using NetArchTest.Rules;

namespace PerfumeShop.ArchitectureTests;

public sealed class ModuleBoundaryTests
{
    public static TheoryData<string> Modules => ModuleAssemblies.Names();

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_does_not_depend_on_other_modules_internals(string moduleName)
    {
        var module = ModuleAssemblies.Get(moduleName);
        var forbidden = ModuleAssemblies.All
            .Where(other => other != module)
            .SelectMany(other => other.InternalTypeNames())
            .ToArray();

        var result = Types.InAssemblies([module.Implementation, module.Contracts])
            .Should()
            .NotHaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{moduleName} uses internals of another module (reference its .Contracts instead): "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Domain_does_not_depend_on_persistence_or_web(string moduleName)
    {
        var module = ModuleAssemblies.Get(moduleName);

        var result = Types.InAssembly(module.Implementation)
            .That()
            .ResideInNamespace($"PerfumeShop.Modules.{moduleName}.Domain")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "Microsoft.AspNetCore",
                "System.Net.Http",
                "PerfumeShop.Shared.Infrastructure",
                $"PerfumeShop.Modules.{moduleName}.Infrastructure",
                $"PerfumeShop.Modules.{moduleName}.Features")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{moduleName}.Domain must stay free of EF Core, HTTP and infrastructure: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>Shipping drives Orders through IOrdersApi; the reverse would create a cycle.</summary>
    [Fact]
    public void Orders_does_not_depend_on_shipping_at_all()
    {
        var orders = ModuleAssemblies.Get("Orders");

        var result = Types.InAssemblies([orders.Implementation, orders.Contracts])
            .ShouldNot()
            .HaveDependencyOn("PerfumeShop.Modules.Shipping")
            .GetResult();

        Assert.True(result.IsSuccessful, "Orders references Shipping: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Contracts_do_not_depend_on_any_module_internals(string moduleName)
    {
        var module = ModuleAssemblies.Get(moduleName);
        var forbidden = ModuleAssemblies.All.SelectMany(m => m.InternalTypeNames()).ToArray();

        var result = Types.InAssembly(module.Contracts)
            .Should()
            .NotHaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{moduleName}.Contracts depends on module internals: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
