using System.Diagnostics;
using ClaimsIntake.Core.Agents.Interfaces;
using ClaimsIntake.Core.Agents.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ClaimsIntake.Core.Agents;

public class SingleAgentAssistant : IClaimAssistant
{
    private static readonly ActivitySource ActivitySource = new("ClaimsIntake");

    private readonly ChatClientAgent _agent;
    private readonly AgentSession _session;
    private readonly string _agentName;
    private int _turnIndex;

    public SingleAgentAssistant(ChatClientAgent agent, AgentSession session, string agentName)
    {
        _agent = agent;
        _session = session;
        _agentName = agentName;
    }

    public async Task<AssistantReplyModel> AskAsync(string question, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("claim.assistant.ask");
        activity?.SetTag("agent.name", _agentName);
        activity?.SetTag("assistant.turn_index", _turnIndex++);

        var response = await _agent.RunAsync(
            question, _session, cancellationToken: ct);

        var toolCalls = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionCallContent>()
            .ToList();

        var toolsUsed = toolCalls
            .Select(c => c.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct()
            .ToList();

        activity?.SetTag("assistant.tool_calls", toolCalls.Count);
        activity?.SetTag("assistant.tools_used", string.Join(",", toolsUsed));

        return new AssistantReplyModel
        {
            Text = response.Text ?? "",
            ToolsUsed = toolsUsed!,
            AgentName = _agentName
        };
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
