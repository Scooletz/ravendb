using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Raven.Client.Documents.Operations.AI;
using Raven.Server.Documents.ETL.Providers.AI;
using Raven.Server.Documents.ETL.Providers.AI.Extensions;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Services;

namespace Raven.Server.Documents.AI;

public static class AiHelper
{
    [Experimental("SKEXP0001")]
    public static ITextEmbeddingGenerationService CreateEmbeddingService(AiConnectionString connectionString)
    {
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Configure(connectionString, withLogging: false);
        var kernel = kernelBuilder.Build();
        return kernel.GetRequiredService<ITextEmbeddingGenerationService>();
    }

    [Experimental("SKEXP0001")]
    public static (ITextEmbeddingGenerationService, InMemoryLoggerProvider) CreateEmbeddingServicesForTest(EmbeddingsGenerationConfiguration configuration)
        => CreateAiServicesForTest<ITextEmbeddingGenerationService, EmbeddingsGenerationConfiguration>(configuration);


    public static (IChatCompletionService, InMemoryLoggerProvider) CreateChatCompletionServicesForTest(GenAiConfiguration configuration)
        => CreateAiServicesForTest<IChatCompletionService, GenAiConfiguration>(configuration);


    private static (TService, InMemoryLoggerProvider) CreateAiServicesForTest<TService, TConfig>(TConfig configuration)
        where TConfig : AbstractAiIntegrationConfiguration
        where TService : class, IAIService
    {
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Configure(configuration, withLogging: true);
        var kernel = kernelBuilder.Build();

        var llmService = kernel.GetRequiredService<TService>();
        var logger = (InMemoryLoggerProvider)kernel.GetRequiredService<ILoggerProvider>();

        return (llmService, logger);
    }
}
