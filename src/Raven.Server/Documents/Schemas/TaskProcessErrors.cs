using Sparrow.Server;
using Voron;
using Voron.Data.Tables;

namespace Raven.Server.Documents.Schemas;

public static class TaskProcessErrors
{
    public static TableSchema Current => TaskProcessErrorsSchemaBase;

    public static TableSchema TaskProcessErrorsSchemaBase = new();

    public static readonly Slice ByCreatedAt;
    public static readonly Slice ByTaskName;

    public const string TaskProcessErrorsTree = "ProcessErrors";

    public static class TaskProcessErrorsTable
    {
        public const int TaskNameIndex = 0;
        public const int CreatedAtIndex = 1;
        public const int AffectedDocumentsCountIndex = 2;
        public const int StepIndex = 3;
        public const int ErrorIndex = 4;
    }

    static TaskProcessErrors()
    {
        using (StorageEnvironment.GetStaticContext(out var ctx))
        {
            Slice.From(ctx, "ByTaskName", ByteStringType.Immutable, out ByTaskName);
            Slice.From(ctx, "ByCreatedAt", ByteStringType.Immutable, out ByCreatedAt);
        }

        TaskProcessErrorsSchemaBase.DefineIndex(new TableSchema.IndexDef
        {
            StartIndex = TaskProcessErrorsTable.TaskNameIndex,
            Name = ByTaskName
        });

        TaskProcessErrorsSchemaBase.DefineIndex(new TableSchema.IndexDef // might be the same ticks, so duplicates are allowed - cannot use fixed size index
        {
            StartIndex = TaskProcessErrorsTable.CreatedAtIndex,
            Name = ByCreatedAt
        });
    }
}
