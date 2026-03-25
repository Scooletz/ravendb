import React from "react";
import { CellContext } from "@tanstack/react-table";
import { Icon } from "components/common/Icon";
import CellValue from "components/common/virtualTable/cells/CellValue";
import { CellWithCopyWrapper } from "components/common/virtualTable/cells/CellWithCopy";
import CellDocumentValue from "components/common/virtualTable/cells/CellDocumentValue";
import PopoverWithHoverWrapper from "components/common/PopoverWithHoverWrapper";
import Badge from "react-bootstrap/Badge";
import Button from "react-bootstrap/Button";
import { useViewSheet } from "components/common/splitView/ViewSheet";
import { useAppSelector } from "components/store";
import { databaseSelectors } from "components/common/shell/databaseSliceSelectors";
import { useAppUrls } from "hooks/useAppUrls";
import assertUnreachable from "components/utils/assertUnreachable";
import clusterDashboard from "viewmodels/resources/clusterDashboard";
import EtlErrorDetailsSheet from "./EtlErrorDetailsSheet";
import {
    FlatError,
    EtlHealthStatus,
    EtlErrorStep,
    healthStatusToBadge,
    getStepIcon,
    getEtlTypeIcon,
    getEtlTypeLabel,
    getPopoverMessageForErrorType,
    getPopoverMessageForTaskHealth,
} from "../utils/tasksErrorsUtils";

export { CellWithCopyWrapper };

export const CellErrorStepWrapper = ({ getValue }: CellContext<FlatError, EtlErrorStep>) => {
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

export const CellErrorTypeWrapper = ({ getValue }: CellContext<FlatError, "Item" | "Process">) => {
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

export const CellShardValueWrapper = ({ getValue }: CellContext<FlatError, string>) => {
    return (
        <>
            <Icon icon="shard" color="shard" />
            <CellValue value={"#" + getValue()} />
        </>
    );
};

interface NodeCircleProps {
    nodeTag: string;
}

export function NodeCircle({ nodeTag }: NodeCircleProps) {
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

export const CellNodeValueWrapper = ({ getValue }: CellContext<FlatError, string>) => {
    return <NodeCircle nodeTag={getValue()} />;
};

export const CellValueButtonWrapper = (args: CellContext<FlatError, unknown>) => {
    const { open } = useViewSheet();

    const handleOpenSheet = () => {
        const allRows = args.table.getRowModel().rows;
        const allErrors = allRows.map((r) => r.original);
        const currentIndex = allRows.findIndex((r) => r.id === args.row.id);

        open({
            component: (
                <EtlErrorDetailsSheet
                    error={args.row.original}
                    allErrors={allErrors}
                    initialIndex={currentIndex >= 0 ? currentIndex : 0}
                />
            ),
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

export const HyperLinkDocumentCellValue = ({ getValue }: Pick<CellContext<FlatError, unknown>, "getValue">) => {
    const dbName = useAppSelector(databaseSelectors.activeDatabaseName);

    if (!getValue()) {
        return <CellValue value="-" />;
    }

    return <CellDocumentValue value={getValue()} databaseName={dbName} hasHyperlinkForIds />;
};

export const CellHyperlinkOngoingTaskValue = ({ getValue, row }: CellContext<FlatError, string>) => {
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
            case "AmazonSqs":
                return appUrl.forEditAmazonSqsEtl(databaseName, taskId);
            case "Snowflake":
                return appUrl.forEditSnowflakeEtl(databaseName, taskId);
            case "EmbeddingsGeneration":
                return appUrl.forEditEmbeddingsGeneration(databaseName, taskId);
            case "GenAi":
                return appUrl.forEditGenAi(databaseName, taskId);
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

export const CellScriptNameWrapper = ({ getValue }: CellContext<FlatError, string>) => {
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

export const CellTaskHealthWrapper = ({ getValue }: CellContext<FlatError, EtlHealthStatus | null>) => {
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

// TODO: Add new icons for different ETL types for higher versions (7.0+)
export const CellEtlTypeWrapper = ({ getValue }: CellContext<FlatError, StudioEtlType>) => {
    const icon = getEtlTypeIcon(getValue());
    const label = getEtlTypeLabel(getValue());
    return (
        <div className="cell-value value-string">
            <Icon icon={icon} />
            <CellValue value={label} />
        </div>
    );
};
