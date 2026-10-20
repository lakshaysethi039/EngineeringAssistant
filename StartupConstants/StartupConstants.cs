using System;
using System.Collections.Generic;
using System.Text;

namespace EngineeringAssistantv0.StartupConstants
{
    public class StartupConstants
    {
        /// <summary>
        /// Route for sending messages to model
        /// </summary>
        public const string ModelChatEndpoint = "/api/chat";

        /// <summary>
        /// Model name to query to
        /// </summary>
        public const string ModelName = "qwen3:4b-instruct";

        /// <summary>
        /// Maximum number of tool calls allowed by Model
        /// </summary>
        public const int MaxToolCallsAllowed = 3;
    }
}
