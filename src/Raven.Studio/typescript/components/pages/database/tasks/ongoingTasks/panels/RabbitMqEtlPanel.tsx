import React, { useCallback } from "react";
import {
    BaseOngoingTaskPanelProps,
    ConnectionStringItem,
    EmptyScriptsWarning,
    ICanShowTransformationScriptPreview,
    OngoingTaskActions,
    OngoingTaskName,
    OngoingTaskResponsibleNode,
    OngoingTaskStatus,
    useTasksOperations,
} from "../../shared/shared";
import { OngoingTaskRabbitMqEtlInfo } from "components/models/tasks";
import { useAppUrls } from "hooks/useAppUrls";
import {
    RichPanel,
    RichPanelActions,
    RichPanelDetails,
    RichPanelHeader,
    RichPanelInfo,
    RichPanelSelect,
    RichPanelDetailItem,
} from "components/common/RichPanel";
import { OngoingEtlTaskDistribution } from "../partials/OngoingEtlTaskDistribution";
import Collapse from "react-bootstrap/Collapse";
import Form from "react-bootstrap/Form";
import { databaseSelectors } from "components/common/shell/databaseSliceSelectors";
import { useAppSelector } from "components/store";
import { accessManagerSelectors } from "components/common/shell/accessManagerSliceSelectors";
import { Icon } from "components/common/Icon";
import Button from "react-bootstrap/esm/Button";
import Badge from "react-bootstrap/Badge";
import { ProgressCircle } from "components/common/ProgressCircle";
import PopoverWithHoverWrapper from "components/common/PopoverWithHoverWrapper";
import {
    computeEtlPanelProgress,
    getTaskErrorCount,
    getTaskHealthStatus,
    healthStatusToBadge,
    getPopoverMessageForTaskHealth,
} from "./etlPanelUtils";

type RabbitMqEtlPanelProps = BaseOngoingTaskPanelProps<OngoingTaskRabbitMqEtlInfo> & {
    etlStats?: EtlTaskStats[];
    etlErrors?: EtlErrors[];
};

function Details(props: RabbitMqEtlPanelProps & { canEdit: boolean }) {
    const { data, canEdit } = props;
    const { appUrl } = useAppUrls();
    const databaseName = useAppSelector(databaseSelectors.activeDatabaseName);
    const connectionStringsUrl = appUrl.forConnectionStrings(
        databaseName,
        "RabbitMQ",
        data.shared.connectionStringName
    );

    return (
        <RichPanelDetails>
            <ConnectionStringItem
                connectionStringDefined
                canEdit={canEdit}
                connectionStringName={data.shared.connectionStringName}
                connectionStringsUrl={connectionStringsUrl}
            />
            <EmptyScriptsWarning task={data} />
        </RichPanelDetails>
    );
}

export function RabbitMqEtlPanel(props: RabbitMqEtlPanelProps & ICanShowTransformationScriptPreview) {
    const {
        data,
        showItemPreview,
        toggleSelection,
        isSelected,
        onTaskOperation,
        isDeleting,
        isTogglingState,
        etlErrors,
        etlStats,
    } = props;

    const hasDatabaseAdminAccess = useAppSelector(accessManagerSelectors.getHasDatabaseAdminAccess)();
    const canEdit = hasDatabaseAdminAccess && !data.shared.serverWide;

    const { forCurrentDatabase, appUrl } = useAppUrls();
    const editUrl = forCurrentDatabase.editRabbitMqEtl(data.shared.taskId)();
    const databaseName = useAppSelector(databaseSelectors.activeDatabaseName);
    const goToTaskErrors = appUrl.forTasksErrors(databaseName, data.shared.taskName);

    const { detailsVisible, toggleDetails, onEdit } = useTasksOperations(editUrl, props);

    const showPreview = useCallback(
        (transformationName: string) => {
            showItemPreview(data, transformationName);
        },
        [data, showItemPreview]
    );

    const taskHealth = getTaskHealthStatus(etlStats ?? [], data.shared.taskName);
    const { bg, icon, label } = healthStatusToBadge(taskHealth);
    const errorCount = getTaskErrorCount(etlErrors ?? [], data.shared.taskName);
    const etlProgress = computeEtlPanelProgress(data);

    return (
        <RichPanel>
            <RichPanelHeader>
                <RichPanelInfo>
                    {canEdit && (
                        <RichPanelSelect>
                            <Form.Check
                                type="checkbox"
                                onChange={(e) => toggleSelection(e.currentTarget.checked, data.shared)}
                                checked={isSelected(data.shared.taskId)}
                            />
                        </RichPanelSelect>
                    )}
                    <OngoingTaskName task={data} canEdit={canEdit} editUrl={editUrl} />
                </RichPanelInfo>
                <RichPanelActions>
                    <span>
                        <Icon icon="rabbitmq-etl" />
                        RabbitMQ ETL
                    </span>
                    <OngoingTaskResponsibleNode task={data} />
                    <OngoingTaskStatus
                        task={data}
                        canEdit={canEdit}
                        onTaskOperation={onTaskOperation}
                        isTogglingState={isTogglingState(data.shared.taskId)}
                    />
                    <OngoingTaskActions
                        task={data}
                        canEdit={canEdit}
                        onEdit={onEdit}
                        onTaskOperation={onTaskOperation}
                        toggleDetails={toggleDetails}
                        isDeleting={isDeleting(data.shared.taskId)}
                        isDetailsOpen={detailsVisible}
                    />
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
                        <Icon icon={detailsVisible ? "fold" : "unfold"} margin="m-0" />
                    </Button>
                </RichPanelDetailItem>
                <RichPanelDetailItem>
                    <span>
                        <Icon icon="rabbitmq-etl" />
                        RabbitMQ ETL
                    </span>
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
                {errorCount > 0 && (
                    <RichPanelDetailItem>
                        <a href={goToTaskErrors} className="d-flex gap-1 align-items-center">
                            <Icon icon="warning" color="danger" margin="m-0" />
                            <span className="text-danger">Errors</span>
                            <b>{errorCount}</b>
                        </a>
                    </RichPanelDetailItem>
                )}
                <RichPanelDetailItem>
                    <ProgressCircle
                        state={etlProgress.state}
                        icon={etlProgress.icon}
                        progress={etlProgress.progress}
                        inline
                    >
                        {etlProgress.label}
                    </ProgressCircle>
                </RichPanelDetailItem>
            </RichPanelDetails>
            <Collapse in={detailsVisible}>
                <div>
                    <Details {...props} canEdit={canEdit} />
                    <OngoingEtlTaskDistribution task={data} showPreview={showPreview} />
                </div>
            </Collapse>
        </RichPanel>
    );
}
