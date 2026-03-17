import { OngoingEtlTaskNodeInfo, OngoingTaskInfo, OngoingTaskSharedInfo } from "components/models/tasks";
import { databaseLocationComparator } from "components/utils/common";
import IconName from "typings/server/icons";
import assertUnreachable from "components/utils/assertUnreachable";

export type EtlHealthStatus = Raven.Server.Documents.ETL.EtlProcessHealthStatus;

export function getTaskHealthStatus(etlStats: EtlTaskStats[], taskName: string): EtlHealthStatus {
    const stats = etlStats?.find((s) => s.TaskName === taskName)?.Stats ?? [];
    if (stats.some((s) => s.Statistics.HealthStatus === "Failed")) {
        return "Failed";
    }
    if (stats.some((s) => s.Statistics.HealthStatus === "Impaired")) {
        return "Impaired";
    }
    return "Healthy";
}

export function healthStatusToBadge(status: EtlHealthStatus): { bg: string; icon: IconName; label: string } {
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
}

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
