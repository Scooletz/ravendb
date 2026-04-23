using Sparrow.Server;
using Voron;
using Voron.Data.Tables;

namespace Raven.Server.Documents.Schemas;

public static class TaskItemErrors
{
    public static TableSchema Current => TaskItemErrorsSchemaBase;

    public static TableSchema TaskItemErrorsSchemaBase = new();

    public static readonly Slice ByCreatedAt;
    public static readonly Slice ByTaskName;

    public const string TaskItemErrorsTree = "ItemErrors";

    public static class TaskItemErrorsTable
    {
        public const int IdIndex = 0;
        public const int TaskNameIndex = 1;
        public const int CreatedAtIndex = 2;
        public const int DocumentIdIndex = 3;
        public const int StepIndex = 4;
        public const int ErrorIndex = 5;
    }

    static TaskItemErrors()
    {
        using (StorageEnvironment.GetStaticContext(out var ctx))
        {
            Slice.From(ctx, "ByTaskName", ByteStringType.Immutable, out ByTaskName);
            Slice.From(ctx, "ByCreatedAt", ByteStringType.Immutable, out ByCreatedAt);
        }

        TaskItemErrorsSchemaBase.DefineKey(new TableSchema.IndexDef
        {
            StartIndex = TaskItemErrorsTable.IdIndex,
            Count = 1
        });

        TaskItemErrorsSchemaBase.DefineIndex(new TableSchema.IndexDef
        {
            StartIndex = TaskItemErrorsTable.TaskNameIndex,
            Name = ByTaskName
        });

        TaskItemErrorsSchemaBase.DefineIndex(new TableSchema.IndexDef // might be the same ticks, so duplicates are allowed - cannot use fixed size index
        {
            StartIndex = TaskItemErrorsTable.CreatedAtIndex,
            Name = ByCreatedAt
        });
    }
}
