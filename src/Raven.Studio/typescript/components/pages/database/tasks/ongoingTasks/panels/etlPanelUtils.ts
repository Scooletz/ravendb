import { useCallback } from "react";
import {
    AnyEtlOngoingTaskInfo,
    OngoingEtlTaskNodeInfo,
    OngoingTaskInfo,
    OngoingTaskSharedInfo,
} from "components/models/tasks";
import { databaseLocationComparator } from "components/utils/common";
import IconName from "typings/server/icons";
import assertUnreachable from "components/utils/assertUnreachable";
import { useAppSelector } from "components/store";
import { accessManagerSelectors } from "components/common/shell/accessManagerSliceSelectors";
import { databaseSelectors } from "components/common/shell/databaseSliceSelectors";
import { useAppUrls } from "hooks/useAppUrls";
import {
    BaseOngoingTaskPanelProps,
    ICanShowTransformationScriptPreview,
    useTasksOperations,
} from "../../shared/shared";
import EtlTaskStats = Raven.Server.Documents.ETL.Stats.EtlTaskStats;
import EtlErrors = Raven.Server.Documents.ETL.Stats.EtlErrors;
import {
    getTaskHealthStatus,
    healthStatusToBadge,
} from "components/pages/database/tasks/tasksErrors/utils/tasksErrorsUtils";

export type EtlHealthStatus = Raven.Server.Documents.ETL.EtlProcessHealthStatus;

export function getPopoverMessageForTaskHealth(status: EtlHealthStatus): string {
    switch (status) {
        case "Healthy":
            return "Your task is in a good health state with none to minor count of errors.";
        case "Impaired":
            return "Your task is mildly affected with errors. It needs your attention.";
        case "Failed":
            return "Your task needs your attention as it's severely affected with errors.";
        default:
            return assertUnreachable(status);
    }
}

export interface EtlPanelProgress {
    state: "success" | "running";
    icon?: IconName;
    progress?: number;
    label: string;
}

export function getTaskErrorCount(etlErrors: EtlErrors[], taskName: string): number {
    return etlErrors
        .filter((e) => {
            const slashIndex = e.ProcessName.indexOf("/");
            const etlName = slashIndex === -1 ? e.ProcessName : e.ProcessName.slice(0, slashIndex);
            return etlName === taskName;
        })
        .reduce((acc, e) => acc + e.ProcessErrors.length + e.ItemErrors.length, 0);
}

export function computeEtlPanelProgress(
    data: OngoingTaskInfo<OngoingTaskSharedInfo, OngoingEtlTaskNodeInfo>
): EtlPanelProgress {
    const disabled = data.shared.taskState === "Disabled";

    const responsibleNodeInfos = data.nodesInfo.filter(
        (nodeInfo) =>
            nodeInfo.details && data.responsibleLocations.some((l) => databaseLocationComparator(l, nodeInfo.location))
    );

    const allProgress = responsibleNodeInfos.flatMap((nodeInfo) => nodeInfo.etlProgress ?? []);

    if (allProgress.length === 0) {
        return { state: "running", icon: disabled ? "stop" : null, label: disabled ? "Disabled" : "?" };
    }

    if (allProgress.every((x) => x.completed) && data.shared.taskState === "Enabled") {
        return { state: "success", icon: "check", label: "up to date" };
    }

    const totalItems = allProgress.reduce((acc, p) => acc + p.global.total, 0);
    const totalProcessed = allProgress.reduce((acc, p) => acc + p.global.processed, 0);
    const percentage = totalItems === 0 ? 1 : Math.floor((totalProcessed * 100) / totalItems) / 100;
    const anyDisabled = allProgress.some((x) => x.disabled);

    return {
        state: "running",
        icon: anyDisabled ? "stop" : null,
        progress: percentage,
        label: anyDisabled ? "Disabled" : "Running",
    };
}

export type EtlPanelBaseProps<T extends AnyEtlOngoingTaskInfo> = BaseOngoingTaskPanelProps<T> &
    ICanShowTransformationScriptPreview & {
        etlStats?: EtlTaskStats[];
        etlErrors?: EtlErrors[];
    };

export function useEtlPanel<T extends AnyEtlOngoingTaskInfo>(props: EtlPanelBaseProps<T>, editUrl: string) {
    const { data, showItemPreview, etlStats, etlErrors } = props;

    const hasDatabaseAdminAccess = useAppSelector(accessManagerSelectors.getHasDatabaseAdminAccess)();
    const { appUrl } = useAppUrls();
    const databaseName = useAppSelector(databaseSelectors.activeDatabaseName);

    const canEdit = hasDatabaseAdminAccess && !data.shared.serverWide;
    const goToTaskErrors = appUrl.forTasksErrors(databaseName, { taskName: data.shared.taskName });

    const { detailsVisible, toggleDetails, onEdit } = useTasksOperations(editUrl, props);

    const showPreview = useCallback(
        (transformationName: string) => showItemPreview(data, transformationName),
        [data, showItemPreview]
    );

    const taskHealth = getTaskHealthStatus(etlStats ?? [], data.shared.taskName);
    const healthBadge = healthStatusToBadge(taskHealth);
    const errorCount = getTaskErrorCount(etlErrors ?? [], data.shared.taskName);
    const etlProgress = computeEtlPanelProgress(data);

    return {
        canEdit,
        goToTaskErrors,
        detailsVisible,
        toggleDetails,
        onEdit,
        showPreview,
        taskHealth,
        healthBadge,
        errorCount,
        etlProgress,
    };
}
