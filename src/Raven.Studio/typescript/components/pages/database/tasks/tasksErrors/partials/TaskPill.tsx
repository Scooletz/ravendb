import React from "react";
import classNames from "classnames";
import { Icon } from "components/common/Icon";
import Badge from "react-bootstrap/Badge";
import { EtlTaskWithErrors, EtlHealthStatus, getTaskPillColor, healthStatusToBadge } from "../utils/tasksErrorsUtils";
import EtlTaskStats = Raven.Server.Documents.ETL.Stats.EtlTaskStats;

export interface TaskPillProps {
    color: "bg-warning" | "bg-danger" | "bg-success";
}

export function TaskPill({ color }: TaskPillProps) {
    return <div className={classNames("tasks-pill rounded", color)} />;
}

interface TaskPillMessageProps {
    etlTaskStats: EtlTaskStats;
    tasksWithErrors: EtlTaskWithErrors[];
}

export function TaskPillMessage({ etlTaskStats, tasksWithErrors }: TaskPillMessageProps) {
    const overallHealth = getOverallHealth(etlTaskStats);
    const { bg, icon, label } = healthStatusToBadge(overallHealth);

    const taskWithErrors = tasksWithErrors.find((t) => t.etlName === etlTaskStats.TaskName);

    const scripts = etlTaskStats.Stats.map((stat) => {
        const transformation = taskWithErrors?.transformations.find(
            (t) => t.transformationName === stat.TransformationName
        );
        const errorCount = transformation ? transformation.itemErrors.length + transformation.processErrors.length : 0;

        return {
            name: stat.TransformationName || "(default)",
            errorCount,
        };
    });

    return (
        <div className="task-pill-message">
            <div className="d-flex align-items-center gap-2 mb-2">
                <b className="flex-grow text-nowrap text-truncate">{etlTaskStats.TaskName}</b>
                <Badge bg={bg} className="rounded-pill ms-2 text-nowrap flex-shrink-0">
                    <Icon icon={icon} />
                    {label}
                </Badge>
            </div>
            {scripts.length > 0 && (
                <div className="d-flex flex-column gap-1">
                    {scripts.map((script) => (
                        <div key={script.name} className="d-flex align-items-center gap-2">
                            <span className="text-truncate flex-grow-1 small">
                                {etlTaskStats.TaskName}/{script.name}
                            </span>
                            <span className="small text-muted ms-auto text-nowrap flex-shrink-0">
                                <Icon icon="warning" color="danger" margin="m-0" />
                                <b> {script.errorCount}</b> {script.errorCount === 1 ? "error" : "errors"}
                            </span>
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
}

function getOverallHealth(etlTaskStats: EtlTaskStats): EtlHealthStatus {
    const color = getTaskPillColor(etlTaskStats.Stats);

    if (color === "bg-danger") {
        return "Failed";
    }

    if (color === "bg-warning") {
        return "Impaired";
    }

    return "Healthy";
}

interface TaskPillGroupMessageProps {
    etlTaskStatsList: EtlTaskStats[];
    tasksWithErrors: EtlTaskWithErrors[];
}

export function TaskPillGroupMessage({ etlTaskStatsList, tasksWithErrors }: TaskPillGroupMessageProps) {
    const overallHealth = getOverallHealth(etlTaskStatsList[0]);
    const { bg, icon, label } = healthStatusToBadge(overallHealth);

    const rows = etlTaskStatsList.flatMap((etlTaskStats) => {
        const taskWithErrors = tasksWithErrors.find((t) => t.etlName === etlTaskStats.TaskName);
        return etlTaskStats.Stats.map((stat) => {
            const transformation = taskWithErrors?.transformations.find(
                (t) => t.transformationName === stat.TransformationName
            );
            const errorCount = transformation
                ? transformation.itemErrors.length + transformation.processErrors.length
                : 0;
            return {
                taskName: etlTaskStats.TaskName,
                scriptName: stat.TransformationName || "(default)",
                errorCount,
            };
        });
    });

    return (
        <div className="task-pill-message">
            <div className="d-flex align-items-center gap-2 mb-2">
                <div>
                    <b className="flex-grow text-nowrap">{etlTaskStatsList.length}</b>{" "}
                    {etlTaskStatsList.length === 1 ? "task" : "tasks"}
                </div>
                <Badge bg={bg} className="rounded-pill ms-2 text-nowrap">
                    <Icon icon={icon} />
                    {label}
                </Badge>
            </div>
            <div className="d-flex flex-column gap-1">
                {rows.map((row) => (
                    <div key={`${row.taskName}/${row.scriptName}`} className="d-flex align-items-center gap-2">
                        <span className="text-truncate flex-grow-1 small">
                            {row.taskName}/{row.scriptName}
                        </span>
                        <span className="small text-muted ms-auto text-nowrap flex-shrink-0">
                            <Icon icon="warning" color="danger" margin="m-0" />
                            <b> {row.errorCount}</b> {row.errorCount === 1 ? "error" : "errors"}
                        </span>
                    </div>
                ))}
            </div>
        </div>
    );
}
