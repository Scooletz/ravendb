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
import assertUnreachable from "components/utils/assertUnreachable";

export function SetupWizardFinishStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { value: isShowLogs, toggle: toggleIsShowLogs } = useBoolean(true); // TODO set to false

    const {
        nodeAddressStep: { nodes },
        securityStep: { securityOption },
        setupMethodStep: { method: setupMethod },
        additionalSettingsStep: { serverEnvironment },
        domainStep,
        licenseKeyStep,
        selfSignedCertificateStep,
    } = useWatch({ control });

    // TODO get rid off jQuery

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
                    break;
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

    const getHttpPort = (port: number) => {
        if (!port && securityOption === "none") {
            return 8080;
        }
        return port;
    };

    const getTcpPort = (port: number) => {
        if (!port && securityOption === "none") {
            return 38888;
        }
        return port;
    };

    const getServerUrl = (dnsName: string, port: number) => {
        if (!dnsName) {
            return null;
        }

        let serverUrl = "https://" + dnsName;
        if (port && port !== 443) {
            serverUrl += ":" + port;
        }

        return serverUrl;
    };

    const getNodeInfo = (
        node: SetupWizardFormData["nodeAddressStep"]["nodes"][number]
    ): Raven.Server.Commercial.NodeInfo => {
        return {
            Addresses: node.ipAddress.map((x) => x.ipAddress),
            Port: getHttpPort(node.httpPort),
            TcpPort: getTcpPort(node.tcpPort),
            PublicServerUrl: getServerUrl(node.dnsName, node.httpPort),
            PublicTcpServerUrl: null,
            ExternalIpAddress: node.hasExternalConfig ? node.externalIpAddress : null,
            ExternalPort: node.hasExternalConfig ? node.externalHttpPort : null,
            ExternalTcpPort: node.hasExternalConfig ? node.externalTcpPort : null,
        };
    };

    const getNodeSetupInfos = (): Record<string, Raven.Server.Commercial.NodeInfo> => {
        const nodesInfo: Record<string, Raven.Server.Commercial.NodeInfo> = {};
        nodes.forEach((node) => {
            nodesInfo[node.nodeTag] = getNodeInfo(node);
        });

        return nodesInfo;
    };

    const getUnsecuredDto = (): Raven.Server.Commercial.UnsecuredSetupInfo => {
        const localNodeTag = nodes[0].nodeTag; // TODO for sure?
        const isPassive = nodes[0].isPassive;

        return {
            EnableExperimentalFeatures: false, // TODO
            LocalNodeTag: isPassive ? null : localNodeTag,
            Environment: isPassive ? null : serverEnvironment,
            ZipOnly: setupMethod === "createPackage",
            NodeSetupInfos: getNodeSetupInfos(),
        };
    };

    const getSecuredDto = (): Raven.Server.Commercial.SetupInfo => {
        const localNodeTag = nodes[0].nodeTag; // TODO for sure?
        const isPassive = nodes[0].isPassive;

        return {
            EnableExperimentalFeatures: false, // TODO
            Environment: serverEnvironment,
            License: JSON.parse(licenseKeyStep.key),
            Email: domainStep.email,
            Domain: domainStep.domain,
            RootDomain: domainStep.rootDomain,
            LocalNodeTag: !isPassive ? localNodeTag : null,
            RegisterClientCert: false, // TODO
            Certificate: selfSignedCertificateStep.certificate, // what about letsEncrypt?
            Password: selfSignedCertificateStep.password, // what about letsEncrypt?
            ClientCertNotAfter: null, // TODO
            ZipOnly: setupMethod === "createPackage",
            NodeSetupInfos: getNodeSetupInfos(),
        };
    };

    const getRegularDto = () => {
        if (!securityOption) {
            return null;
        }

        switch (securityOption) {
            case "none":
                return getUnsecuredDto();
            case "letsEncrypt":
            case "ownCertificate":
                return getSecuredDto();
            default:
                assertUnreachable(securityOption);
        }
    };

    const getSubmitUrlBase = () => {
        if (!securityOption) {
            return null;
        }

        switch (securityOption) {
            case "none":
                return endpoints.global.setup.setupUnsecuredPackage;
            case "letsEncrypt":
                return endpoints.global.setup.setupLetsencrypt;
            case "ownCertificate":
                return endpoints.global.setup.setupSecured;
            default:
                assertUnreachable(securityOption);
        }
    };

    useEffect(() => {
        const finish = async () => {
            // todo condition
            await regularFinish();
        };

        finish();
    }, []);

    const regularFinish = async () => {
        const $form = $("#setupForm");
        const $downloadOptions = $("[name=Options]", $form);

        const operationId = await databasesService.getNextOperationId(null);
        const operationPart = "?operationId=" + operationId;
        const urlBase = getSubmitUrlBase();

        const dto = getRegularDto();

        $form.attr("action", urlBase + operationPart);
        $downloadOptions.val(JSON.stringify(dto));
        $form.submit();

        websocket.watchOperation(operationId, handleWebSocketOperation);
    };

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
