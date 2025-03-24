import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { Switch } from "components/common/Checkbox";
import { FormGroup } from "components/common/Form";
import useBoolean from "components/hooks/useBoolean";
import { useEffect, useMemo, useState } from "react";
import { useServices } from "components/hooks/useServices";
import serverNotificationCenterClient from "common/serverNotificationCenterClient";
import { TextColor } from "components/models/common";
import endpoints from "endpoints";
import Button from "react-bootstrap/Button";
import { Icon } from "components/common/Icon";

export function SetupWizardFinishStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { value: isShowLogs, toggle: toggleIsShowLogs } = useBoolean(true); // TODO set to false

    const {
        nodeAddressStep: { nodes },
    } = useWatch({ control });

    const websocket = useMemo(() => new serverNotificationCenterClient(), []);

    const { databasesService } = useServices();

    const [readme, setReadme] = useState<string>();
    const [status, setStatus] = useState<string>();
    const [logs, setLogs] = useState<{ message: string; color?: TextColor }[]>([]);

    const handleWebSocketOperation = (operation: Raven.Server.NotificationCenter.Notifications.OperationChanged) => {
        if (operation.TaskType === "Setup") {
            let dto: Raven.Server.Commercial.SetupProgressAndResult = null;

            switch (operation.State.Status) {
                case "Completed":
                    dto = operation.State.Result as Raven.Server.Commercial.SetupProgressAndResult;
                    setReadme(dto.Readme);
                    setStatus("Completed");
                    break;
                case "InProgress":
                    dto = operation.State.Progress as Raven.Server.Commercial.SetupProgressAndResult;
                    break;
                case "Faulted": {
                    const failure = operation.State
                        .Result as Raven.Client.Documents.Operations.OperationExceptionResult;
                    setLogs((prev) => [...prev, { message: failure.Message, color: "danger" }]);
                    setLogs((prev) => [...prev, { message: failure.Error, color: "danger" }]);
                    setStatus("Faulted");
                }
            }

            if (dto) {
                switch (operation.TaskType) {
                    case "Setup":
                        setLogs(dto.Messages.map((x) => ({ message: x })));
                        break;
                }
            }
        }
    };

    const getUnsecuredDto = (): Raven.Server.Commercial.UnsecuredSetupInfo => {
        const nodesInfo: Record<string, Raven.Server.Commercial.NodeInfo> = {};
        nodes.forEach((node) => {
            nodesInfo[node.nodeTag] = {
                Addresses: node.ipAddress.map((x) => x.ipAddress),
                Port: node.httpPort,
                TcpPort: node.tcpPort,
                PublicServerUrl: null,
                PublicTcpServerUrl: null,
                ExternalIpAddress: null,
                ExternalPort: null,
                ExternalTcpPort: null,
            };
        });

        // TODO get from form
        return {
            EnableExperimentalFeatures: false,
            LocalNodeTag: nodes[0].nodeTag,
            Environment: "None",
            ZipOnly: false,
            NodeSetupInfos: nodesInfo,
        };
    };

    useEffect(() => {
        const finish = async () => {
            const $form = $("#setupForm");
            const $downloadOptions = $("[name=Options]", $form);

            const operationId = await databasesService.getNextOperationId(null);

            const operationPart = "?operationId=" + operationId;

            // TODO switch url and dto based on the operation type
            const url = endpoints.global.setup.setupUnsecuredPackage;
            const dto = getUnsecuredDto();

            $form.attr("action", url + operationPart);
            $downloadOptions.val(JSON.stringify(dto));
            $form.submit();

            websocket.watchOperation(operationId, handleWebSocketOperation);
        };

        finish();
    }, []);

    return (
        <div>
            <h2>Configuration in process</h2>
            <p>Please, wait a moment. Your RavenDB will be ready in no time. </p>
            <h3>Status: {status}</h3>
            {readme && (
                <pre>
                    <b>Readme:</b>
                    {readme}
                </pre>
            )}
            <FormGroup className="mt-4">
                <Switch selected={isShowLogs} toggleSelection={toggleIsShowLogs} color="primary">
                    Show configuration log
                </Switch>
            </FormGroup>
            {isShowLogs && (
                <pre>
                    {logs.map((message, idx) => (
                        <div key={idx} className={message.color ? `text-${message.color}` : ""}>
                            {message.message}
                        </div>
                    ))}
                </pre>
            )}
            <div className="mt-2 p-2 panel-bg-1 rounded border border-secondary">TODO</div>
            <div className="d-none">
                <form method="post" target="hidden-form" id="setupForm">
                    <input type="hidden" name="Options" />
                </form>
            </div>
        </div>
    );
}

export function SetupWizardFinishStepFooter() {
    const { setupWizardService } = useServices();

    const redirectToStudio = () => {
        // TODO get href
        window.location.href = `http://${document.location.hostname}:8080`;
    };

    const resetServer = async (waitBeforeRedirectInMs: number) => {
        await setupWizardService.finishSetup();
        setTimeout(() => {
            redirectToStudio();
        }, waitBeforeRedirectInMs);
    };

    return (
        <div className="d-flex justify-content-end">
            <Button variant="primary" onClick={() => resetServer(2000)} className="mt-2 rounded-pill">
                Reset server <Icon icon="reset" margin="m-0" />
            </Button>
        </div>
    );
}
