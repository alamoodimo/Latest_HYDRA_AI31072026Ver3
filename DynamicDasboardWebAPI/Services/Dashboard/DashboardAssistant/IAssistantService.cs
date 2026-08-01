using DynamicDashboardCommon.Models;
using System.Threading.Tasks;

namespace DynamicDasboardWebAPI.Services
{
    /// <summary>
    /// Interface for AI assistant services
    /// </summary>
    public interface IAssistantService
    {
        /// <summary>
        /// Generates smart suggestions for dashboard improvements
        /// </summary>
        /// <param name="request">Request containing dashboard info</param>
        /// <returns>List of component suggestions</returns>
        Task<AssistantSuggestionResponse> GenerateSuggestionsAsync(AssistantChatRequest request);

        /// <summary>
        /// Regenerates a single failed component with new title, SQL, and optionally different type
        /// </summary>
        /// <param name="request">Request containing failed component info</param>
        /// <returns>Single new component suggestion</returns>
        Task<AssistantSuggestionResponse> RegenerateComponentAsync(RegenerateComponentRequest request);
    }
}