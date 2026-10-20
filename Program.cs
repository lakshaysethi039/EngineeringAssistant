using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EngineeringAssistantv0.StartupConstants;

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

    using HttpClient httpClient = new HttpClient();
    try
    {
        bool finalAnswerReceived = false;
        for (int ri = 0; !finalAnswerReceived && ri < StartupConstants.MaxToolCallsAllowed; ri++)
        {
            object requestBody = new
            {
                model = StartupConstants.ModelName,
                messages = chatMessages,
                tools,
                stream = false,
            };

            string requestJson = JsonSerializer.Serialize(requestBody);
            Console.WriteLine($"Following request body is sent to model: \n{requestJson}");
            using StringContent requestContent = new(requestJson, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await httpClient.PostAsync(LocalStartupConstants.ModelBaseUrl + StartupConstants.ModelChatEndpoint, requestContent);
            string responseJson = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Following response came from model:\n{responseJson}");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    $"Ollama returned HTTP {(int)response.StatusCode}: {responseJson}");

                // Remove this user message because Ollama did not answer it.
                chatMessages.RemoveAt(chatMessages.Count - 1);
                break;
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

                    JsonObject? arguments =
                        toolCall?["function"]?["arguments"] as JsonObject;

                    string? fileName =
                        arguments?["fileName"]?.GetValue<string>();

                    Console.WriteLine($"Model requested tool: {toolName}");
                    string toolResult;

                    // Allow only added tools with their possible argument values
                    if (toolName == "read_test_file" && fileName == "sample.txt")
                    {
                        string sandboxDirectory =
                            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "assistant-sandbox"));

                        string allowedFilePath = Path.Combine(sandboxDirectory, "sample.txt");
                        if (File.Exists(allowedFilePath))
                        {
                            string fileContents = await File.ReadAllTextAsync(allowedFilePath);
                            toolResult = $"Contents of sample.txt:\n{fileContents}";
                        }
                        else
                            toolResult = "The permitted sample.txt file was not found in the sandbox folder";
                    }
                    else
                    {
                        toolResult =
                            "Tool request rejected. As of now only read_test_file tool call is permitted with sample.txt as value for its fileName argument";
                    }

                    Console.WriteLine("Tool result returned to the model: " + toolResult);
                    chatMessages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_name"] = toolName,
                        ["content"] = toolResult
                    });
                }
            }

            string? answer = assistantMessage["content"]?.GetValue<string>();
            // If we have got final answer from model
            if (!string.IsNullOrEmpty(answer))
            {
                finalAnswerReceived = true;
                // Preserve the whole message, including tool_calls if present.
                chatMessages.Add((JsonObject)assistantMessage.DeepClone());
                Console.WriteLine();
                Console.WriteLine($"Assistant: {answer}");
                Console.WriteLine();
            }
        }

        if (!finalAnswerReceived)
        {
            Console.WriteLine("Stopped after the tool-round limit or an API error. You can try asking again");
        }
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