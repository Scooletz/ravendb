import React, { ReactNode } from "react";
import classNames from "classnames";
import { Icon } from "components/common/Icon";
import Badge from "react-bootstrap/Badge";
import Card from "react-bootstrap/Card";
import Button from "react-bootstrap/Button";
import { useViewSheet, ViewSheet } from "components/common/splitView/ViewSheet";
import CellDocumentValue from "components/common/virtualTable/cells/CellDocumentValue";
import { useAppSelector } from "components/store";
import { databaseSelectors } from "components/common/shell/databaseSliceSelectors";
import genUtils from "common/generalUtils";
import moment from "moment";
import {
    FlatError,
    getEtlTypeIcon,
    getEtlTypeLabel,
    getStepIcon,
    healthStatusToBadge,
} from "../utils/tasksErrorsUtils";

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

interface EtlErrorDetailsSheetProps {
    error: FlatError;
    allErrors?: FlatError[];
    initialIndex?: number;
}

export default function EtlErrorDetailsSheet({
    error: initialError,
    allErrors = [],
    initialIndex = 0,
}: EtlErrorDetailsSheetProps) {
    const { close } = useViewSheet();
    const dbName = useAppSelector(databaseSelectors.activeDatabaseName);

    const [currentIndex, setCurrentIndex] = React.useState(initialIndex);
    const error = allErrors.length > 0 ? allErrors[currentIndex] : initialError;

    const hasPrevious = currentIndex > 0;
    const hasNext = currentIndex < allErrors.length - 1;

    const { bg, icon, label } = healthStatusToBadge(error.healthStatus);
    const stepIcon = getStepIcon(error.Step);
    const etlTypeIcon = getEtlTypeIcon(error.etlType);
    const etlTypeLabel = getEtlTypeLabel(error.etlType);

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
                    {error.etlName && error.transformationName ? (
                        <SheetDetailRow>
                            <div className="small-label">Task name/Script name</div>
                            <div className="d-flex align-items-center">
                                {etlTypeIcon && <Icon icon={etlTypeIcon} />}
                                <div>
                                    {error.etlName}/{error.transformationName}
                                </div>
                            </div>
                        </SheetDetailRow>
                    ) : (
                        error.EtlProcessName && (
                            <SheetDetailRow>
                                <div className="small-label">Task name/Script name</div>
                                <div className="d-flex align-items-center">
                                    {etlTypeIcon && <Icon icon={etlTypeIcon} />}
                                    <div>{error.EtlProcessName}</div>
                                </div>
                            </SheetDetailRow>
                        )
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
            <ViewSheet.Footer className="d-flex justify-content-between">
                <div className="d-flex gap-2">
                    <Button variant="secondary" disabled={!hasPrevious} onClick={() => setCurrentIndex((i) => i - 1)}>
                        <Icon icon="arrow-left" />
                        Previous
                    </Button>
                    <Button variant="secondary" disabled={!hasNext} onClick={() => setCurrentIndex((i) => i + 1)}>
                        Next
                        <Icon icon="arrow-right" margin="ms-1" />
                    </Button>
                </div>
                <Button variant="secondary" onClick={close}>
                    <Icon icon="close" />
                    Close
                </Button>
            </ViewSheet.Footer>
        </ViewSheet>
    );
}
