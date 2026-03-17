import React, { useCallback } from "react";
import {
    RichPanel,
    RichPanelActions,
    RichPanelDetailItem,
    RichPanelDetails,
    RichPanelHeader,
    RichPanelInfo,
    RichPanelSelect,
} from "components/common/RichPanel";
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
import { useAppUrls } from "hooks/useAppUrls";
import { OngoingTaskRavenEtlInfo } from "components/models/tasks";
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
    getPopoverMessageForTaskHealth,
    getTaskErrorCount,
    getTaskHealthStatus,
    healthStatusToBadge,
} from "./etlPanelUtils";

type RavenEtlPanelProps = BaseOngoingTaskPanelProps<OngoingTaskRavenEtlInfo> & {
    etlStats?: EtlTaskStats[];
    etlErrors?: EtlErrors[];
};

interface EtlPanelProps extends RavenEtlPanelProps {
    canEdit: boolean;
}

function Details(props: EtlPanelProps) {
    const { data, canEdit } = props;
    const connectionStringDefined = !!data.shared.destinationDatabase;
    const { appUrl } = useAppUrls();
    const databaseName = useAppSelector(databaseSelectors.activeDatabaseName);
    const connectionStringsUrl = appUrl.forConnectionStrings(databaseName, "Raven", data.shared.connectionStringName);

    return (
        <RichPanelDetails>
            <ConnectionStringItem
                connectionStringDefined={connectionStringDefined}
                canEdit={canEdit}
                connectionStringName={data.shared.connectionStringName}
                connectionStringsUrl={connectionStringsUrl}
            />
            {data.shared.destinationDatabase && (
                <RichPanelDetailItem label="Destination Database">
                    {data.shared.destinationDatabase}
                </RichPanelDetailItem>
            )}
            <RichPanelDetailItem label="Actual Destination URL">
                {data.shared.destinationUrl ? (
                    <a href={data.shared.destinationUrl} target="_blank">
                        {data.shared.destinationUrl}
                    </a>
                ) : (
                    <div>N/A</div>
                )}
            </RichPanelDetailItem>
            {data.shared.topologyDiscoveryUrls?.length > 0 && (
                <RichPanelDetailItem label="Topology Discovery URLs">
                    {data.shared.topologyDiscoveryUrls.join(", ")}
                </RichPanelDetailItem>
            )}
            <EmptyScriptsWarning task={data} />
        </RichPanelDetails>
    );
}

export function RavenEtlPanel(props: RavenEtlPanelProps & ICanShowTransformationScriptPreview) {
    const {
        data,
        showItemPreview,
        toggleSelection,
        isSelected,
        onTaskOperation,
        isDeleting,
        isTogglingState,
        etlStats,
        etlErrors,
    } = props;

    const hasDatabaseAdminAccess = useAppSelector(accessManagerSelectors.getHasDatabaseAdminAccess)();
    const { forCurrentDatabase, appUrl } = useAppUrls();
    const databaseName = useAppSelector(databaseSelectors.activeDatabaseName);

    const goToTaskErrors = appUrl.forTasksErrors(databaseName, data.shared.taskName);
    const canEdit = hasDatabaseAdminAccess && !data.shared.serverWide;
    const editUrl = forCurrentDatabase.editRavenEtl(data.shared.taskId)();

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
                        <Icon icon="ravendb-etl" />
                        RavenDB ETL
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
                        <Icon icon="ravendb-etl" />
                        RavenDB ETL
                    </span>
                </RichPanelDetailItem>
                <RichPanelDetailItem>
                    <Icon icon="manage-connection-strings" />
                    {data.shared.connectionStringName}
                </RichPanelDetailItem>
                <RichPanelDetailItem>
                    <Icon icon="database" />
                    {data.shared.destinationDatabase}
                </RichPanelDetailItem>
                <RichPanelDetailItem>
                    <Icon icon="link" addon="arrow-up" />
                    {data.shared.destinationUrl}
                </RichPanelDetailItem>
                <RichPanelDetailItem>
                    <Icon icon="cluster" addon="link" />
                    {data.shared.topologyDiscoveryUrls.join(", ")}
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
