using System;
using Raven.Server.Documents.ETL;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.TransactionMerger.Commands;

public sealed class DeleteEtlErrorsTablesForProcessCommand : MergedTransactionCommand<DocumentsOperationContext, DocumentsTransaction>
{
    private readonly string _processErrorsTableName;
    private readonly string _itemErrorsTableName;

    public DeleteEtlErrorsTablesForProcessCommand(string processErrorsTableName, string itemErrorsTableName)
    {
        _processErrorsTableName = processErrorsTableName;
        _itemErrorsTableName = itemErrorsTableName;
    }

    protected override long ExecuteCmd(DocumentsOperationContext context)
    {
        EtlErrorsStorage.DeleteEtlErrorsTablesForProcess(context, _processErrorsTableName, _itemErrorsTableName);
        return 1;
    }

    public override IReplayableCommandDto<DocumentsOperationContext, DocumentsTransaction, MergedTransactionCommand<DocumentsOperationContext, DocumentsTransaction>> ToDto(DocumentsOperationContext context)
    {
        throw new NotImplementedException();
    }
}
