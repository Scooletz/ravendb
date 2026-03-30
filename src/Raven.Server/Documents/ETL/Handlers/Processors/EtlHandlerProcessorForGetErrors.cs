using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Raven.Client.Documents.Operations.ETL;
using Raven.Server.Documents.ETL.Stats;
using Raven.Server.Documents.Sharding;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Raven.Server.Web.Http;
using Sparrow.Json;

namespace Raven.Server.Documents.ETL.Handlers.Processors;

internal sealed class EtlHandlerProcessorForGetErrors : AbstractEtlHandlerProcessorForGetErrors<DatabaseRequestHandler, DocumentsOperationContext>
{
    public EtlHandlerProcessorForGetErrors([NotNull] DatabaseRequestHandler requestHandler) : base(requestHandler)
    {
    }
    
    protected override bool SupportsCurrentNode => true;
    
    protected override async ValueTask HandleCurrentNodeAsync()
    {
        var response = new Response
        {
            NodeTag = RequestHandler.ServerStore.NodeTag
        };
        
        if (RequestHandler.Database is ShardedDocumentDatabase shardedDatabase)
            response.ShardNumber = shardedDatabase.ShardNumber;
        
        var storage = RequestHandler.Database.EtlErrorsStorage;
        var processNames = GetNames().ToList();
        
        if (processNames.Count == 0)
            processNames = GetAllEtlProcessNamesFromDatabaseRecord();
        
        foreach (var etlProcessName in processNames)
        {
            var processErrors = storage.ReadProcessErrorsOfEtl(etlProcessName);
            var itemErrors = storage.ReadItemErrorsOfEtl(etlProcessName);

            var etlProcessErrors = new EtlErrors()
            {
                ProcessName = etlProcessName,
                ProcessErrors = processErrors.Select(x => x.ToEtlProcessError()).ToArray(),
                ItemErrors = itemErrors.Select(x => x.ToEtlItemError()).ToArray()
            };
                
            response.Results.Add(etlProcessErrors);
        }

        using (ContextPool.AllocateOperationContext(out JsonOperationContext context))
        {
            await using (var writer = new AsyncBlittableJsonTextWriter(context, RequestHandler.ResponseBodyStream()))
            {
                writer.WriteStartObject();
                
                writer.WritePropertyName(nameof(Response.NodeTag));
                writer.WriteString(response.NodeTag);
                writer.WriteComma();

                writer.WritePropertyName(nameof(response.ShardNumber));
                if (response.ShardNumber != null)
                    writer.WriteInteger(response.ShardNumber.Value);
                else
                    writer.WriteNull();
                writer.WriteComma();
                
                writer.WriteArray(context, nameof(Response.Results), response.Results, (w, c, errors) => w.WriteObject(c.ReadObject(errors.ToJson(), "etl/errors")));
                writer.WriteEndObject();
            }
        }
    }

    private List<string> GetAllEtlProcessNamesFromDatabaseRecord()
    {
        var record = RequestHandler.Database.ReadDatabaseRecord();

        var processNames = new List<string>();

        foreach (var config in record.RavenEtls)
            AddProcessNames(config.Name, config.Transforms);
        foreach (var config in record.SqlEtls)
            AddProcessNames(config.Name, config.Transforms);
        foreach (var config in record.OlapEtls)
            AddProcessNames(config.Name, config.Transforms);
        foreach (var config in record.ElasticSearchEtls)
            AddProcessNames(config.Name, config.Transforms);
        foreach (var config in record.QueueEtls)
            AddProcessNames(config.Name, config.Transforms);
        foreach (var config in record.SnowflakeEtls)
            AddProcessNames(config.Name, config.Transforms);
        foreach (var config in record.EmbeddingsGenerations)
            AddProcessNames(config.Name, config.Transforms);
        foreach (var config in record.GenAis)
            AddProcessNames(config.Name, config.Transforms);

        return processNames;

        void AddProcessNames(string configName, List<Transformation> transforms)
        {
            foreach (var transform in transforms)
            {
                processNames.Add(EtlProcess.GetProcessName(configName, transform.Name));
            }
        }
    }

    protected override Task HandleRemoteNodeAsync(ProxyCommand<EtlErrors[]> command, OperationCancelToken token) => RequestHandler.ExecuteRemoteAsync(command, token.Token);

    internal class Response
    {
        public string NodeTag { get; set; }
        public int? ShardNumber { get; set; }
        public List<EtlErrors> Results { get; set; } = new List<EtlErrors>();        
    }
}

