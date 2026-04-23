namespace Raven.Server.Documents.ETL;

public class TaskProcessErrorTableValue : TaskErrorTableValueBase
{
    public long AffectedDocumentsCount;

    protected override string GetId()
    {
        return $"{TaskName}/{CreatedAt.Ticks}";
    }

    public TaskProcessError ToTaskProcessError()
    {
        return new TaskProcessError
        {
            CreatedAt = CreatedAt,
            TaskName = TaskName,
            AffectedDocumentsCount = AffectedDocumentsCount,
            Step = (TaskErrorStep)Step,
            Error = Error
        };
    }
}
