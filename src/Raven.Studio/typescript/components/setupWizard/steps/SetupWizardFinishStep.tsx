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
import moment from "moment";
import genUtils from "common/generalUtils";
import Tab from "react-bootstrap/Tab";
import Nav from "react-bootstrap/Nav";
import Row from "react-bootstrap/Row";
import Col from "react-bootstrap/Col";
import RichAlert from "components/common/RichAlert";
import { NumberedList, NumberedListItem } from "components/common/NumberedList";

type OperationStatus = Raven.Client.Documents.Operations.OperationStatus;

export function SetupWizardFinishStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { value: isShowLogs, toggle: toggleIsShowLogs } = useBoolean(true); // TODO set to false

    const {
        nodeAddressStep,
        securityStep,
        setupMethodStep,
        additionalSettingsStep,
        domainStep,
        licenseKeyStep,
        selfSignedCertificateStep,
        usePackageStep,
    } = useWatch({ control });

    // TODO get rid off jQuery

    const websocket = useMemo(() => new serverNotificationCenterClient(), []);

    const { databasesService, setupWizardService } = useServices();

    const [readme, setReadme] = useState<string>();
    const [status, setStatus] = useState<OperationStatus>("Completed");
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

    // TODO move functions to utils

    const { getLocalNode, getHttpPort, getTcpPort, getServerUrl } = useSetupWizardFinishUtils();

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
        nodeAddressStep.nodes.forEach((node) => {
            nodesInfo[node.nodeTag] = getNodeInfo(node);
        });

        return nodesInfo;
    };

    const getUnsecuredDto = (): Raven.Server.Commercial.UnsecuredSetupInfo => {
        const localNode = getLocalNode();
        const isPassive = localNode.isPassive;

        // TODO pass advanced settings (DataDir, cert path ??) (waiting for server)

        return {
            EnableExperimentalFeatures: additionalSettingsStep.postgresqlIntegration,
            LocalNodeTag: isPassive ? null : localNode.nodeTag,
            Environment: isPassive ? null : additionalSettingsStep.serverEnvironment,
            ZipOnly: setupMethodStep.method === "createPackage",
            NodeSetupInfos: getNodeSetupInfos(),
        };
    };

    const getSecuredDto = (): Raven.Server.Commercial.SetupInfo => {
        // TODO pass advanced settings (DataDir, cert path ??) (waiting for server)

        const { adminCertificateExpirationTime } = additionalSettingsStep;

        const ClientCertNotAfter = adminCertificateExpirationTime
            ? moment.utc().add(additionalSettingsStep.adminCertificateExpirationTime, "months").format()
            : null;

        return {
            EnableExperimentalFeatures: additionalSettingsStep.postgresqlIntegration,
            Environment: additionalSettingsStep.serverEnvironment,
            License: JSON.parse(licenseKeyStep.key),
            Email: domainStep.email,
            Domain: domainStep.domain,
            RootDomain: domainStep.rootDomain,
            LocalNodeTag: getLocalNode().nodeTag,
            RegisterClientCert: true, // it should be always true. we should detect if same cert is installed and if no then install
            Certificate: selfSignedCertificateStep.certificate,
            Password: selfSignedCertificateStep.password,
            ClientCertNotAfter,
            ZipOnly: setupMethodStep.method === "createPackage",
            NodeSetupInfos: getNodeSetupInfos(),
        };
    };

    const getRegularDto = () => {
        if (!securityStep.securityOption) {
            return null;
        }

        switch (securityStep.securityOption) {
            case "none":
                return getUnsecuredDto();
            case "letsEncrypt":
            case "ownCertificate":
                return getSecuredDto();
            default:
                assertUnreachable(securityStep.securityOption);
        }
    };

    const getSubmitUrlBase = () => {
        if (!securityStep.securityOption) {
            return null;
        }

        switch (securityStep.securityOption) {
            case "none":
                return endpoints.global.setup.setupUnsecuredPackage;
            case "letsEncrypt":
                return endpoints.global.setup.setupLetsencrypt;
            case "ownCertificate":
                return endpoints.global.setup.setupSecured;
            default:
                assertUnreachable(securityStep.securityOption);
        }
    };

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

    const getContinueWithPackageDto = (): Raven.Server.Commercial.ContinueSetupInfo => {
        return {
            NodeTag: usePackageStep.nodeTag,
            Zip: usePackageStep.fileZip,
            RegisterClientCert: usePackageStep.isZipSecure, // we should detect if the same cert is installed and if no then install it
        };
    };

    const continueWithPackageFinish = async () => {
        const operationId = await databasesService.getNextOperationId(null);

        websocket.watchOperation(operationId, handleWebSocketOperation);

        const dto = getContinueWithPackageDto();

        if (usePackageStep.isZipSecure) {
            await setupWizardService.continueSecureClusterConfiguration(operationId, dto);
        } else {
            await setupWizardService.continueUnsecureClusterConfiguration(operationId, dto);
        }
    };

    useEffect(() => {
        const finish = async () => {
            if (setupMethodStep.method === "usePackage") {
                await continueWithPackageFinish();
            } else {
                await regularFinish();
            }
        };

        finish();
    }, []);

    return (
        <div className="finish-step">
            <TopInfo status={status} />
            <div className="hstack justify-content-between">
                <FormGroup className="mt-4">
                    <Switch selected={isShowLogs} toggleSelection={toggleIsShowLogs} color="primary">
                        Show configuration log
                    </Switch>
                </FormGroup>
                <Button
                    variant="link"
                    onClick={() => {
                        console.log("TODO: download configuration log");
                    }}
                    size="xs"
                >
                    <Icon icon="download" />
                    Download configuration log
                </Button>
            </div>
            {isShowLogs && (
                <pre>
                    {logs.map((message, idx) => (
                        <div key={idx} className={message.color ? `text-${message.color}` : ""}>
                            {message.message}
                        </div>
                    ))}
                </pre>
            )}

            {status !== "Completed" && (
                <div className="mt-2 p-2 panel-bg-1 rounded border border-secondary">TODO preety summary</div>
            )}
            {status === "Completed" && <CompletedSummary />}

            <div className="d-none">
                <form method="post" target="hidden-form" id="setupForm">
                    <input type="hidden" name="Options" />
                </form>
            </div>
        </div>
    );
}

function TopInfo({ status }: { status: OperationStatus }) {
    return (
        <>
            {status === "InProgress" && (
                <>
                    <h3>Configuration in process</h3>
                    <p>Please, wait a moment. Your RavenDB will be ready in no time.</p>
                </>
            )}
            {status === "Faulted" && (
                <>
                    <h3>Setup failed</h3>
                    <p>
                        It seems like something went wrong. Read the error message to find out what might&apos;ve been
                        an issue.
                    </p>
                </>
            )}
            {status === "Canceled" && (
                <>
                    <h3>Setup canceled</h3>
                </>
            )}
            {status === "Completed" && (
                <>
                    <h3>All set!</h3>
                    <p>You&apos;re almost ready to go. Follow the instructions to successfully complete the process.</p>
                </>
            )}
        </>
    );
}

function CompletedSummary() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { nodeAddressStep } = useWatch({ control });

    const { getStudioUrl } = useSetupWizardFinishUtils();

    const isSettingCluster = nodeAddressStep.nodes.length > 1;
    const localNodeTag = nodeAddressStep.nodes[0].nodeTag;

    const studioUrl = getStudioUrl();

    return (
        <div className="panel-bg-1 rounded border border-secondary completed-summary">
            <Tab.Container id="summary-tabs" defaultActiveKey="whatsNew">
                <Nav className="mb-2">
                    <Nav.Item className="flex-grow">
                        <Nav.Link eventKey="whatsNew" className="whats-new-tab" style={{ backgroundImage: "none" }}>
                            What&apos;s next?
                        </Nav.Link>
                    </Nav.Item>
                    {isSettingCluster && (
                        <Nav.Item className="flex-grow">
                            <Nav.Link eventKey="cluster" className="cluster-tab" style={{ backgroundImage: "none" }}>
                                Setting up a cluster
                            </Nav.Link>
                        </Nav.Item>
                    )}
                </Nav>
                <Tab.Content className="p-4 text-break">
                    <Tab.Pane eventKey="whatsNew">
                        <Row>
                            <Col
                                md={4}
                                className="border-secondary border-end vstack gap-2 text-center justify-content-center"
                            >
                                <Icon icon="server" color="primary" size="lg" />
                                <span>The new server will be available at: {studioUrl}</span>
                            </Col>
                            <Col
                                md={4}
                                className="border-secondary border-end vstack gap-2 text-center justify-content-center"
                            >
                                <Icon icon="node" color="node" size="lg" />
                                <span>
                                    The current <span className="text-node fw-bold">Node {localNodeTag}</span> has
                                    already been configured and requires no further action on your part.
                                </span>
                            </Col>
                            <Col md={4} className="vstack gap-2 text-center justify-content-center">
                                <Icon icon="certificate" size="lg" />
                                <span>An administrator client certificate has been installed on this machine.</span>
                            </Col>
                        </Row>
                        <RichAlert variant="info" className="mt-3">
                            You&apos;ll need to restart the server before you can access RavenDB Studio.
                        </RichAlert>
                    </Tab.Pane>
                    <Tab.Pane eventKey="cluster">
                        <Row>
                            <Col md={6} className="vstack gap-2 text-center justify-content-center">
                                <div>
                                    <Icon icon="folder" addon="attachment" color="primary" size="lg" />
                                </div>
                                <span>The new server will be available at: {studioUrl}</span>
                            </Col>
                            <Col md={6} className="vstack gap-2 text-center justify-content-center">
                                <Icon icon="cluster" color="node" size="lg" />
                                <span>
                                    The current <span className="text-node fw-bold">Node A</span> has already been
                                    configured and requires no further action on your part.
                                </span>
                            </Col>
                        </Row>
                        <hr />
                        <h5>How to setup the other nodes?</h5>
                        <NumberedList>
                            <NumberedListItem stepKey={1}>
                                The next step is to download a new RavenDB server for each of the other nodes.
                            </NumberedListItem>
                            <NumberedListItem stepKey={2}>
                                When you enter the Setup Wizard on a new node, please choose &apos;
                                <b>Use Setup Package</b>&apos;.
                                <br />
                                Do not try to start a new setup process again in this new node, it is not supported.
                            </NumberedListItem>
                            <NumberedListItem stepKey={3}>
                                You will be asked to upload the zip file which was just downloaded.
                            </NumberedListItem>
                            <NumberedListItem stepKey={4}>
                                The new server node will join the already existing cluster.
                            </NumberedListItem>
                        </NumberedList>
                        <RichAlert variant="info" className="mt-2">
                            When the Setup Wizard is done and the new node was restarted, the cluster will automatically
                            detect it.
                            <br />
                            There is no need to manually add it again from the studio. Simply access the
                            &apos;Cluster&apos; view and observe the topology being updated.
                        </RichAlert>
                    </Tab.Pane>
                </Tab.Content>
            </Tab.Container>
        </div>
    );
}

function useSetupWizardFinishUtils() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { nodeAddressStep, securityStep, setupMethodStep, domainStep, selfSignedCertificateStep, usePackageStep } =
        useWatch({ control });

    const getLocalNode = () => {
        return nodeAddressStep.nodes[0];
    };

    const getHttpPort = (port: number) => {
        if (!port && securityStep.securityOption === "none") {
            return 8080;
        }
        return port;
    };

    const getTcpPort = (port: number) => {
        if (!port && securityStep.securityOption === "none") {
            return 38888;
        }
        return port;
    };

    const formatIpAddress = (ip: string): string => {
        const address = genUtils.getAddressInfo(ip);
        if (address.Type === "ipv6" && !ip.startsWith("[") && !ip.endsWith("]")) {
            return `[${ip}]`;
        }
        return ip;
    };

    const getPortPart = () => {
        const port = getLocalNode().httpPort;
        return port && port !== 443 ? ":" + port : "";
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

    const getDomainForWildcard = (tag: string) => {
        if (selfSignedCertificateStep.cns.length === 0) {
            return "";
        }

        const cn = selfSignedCertificateStep.cns[0];

        if (!tag) {
            return cn.replace("*.", "");
        }
        return cn.replace("*", tag);
    };

    const getStudioUrl = () => {
        if (setupMethodStep.method === "usePackage") {
            return usePackageStep.publicServerUrl || usePackageStep.serverUrl;
        }

        if (securityStep.securityOption === "none") {
            const setupPort = getLocalNode().httpPort || 8080;
            const setupAddress = getLocalNode().ipAddress[0].ipAddress;

            let host;
            const port = setupPort;
            if (setupAddress === "0.0.0.0") {
                host = document.location.hostname;
            } else {
                host = formatIpAddress(setupAddress);
            }

            return `http://${host}:${port}`;
        }

        if (securityStep.securityOption === "letsEncrypt") {
            return `https://${getLocalNode().nodeTag.toLocaleLowerCase()}.${domainStep.domain}.${domainStep.rootDomain}${getPortPart()}`;
        }

        if (securityStep.securityOption === "ownCertificate") {
            const localNode = getLocalNode();

            if (selfSignedCertificateStep.isWildcardCertificate) {
                const domain = getDomainForWildcard(localNode.nodeTag.toLocaleLowerCase());
                return "https://" + domain + getPortPart();
            }

            return getServerUrl(localNode.dnsName, localNode.httpPort);
        }

        return null;
    };

    return {
        getStudioUrl,
        getLocalNode,
        getHttpPort,
        getTcpPort,
        getServerUrl,
    };
}

export function SetupWizardFinishStepFooter() {
    const { setupWizardService } = useServices();

    const { getStudioUrl } = useSetupWizardFinishUtils();

    const redirectToStudio = () => {
        window.location.href = getStudioUrl();
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
