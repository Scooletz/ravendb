using System;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.TransactionMerger.Commands;

public sealed class DeleteEtlErrorsTablesForProcessCommand : MergedTransactionCommand<DocumentsOperationContext, DocumentsTransaction>
{
    private readonly string _processName;

    public DeleteEtlErrorsTablesForProcessCommand(string processName)
    {
        _processName = processName;
    }

    protected override long ExecuteCmd(DocumentsOperationContext context)
    {
        context.DocumentDatabase.EtlErrorsStorage.DeleteEtlErrorsTablesForProcess(context, _processName);
        return 1;
    }

    public override IReplayableCommandDto<DocumentsOperationContext, DocumentsTransaction, MergedTransactionCommand<DocumentsOperationContext, DocumentsTransaction>> ToDto(DocumentsOperationContext context)
    {
        throw new NotImplementedException();
    }
}
