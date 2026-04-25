namespace Raven.Server.Documents.ETL;

public class TaskItemErrorTableValue : TaskErrorTableValueBase
{
    public string DocumentId;

    protected override string GetId()
    {
        return $"{TaskName}/{DocumentId}";
    }

    public TaskItemError ToTaskItemError()
    {
        return new TaskItemError
        {
            Id = Id,
            CreatedAt = CreatedAt,
            TaskName = TaskName,
            DocumentId = DocumentId,
            Step = (TaskErrorStep)Step,
            Error = Error
        };
    }
}
