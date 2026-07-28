// TID-3b: Plan test runs for `titi tests record` (CLI-22).
//
// Pure projection from the project set to per-test-project run plans. Each
// test project gets a unique results directory (so concurrent/sequential runs
// don't clobber each other) and the dotnet-test argument string that enables
// both the TRX logger and XPlat Cobertura coverage collection.

namespace titi;

public record TestRunPlan(string ProjectPath, string ResultsDir, string Arguments);

public static class RecordPlanner
{
    /// <summary>
    /// Build a run plan for every test project (IsTestProject = true) in
    /// <paramref name="projects"/>. Non-test projects are skipped. Each plan
    /// targets a unique results directory under <paramref name="resultsRoot"/>.
    /// </summary>
    public static TestRunPlan[] PlanTestRuns(IEnumerable<ProjectDescriptor> projects, string resultsRoot)
    {
        var testProjects = projects.Where(p => p.IsTestProject).ToArray();
        if (testProjects.Length == 0)
            return [];

        // Pure: compute paths only. The caller owns directory creation so this
        // stays testable with in-memory/fake paths.
        var plans = new List<TestRunPlan>(testProjects.Length);
        foreach (var p in testProjects)
        {
            var resultsDir = Path.Combine(resultsRoot, Guid.NewGuid().ToString("N"));
            var args = $"test \"{p.Path}\" --collect \"XPlat Code Coverage\" --logger trx --results-directory \"{resultsDir}\"";
            plans.Add(new TestRunPlan(p.Path, resultsDir, args));
        }
        return plans.ToArray();
    }
}
