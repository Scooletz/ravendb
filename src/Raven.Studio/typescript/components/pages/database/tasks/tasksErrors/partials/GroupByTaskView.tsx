import React, { useMemo } from "react";
import { EmptySet } from "components/common/EmptySet";
import { EtlTaskWithErrors, TasksFiltersState, getTaskHealthStatus } from "../utils/tasksErrorsUtils";
import { TaskPanel } from "./TaskPanel";
import TaskUtils from "components/utils/TaskUtils";
import EtlTaskStats = Raven.Server.Documents.ETL.Stats.EtlTaskStats;

interface GroupByTaskViewProps {
    tasksWithErrors: EtlTaskWithErrors[];
    etlStats: EtlTaskStats[];
    filters: TasksFiltersState;
    onRefresh: () => void;
}

export function GroupByTaskView({ tasksWithErrors, etlStats, filters, onRefresh }: GroupByTaskViewProps) {
    const filteredTasksWithErrors = useMemo(() => {
        const { searchText, nodeTags, shardNumbers, healthStatuses, taskTypes } = filters;

        return tasksWithErrors
            .filter((task) => {
                const taskStats = etlStats.find((s) => s.TaskName === task.etlName);
                const taskEtlType = TaskUtils.etlTypeToStudioType(taskStats?.EtlType, taskStats?.EtlSubType);
                const matchesTaskType = !taskTypes.length || (taskEtlType != null && taskTypes.includes(taskEtlType));

                const taskHealth = getTaskHealthStatus(etlStats, task.etlName);
                const matchesHealth = !healthStatuses.length || healthStatuses.includes(taskHealth);

                return matchesTaskType && matchesHealth;
            })
            .map((task) => ({
                ...task,
                transformations: task.transformations.filter((t) => {
                    const allErrors = [...t.itemErrors, ...t.processErrors];

                    const matchesSearch =
                        !searchText ||
                        task.etlName.toLowerCase().includes(searchText.toLowerCase()) ||
                        t.transformationName.toLowerCase().includes(searchText.toLowerCase());
                    const matchesNode = !nodeTags.length || allErrors.some((e) => nodeTags.includes(e.nodeTag));
                    const matchesShard =
                        !shardNumbers.length || allErrors.some((e) => shardNumbers.includes(String(e.shard)));

                    return matchesSearch && matchesNode && matchesShard;
                }),
            }))
            .filter((task) => task.transformations.length > 0);
    }, [tasksWithErrors, etlStats, filters]);

    if (filteredTasksWithErrors.length === 0) {
        return <EmptySet>No tasks match the current filters.</EmptySet>;
    }

    return (
        <>
            {filteredTasksWithErrors.map((task) => (
                <TaskPanel {...task} etlStats={etlStats} onRefresh={onRefresh} key={task.etlName} />
            ))}
        </>
    );
}
