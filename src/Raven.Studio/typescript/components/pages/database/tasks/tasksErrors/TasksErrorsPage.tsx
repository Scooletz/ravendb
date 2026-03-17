import { AboutViewFloating, AboutViewHeading, AccordionItemWrapper } from "components/common/AboutView";
import React, { ReactNode, useCallback, useMemo, useState } from "react";
import { Icon } from "components/common/Icon";
import "./TasksErrorsPage.scss";
import classNames from "classnames";
import Form from "react-bootstrap/esm/Form";
import Button from "react-bootstrap/esm/Button";
import Row from "react-bootstrap/Row";
import Col from "react-bootstrap/Col";
import Select, { SelectOption } from "components/common/select/Select";
import { InputItem } from "components/models/common";
import { MultiRadioToggle } from "components/common/toggles/MultiRadioToggle";
import {
    RichPanel,
    RichPanelActions,
    RichPanelDetailItem,
    RichPanelDetails,
    RichPanelHeader,
    RichPanelInfo,
} from "components/common/RichPanel";
import useBoolean from "hooks/useBoolean";
import Badge from "react-bootstrap/Badge";
import Collapse from "react-bootstrap/Collapse";
import Card from "react-bootstrap/Card";
import {
    CellContext,
    ColumnDef,
    ColumnFiltersState,
    ColumnPinningState,
    getCoreRowModel,
    getFilteredRowModel,
    getSortedRowModel,
    useReactTable,
} from "@tanstack/react-table";
import { virtualTableUtils } from "components/common/virtualTable/utils/virtualTableUtils";
import CellValue, { CellValueWrapper } from "components/common/virtualTable/cells/CellValue";
import VirtualTable from "components/common/virtualTable/VirtualTable";
import SizeGetter from "components/common/SizeGetter";
import CellDocumentValue from "components/common/virtualTable/cells/CellDocumentValue";
import { useServices } from "hooks/useServices";
import { useAppSelector } from "components/store";
import { databaseSelectors } from "components/common/shell/databaseSliceSelectors";
import { useClusterWideAsync } from "hooks/useClusterWideAsync";
import { LoadingView } from "components/common/LoadingView";
import IconName from "../../../../../../typings/server/icons";
import { EmptySet } from "components/common/EmptySet";
import DateFormatterCell from "components/common/virtualTable/cells/CellDateFormatter";
import Modal from "components/common/Modal";
import { useAsync, useAsyncCallback } from "react-async-hook";
import studioSettings from "common/settings/studioSettings";
import messagePublisher from "common/messagePublisher";
import { Switch } from "components/common/Checkbox";
import ButtonWithSpinner from "components/common/ButtonWithSpinner";
import DatabaseUtils from "components/utils/DatabaseUtils";
import TableDisplaySettings from "components/common/virtualTable/commonComponents/columnsSelect/TableDisplaySettings";
import { useViewSheet, ViewSheet } from "components/common/splitView/ViewSheet";
import PopoverWithHoverWrapper from "components/common/PopoverWithHoverWrapper";
import genUtils from "common/generalUtils";
import { CellWithCopyWrapper } from "components/common/virtualTable/cells/CellWithCopy";
import clusterDashboard from "viewmodels/resources/clusterDashboard";
import { useAppUrls } from "components/hooks/useAppUrls";
import assertUnreachable from "components/utils/assertUnreachable";
import { LoadError } from "components/common/LoadError";
import moment from "moment";
import RichAlert from "components/common/RichAlert";
import { FormGroup, FormLabel } from "components/common/Form";

type EtlErrorStep = Raven.Server.Documents.ETL.EtlErrorStep;
type GroupByType = "task" | "none";
type EtlHealthStatus = Raven.Server.Documents.ETL.EtlProcessHealthStatus;

interface TasksErrorsPageQueryParams {
    taskName?: string;
}

export default function TasksErrorsPage({ queryParams }: ReactQueryParamsProps<TasksErrorsPageQueryParams>) {
    const db = useAppSelector(databaseSelectors.activeDatabase);
    const { tasksService } = useServices();

    const getEtlErrors = useCallback(async (location: databaseLocationSpecifier) => {
        return await tasksService.getEtlErrors(db.name, location);
    }, []);

    const {
        result: asyncFetchAllEtlErrors,
        loading: isLoadingEtlErrors,
        refresh: refreshEtlErrors,
    } = useClusterWideAsync(getEtlErrors);

    const getEtlStats = useCallback(async (location: databaseLocationSpecifier) => {
        return await tasksService.getEtlStats(db.name, location);
    }, []);

    const {
        result: asyncFetchAllEtlStats,
        loading: isLoadingEtlStats,
        refresh: refreshEtlStats,
    } = useClusterWideAsync(getEtlStats);

    if (isLoadingEtlErrors || isLoadingEtlStats) {
        return <LoadingView />;
    }

    const hasAnyError = asyncFetchAllEtlErrors.some((x) => x.error) || asyncFetchAllEtlStats.some((x) => x.error);

    const handleRefresh = async () => {
        await refreshEtlErrors();
        await refreshEtlStats();
    };

    if (hasAnyError) {
        return <LoadError refresh={handleRefresh} />;
    }

    const allEtlErrors = asyncFetchAllEtlErrors.flatMap((x) =>
        (x.data ?? []).map((error) => ({
            ...error,
            nodeTag: x.nodeTag,
            shard: x.shard,
        }))
    );
    const tasksWithErrors = getTasksWithErrors(allEtlErrors);

    const flattenAllEtlStats = asyncFetchAllEtlStats.flatMap((x) => x.data);

    return (
        <div className="content-padding tasks-errors-page">
            <div className="d-flex flex-column gap-2 flex-shrink-0">
                <div className="d-flex justify-content-between">
                    <AboutViewHeading marginBottom={0} title="Tasks Errors" icon="tasks-errors" />
                    <TasksErrorsAboutView />
                </div>
                {tasksWithErrors.length > 0 && <div>Analyze and get more details on your Tasks errors. </div>}
            </div>
            <TasksErrorsPageBody
                tasksWithErrors={tasksWithErrors}
                flattenAllEtlStats={flattenAllEtlStats}
                initialSearchText={queryParams?.taskName}
            />
        </div>
    );
}

interface TasksErrorsPageBodyProps {
    tasksWithErrors: EtlTaskWithErrors[];
    flattenAllEtlStats: EtlTaskStats[];
    initialSearchText?: string;
}

const TasksErrorsPageBody = ({ tasksWithErrors, flattenAllEtlStats, initialSearchText }: TasksErrorsPageBodyProps) => {
    const [selectedGroupByType, setSelectedGroupByType] = useState<GroupByType>("task");
    const [filters, updateFilters] = useTasksFilters(initialSearchText);

    if (tasksWithErrors.length === 0) {
        return (
            <EmptySet>
                Your Ongoing tasks processes are running smoothly. You can monitor and resolve any future data issues
                right here.
            </EmptySet>
        );
    }

    return (
        <div className="d-flex flex-column flex-grow-1 min-h-0 mt-3">
            <div className="border-1 align-items-center d-flex w-100 bg-dark border-secondary border p-1 my-2 rounded flex-shrink-0">
                <Icon icon="tasks" />
                <span className="flex-grow">
                    <b>{tasksWithErrors.length ?? 0}</b> {tasksWithErrors.length === 1 ? "task" : "tasks"} with errors
                </span>
                <div className="d-flex gap-1">
                    {flattenAllEtlStats.map((etl, index) => (
                        <TaskPill
                            color={getTaskPillColor(etl.Stats)}
                            key={index}
                            message={<TaskPillMessage etlTaskStats={etl} tasksWithErrors={tasksWithErrors} />}
                        />
                    ))}
                </div>
            </div>

            <TasksFilters
                selectedGroupByType={selectedGroupByType}
                setSelectedGroupByType={setSelectedGroupByType}
                filters={filters}
                updateFilters={updateFilters}
            />

            <div className="mt-4 flex-grow-1 min-h-0">
                {selectedGroupByType === "task" && (
                    <GroupByTaskView
                        tasksWithErrors={tasksWithErrors}
                        etlStats={flattenAllEtlStats}
                        filters={filters}
                    />
                )}
                {selectedGroupByType === "none" && (
                    <GroupByNoneView
                        tasksWithErrors={tasksWithErrors}
                        etlStats={flattenAllEtlStats}
                        filters={filters}
                    />
                )}
            </div>
        </div>
    );
};

interface TasksFiltersProps {
    selectedGroupByType: GroupByType;
    setSelectedGroupByType: (x: GroupByType) => void;
    filters: TasksFiltersState;
    updateFilters: (patch: Partial<TasksFiltersState>) => void;
}

function TasksFilters({ setSelectedGroupByType, selectedGroupByType, filters, updateFilters }: TasksFiltersProps) {
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
        <Row className="mt-4">
            <Col>
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
            </Col>
            <Col>
                <div className="small-label ms-1 mb-1">Filter by node</div>
                <Select
                    isMulti
                    isClearable
                    options={nodeOptions}
                    onChange={(options) => updateFilters({ nodeTags: options ? options.map((o) => o.value) : [] })}
                />
            </Col>
            {db.isSharded && (
                <Col>
                    <div className="small-label ms-1 mb-1">Filter by shard</div>
                    <Select
                        isMulti
                        isClearable
                        options={shardOptions}
                        onChange={(options) =>
                            updateFilters({ shardNumbers: options ? options.map((o) => o.value) : [] })
                        }
                    />
                </Col>
            )}
            <Col>
                <div className="small-label ms-1 mb-1">Filter by task type</div>
                <Select
                    isMulti
                    isClearable
                    options={taskTypeOptions}
                    onChange={(options) =>
                        updateFilters({
                            taskTypes: options ? (options.map((o) => o.value) as StudioEtlType[]) : [],
                        })
                    }
                />
            </Col>
            <Col>
                <div className="small-label ms-1 mb-1">Filter by task health</div>
                <Select
                    isMulti
                    isClearable
                    options={taskHealthOptions}
                    onChange={(options) =>
                        updateFilters({
                            healthStatuses: options ? (options.map((o) => o.value) as EtlHealthStatus[]) : [],
                        })
                    }
                />
            </Col>
            <Col xs="auto">
                <div className="small-label ms-1 mb-1">Group by</div>
                <MultiRadioToggle<GroupByType>
                    inputItems={groupByOptions}
                    selectedItem={selectedGroupByType}
                    setSelectedItem={(x) => setSelectedGroupByType(x)}
                />
            </Col>
        </Row>
    );
}

const taskHealthOptions: SelectOption<EtlHealthStatus>[] = [
    { label: "Healthy", value: "Healthy" },
    { label: "Failed", value: "Failed" },
    { label: "Impaired", value: "Impaired" },
];

const taskTypeOptions: SelectOption<StudioEtlType>[] = [
    {
        label: "RavenDB ETL",
        value: "Raven",
    },
    {
        label: "SQL ETL",
        value: "Sql",
    },
    {
        label: "Azure Queue Storage ETL",
        value: "AzureQueueStorage",
    },
    {
        label: "OLAP ETL",
        value: "Olap",
    },
    {
        label: "Kafka ETL",
        value: "Kafka",
    },
    {
        label: "Elastic Search ETL",
        value: "ElasticSearch",
    },
    {
        label: "RabbitMQ ETL",
        value: "RabbitMQ",
    },
];

const groupByOptions: InputItem<GroupByType>[] = [
    { value: "task", label: "Task" },
    { value: "none", label: "None" },
];

interface GroupByTaskViewProps {
    tasksWithErrors: EtlTaskWithErrors[];
    etlStats: EtlTaskStats[];
    filters: TasksFiltersState;
}

const GroupByTaskView = ({ tasksWithErrors, etlStats, filters }: GroupByTaskViewProps) => {
    const filteredTasksWithErrors = useMemo(() => {
        const { searchText, nodeTags, shardNumbers, healthStatuses, taskTypes } = filters;

        return tasksWithErrors
            .filter((task) => {
                const taskEtlType = etlStats.find((s) => s.TaskName === task.etlName)?.EtlType;
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
                <TaskPanel {...task} etlStats={etlStats} key={task.etlName} />
            ))}
        </>
    );
};

interface TaskPanelProps extends EtlTaskWithErrors {
    etlStats: EtlTaskStats[];
}

const TaskPanel = ({ etlName, transformations, etlStats }: TaskPanelProps) => {
    const { value: isDetailsVisible, toggle: toggleDetails } = useBoolean(true);
    const { value: isDeleteModalOpen, toggle: toggleDeleteModal } = useBoolean(false);
    const errorsCount = transformations.reduce(
        (acc, transformation) => acc + transformation.processErrors.length + transformation.itemErrors.length,
        0
    );

    const taskHealth = getTaskHealthStatus(etlStats, etlName);
    const { bg, icon, label } = healthStatusToBadge(taskHealth);
    const etlType = etlStats.find((s) => s.TaskName === etlName)?.EtlType;
    return (
        <>
            <RichPanel>
                <RichPanelHeader>
                    <RichPanelInfo>
                        <a href="#" className="fs-3">
                            {etlName}
                        </a>
                    </RichPanelInfo>
                    <RichPanelActions>
                        <Button variant="danger" onClick={toggleDeleteModal}>
                            <Icon icon="trash" />
                            Delete errors
                        </Button>
                    </RichPanelActions>
                </RichPanelHeader>
                <RichPanelDetails>
                    <RichPanelDetailItem>
                        <Button
                            variant="secondary"
                            className="btn-toggle-panel rounded-pill"
                            onClick={toggleDetails}
                            title="Click for details"
                        >
                            <Icon icon={isDetailsVisible ? "fold" : "unfold"} margin="m-0" />
                        </Button>
                    </RichPanelDetailItem>
                    <EtlTypeRichPanelItem etlType={etlType} />
                    <RichPanelDetailItem childrenClassName="d-flex gap-1 align-items-center">
                        <Icon icon="warning" color="danger" margin="m-0" />
                        <span>Errors</span> <b>{errorsCount}</b>
                    </RichPanelDetailItem>
                    <RichPanelDetailItem childrenClassName="d-flex gap-1 align-items-center">
                        <Icon icon="console" />
                        <span>Scripts</span> <b>{transformations.length}</b>
                    </RichPanelDetailItem>

                    <RichPanelDetailItem>
                        <PopoverWithHoverWrapper
                            wrapperClassName="d-flex align-items-center"
                            message={getPopoverMessageForTaskHealth(taskHealth)}
                        >
                            <Icon icon="healthcheck" />
                            <Badge bg={bg} className="rounded-pill">
                                <Icon icon={icon} />
                                {label}
                            </Badge>
                        </PopoverWithHoverWrapper>
                    </RichPanelDetailItem>
                </RichPanelDetails>
                <SizeGetter
                    render={(sizeProps) => (
                        <Collapse in={isDetailsVisible} unmountOnExit mountOnEnter>
                            <div className="m-2 d-flex gap-2 flex-column">
                                {transformations.map((transformation) => (
                                    <NestedTaskPanelDetails
                                        key={transformation.transformationName}
                                        {...sizeProps}
                                        {...transformation}
                                    />
                                ))}
                            </div>
                        </Collapse>
                    )}
                />
            </RichPanel>
            {isDeleteModalOpen && (
                <DeleteTaskErrorsModal etlName={etlName} errorsCount={errorsCount} toggle={toggleDeleteModal} />
            )}
        </>
    );
};

function getPopoverMessageForTaskHealth(status: EtlHealthStatus) {
    switch (status) {
        case "Healthy":
            return "Your task is in a good health state with none to minor count of errors.";
        case "Impaired":
            return "Your task is mildly affected with errors. It needs your attention.";
        case "Failed":
            return "Your task needs your attention as it’s severely affected with errors.";
        default:
            return genUtils.assertUnreachable(status);
    }
}

interface NestedTaskPanelDetailsProps extends EtlTransformationWithErrors {
    width: number;
}

const NestedTaskPanelDetails = ({
    width,
    transformationName,
    processErrors,
    itemErrors,
}: NestedTaskPanelDetailsProps) => {
    const { value: isNestedDetailsVisible, toggle: toggleNestedDetailsVisible } = useBoolean(true);

    const totalErrors = processErrors.length + itemErrors.length;

    return (
        <Card className="bg-black p-2">
            <div className="d-flex w-100 gap-2 align-items-center">
                <span>{transformationName}</span>
                <div className="flex-grow">
                    <Icon icon="warning" color="danger" />
                    <span>Errors</span> <b>{totalErrors}</b>
                </div>
                <Button variant="secondary" onClick={toggleNestedDetailsVisible}>
                    <Icon icon={isNestedDetailsVisible ? "collapse-vertical" : "expand-vertical"} margin="m-0" />
                </Button>
            </div>
            <Collapse in={isNestedDetailsVisible} unmountOnExit mountOnEnter>
                <div className="mt-4">
                    <NestedTaskPanelDetailsTable width={width} itemErrors={itemErrors} processErrors={processErrors} />{" "}
                </div>
            </Collapse>
        </Card>
    );
};

interface NestedTaskPanelDetailsTableProps {
    width: number;
    itemErrors: EtlTransformationWithErrors["itemErrors"];
    processErrors: EtlTransformationWithErrors["processErrors"];
}

const NestedTaskPanelDetailsTable = ({ width, itemErrors, processErrors }: NestedTaskPanelDetailsTableProps) => {
    const columns = useTasksErrorsPanelTableColumns(width);
    const data = useMemo(() => flattenTransformationErrors(itemErrors, processErrors), [itemErrors, processErrors]);

    const tasksErrorsPanelTable = useReactTable({
        data,
        columns,
        columnResizeMode: "onChange",
        getCoreRowModel: getCoreRowModel(),
        getSortedRowModel: getSortedRowModel(),
        getFilteredRowModel: getFilteredRowModel(),
    });

    return <VirtualTable table={tasksErrorsPanelTable} heightInPx={400} />;
};

const useTasksErrorsPanelTableColumns = (availableWidth: number) => {
    const db = useAppSelector(databaseSelectors.activeDatabase);
    const bodyWidth = virtualTableUtils.getTableBodyWidth(availableWidth);
    const getSize = virtualTableUtils.getCellSizeProvider(bodyWidth - 70);

    const tasksErrorsPanelColumns: ColumnDef<any>[] = useMemo(
        () => [
            {
                header: "Show",
                cell: CellValueButtonWrapper,
                size: 70,
            },
            {
                header: "Error Type",
                accessorKey: "errorType",
                cell: CellErrorTypeWrapper,
                size: getSize(10),
            },
            {
                header: "Error step",
                cell: CellErrorStepWrapper,
                accessorKey: "Step",
                size: getSize(10),
            },
            {
                header: "Document",
                cell: HyperLinkDocumentCellValue,
                accessorKey: "DocumentId",
                size: getSize(10),
            },
            {
                header: "Date",
                cell: DateFormatterCell,
                accessorKey: "CreatedAt",
                size: getSize(20),
            },
            {
                header: "Content",
                cell: CellValueWrapper,
                accessorKey: "Error",
                size: getSize(db.isSharded ? 40 : 45),
                enableSorting: false,
            },
            {
                header: "Node",
                cell: CellNodeValueWrapper,
                accessorKey: "nodeTag",
                size: getSize(5),
                enableSorting: false,
            },
        ],
        []
    );

    if (db.isSharded) {
        tasksErrorsPanelColumns.push({
            header: "Shard",
            cell: CellShardValueWrapper,
            accessorKey: "shard",
            size: getSize(5),
            enableSorting: false,
        });
    }

    return tasksErrorsPanelColumns;
};

const getStepIcon = (step: EtlErrorStep): IconName => {
    switch (step) {
        case "Transformation":
            return "replace";
        case "Load":
            return "import";
        case "Configuration":
            return "config";
        default:
            return null;
    }
};

const CellErrorStepWrapper = ({ getValue }: CellContext<FlatError, EtlErrorStep>) => {
    const value = getValue();

    if (!value) {
        return <CellValue value="-" />;
    }

    const stepIcon = getStepIcon(value);
    return (
        <div className="cell-value value-string">
            {stepIcon && <Icon icon={stepIcon} />}
            <CellValue value={value} />
        </div>
    );
};

const CellErrorTypeWrapper = ({ getValue }: CellContext<FlatError, "Item" | "Process">) => {
    const value = getValue();
    return (
        <PopoverWithHoverWrapper message={getPopoverMessageForErrorType(value)}>
            <Badge bg={value === "Item" ? "secondary" : "info"} className="rounded-pill cell-value">
                <Icon icon={value === "Item" ? "tasks" : "hammer-driver"} />
                {value === "Item" ? "Item Error" : "Process Error"}
            </Badge>
        </PopoverWithHoverWrapper>
    );
};

function getPopoverMessageForErrorType(errorType: "Item" | "Process") {
    switch (errorType) {
        case "Item":
            return "Error that applies to the single document and doesn’t affect the whole task process.";
        case "Process":
            return "Error that affects the process and the whole batch of documents.";
        default:
            genUtils.assertUnreachable(errorType);
    }
}

const CellShardValueWrapper = ({ getValue }: CellContext<FlatError, string>) => {
    return (
        <>
            <Icon icon="shard" color="shard" />
            <CellValue value={"#" + getValue()} />
        </>
    );
};

const CellNodeValueWrapper = ({ getValue }: CellContext<FlatError, string>) => {
    return <NodeCircle nodeTag={getValue()} />;
};

interface NodeCircleProps {
    nodeTag: string;
}

function NodeCircle({ nodeTag }: NodeCircleProps) {
    const db = useAppSelector(databaseSelectors.activeDatabase);
    const nodeColors = clusterDashboard.nodeColors;
    const nodeIndex = db.nodes.findIndex((n) => n.tag === nodeTag);

    return (
        <div
            className="node-circle rounded-circle p-2 d-flex text-black justify-content-center align-items-center fw-bold"
            style={{ backgroundColor: nodeColors.at(nodeIndex) }}
        >
            {nodeTag}
        </div>
    );
}

const CellValueButtonWrapper = (args: CellContext<FlatError, unknown>) => {
    const { open } = useViewSheet();

    const handleOpenSheet = () => {
        open({
            component: <EtlErrorDetailsSheet error={args.row.original} />,
            initialWidth: "40%",
            minWidth: "25%",
            maxWidth: "60%",
            isPinned: false,
        });
    };

    return (
        <Button variant="secondary" onClick={handleOpenSheet}>
            <Icon icon="preview" margin="m-0" />
        </Button>
    );
};

type HyperLinkDocumentCellValueProps = Pick<CellContext<FlatError, unknown>, "getValue">;

const HyperLinkDocumentCellValue = ({ getValue }: HyperLinkDocumentCellValueProps) => {
    const dbName = useAppSelector(databaseSelectors.activeDatabaseName);

    if (!getValue()) {
        return <CellValue value="-" />;
    }

    return <CellDocumentValue value={getValue()} databaseName={dbName} hasHyperlinkForIds />;
};

interface TaskPillProps {
    color: "bg-warning" | "bg-danger" | "bg-success";
    message?: ReactNode;
}

const TaskPill = ({ color, message }: TaskPillProps) => {
    return (
        <PopoverWithHoverWrapper placement="left-end" message={message}>
            <div className={classNames("tasks-pill rounded", color)} />
        </PopoverWithHoverWrapper>
    );
};

interface TaskPillMessageProps {
    etlTaskStats: EtlTaskStats;
    tasksWithErrors: EtlTaskWithErrors[];
}

const TaskPillMessage = ({ etlTaskStats, tasksWithErrors }: TaskPillMessageProps) => {
    const overallHealth = getTaskHealthStatus([etlTaskStats], etlTaskStats.TaskName);
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
};

const TasksErrorsAboutView = () => {
    return (
        <AboutViewFloating>
            <AccordionItemWrapper icon="about" color="info" heading="Tasks Errors" description="View tasks errors">
                <div>todo</div>
            </AccordionItemWrapper>
        </AboutViewFloating>
    );
};

function parseProcessName(processName: string): [etlName: string, transformationName: string] {
    const slashIndex = processName.indexOf("/");
    if (slashIndex === -1) {
        return [processName, ""];
    }
    return [processName.slice(0, slashIndex), processName.slice(slashIndex + 1)];
}

type EtlErrorsWithLocation = EtlErrors & { nodeTag: string; shard?: number };

interface EtlTransformationWithErrors {
    transformationName: string;
    processErrors: (EtlErrors["ProcessErrors"][number] & { nodeTag: string; shard?: number })[];
    itemErrors: (EtlErrors["ItemErrors"][number] & { nodeTag: string; shard?: number })[];
}

interface EtlTaskWithErrors {
    etlName: string;
    transformations: EtlTransformationWithErrors[];
}

function getTasksWithErrors(processes: EtlErrorsWithLocation[]): EtlTaskWithErrors[] {
    if (!processes?.length) {
        return [];
    }

    return _.chain(processes)
        .filter((p: EtlErrorsWithLocation) => _.size(p?.ProcessErrors) || _.size(p?.ItemErrors))
        .groupBy((p: EtlErrorsWithLocation) => parseProcessName(p.ProcessName)[0])
        .map(
            (group: EtlErrorsWithLocation[], etlName: string): EtlTaskWithErrors => ({
                etlName,
                transformations: _.chain(group)
                    .groupBy((p: EtlErrorsWithLocation) => parseProcessName(p.ProcessName)[1])
                    .map(
                        (
                            transformationGroup: EtlErrorsWithLocation[],
                            transformationName: string
                        ): EtlTransformationWithErrors => ({
                            transformationName,
                            processErrors: transformationGroup.flatMap((p) =>
                                p.ProcessErrors.map((e) => ({ ...e, nodeTag: p.nodeTag, shard: p.shard }))
                            ),
                            itemErrors: transformationGroup.flatMap((p) =>
                                p.ItemErrors.map((e) => ({ ...e, nodeTag: p.nodeTag, shard: p.shard }))
                            ),
                        })
                    )
                    .value(),
            })
        )
        .value();
}

interface GroupByNoneViewProps {
    tasksWithErrors: EtlTaskWithErrors[];
    etlStats: EtlTaskStats[];
    filters: TasksFiltersState;
}

function GroupByNoneView({ tasksWithErrors, etlStats, filters }: GroupByNoneViewProps) {
    const { value: isDeleteAllErrorsModalOpen, toggle: toggleDeleteAllErrorsModal } = useBoolean(false);

    return (
        <>
            <div className="d-flex flex-column gap-2 h-100">
                <SizeGetter
                    isHeighRequired
                    className="flex-grow-1 min-h-0"
                    render={({ width }) => (
                        <GroupByNoneTable
                            toggleDeleteAllErrorsModal={toggleDeleteAllErrorsModal}
                            tasksWithErrors={tasksWithErrors}
                            etlStats={etlStats}
                            width={width}
                            filters={filters}
                        />
                    )}
                />
            </div>
            {isDeleteAllErrorsModalOpen && (
                <DeleteAllErrorsModal tasksWithErrors={tasksWithErrors} toggle={toggleDeleteAllErrorsModal} />
            )}
        </>
    );
}

interface EtlError {
    etlName: string;
    transformationName: string;
    healthStatus: EtlHealthStatus;
    taskId: number | null;
    etlType: StudioEtlType | null;
}

type FlatError = (
    | (EtlTransformationWithErrors["itemErrors"][number] & { errorType: "Item" })
    | (EtlTransformationWithErrors["processErrors"][number] & { errorType: "Process" })
) &
    EtlError;

interface GroupByNoneTableProps {
    tasksWithErrors: EtlTaskWithErrors[];
    etlStats: EtlTaskStats[];
    width: number;
    toggleDeleteAllErrorsModal: () => void;
    filters: TasksFiltersState;
}

function GroupByNoneTable({
    tasksWithErrors,
    toggleDeleteAllErrorsModal,
    etlStats,
    width,
    filters,
}: GroupByNoneTableProps) {
    const [rowSelection, setRowSelection] = useState({});
    const [columnVisibility, setColumnVisibility] = useState<Record<string, boolean>>({});
    const [columnFilters, setColumnFilters] = useState<ColumnFiltersState>([]);
    const [columnOrder, setColumnOrder] = useState<string[]>([]);
    const [columnPinning, setColumnPinning] = useState<ColumnPinningState>({});

    const columns = useGroupByNoneTableColumns(width);

    const data = useMemo<FlatError[]>(() => {
        const allErrors = flattenAllTasksErrors(tasksWithErrors, etlStats);
        const { searchText, nodeTags, shardNumbers, healthStatuses, taskTypes } = filters;

        return allErrors.filter((error) => {
            const matchesSearch =
                !searchText ||
                error.etlName.toLowerCase().includes(searchText.toLowerCase()) ||
                error.transformationName?.toLowerCase().includes(searchText.toLowerCase());
            const matchesNode = !nodeTags.length || nodeTags.includes(error.nodeTag);
            const matchesShard = !shardNumbers.length || shardNumbers.includes(String(error.shard));
            const matchesHealth = !healthStatuses.length || healthStatuses.includes(error.healthStatus);
            const matchesTaskType = !taskTypes.length || taskTypes.includes(error.etlType);

            return matchesSearch && matchesNode && matchesShard && matchesHealth && matchesTaskType;
        });
    }, [tasksWithErrors, etlStats, filters]);

    const table = useReactTable({
        data,
        columns,
        columnResizeMode: "onChange",
        state: {
            rowSelection,
            columnVisibility,
            columnFilters,
            columnOrder,
            columnPinning,
        },
        onColumnFiltersChange: setColumnFilters,
        getCoreRowModel: getCoreRowModel(),
        getSortedRowModel: getSortedRowModel(),
        getFilteredRowModel: getFilteredRowModel(),
        onRowSelectionChange: setRowSelection,
        onColumnVisibilityChange: setColumnVisibility,
        onColumnOrderChange: setColumnOrder,
        onColumnPinningChange: setColumnPinning,
    });

    return (
        <div className="d-flex flex-column h-100">
            <div className="d-flex justify-content-between mb-1 flex-shrink-0">
                <Button variant="danger" onClick={toggleDeleteAllErrorsModal}>
                    <Icon icon="trash" />
                    <span>Delete all errors</span>
                </Button>
                <TableDisplaySettings table={table} />
            </div>
            {data.length === 0 ? (
                <EmptySet>No tasks match the current filters.</EmptySet>
            ) : (
                <SizeGetter
                    isHeighRequired
                    className="flex-grow-1 min-h-0"
                    render={({ height }) => <VirtualTable table={table} heightInPx={height} />}
                />
            )}
        </div>
    );
}

const useGroupByNoneTableColumns = (availableWidth: number) => {
    const db = useAppSelector(databaseSelectors.activeDatabase);
    const bodyWidth = virtualTableUtils.getTableBodyWidth(availableWidth);
    const getSize = virtualTableUtils.getCellSizeProvider(bodyWidth - 70);

    const columns = useMemo<ColumnDef<FlatError>[]>(
        () => [
            {
                header: "Show",
                cell: CellValueButtonWrapper,
                size: 70,
            },
            {
                header: "Task",
                accessorKey: "etlName",
                cell: CellHyperlinkOngoingTaskValue,
                size: getSize(10),
                enableColumnFilter: false,
            },
            {
                header: "Script",
                accessorKey: "transformationName",
                cell: CellScriptNameWrapper,
                size: getSize(8),
                enableColumnFilter: false,
            },
            {
                header: "Error Type",
                accessorKey: "errorType",
                cell: CellErrorTypeWrapper,
                size: getSize(8),
            },
            {
                header: "Error Step",
                accessorKey: "Step",
                cell: CellErrorStepWrapper,
                size: getSize(8),
            },
            {
                header: "Document",
                accessorKey: "DocumentId",
                cell: HyperLinkDocumentCellValue,
                size: getSize(8),
            },
            {
                header: "Date",
                accessorKey: "CreatedAt",
                cell: DateFormatterCell,
                size: getSize(15),
            },
            {
                header: "Content",
                accessorKey: "Error",
                cell: CellWithCopyWrapper,
                size: getSize(db.isSharded ? 25 : 35),
                enableSorting: false,
            },
            {
                header: "Current task health",
                accessorKey: "healthStatus",
                cell: CellTaskHealthWrapper,
                size: getSize(8),
                enableColumnFilter: false,
                enableSorting: false,
            },
            {
                header: "Task type",
                accessorKey: "etlType",
                cell: CellEtlTypeWrapper,
                size: getSize(5),
                enableSorting: false,
                enableColumnFilter: false,
            },
            {
                header: "Node",
                accessorKey: "nodeTag",
                cell: CellNodeValueWrapper,
                size: getSize(3),
                enableColumnFilter: false,
                enableSorting: false,
            },
        ],
        []
    );

    if (db.isSharded) {
        columns.push({
            header: "Shard",
            accessorKey: "shard",
            cell: CellShardValueWrapper,
            size: getSize(3),
            enableColumnFilter: false,
            enableSorting: false,
        });
    }

    return columns;
};

const CellHyperlinkOngoingTaskValue = ({ getValue, row }: CellContext<FlatError, string>) => {
    const databaseName = useAppSelector(databaseSelectors.activeDatabaseName);
    const { appUrl } = useAppUrls();

    const getTaskLink = (value: string) => {
        if (typeof value !== "string" || row.original.taskId == null || row.original.etlType == null) {
            return null;
        }

        const { taskId, etlType } = row.original;

        switch (etlType) {
            case "Raven":
                return appUrl.forEditRavenEtl(databaseName, taskId);
            case "Sql":
                return appUrl.forEditSqlEtl(databaseName, taskId);
            case "Olap":
                return appUrl.forEditOlapEtl(databaseName, taskId);
            case "ElasticSearch":
                return appUrl.forEditElasticSearchEtl(databaseName, taskId);
            case "Kafka":
                return appUrl.forEditKafkaEtl(databaseName, taskId);
            case "RabbitMQ":
                return appUrl.forEditRabbitMqEtl(databaseName, taskId);
            case "AzureQueueStorage":
                return appUrl.forEditAzureQueueStorageEtl(databaseName, taskId);
            default:
                return assertUnreachable(etlType);
        }
    };

    const taskLink = getTaskLink(getValue());

    if (taskLink) {
        return (
            <div className="cell-value value-string">
                <a href={taskLink}>
                    <Icon icon="ongoing-tasks" /> {getValue()}
                </a>
            </div>
        );
    }

    return (
        <div className="cell-value value-string">
            <Icon icon="ongoing-tasks" />
            <CellValue value={getValue()} />
        </div>
    );
};

const CellScriptNameWrapper = ({ getValue }: CellContext<FlatError, string>) => {
    if (!getValue()) {
        return <CellValue value="-" />;
    }

    return (
        <div className="cell-value value-string">
            <Icon icon="console" />
            <CellValue value={getValue()} />
        </div>
    );
};

const healthStatusToBadge = (status: EtlHealthStatus): { bg: string; icon: IconName; label: string } => {
    switch (status) {
        case "Failed":
            return { bg: "danger", icon: "close", label: "Failed" };
        case "Impaired":
            return { bg: "warning", icon: "warning", label: "Impaired" };
        case "Healthy":
            return { bg: "success", icon: "check", label: "Healthy" };
        default:
            return { bg: "secondary", icon: "help", label: "Unknown" };
    }
};

const CellTaskHealthWrapper = ({ getValue }: CellContext<FlatError, EtlHealthStatus | null>) => {
    const { bg, icon, label } = healthStatusToBadge(getValue());
    return (
        <PopoverWithHoverWrapper message={getPopoverMessageForTaskHealth(getValue())}>
            <Badge bg={bg} className="rounded-pill cell-value">
                <Icon icon={icon} />
                {label}
            </Badge>
        </PopoverWithHoverWrapper>
    );
};

const getEtlTypeIcon = (value: StudioEtlType): IconName => {
    switch (value) {
        case "Raven":
            return "ravendb-etl";
        case "Sql":
            return "sql-etl";
        case "Olap":
            return "olap-etl";
        case "ElasticSearch":
            return "elastic-search-etl";
        case "Kafka":
            return "kafka-etl";
        case "AzureQueueStorage":
            return "azure-queue-storage-etl";
        case "RabbitMQ":
            return "rabbitmq-etl";
        default:
            return null;
    }
};

// TODO: Add new icons for different ETL types for higher versions (7.0+)
const CellEtlTypeWrapper = ({ getValue }: CellContext<FlatError, StudioEtlType>) => {
    const icon = getEtlTypeIcon(getValue());
    const label = getEtlTypeLabel(getValue());
    return (
        <div className="cell-value value-string">
            <Icon icon={icon} />
            <CellValue value={label} />
        </div>
    );
};

function getTaskHealthStatus(etlStats: EtlTaskStats[], etlName: string): EtlHealthStatus {
    const stats = etlStats.find((s) => s.TaskName === etlName)?.Stats ?? [];

    if (stats.some((s) => s.Statistics.HealthStatus === "Failed")) {
        return "Failed";
    }

    if (stats.some((s) => s.Statistics.HealthStatus === "Impaired")) {
        return "Impaired";
    }

    return "Healthy";
}

const getEtlTypeLabel = (etlType: StudioEtlType) => {
    switch (etlType) {
        case "Raven":
            return "RavenDB ETL";
        case "Sql":
            return "SQL";
        case "Olap":
            return "OLAP";
        case "ElasticSearch":
            return "ElasticSearch ETL";
        case "Kafka":
            return "Kafka ETL";
        case "AzureQueueStorage":
            return "Azure Queue Storage ETL";
        case "RabbitMQ":
            return "RabbitMQ ETL";
        default:
            return etlType;
    }
};

interface EtlTypeRichPanelItemProps {
    etlType: StudioEtlType;
}

const EtlTypeRichPanelItem = ({ etlType }: EtlTypeRichPanelItemProps) => {
    const icon = getEtlTypeIcon(etlType);

    const label = getEtlTypeLabel(etlType);

    return (
        <RichPanelDetailItem>
            <Icon icon={icon} />
            <span>{label}</span>
        </RichPanelDetailItem>
    );
};

function getTaskPillColor(stats: EtlTaskStats["Stats"]): TaskPillProps["color"] {
    if (stats.some((s) => s.Statistics.HealthStatus === "Failed")) {
        return "bg-danger";
    }

    if (stats.some((s) => s.Statistics.HealthStatus === "Impaired")) {
        return "bg-warning";
    }

    return "bg-success";
}

function flattenTransformationErrors(
    itemErrors: EtlTransformationWithErrors["itemErrors"],
    processErrors: EtlTransformationWithErrors["processErrors"]
) {
    return [
        ...itemErrors.map((e) => ({ ...e, errorType: "Item" as const })),
        ...processErrors.map((e) => ({ ...e, errorType: "Process" as const })),
    ];
}

function flattenAllTasksErrors(tasksWithErrors: EtlTaskWithErrors[], etlStats: EtlTaskStats[]): FlatError[] {
    return tasksWithErrors.flatMap((task) => {
        const taskStats = etlStats.find((s) => s.TaskName === task.etlName);
        const taskId = taskStats?.TaskId;
        const etlType = taskStats?.EtlType;

        return task.transformations.flatMap((transformation) => {
            const healthStatus =
                taskStats?.Stats.find((s) => s.TransformationName === transformation.transformationName)?.Statistics
                    .HealthStatus ?? null;

            return [
                ...transformation.itemErrors.map((e) => ({
                    ...e,
                    errorType: "Item" as const,
                    etlName: task.etlName,
                    transformationName: transformation.transformationName,
                    healthStatus,
                    taskId,
                    etlType,
                })),
                ...transformation.processErrors.map((e) => ({
                    ...e,
                    errorType: "Process" as const,
                    etlName: task.etlName,
                    transformationName: transformation.transformationName,
                    healthStatus,
                    taskId,
                    etlType,
                })),
            ];
        });
    });
}

interface DeleteTaskErrorsModalProps {
    toggle: () => void;
    etlName: string;
    errorsCount: number;
}

function DeleteTaskErrorsModal({ toggle, etlName, errorsCount }: DeleteTaskErrorsModalProps) {
    const db = useAppSelector(databaseSelectors.activeDatabase);
    const { tasksService } = useServices();

    const asyncGlobalSettings = useAsync(async () => await studioSettings.default.globalSettings(), []);

    const isRequireTypedConfirm =
        asyncGlobalSettings.result?.isRequireTypedConfirmationToDeleteEtlErrors.getValue() ?? true;

    const { confirmText, handleTextChange, isConfirmed } = useDeleteConfirmation(isRequireTypedConfirm);

    const asyncDeleteErrors = useAsyncCallback(async () => {
        try {
            if (db.isSharded) {
                const locations = DatabaseUtils.getLocations(db);
                await Promise.all(
                    locations.map((location) =>
                        tasksService.deleteEtlErrors(db.name, {
                            name: [etlName],
                            nodeTag: location.nodeTag,
                            shardNumber: location.shardNumber,
                        })
                    )
                );
            } else {
                await tasksService.deleteEtlErrors(db.name, { name: [etlName] });
            }
            toggle();
        } catch (e) {
            console.error(e);
        }
    });

    const toggleIsRequireTypedConfirm = async () => {
        if (!asyncGlobalSettings.result) {
            messagePublisher.reportError("Failed to load studio global settings");
            return;
        }

        asyncGlobalSettings.result.isRequireTypedConfirmationToDeleteEtlErrors.setValue(!isRequireTypedConfirm);
        await asyncGlobalSettings.execute();
    };

    return (
        <Modal show contentClassName="modal-border bulge-danger" size="lg">
            <Modal.Header closeButton onCloseClick={toggle} className="pb-0">
                <h3>
                    <Icon icon="trash" color="danger" />
                    <span>Delete all errors for {etlName} task?</span>
                </h3>
            </Modal.Header>
            <Modal.Body className="pt-0">
                <p>
                    You are about to delete all <b>{errorsCount} errors</b> from <b>{etlName}</b> task.
                </p>
                <RichAlert variant="info" icon="info">
                    While the current AI task errors will be deleted, a task in an <b>Error state</b> will not set back
                    to the <b>Normal</b> state.
                </RichAlert>
                {isRequireTypedConfirm && (
                    <FormGroup>
                        <FormLabel className="fw-bold">Type DELETE to confirm</FormLabel>
                        <Form.Control placeholder="DELETE" value={confirmText} onChange={handleTextChange} />
                    </FormGroup>
                )}
            </Modal.Body>
            <Modal.Footer className="hstack justify-content-between">
                <Switch selected={isRequireTypedConfirm} toggleSelection={toggleIsRequireTypedConfirm} color="primary">
                    Require typed confirmation
                </Switch>
                <div className="hstack gap-2 flex-grow-1 justify-content-end">
                    <Button variant="link" onClick={toggle} className="link-muted">
                        Cancel
                    </Button>
                    <ButtonWithSpinner
                        isSpinning={asyncDeleteErrors.loading}
                        variant="danger"
                        onClick={asyncDeleteErrors.execute}
                        className="rounded-pill"
                        disabled={!isConfirmed || asyncDeleteErrors.loading}
                    >
                        Delete
                    </ButtonWithSpinner>
                </div>
            </Modal.Footer>
        </Modal>
    );
}

interface DeleteAllErrorsModalProps {
    toggle: () => void;
    tasksWithErrors: EtlTaskWithErrors[];
}

function DeleteAllErrorsModal({ toggle, tasksWithErrors }: DeleteAllErrorsModalProps) {
    const db = useAppSelector(databaseSelectors.activeDatabase);
    const { tasksService } = useServices();

    const asyncGlobalSettings = useAsync(async () => await studioSettings.default.globalSettings(), []);

    const isRequireTypedConfirm =
        asyncGlobalSettings.result?.isRequireTypedConfirmationToDeleteEtlErrors.getValue() ?? true;

    const { confirmText, handleTextChange, isConfirmed } = useDeleteConfirmation(isRequireTypedConfirm);

    const asyncDeleteAllErrors = useAsyncCallback(async () => {
        const etlNames = tasksWithErrors.map((task) => task.etlName);

        try {
            if (db.isSharded) {
                const locations = DatabaseUtils.getLocations(db);
                await Promise.all(
                    locations.map((location) =>
                        tasksService.deleteEtlErrors(db.name, {
                            name: [tasksWithErrors[0].etlName],
                            nodeTag: location.nodeTag,
                            shardNumber: location.shardNumber,
                        })
                    )
                );
            } else {
                await tasksService.deleteEtlErrors(db.name, { name: etlNames });
            }
            toggle();
        } catch (e) {
            console.error(e);
        }
    });

    const toggleIsRequireTypedConfirm = async () => {
        if (!asyncGlobalSettings.result) {
            messagePublisher.reportError("Failed to load studio global settings");
            return;
        }

        asyncGlobalSettings.result.isRequireTypedConfirmationToDeleteEtlErrors.setValue(!isRequireTypedConfirm);
        await asyncGlobalSettings.execute();
    };

    return (
        <Modal show contentClassName="modal-border bulge-danger">
            <Modal.Header closeButton onCloseClick={toggle} className="pb-0">
                <h3>
                    <Icon icon="trash" color="danger" />
                    <span>Delete all errors?</span>
                </h3>
            </Modal.Header>
            <Modal.Body className="pt-0">
                <p>
                    You are about to delete errors from <b>{tasksWithErrors.length}</b>{" "}
                    {tasksWithErrors.length === 1 ? "task" : "tasks"}.
                </p>
                {isRequireTypedConfirm && (
                    <Form.Group>
                        <Form.Label className="fw-bold">Type DELETE to confirm</Form.Label>
                        <Form.Control placeholder="DELETE" value={confirmText} onChange={handleTextChange} />
                    </Form.Group>
                )}
            </Modal.Body>
            <Modal.Footer className="hstack justify-content-between">
                <Switch selected={isRequireTypedConfirm} toggleSelection={toggleIsRequireTypedConfirm} color="primary">
                    Require typed confirmation
                </Switch>
                <div className="hstack gap-2 flex-grow-1 justify-content-end">
                    <Button variant="link" onClick={toggle} className="link-muted">
                        Cancel
                    </Button>
                    <ButtonWithSpinner
                        isSpinning={asyncDeleteAllErrors.loading}
                        variant="danger"
                        onClick={asyncDeleteAllErrors.execute}
                        className="rounded-pill"
                        disabled={!isConfirmed || asyncDeleteAllErrors.loading}
                    >
                        Delete
                    </ButtonWithSpinner>
                </div>
            </Modal.Footer>
        </Modal>
    );
}

function useDeleteConfirmation(isRequireTypedConfirm: boolean) {
    const [confirmText, setConfirmText] = useState("");

    const handleTextChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        setConfirmText(e.target.value.trim());
    };

    return {
        confirmText,
        handleTextChange,
        isConfirmed: isRequireTypedConfirm ? confirmText === "DELETE" : true,
    };
}

interface EtlErrorDetailsSheetProps {
    error: FlatError;
}

interface SheetDetailRowProps {
    children: ReactNode;
    className?: string;
}

function SheetDetailRow({ children, className }: SheetDetailRowProps) {
    return (
        <div
            className={classNames(
                "d-flex justify-content-between align-items-center pb-1 border-bottom border-secondary",
                className
            )}
        >
            {children}
        </div>
    );
}

function EtlErrorDetailsSheet({ error }: EtlErrorDetailsSheetProps) {
    const { close } = useViewSheet();
    const dbName = useAppSelector(databaseSelectors.activeDatabaseName);

    const { bg, icon, label } = healthStatusToBadge(error.healthStatus ?? null);
    const stepIcon = error.Step ? getStepIcon(error.Step) : null;
    const etlTypeIcon = error.etlType ? getEtlTypeIcon(error.etlType) : null;
    const etlTypeLabel = error.etlType ? getEtlTypeLabel(error.etlType) : null;

    console.log("maxym error", error);

    return (
        <ViewSheet>
            <ViewSheet.Header>
                <h3 className="mb-0">
                    <Icon icon="warning" color="warning" />
                    ETL error details
                </h3>
            </ViewSheet.Header>
            <ViewSheet.Body className="m-2">
                <div className="vstack gap-3">
                    {error.etlName && error.transformationName && (
                        <SheetDetailRow>
                            <div className="small-label">Task name/Script name</div>
                            <div className="d-flex align-items-center">
                                {etlTypeIcon && <Icon icon={etlTypeIcon} />}
                                <div>
                                    {error.etlName}/{error.transformationName}
                                </div>
                            </div>
                        </SheetDetailRow>
                    )}

                    {error.EtlProcessName && (
                        <SheetDetailRow>
                            <div className="small-label">Task name/Script name</div>
                            <div className="d-flex align-items-center">
                                {etlTypeIcon && <Icon icon={etlTypeIcon} />}
                                <div>{error.EtlProcessName}</div>
                            </div>
                        </SheetDetailRow>
                    )}

                    {error.etlType && (
                        <SheetDetailRow>
                            <div className="small-label mb-1">Task type</div>
                            <div className="d-flex align-items-center">
                                {etlTypeIcon && <Icon icon={etlTypeIcon} />}
                                {etlTypeLabel}
                            </div>
                        </SheetDetailRow>
                    )}

                    {error.errorType && (
                        <SheetDetailRow>
                            <div className="small-label mb-1">Error type</div>
                            <Badge
                                bg={error.errorType === "Item" ? "secondary" : "info"}
                                className="rounded-pill cell-value"
                            >
                                <Icon icon={error.errorType === "Item" ? "tasks" : "hammer-driver"} />
                                {error.errorType === "Item" ? "Item Error" : "Process Error"}
                            </Badge>
                        </SheetDetailRow>
                    )}

                    {error.Step && (
                        <SheetDetailRow>
                            <div className="small-label mb-1">Error step</div>
                            <div>
                                {stepIcon && <Icon icon={stepIcon} />}
                                {error.Step}
                            </div>
                        </SheetDetailRow>
                    )}

                    {error.errorType === "Item" && error.DocumentId && (
                        <SheetDetailRow>
                            <div className="small-label mb-1">Document ID</div>
                            <CellDocumentValue value={error.DocumentId} databaseName={dbName} hasHyperlinkForIds />
                        </SheetDetailRow>
                    )}

                    {error.CreatedAt && (
                        <SheetDetailRow>
                            <div className="small-label mb-1">Date</div>
                            <div>{moment(error.CreatedAt).format(genUtils.dateFormat)}</div>
                        </SheetDetailRow>
                    )}

                    {error.healthStatus && (
                        <SheetDetailRow>
                            <div className="small-label mb-1">Current Task Health</div>
                            <Badge bg={bg} className="rounded-pill">
                                <Icon icon={icon} />
                                {label}
                            </Badge>
                        </SheetDetailRow>
                    )}

                    <SheetDetailRow className="border-bottom-0">
                        <div className="small-label">Localization</div>
                        <div className="d-flex align-items-center gap-2">
                            <div className="d-flex align-items-center justify-content-center">
                                <Icon icon="node" color="node" />
                                {error.nodeTag}
                            </div>
                            {error.shard != null && (
                                <div className="d-flex align-items-center justify-content-center">
                                    <Icon icon="shard" color="shard" />#{error.shard}
                                </div>
                            )}
                        </div>
                    </SheetDetailRow>

                    {error.Error && (
                        <div>
                            <Card className="bg-black p-2">
                                <pre className="text-wrap mb-0 small">{error.Error}</pre>
                            </Card>
                        </div>
                    )}
                </div>
            </ViewSheet.Body>
            <ViewSheet.Footer>
                <Button variant="secondary" onClick={close}>
                    <Icon icon="close" />
                    Close
                </Button>
            </ViewSheet.Footer>
        </ViewSheet>
    );
}

interface TasksFiltersState {
    searchText: string;
    nodeTags: string[];
    shardNumbers: string[];
    healthStatuses: EtlHealthStatus[];
    taskTypes: StudioEtlType[];
}

function useTasksFilters(initialSearchText = ""): [TasksFiltersState, (patch: Partial<TasksFiltersState>) => void] {
    const [filters, setFilters] = useState<TasksFiltersState>({
        searchText: initialSearchText,
        nodeTags: [],
        shardNumbers: [],
        healthStatuses: [],
        taskTypes: [],
    });

    const updateFilters = (patch: Partial<TasksFiltersState>) => setFilters((prev) => ({ ...prev, ...patch }));

    return [filters, updateFilters];
}
