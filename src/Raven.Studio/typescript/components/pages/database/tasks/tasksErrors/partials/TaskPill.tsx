import React from "react";
import classNames from "classnames";
import { Icon } from "components/common/Icon";
import Badge from "react-bootstrap/Badge";
import { EtlTaskWithErrors, EtlHealthStatus, getTaskPillColor, healthStatusToBadge } from "../utils/tasksErrorsUtils";
import PopoverWithHoverWrapper from "components/common/PopoverWithHoverWrapper";
import EtlTaskStats = Raven.Server.Documents.ETL.Stats.EtlTaskStats;

export interface TaskPillProps {
    color: "bg-warning" | "bg-danger" | "bg-success";
    message?: React.ReactNode;
}

export function TaskPill({ color, message }: TaskPillProps) {
    return (
        <PopoverWithHoverWrapper placement="left-end" message={message}>
            <div className={classNames("tasks-pill rounded", color)} />
        </PopoverWithHoverWrapper>
    );
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
            name: stat.TransformationName,
            errorCount,
            healthStatus: stat.Statistics.HealthStatus,
        };
    });

    return (
        <div className="task-pill-message">
            <div className="d-flex align-items-center gap-2 mb-1">
                <div>
                    <b className="flex-grow text-nowrap">{scripts.length}</b>{" "}
                    {scripts.length === 1 ? "script" : "scripts"}
                </div>
                <Badge bg={bg} className="rounded-pill ms-2 text-nowrap">
                    <Icon icon={icon} />
                    {label}
                </Badge>
            </div>
            {scripts.length > 0 && (
                <div className="d-flex flex-column gap-1">
                    {scripts.map((script) => {
                        return (
                            <div key={script.name} className="d-flex align-items-center gap-2">
                                <span className="text-truncate flex-grow-1 small">{script.name || "(default)"}</span>
                                <span className="small text-muted ms-auto text-nowrap flex-shrink-0">
                                    <Icon icon="warning" color="danger" margin="m-0" />
                                    <b> {script.errorCount}</b> {script.errorCount === 1 ? "error" : "errors"}
                                </span>
                            </div>
                        );
                    })}
                </div>
            )}
        </div>
    );
}

function getOverallHealth(etlTaskStats: EtlTaskStats): EtlHealthStatus {
    const stats = etlTaskStats.Stats;
    const color = getTaskPillColor(stats);
    if (color === "bg-danger") return "Failed";
    if (color === "bg-warning") return "Impaired";
    return "Healthy";
}
