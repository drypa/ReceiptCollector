using System.Reflection;
using NetArchTest.Rules;
using ReceiptCollector.Analytics.Api.Modules.Receipts;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;
using ReceiptCollector.Analytics.Infrastructure.Configuration;

namespace ReceiptCollector.Analytics.Api.Tests.Architecture;

public class ProjectDependencyTests
{
    public static IEnumerable<object[]> ProjectRules() => GetRules().Select(rule => new object[] { rule.Namespace });

    [Theory]
    [MemberData(nameof(ProjectRules))]
    public void Projects_should_not_reference_unexpected_dependencies(string projectNamespace)
    {
        var rules = GetRules().ToArray();
        var rule = rules.Single(r => r.Namespace == projectNamespace);
        var allowed = rule.AllowedNamespaces ?? Array.Empty<string>();
        var allowedSet = new HashSet<string>(allowed) { rule.Namespace };
        var disallowedNamespaces = new List<string>();
        foreach (var candidate in rules)
        {
            if (!allowedSet.Contains(candidate.Namespace))
            {
                disallowedNamespaces.Add(candidate.Namespace);
            }
        }

        var disallowed = disallowedNamespaces.ToArray();

        var result = Types.InAssembly(rule.Assembly)
            .That().ResideInNamespaceStartingWith(rule.Namespace)
            .Should().NotHaveDependencyOnAny(disallowed)
            .GetResult();

        var failingTypes = result.FailingTypes?.Select(t => t.FullName) ?? Array.Empty<string>();
        Assert.True(result.IsSuccessful,
            $"Project {rule.Namespace} has unexpected dependencies. Types violating rule: {string.Join(", ", failingTypes)}");
    }


    private static IReadOnlyCollection<ProjectRule> GetRules() =>
    [
        new(typeof(Receipt).Assembly, "ReceiptCollector.Analytics.Domain", Array.Empty<string>()),
        new(typeof(IReceiptReadService).Assembly, "ReceiptCollector.Analytics.Application",
            ["ReceiptCollector.Analytics.Domain"]),
        new(typeof(DependencyInjectionExtensions).Assembly, "ReceiptCollector.Analytics.Infrastructure",
            ["ReceiptCollector.Analytics.Application", "ReceiptCollector.Analytics.Domain"]),
        new(typeof(ReceiptEndpoints).Assembly, "ReceiptCollector.Analytics.Api",
            ["ReceiptCollector.Analytics.Application", "ReceiptCollector.Analytics.Infrastructure"])
    ];

    private sealed record ProjectRule(
        Assembly Assembly,
        string Namespace,
        IReadOnlyCollection<string> AllowedNamespaces);
}