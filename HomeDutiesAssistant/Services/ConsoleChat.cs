using System.Text;
using Spectre.Console;

namespace HomeDutiesAssistant.Services;

// The CLI front-end for the RAG core. Owns everything transport-specific:
// the interactive prompt loop, Spectre rendering, the retrieval spinner, and
// the short-term conversation history. RagChatService stays unaware of all of
// this, so the same core can sit behind an HTTP API or any other interface.
public sealed class ConsoleChat(RagChatService chat)
{
    public async Task RunAsync(long homeId, CancellationToken ct = default)
    {
        AnsiConsole.Write(new Rule("[bold yellow]🏠 Home Duties Assistant[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.MarkupLine("[grey]Ask about bills, due dates, amounts, sewer emptying, etc. Type [yellow]exit[/] to quit.[/]");
        AnsiConsole.WriteLine();

        var history = new List<ChatMessage>();

        while (!ct.IsCancellationRequested)
        {
            var question = ReadQuestion();

            if (question is null) break;
            if (string.IsNullOrWhiteSpace(question)) continue;
            if (question.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

            // Retrieval runs under a spinner; AskAsync returns once it's done.
            RagAnswer response = null!;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("[grey]Searching your home records...[/]", async _ =>
                {
                    response = await chat.AskAsync(question, history, homeId, ct);
                });

            // Stream the answer. Tokens are written raw (no markup parsing) so
            // characters like '[' in the answer are not mistaken for markup.
            AnsiConsole.Markup("[blue]Assistant[/] [grey]>[/] ");
            var answer = new StringBuilder();
            await foreach (var token in response.Tokens.WithCancellation(ct))
            {
                Console.Write(token);
                answer.Append(token);
            }
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine();

            history.Add(new("user", question));
            history.Add(new("assistant", answer.ToString()));
        }
    }

    // Read one question. Spectre's interactive prompt only works against a real
    // terminal, so when stdin is redirected (a pipe, a here-doc, a test) we fall
    // back to plain ReadLine — which also returns null at end of stream so the
    // loop can stop cleanly.
    private static string? ReadQuestion()
    {
        if (Console.IsInputRedirected)
        {
            AnsiConsole.Markup("[green]You[/] [grey]>[/] ");
            return Console.ReadLine();
        }

        return AnsiConsole.Prompt(
            new TextPrompt<string>("[green]You[/] [grey]>[/]")
                .PromptStyle("white")
                .AllowEmpty());
    }
}