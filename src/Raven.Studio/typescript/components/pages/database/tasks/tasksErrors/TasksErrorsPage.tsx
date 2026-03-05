import { AboutViewFloating, AboutViewHeading, AccordionItemWrapper } from "components/common/AboutView";
import React, { useCallback, useMemo, useState } from "react";
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
import PopoverWithHoverWrapper from "components/common/PopoverWithHoverWrapper";
import genUtils from "common/generalUtils";
import { CellWithCopyWrapper } from "components/common/virtualTable/cells/CellWithCopy";

type GroupByType = "task" | "none";
type EtlHealthStatus = Raven.Server.Documents.ETL.EtlProcessHealthStatus;

interface TasksFiltersState {
    searchText: string;
    nodeTag: string | null;
    shardNumber: string | null;
    healthStatus: EtlHealthStatus;
}

function useTasksFilters(): [TasksFiltersState, (patch: Partial<TasksFiltersState>) => void] {
    const [filters, setFilters] = useState<TasksFiltersState>({
        searchText: "",
        nodeTag: null,
        shardNumber: null,
        healthStatus: null,
    });

    const updateFilters = (patch: Partial<TasksFiltersState>) => setFilters((prev) => ({ ...prev, ...patch }));

    return [filters, updateFilters];
}

export default function TasksErrorsPage() {
    const db = useAppSelector(databaseSelectors.activeDatabase);
    const { databasesService } = useServices();

    const getEtlErrors = useCallback(async (location: databaseLocationSpecifier) => {
        return await databasesService.getEtlErrors(db.name, location);
    }, []);

    const { result: asyncFetchAllEtlErrors, loading: isLoadingEtlErrors } = useClusterWideAsync(getEtlErrors);

    const getEtlStats = useCallback(async (location: databaseLocationSpecifier) => {
        return await databasesService.getEtlStats(db.name, location);
    }, []);

    const { result: asyncFetchAllEtlStats, loading: isLoadingEtlStats } = useClusterWideAsync(getEtlStats);

    if (isLoadingEtlErrors || isLoadingEtlStats) {
        return <LoadingView />;
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
                    <AboutViewHeading marginBottom={0} title="Tasks Errors" icon="ongoing-tasks" iconAddon="cancel" />
                    <TasksErrorsAboutView />
                </div>
                {tasksWithErrors.length > 0 && <div>Analyze and get more details on your Tasks errors. </div>}
            </div>
            <TasksErrorsPageBody tasksWithErrors={tasksWithErrors} flattenAllEtlStats={flattenAllEtlStats} />
        </div>
    );
}

interface TasksErrorsPageBodyProps {
    tasksWithErrors: EtlTaskWithErrors[];
    flattenAllEtlStats: EtlTaskStats[];
}

const TasksErrorsPageBody = ({ tasksWithErrors, flattenAllEtlStats }: TasksErrorsPageBodyProps) => {
    const [selectedGroupByType, setSelectedGroupByType] = useState<GroupByType>("none");
    const [filters, updateFilters] = useTasksFilters();

    if (tasksWithErrors.length === 0) {
        return (
            <EmptySet>
                Your Ongoing tasks processes are running smoothly. You can monitor and resolve any future data issues
                right here.
            </EmptySet>
        );
    }

    return (
        <div className="d-flex flex-column flex-grow-1 min-h-0">
            <div className="border-1 align-items-center d-flex w-100 bg-dark border-secondary p-1 my-2 rounded flex-shrink-0">
                <Icon icon="tasks" />
                <span className="flex-grow">
                    <b>{tasksWithErrors.length ?? 0}</b> {tasksWithErrors.length === 1 ? "task" : "tasks"} with errors
                </span>
                <div className="d-flex gap-1">
                    {flattenAllEtlStats.map((etl, index) => (
                        <TaskPill color={getTaskPillColor(etl.Stats)} key={index} />
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
                    <GroupByNoneView tasksWithErrors={tasksWithErrors} etlStats={flattenAllEtlStats} />
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
        <Row className="gap-2">
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
                    isClearable
                    options={nodeOptions}
                    onChange={(o) => updateFilters({ nodeTag: o?.value ?? null })}
                />
            </Col>
            {db.isSharded && (
                <Col>
                    <div className="small-label ms-1 mb-1">Filter by shard</div>
                    <Select
                        isClearable
                        options={shardOptions}
                        onChange={(o) => updateFilters({ shardNumber: o?.value ?? null })}
                    />
                </Col>
            )}
            <Col>
                <div className="small-label ms-1 mb-1">Filter by task health</div>
                <Select
                    isClearable
                    options={taskHealthOptions}
                    onChange={(o) => updateFilters({ healthStatus: (o?.value ?? null) as EtlHealthStatus })}
                />
            </Col>
            <Col>
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
        const { searchText, nodeTag, shardNumber, healthStatus } = filters;

        return tasksWithErrors
            .map((task) => ({
                ...task,
                transformations: task.transformations.filter((t) => {
                    const allErrors = [...t.itemErrors, ...t.processErrors];

                    const matchesSearch =
                        !searchText ||
                        task.etlName.toLowerCase().includes(searchText.toLowerCase()) ||
                        t.transformationName.toLowerCase().includes(searchText.toLowerCase());
                    const matchesNode = !nodeTag || allErrors.some((e) => e.nodeTag === nodeTag);
                    const matchesShard = !shardNumber || allErrors.some((e) => e.shard === +shardNumber);
                    const matchesHealth =
                        !healthStatus ||
                        etlStats
                            .find((s) => s.TaskName === task.etlName)
                            ?.Stats.find((s) => s.TransformationName === t.transformationName)?.Statistics
                            .HealthStatus === healthStatus;

                    return matchesSearch && matchesNode && matchesShard && matchesHealth;
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
    const errorsCount = transformations.reduce(
        (acc, transformation) => acc + transformation.processErrors.length + transformation.itemErrors.length,
        0
    );

    const taskHealth = getTaskHealthStatus(etlStats, etlName);
    const { bg, icon, label } = healthStatusToBadge(taskHealth);
    return (
        <RichPanel>
            <RichPanelHeader>
                <RichPanelInfo>
                    <a href="#" className="fs-3">
                        {etlName}
                    </a>
                </RichPanelInfo>
                <RichPanelActions>
                    <Button variant="danger">
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
                <RichPanelDetailItem>
                    <Icon icon="ravendb-etl" />
                    <span>RavenDB ETL</span>
                </RichPanelDetailItem>
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
            },
            {
                header: "Node",
                cell: CellNodeValueWrapper,
                accessorKey: "nodeTag",
                size: getSize(5),
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
        });
    }

    return tasksErrorsPanelColumns;
};

type Step = "Transformation" | "Load" | "Extract";

const getStepIcon = (step: Step): IconName => {
    switch (step) {
        case "Transformation":
            return "replace";
        case "Load":
            return "import";
        case "Extract":
            return "export";
        default:
            return null;
    }
};

const CellErrorStepWrapper = ({ getValue }: CellContext<FlatError, Step>) => {
    const value = getValue();

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
    return (
        <>
            <Icon icon="node" color="node" />
            <CellValue value={getValue()} />
        </>
    );
};

const CellValueButtonWrapper = (args: any) => {
    const { value: isOpen, toggle: toggleIsOpen } = useBoolean(false);

    return (
        <>
            <Button variant="secondary" onClick={toggleIsOpen}>
                <Icon icon="preview" margin="m-0" />
            </Button>
            {/*<IndexErrorsModal*/}
            {/*    isOpen={isOpen}*/}
            {/*    toggleModal={toggleIsOpen}*/}
            {/*    errorDetails={args.row}*/}
            {/*    dataLength={args.table.options.data.length}*/}
            {/*    getRow={args.table.getRow}*/}
            {/*/>*/}
        </>
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
    color: "warning" | "danger" | "success";
}

const TaskPill = ({ color }: TaskPillProps) => {
    return <div className={classNames("tasks-pill rounded", color ? `bg-${color}` : "")}></div>;
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
}

function GroupByNoneView({ tasksWithErrors, etlStats }: GroupByNoneViewProps) {
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
}

function GroupByNoneTable({ tasksWithErrors, toggleDeleteAllErrorsModal, etlStats, width }: GroupByNoneTableProps) {
    const [rowSelection, setRowSelection] = useState({});
    const [columnVisibility, setColumnVisibility] = useState<Record<string, boolean>>({});
    const [columnFilters, setColumnFilters] = useState<ColumnFiltersState>([]);
    const [columnOrder, setColumnOrder] = useState<string[]>([]);
    const [columnPinning, setColumnPinning] = useState<ColumnPinningState>({});

    const columns = useGroupByNoneTableColumns(width);

    const data = useMemo<FlatError[]>(
        () => flattenAllTasksErrors(tasksWithErrors, etlStats),
        [tasksWithErrors, etlStats]
    );

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
            <div className="d-flex justify-content-between mb-3 flex-shrink-0">
                <Button variant="danger" onClick={toggleDeleteAllErrorsModal}>
                    <Icon icon="trash" />
                    <span>Delete all errors</span>
                </Button>
                <TableDisplaySettings table={table} />
            </div>
            <SizeGetter
                isHeighRequired
                className="flex-grow-1 min-h-0"
                render={({ height }) => <VirtualTable table={table} heightInPx={height} />}
            />
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
                cell: CellTaskNameWrapper,
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
            },
            {
                header: "Health",
                accessorKey: "healthStatus",
                cell: CellTaskHealthWrapper,
                size: getSize(8),
                enableColumnFilter: false,
            },
            {
                header: "Node",
                accessorKey: "nodeTag",
                cell: CellNodeValueWrapper,
                size: getSize(5),
                enableColumnFilter: false,
            },
        ],
        []
    );

    if (db.isSharded) {
        columns.push({
            header: "Shard",
            accessorKey: "shard",
            cell: CellShardValueWrapper,
            size: getSize(5),
            enableColumnFilter: false,
        });
    }

    return columns;
};

const CellTaskNameWrapper = ({ getValue }: CellContext<FlatError, string>) => (
    <div className="cell-value value-string">
        <Icon icon="ongoing-tasks" />
        <CellValue value={getValue()} />
    </div>
);

const CellScriptNameWrapper = ({ getValue }: CellContext<FlatError, string>) => (
    <div className="cell-value value-string">
        <Icon icon="console" />
        <CellValue value={getValue()} />
    </div>
);

const healthStatusToBadge = (status: EtlHealthStatus | null): { bg: string; icon: IconName; label: string } => {
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

function getTaskPillColor(stats: EtlTaskStats["Stats"]): TaskPillProps["color"] {
    if (stats.some((s) => s.Statistics.HealthStatus === "Failed")) {
        return "danger";
    }
    if (stats.some((s) => s.Statistics.HealthStatus === "Impaired")) {
        return "warning";
    }
    return "success";
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
                })),
                ...transformation.processErrors.map((e) => ({
                    ...e,
                    errorType: "Process" as const,
                    etlName: task.etlName,
                    transformationName: transformation.transformationName,
                    healthStatus,
                })),
            ];
        });
    });
}

interface DeleteAllErrorsModalProps {
    toggle: () => void;
    tasksWithErrors: EtlTaskWithErrors[];
}

function DeleteAllErrorsModal({ toggle, tasksWithErrors }: DeleteAllErrorsModalProps) {
    const db = useAppSelector(databaseSelectors.activeDatabase);
    const { databasesService } = useServices();

    const asyncGlobalSettings = useAsync(async () => await studioSettings.default.globalSettings(), []);

    const isRequireTypedConfirm =
        asyncGlobalSettings.result?.isRequireTypedConfirmationToDeleteEtlErrors.getValue() ?? true;

    const { confirmText, handleTextChange, isConfirmed } = useDeleteConfirmation(isRequireTypedConfirm);

    const asyncDeleteAllErrors = useAsyncCallback(async () => {
        const etlNames = tasksWithErrors.map((task) => task.etlName);

        //TODO: Something doesnt work, check that. api returns 200, but it fall into catch.
        try {
            if (db.isSharded) {
                const locations = DatabaseUtils.getLocations(db);
                await Promise.all(
                    locations.map((location) =>
                        databasesService.deleteEtlErrors(db.name, {
                            name: [tasksWithErrors[0].etlName],
                            nodeTag: location.nodeTag,
                            shardNumber: location.shardNumber,
                        })
                    )
                );
            } else {
                await databasesService.deleteEtlErrors(db.name, { name: etlNames });
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

        asyncGlobalSettings.result.isRequireTypedConfirmationToDeleteDocuments.setValue(!isRequireTypedConfirm);
        await asyncGlobalSettings.execute();
    };

    return (
        <Modal show contentClassName="modal-border bulge-danger">
            <Modal.Header closeButton onHide={toggle} className="pb-0">
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
