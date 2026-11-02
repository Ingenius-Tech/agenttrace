using AgentTrace.Core;
using AgentTrace.Testing;

namespace AgentTrace.Adapters.ClaudeCode.Tests;

/// <summary>Runs the 8 cases of the <see cref="IAgentAdapter"/> contract with a temporary projects folder.</summary>
public sealed class ClaudeCodeAdapterContractTest : AgentAdapterContractTest, IDisposable
{
    private const string SessionFile = "aaaaaaaa-0000-0000-0000-000000000001.jsonl";

    private readonly string _tmp = Directory.CreateTempSubdirectory("agenttrace-claude-contract-").FullName;

    private FilePath Root() => new FilePath(_tmp).Combine("projects");

    protected override IAgentAdapter Adapter() => new ClaudeCodeAdapter(Root(), false);

    protected override FilePath ProjectWithTraces()
    {
        FilePath project = new FilePath(_tmp).Combine("project");
        Directory.CreateDirectory(project.Value);
        Directory.CreateDirectory(Root().Combine(ClaudeProjectsLocator.Encode(project.Value)).Value);
        return project;
    }

    protected override FilePath SampleTrace()
    {
        FilePath folder = Root().Combine(ClaudeProjectsLocator.Encode(ProjectWithTraces().Value));
        FilePath target = folder.Combine(SessionFile);
        if (!File.Exists(target.Value)) File.Copy(TestSupport.Resource("claude/projects/D--fake-proj/" + SessionFile).Value, target.Value);
        return target;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmp, recursive: true);
        }
        catch (IOException)
        {
            // a temporary folder that cannot be deleted does not invalidate the test
        }
    }
}
