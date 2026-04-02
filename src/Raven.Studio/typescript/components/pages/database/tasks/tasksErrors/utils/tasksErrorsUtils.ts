import genUtils from "common/generalUtils";
import IconName from "typings/server/icons";
import { RavenBadgeBgVariants } from "react-bootstrap/Badge";
import appUrl from "common/appUrl";
import assertUnreachable from "components/utils/assertUnreachable";
import EtlTaskStats = Raven.Server.Documents.ETL.Stats.EtlTaskStats;
import EtlErrors = Raven.Server.Documents.ETL.Stats.EtlErrors;

export type EtlErrorStep = Raven.Server.Documents.ETL.TaskErrorStep;
export type EtlHealthStatus = Raven.Server.Documents.ETL.EtlProcessHealthStatus;

export type GroupByType = "task" | "none";

export function getEtlEditLink(databaseName: string, taskId: number, etlType: StudioEtlType): string | null {
    if (taskId == null || etlType == null) {
        return null;
    }

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
}

export type EtlErrorsWithLocation = EtlErrors & { nodeTag: string; shard?: number };

export interface EtlTransformationWithErrors {
    transformationName: string;
    processErrors: (EtlErrors["ProcessErrors"][number] & { nodeTag: string; shard?: number })[];
    itemErrors: (EtlErrors["ItemErrors"][number] & { nodeTag: string; shard?: number })[];
}

export interface EtlTaskWithErrors {
    etlName: string;
    transformations: EtlTransformationWithErrors[];
}

export interface EtlError {
    etlName: string;
    transformationName: string;
    healthStatus: EtlHealthStatus;
    taskId?: number;
    etlType?: StudioEtlType;
}

export type FlatError = (
    | (EtlTransformationWithErrors["itemErrors"][number] & { errorType: "Item" })
    | (EtlTransformationWithErrors["processErrors"][number] & { errorType: "Process" })
) &
    EtlError;

export interface TasksFiltersState {
    searchText: string;
    nodeTags: string[];
    shardNumbers: string[];
    healthStatuses: EtlHealthStatus[];
    taskTypes: StudioEtlType[];
}

export function parseProcessName(processName: string): [etlName: string, transformationName: string] {
    const slashIndex = processName.indexOf("/");
    if (slashIndex === -1) {
        return [processName, ""];
    }
    return [processName.slice(0, slashIndex), processName.slice(slashIndex + 1)];
}

export function getTasksWithErrors(processes: EtlErrorsWithLocation[]): EtlTaskWithErrors[] {
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

export function flattenTransformationErrors(
    itemErrors: EtlTransformationWithErrors["itemErrors"],
    processErrors: EtlTransformationWithErrors["processErrors"]
) {
    return [
        ...itemErrors.map((e) => ({ ...e, errorType: "Item" as const })),
        ...processErrors.map((e) => ({ ...e, errorType: "Process" as const })),
    ];
}

export function flattenAllTasksErrors(tasksWithErrors: EtlTaskWithErrors[], etlStats: EtlTaskStats[]): FlatError[] {
    return tasksWithErrors.flatMap((task) => {
        const taskStats = etlStats.find((s) => s.TaskName === task.etlName);
        const taskId = taskStats?.TaskId;
        const etlType = taskStats?.EtlType as StudioEtlType;

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

export function getHealthStatusFromStats(stats: EtlTaskStats["Stats"]): EtlHealthStatus {
    if (stats.some((s) => s.Statistics.HealthStatus === "Failed")) {
        return "Failed";
    }

    if (stats.some((s) => s.Statistics.HealthStatus === "Impaired")) {
        return "Impaired";
    }

    return "Healthy";
}

export function getTaskHealthStatus(etlStats: EtlTaskStats[], etlName: string): EtlHealthStatus {
    const stats = etlStats.find((s) => s.TaskName === etlName)?.Stats ?? [];
    return getHealthStatusFromStats(stats);
}

export function getTaskPillColor(stats: EtlTaskStats["Stats"]): "bg-warning" | "bg-danger" | "bg-success" {
    const health = getHealthStatusFromStats(stats);
    if (health === "Failed") {
        return "bg-danger";
    }

    if (health === "Impaired") {
        return "bg-warning";
    }

    return "bg-success";
}

interface HealthStatusBadge {
    bg: RavenBadgeBgVariants;
    icon: IconName;
    label: EtlHealthStatus | "Unknown";
}
export function healthStatusToBadge(status?: EtlHealthStatus): HealthStatusBadge {
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

export function getStepIcon(step: EtlErrorStep): IconName {
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
}

export function getEtlTypeIcon(value: StudioEtlType): IconName {
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
}

export function getEtlTypeLabel(etlType: StudioEtlType): string {
    switch (etlType) {
        case "Raven":
            return "RavenDB ETL";
        case "Sql":
            return "SQL ETL";
        case "Snowflake":
            return "Snowflake ETL";
        case "Olap":
            return "OLAP ETL";
        case "ElasticSearch":
            return "ElasticSearch ETL";
        case "Kafka":
            return "Kafka ETL";
        case "RabbitMQ":
            return "RabbitMQ ETL";
        case "AzureQueueStorage":
            return "Azure Queue Storage ETL";
        case "AmazonSqs":
            return "Amazon SQS ETL";
        case "EmbeddingsGeneration":
            return "Embeddings Generation";
        case "GenAi":
            return "GenAI";
        default:
            return etlType;
    }
}

export function getPopoverMessageForErrorType(errorType: "Item" | "Process"): string {
    switch (errorType) {
        case "Item":
            return "Error that applies to the single document and doesn't affect the whole task process.";
        case "Process":
            return "Error that affects the process and the whole batch of documents.";
        default:
            genUtils.assertUnreachable(errorType);
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
            return genUtils.assertUnreachable(status);
    }
}

export const SHOW_WIDTH_SIZE = 70;
