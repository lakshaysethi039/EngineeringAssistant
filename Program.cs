using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

string model = "qwen3:4b-instruct";
List<JsonObject> chatMessages = new List<JsonObject> {
    new JsonObject
    {
        ["role"] = "system",
        ["content"] = """
            You are an engineering assistant helping with a C#/.NET project.
            Explain technical concepts clearly.
            If a request is ambiguous, ask a clarifying question.
            Do not claim to have inspected, changed, built, or tested project files
            unless the application has actually provided and performed that capability.
        """
    }
};

object tools = new[]
{
    new
    {
        type = "function",
        function = new
        {
            name = "read_test_file",
            description =
                "Request reading the sample.txt file from the assistant-sandbox folder. " +
                "This tool is only for that sample file.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    fileName = new
                    {
                        type = "string",
                        description = "The name of the sample file. Use sample.txt."
                    }
                },
                required = new[] { "fileName" }
            }
        }
    }
};

Console.WriteLine("Engineering Assitant");
Console.WriteLine("Type in /exit to end this program.");

while (true)
{
    Console.Write("Prompt: ");
    string prompt = Console.ReadLine() ?? "";
    if (prompt.Equals("/exit", StringComparison.OrdinalIgnoreCase))
        return;

    if (string.IsNullOrEmpty(prompt))
    {
        Console.WriteLine("Please provide a non-empty prompt");
        continue;
    }
    chatMessages.Add(new JsonObject
    {
        ["role"] = "user",
        ["content"] = prompt
    }
    );

    string modelUrl = "http://127.0.0.1:11434/api/chat";

    using HttpClient httpClient = new HttpClient();
    object requestBody = new
    {
        model,
        messages = chatMessages,
        tools,
        stream = false,
    };

    string requestJson = JsonSerializer.Serialize(requestBody);

    using StringContent requestContent = new (requestJson, Encoding.UTF8, "application/json");
    try
    {
        using HttpResponseMessage response = await httpClient.PostAsync(modelUrl, requestContent);
        string responseJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine(
                $"Ollama returned HTTP {(int)response.StatusCode}: {responseJson}");

            // Remove this user message because Ollama did not answer it.
            chatMessages.RemoveAt(chatMessages.Count - 1);
            continue;
        }

        using JsonDocument document = JsonDocument.Parse(responseJson);
        JsonElement messageElement = document.RootElement.GetProperty("message");

        // Store the assistant's answer too, so it becomes context next turn.
        JsonObject assistantMessage = JsonNode.Parse(messageElement.GetRawText())!.AsObject();

        if (assistantMessage["tool_calls"] is JsonArray toolCalls && toolCalls.Count > 0)
        {
            foreach (JsonNode? toolCall in toolCalls)
            {
                string toolName =
                    toolCall?["function"]?["name"]?.GetValue<string>() ?? "";

                string arguments =
                    toolCall?["function"]?["arguments"]?.ToJsonString() ?? "{}";

                Console.WriteLine($"Tool: {toolName}");
                Console.WriteLine($"Arguments: {arguments}");
            }
        }

        // Preserve the whole message, including tool_calls if present.
        chatMessages.Add((JsonObject)assistantMessage.DeepClone());
        Console.WriteLine();
        Console.WriteLine($"Assistant: {assistantMessage}");
        Console.WriteLine();
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine("Could not connect to Ollama.");
        Console.WriteLine(ex.Message);

        // Remove the unanswered user message so the history stays consistent.
        chatMessages.RemoveAt(chatMessages.Count - 1);
    }
    catch (JsonException ex)
    {
        Console.WriteLine("Could not parse Ollama's response.");
        Console.WriteLine(ex.Message);

        // The model response wasn't successfully added to history.
        chatMessages.RemoveAt(chatMessages.Count - 1);
    }
}