import React, { useMemo, useState } from "react";
import { Icon } from "components/common/Icon";
import Form from "react-bootstrap/Form";
import Button from "react-bootstrap/Button";
import Select, { SelectOption } from "components/common/select/Select";
import { InputItem } from "components/models/common";
import { MultiRadioToggle } from "components/common/toggles/MultiRadioToggle";
import { useAppSelector } from "components/store";
import { databaseSelectors } from "components/common/shell/databaseSliceSelectors";
import DatabaseUtils from "components/utils/DatabaseUtils";
import { EtlHealthStatus, GroupByType, TasksFiltersState } from "../utils/tasksErrorsUtils";

export type { TasksFiltersState };

export const taskHealthOptions: SelectOption<EtlHealthStatus>[] = [
    { label: "Healthy", value: "Healthy" },
    { label: "Failed", value: "Failed" },
    { label: "Impaired", value: "Impaired" },
];

export const taskTypeOptions: SelectOption<StudioEtlType>[] = [
    { label: "RavenDB ETL", value: "Raven" },
    { label: "SQL ETL", value: "Sql" },
    { label: "Snowflake ETL", value: "Snowflake" },
    { label: "Azure Queue Storage ETL", value: "AzureQueueStorage" },
    { label: "OLAP ETL", value: "Olap" },
    { label: "Kafka ETL", value: "Kafka" },
    { label: "Elastic Search ETL", value: "ElasticSearch" },
    { label: "RabbitMQ ETL", value: "RabbitMQ" },
    { label: "Amazon SQS ETL", value: "AmazonSqs" },
    { label: "Embeddings Generation", value: "EmbeddingsGeneration" },
    { label: "GenAI ETL", value: "GenAi" },
];

export const groupByOptions: InputItem<GroupByType>[] = [
    { value: "task", label: "Task", badgeColor: "secondary" },
    { value: "none", label: "None", badgeColor: "secondary" },
];

export function useTasksFilters(
    initialSearchText = "",
    initialTaskTypes: StudioEtlType[] = []
): [TasksFiltersState, (patch: Partial<TasksFiltersState>) => void] {
    const [filters, setFilters] = useState<TasksFiltersState>({
        searchText: initialSearchText,
        nodeTags: [],
        shardNumbers: [],
        healthStatuses: [],
        taskTypes: initialTaskTypes,
    });

    const updateFilters = (patch: Partial<TasksFiltersState>) => setFilters((prev) => ({ ...prev, ...patch }));

    return [filters, updateFilters];
}

interface TasksFiltersProps {
    selectedGroupByType: GroupByType;
    setSelectedGroupByType: (x: GroupByType) => void;
    filters: TasksFiltersState;
    updateFilters: (patch: Partial<TasksFiltersState>) => void;
}

export function TasksFilters({
    setSelectedGroupByType,
    selectedGroupByType,
    filters,
    updateFilters,
}: TasksFiltersProps) {
    const db = useAppSelector(databaseSelectors.activeDatabase);

    const nodeOptions = useMemo(
        () => db.nodes.map((node) => ({ label: `Node ${node.tag}`, value: node.tag })),
        [db.nodes]
    );

    const shardOptions = useMemo(() => {
        if (!db.isSharded) {
            return [];
        }
        const shardNumbers: number[] = _.uniq(
            DatabaseUtils.getLocations(db)
                .map((l) => l.shardNumber)
                .filter((n) => n != null)
        );

        return shardNumbers.map((n) => ({ label: `Shard #${n}`, value: String(n) }));
    }, [db]);

    return (
        <div className="hstack flex-wrap align-items-end gap-2 my-3 justify-content-start">
            <div className="flex-grow">
                <div className="small-label ms-1 mb-1">Filter by task/script name</div>
                <div className="clearable-input">
                    <Form.Control
                        type="text"
                        accessKey="/"
                        placeholder="e.g. MyPeriodicBackupTask"
                        title="Filter ongoing tasks"
                        className="filtering-input"
                        value={filters.searchText}
                        onChange={(e) => updateFilters({ searchText: e.target.value })}
                    />
                    {filters.searchText && (
                        <div className="clear-button">
                            <Button variant="secondary" size="sm" onClick={() => updateFilters({ searchText: "" })}>
                                <Icon icon="clear" margin="m-0" />
                            </Button>
                        </div>
                    )}
                </div>
            </div>
            <div className="TaskErrorFilter">
                <div className="small-label ms-1 mb-1">Filter by node</div>
                <Select
                    isMulti
                    isClearable
                    options={nodeOptions}
                    onChange={(options) => updateFilters({ nodeTags: options ? options.map((o) => o.value) : [] })}
                />
            </div>
            {db.isSharded && (
                <div className="TaskErrorFilter">
                    <div className="small-label ms-1 mb-1">Filter by shard</div>
                    <Select
                        isMulti
                        isClearable
                        options={shardOptions}
                        onChange={(options) =>
                            updateFilters({ shardNumbers: options ? options.map((o) => o.value) : [] })
                        }
                    />
                </div>
            )}
            <div className="TaskErrorFilter">
                <div className="small-label ms-1 mb-1">Filter by task type</div>
                <Select
                    isMulti
                    isClearable
                    options={taskTypeOptions}
                    value={taskTypeOptions.filter((o) => filters.taskTypes.includes(o.value))}
                    onChange={(options) =>
                        updateFilters({
                            taskTypes: options ? options.map((o) => o.value) : [],
                        })
                    }
                />
            </div>
            <div className="TaskErrorFilter">
                <div className="small-label ms-1 mb-1">Filter by task health</div>
                <Select
                    isMulti
                    isClearable
                    options={taskHealthOptions}
                    onChange={(options) =>
                        updateFilters({
                            healthStatuses: options ? options.map((o) => o.value) : [],
                        })
                    }
                />
            </div>
            <div>
                <div className="small-label ms-1 mb-1">Group by</div>
                <MultiRadioToggle<GroupByType>
                    inputItems={groupByOptions}
                    selectedItem={selectedGroupByType}
                    setSelectedItem={(x) => setSelectedGroupByType(x)}
                />
            </div>
        </div>
    );
}
