import React from "react";
import classNames from "classnames";
import { Icon } from "components/common/Icon";
import Badge from "react-bootstrap/Badge";
import { EtlTaskWithErrors, getHealthStatusFromStats, healthStatusToBadge } from "../utils/tasksErrorsUtils";
import { ThemeColor } from "components/models/common";
import EtlTaskStats = Raven.Server.Documents.ETL.Stats.EtlTaskStats;

export interface TaskPillProps {
    color: `bg-${ThemeColor}`;
}

export function TaskPill({ color }: TaskPillProps) {
    return <div className={classNames("tasks-pill rounded", color)} />;
}

interface TaskPillGroupMessageProps {
    etlTaskStatsList: EtlTaskStats[];
    tasksWithErrors: EtlTaskWithErrors[];
}

export function TaskPillGroupMessage({ etlTaskStatsList, tasksWithErrors }: TaskPillGroupMessageProps) {
    const overallHealth = getHealthStatusFromStats(etlTaskStatsList[0].Stats);
    const { bg, icon, label } = healthStatusToBadge(overallHealth);

    const rows = etlTaskStatsList.map((etlTaskStats) => {
        const taskWithErrors = tasksWithErrors.find((t) => t.etlName === etlTaskStats.TaskName);
        const matchesLocation = (e: { nodeTag: string; shardNumber?: number }) =>
            e.nodeTag === etlTaskStats.NodeTag && e.shardNumber === etlTaskStats.ShardNumber;
        const errorCount = taskWithErrors
            ? taskWithErrors.transformations.reduce(
                  (acc, t) =>
                      acc +
                      t.itemErrors.filter(matchesLocation).length +
                      t.processErrors.filter(matchesLocation).length,
                  0
              )
            : 0;

        return {
            taskName: etlTaskStats.TaskName,
            errorCount,
            nodeTag: etlTaskStats.NodeTag,
            shardNumber: etlTaskStats.ShardNumber,
        };
    });

    return (
        <div className="task-pill-message">
            <div className="d-flex align-items-center gap-2 mb-2">
                <div>
                    <b className="flex-grow text-nowrap">{etlTaskStatsList.length}</b>{" "}
                    {etlTaskStatsList.length === 1 ? "task" : "tasks"}
                </div>
                <Badge bg={bg} className="rounded-pill">
                    <Icon icon={icon} />
                    {label}
                </Badge>
            </div>
            <div className="d-flex flex-column gap-1">
                {rows.map((row, index) => (
                    <TaskPillRow key={row.taskName + index} {...row} />
                ))}
            </div>
        </div>
    );
}

interface TaskPillRowProps extends databaseLocationSpecifier {
    taskName: string;
    errorCount: number;
}

function TaskPillRow({ nodeTag, shardNumber, taskName, errorCount }: TaskPillRowProps) {
    return (
        <div className="d-flex align-items-center gap-2">
            <div className="d-flex flex-grow-1 gap-1">
                <span className="text-truncate small">{taskName}</span>
                {nodeTag && (
                    <span className="text-nowrap small flex-shrink-0">
                        <Icon icon="node" color="node" />
                        <span>{nodeTag}</span>
                    </span>
                )}
                {shardNumber != null && (
                    <span className="text-nowrap small flex-shrink-0">
                        <Icon icon="shard" color="shard" />
                        <span>#{shardNumber}</span>
                    </span>
                )}
            </div>
            <span className="small text-muted ms-auto text-nowrap flex-shrink-0">
                <Icon icon="warning" color="danger" margin="m-0" />
                <b> {errorCount}</b> {errorCount === 1 ? "error" : "errors"}
            </span>
        </div>
    );
}
