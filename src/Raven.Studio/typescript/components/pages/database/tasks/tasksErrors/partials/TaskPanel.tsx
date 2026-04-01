import React, { useMemo } from "react";
import { Icon } from "components/common/Icon";
import Button from "react-bootstrap/Button";
import Badge from "react-bootstrap/Badge";
import Card from "react-bootstrap/Card";
import Collapse from "react-bootstrap/Collapse";
import {
    ColumnDef,
    getCoreRowModel,
    getFilteredRowModel,
    getSortedRowModel,
    useReactTable,
} from "@tanstack/react-table";
import { virtualTableUtils } from "components/common/virtualTable/utils/virtualTableUtils";
import { virtualTableConstants } from "components/common/virtualTable/utils/virtualTableConstants";
import VirtualTable from "components/common/virtualTable/VirtualTable";
import SizeGetter from "components/common/SizeGetter";
import { CellWithCopyWrapper } from "components/common/virtualTable/cells/CellWithCopy";
import {
    RichPanel,
    RichPanelActions,
    RichPanelDetailItem,
    RichPanelDetails,
    RichPanelHeader,
    RichPanelInfo,
} from "components/common/RichPanel";
import useBoolean from "hooks/useBoolean";
import PopoverWithHoverWrapper from "components/common/PopoverWithHoverWrapper";
import { useAppSelector } from "components/store";
import { databaseSelectors } from "components/common/shell/databaseSliceSelectors";
import {
    EtlTaskWithErrors,
    EtlTransformationWithErrors,
    flattenTransformationErrors,
    getEtlEditLink,
    getEtlTypeIcon,
    getEtlTypeLabel,
    getPopoverMessageForTaskHealth,
    getTaskHealthStatus,
    healthStatusToBadge,
    SHOW_WIDTH_SIZE,
} from "../utils/tasksErrorsUtils";
import {
    CellAffectedDocumentsWrapper,
    CellDateWithRelativeTimeWrapper,
    CellErrorStepWrapper,
    CellErrorTypeWrapper,
    CellNodeValueWrapper,
    CellShardValueWrapper,
    CellValueButtonWrapper,
    HyperLinkDocumentCellValue,
} from "./TasksErrorsCells";
import { DeleteTaskErrorsModal } from "./DeleteModals";
import { accessManagerSelectors } from "components/common/shell/accessManagerSliceSelectors";
import { DatabaseAccessPopover } from "components/common/DatabaseAccessPopover";
import EtlTaskStats = Raven.Server.Documents.ETL.Stats.EtlTaskStats;

interface EtlTypeRichPanelItemProps {
    etlType: StudioEtlType;
}

function EtlTypeRichPanelItem({ etlType }: EtlTypeRichPanelItemProps) {
    const icon = getEtlTypeIcon(etlType);
    const label = getEtlTypeLabel(etlType);

    return (
        <RichPanelDetailItem>
            <Icon icon={icon} />
            <span>{label}</span>
        </RichPanelDetailItem>
    );
}

function useTasksErrorsPanelTableColumns(availableWidth: number, hasProcessErrors: boolean) {
    const db = useAppSelector(databaseSelectors.activeDatabase);
    const bodyWidth = virtualTableUtils.getTableBodyWidth(availableWidth);
    const getSize = virtualTableUtils.getCellSizeProvider(bodyWidth - SHOW_WIDTH_SIZE);

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
                cell: CellDateWithRelativeTimeWrapper,
                accessorKey: "CreatedAt",
                size: getSize(20),
            },
            ...(hasProcessErrors
                ? [
                      {
                          header: "Affected Documents",
                          cell: CellAffectedDocumentsWrapper,
                          accessorKey: "AffectedDocumentsCount",
                          size: getSize(10),
                      },
                  ]
                : []),
            {
                header: "Error",
                cell: CellWithCopyWrapper,
                accessorKey: "Error",
                size: getSize((db.isSharded ? 40 : 45) - (hasProcessErrors ? 10 : 0)),
                enableSorting: false,
            },
            {
                header: "Node",
                cell: CellNodeValueWrapper,
                accessorKey: "nodeTag",
                size: getSize(3),
                enableSorting: false,
                enableColumnFilter: false,
            },
        ],
        [getSize, hasProcessErrors]
    );

    if (db.isSharded) {
        tasksErrorsPanelColumns.push({
            header: "Shard",
            cell: CellShardValueWrapper,
            accessorKey: "shard",
            size: getSize(3),
            enableSorting: false,
        });
    }

    return tasksErrorsPanelColumns;
}

interface NestedTaskPanelDetailsTableProps {
    width: number;
    itemErrors: EtlTransformationWithErrors["itemErrors"];
    processErrors: EtlTransformationWithErrors["processErrors"];
}

function NestedTaskPanelDetailsTable({ width, itemErrors, processErrors }: NestedTaskPanelDetailsTableProps) {
    const columns = useTasksErrorsPanelTableColumns(width, processErrors.length > 0);
    const data = useMemo(() => flattenTransformationErrors(itemErrors, processErrors), [itemErrors, processErrors]);

    const tasksErrorsPanelTable = useReactTable({
        data,
        columns,
        columnResizeMode: "onChange",
        getCoreRowModel: getCoreRowModel(),
        getSortedRowModel: getSortedRowModel(),
        getFilteredRowModel: getFilteredRowModel(),
    });

    return (
        <VirtualTable
            table={tasksErrorsPanelTable}
            heightInPx={400}
            rowHeightInPx={virtualTableConstants.doubleLineRowHeightInPx}
        />
    );
}

interface NestedTaskPanelDetailsProps extends EtlTransformationWithErrors {
    width: number;
}

function NestedTaskPanelDetails({ width, transformationName, processErrors, itemErrors }: NestedTaskPanelDetailsProps) {
    const { value: isNestedDetailsVisible, toggle: toggleNestedDetailsVisible } = useBoolean(true);

    const totalErrors = processErrors.length + itemErrors.length;

    return (
        <Card className="bg-black p-3">
            <div className="d-flex w-100 gap-2 align-items-center">
                <span className="h4 mb-0">{transformationName}</span>
                <div className="flex-grow">
                    <Icon icon="warning" color="danger" />
                    <span>Errors</span> <b>{totalErrors}</b>
                </div>
                <Button variant="secondary" onClick={toggleNestedDetailsVisible}>
                    <Icon icon={isNestedDetailsVisible ? "collapse-vertical" : "expand-vertical"} margin="m-0" />
                </Button>
            </div>
            <Collapse in={isNestedDetailsVisible} unmountOnExit mountOnEnter>
                <div className="mt-3">
                    <NestedTaskPanelDetailsTable width={width} itemErrors={itemErrors} processErrors={processErrors} />
                </div>
            </Collapse>
        </Card>
    );
}

interface TaskPanelProps extends EtlTaskWithErrors {
    etlStats: EtlTaskStats[];
}

export function TaskPanel({ etlName, transformations, etlStats }: TaskPanelProps) {
    const hasDatabaseWriteAccess = useAppSelector(accessManagerSelectors.getHasDatabaseWriteAccess)();
    const databaseName = useAppSelector(databaseSelectors.activeDatabaseName);
    const { value: isDetailsVisible, toggle: toggleDetails } = useBoolean(true);
    const { value: isDeleteModalOpen, toggle: toggleDeleteModal } = useBoolean(false);

    const errorsCount = transformations.reduce(
        (acc, transformation) => acc + transformation.processErrors.length + transformation.itemErrors.length,
        0
    );

    const taskHealth = getTaskHealthStatus(etlStats, etlName);
    const { bg, icon, label } = healthStatusToBadge(taskHealth);
    const taskStats = etlStats.find((s) => s.TaskName === etlName);
    const etlType = taskStats?.EtlType as StudioEtlType;
    const taskId = taskStats?.TaskId;

    const taskLink = getEtlEditLink(databaseName, taskId, etlType);

    return (
        <>
            <RichPanel>
                <RichPanelHeader>
                    <RichPanelInfo>
                        <a href={taskLink ?? "#"} className="fs-3">
                            {etlName}
                        </a>
                    </RichPanelInfo>
                    <RichPanelActions>
                        <DatabaseAccessPopover accessRequired="DatabaseReadWrite">
                            <Button variant="danger" disabled={!hasDatabaseWriteAccess} onClick={toggleDeleteModal}>
                                <Icon icon="trash" />
                                Delete errors
                            </Button>
                        </DatabaseAccessPopover>
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
                    <RichPanelDetailItem contentClassName="d-flex gap-1 align-items-center">
                        <Icon icon="warning" color="danger" margin="m-0" />
                        <span>Errors</span> <b>{errorsCount}</b>
                    </RichPanelDetailItem>
                    <RichPanelDetailItem contentClassName="d-flex gap-1 align-items-center">
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
}
