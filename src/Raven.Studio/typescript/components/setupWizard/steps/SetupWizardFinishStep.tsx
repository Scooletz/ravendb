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
import Modal from "components/common/Modal";
import { useBrowser } from "components/hooks/useBrowser";
import Spinner from "react-bootstrap/Spinner";
import { useRavenLink } from "hooks/useRavenLink";

type OperationStatus = Raven.Client.Documents.Operations.OperationStatus;

export function SetupWizardFinishStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { value: isShowLogs, toggle: toggleIsShowLogs } = useBoolean(false);

    const { setupMethodStep, usePackageStep } = useWatch({ control });

    const websocket = useMemo(() => new serverNotificationCenterClient(), []);

    const { databasesService, setupWizardService } = useServices();

    const [readme, setReadme] = useState<string>();
    const [status, setStatus] = useState<OperationStatus>("Completed");
    const [logs, setLogs] = useState<{ message: string; color?: TextColor }[]>([]);
    const [configurationProcess, setConfigurationProcess] =
        useState<Raven.Server.Commercial.SetupProgressAndResult>(null);
    const handleWebSocketOperation = (operation: Raven.Server.NotificationCenter.Notifications.OperationChanged) => {
        if (operation.TaskType === "Setup") {
            let dto: Raven.Server.Commercial.SetupProgressAndResult = operation.State.Progress;

            switch (operation.State.Status) {
                case "Completed":
                    dto = operation.State.Result as Raven.Server.Commercial.SetupProgressAndResult;
                    setConfigurationProcess(operation.State.Result);
                    setReadme(dto.Readme);
                    setStatus("Completed");
                    break;
                case "InProgress":
                    dto = operation.State.Progress as Raven.Server.Commercial.SetupProgressAndResult;
                    setConfigurationProcess(operation.State.Progress);
                    setStatus("InProgress");
                    break;
                case "Faulted": {
                    setConfigurationProcess(operation.State.Progress);
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

    const { getRegularDto, getContinueWithPackageDto, getSubmitUrlBase, downloadConfigurationLog } =
        useSetupWizardFinishUtils();

    const regularFinish = async () => {
        const operationId = await databasesService.getNextOperationId(null);
        const operationPart = "?operationId=" + operationId;
        const urlBase = getSubmitUrlBase();

        const dto = getRegularDto();

        const form = document.getElementById("setupForm") as HTMLFormElement;
        const optionsInput = form.querySelector("[name=Options]") as HTMLInputElement;

        form.action = urlBase + operationPart;
        optionsInput.value = JSON.stringify(dto);
        form.submit();

        websocket.watchOperation(operationId, handleWebSocketOperation);
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
            <div className="hstack mt-4 mb-1 justify-content-between">
                <FormGroup marginClass="mb-0">
                    <Switch className="mb-0" selected={isShowLogs} toggleSelection={toggleIsShowLogs} color="primary">
                        Show configuration log
                    </Switch>
                </FormGroup>
                <Button
                    disabled={!configurationProcess?.Messages?.length}
                    variant="link"
                    onClick={() =>
                        downloadConfigurationLog(
                            configurationProcess?.Messages ?? [],
                            `configurationLog_${moment.utc().format("YYYY-MM-DD-HH-mm-ss")}`
                        )
                    }
                    size="xs"
                >
                    <Icon icon="download" />
                    Download configuration log
                </Button>
            </div>
            {configurationProcess && (
                <div className="summary-tab-container mb-4">
                    <pre className="p-4 mb-0">
                        <Configuration configurationProcess={configurationProcess} />
                    </pre>
                </div>
            )}
            {isShowLogs && (
                <div className="mb-4">
                    <pre>
                        {logs.map((message, idx) => (
                            <div key={idx} className={message.color ? `text-${message.color}` : ""}>
                                {message.message}
                            </div>
                        ))}
                    </pre>
                </div>
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

interface ConfigurationProps {
    configurationProcess: Raven.Server.Commercial.SetupProgressAndResult;
}

const Configuration = ({ configurationProcess }: ConfigurationProps) => {
    return (
        <div>
            <ConfigurationItem
                stepTitle="Validation"
                configurationState={configurationProcess?.SetupActionSteps?.StepsByConfigurationStepType.Validation}
            />

            <ConfigurationItem
                stepTitle="Let's encrypt"
                configurationState={configurationProcess?.SetupActionSteps?.StepsByConfigurationStepType.LetsEncrypt}
            />

            <ConfigurationItem
                stepTitle="DNS records"
                configurationState={configurationProcess?.SetupActionSteps?.StepsByConfigurationStepType.DnsRecords}
            />

            <ConfigurationItem
                stepTitle="Acquiring let's encrypt certificate"
                configurationState={
                    configurationProcess?.SetupActionSteps?.StepsByConfigurationStepType.ClientCertificate
                }
            />

            <ConfigurationItem
                stepTitle="Configuration settings"
                configurationState={
                    configurationProcess?.SetupActionSteps?.StepsByConfigurationStepType.ConfigurationSettings
                }
            />

            <ConfigurationItem
                stepTitle="Client certificate"
                configurationState={
                    configurationProcess?.SetupActionSteps?.StepsByConfigurationStepType.ClientCertificate
                }
            />

            <ConfigurationItem
                stepTitle="Creating settings.json"
                configurationState={
                    configurationProcess?.SetupActionSteps?.StepsByConfigurationStepType.CreatingSettingsJson
                }
            />
        </div>
    );
};

interface ConfigurationItemProps {
    stepTitle: string;
    configurationState: Raven.Server.Commercial.SetupActionInfo;
}

const ConfigurationItem = ({ configurationState, stepTitle }: ConfigurationItemProps) => {
    if (!configurationState?.State || configurationState?.State === "NotApplicable") {
        return null;
    }

    const getConfigurationItemStatus = () => {
        switch (configurationState?.State) {
            case "Pending":
            case "InProgress":
                return <Spinner className="spinner-gradient" size="sm" />;
            case "Completed":
                return <Icon color="success" icon="checkmark" margin="m-0" />;
            case "SkippedDueToError":
                return <Icon color="muted" icon="skip" margin="m-0" />;
            default:
                return <Icon color="danger" icon="close" margin="m-0" />;
        }
    };

    return (
        <div className="d-flex flex-column mb-1 align-items-center">
            <div className="w-100 d-flex align-items-center justify-content-between">
                <span>{stepTitle}</span>
                {getConfigurationItemStatus()}
            </div>
            {configurationState?.State === "Error" && (
                <RichAlert variant="danger" title={configurationState?.ErrorType ?? "XDD"} className="my-2">
                    {configurationState?.ErrorMessage}
                </RichAlert>
            )}
        </div>
    );
};

function TopInfo({ status }: { status: OperationStatus }) {
    return (
        <>
            {status === "InProgress" && (
                <>
                    <h2 className="mb-1">Configuration in process</h2>
                    <p className="mb-4 text-muted">Please, wait a moment. Your RavenDB will be ready in no time.</p>
                </>
            )}
            {status === "Faulted" && (
                <>
                    <h2 className="mb-1">Setup failed</h2>
                    <p className="mb-4 text-muted">
                        It seems like something went wrong. Read the error message to find out what might&apos;ve been
                        an issue.
                    </p>
                </>
            )}
            {status === "Canceled" && (
                <>
                    <h2 className="mb-1">Setup canceled</h2>
                </>
            )}
            {status === "Completed" && (
                <>
                    <h2 className="mb-1">All set!</h2>
                    <p className="mb-4 text-muted">
                        You&apos;re almost ready to go. Follow the instructions to successfully complete the process.
                    </p>
                </>
            )}
        </>
    );
}

function CompletedSummary() {
    const { control } = useFormContext<SetupWizardFormData>();

    const {
        nodeAddressStep,
        usePackageStep: { nodeTag },
    } = useWatch({ control });

    const { getStudioUrl } = useSetupWizardFinishUtils();

    const isSettingCluster = nodeAddressStep.nodes.length > 1;
    const localNodeTag = nodeAddressStep.nodes?.[0]?.nodeTag ?? nodeTag;

    const studioUrl = getStudioUrl();

    return (
        <div className="summary-tab-container">
            <Tab.Container id="summary-tabs" defaultActiveKey="whatsNew">
                <Nav className="mb-2">
                    <Nav.Item className="flex-grow">
                        <Nav.Link eventKey="whatsNew" className="whats-new-tab">
                            What&apos;s next?
                        </Nav.Link>
                    </Nav.Item>
                    {isSettingCluster && (
                        <Nav.Item className="flex-grow">
                            <Nav.Link eventKey="cluster" className="cluster-tab">
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
                                <span>
                                    The new server will be available at:{" "}
                                    <a href={studioUrl} target="_blank" className="pe-none">
                                        {studioUrl}
                                    </a>
                                </span>
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

    const {
        nodeAddressStep,
        securityStep,
        setupMethodStep,
        licenseKeyStep,
        domainStep,
        selfSignedCertificateStep,
        usePackageStep,
        additionalSettingsStep,
    } = useWatch({ control });

    const getLocalNode = () => {
        return nodeAddressStep.nodes[0];
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
        nodeAddressStep.nodes.forEach((node) => {
            nodesInfo[node.nodeTag] = getNodeInfo(node);
        });

        return nodesInfo;
    };

    const getUnsecuredDto = (): Raven.Server.Commercial.UnsecuredSetupInfo => {
        const localNode = getLocalNode();
        const isPassive = localNode.isPassive;

        return {
            AutoIndexingEngineType: additionalSettingsStep.isAdvancedSettingsVisible
                ? additionalSettingsStep.autoIndexingEngineType
                : null,
            DataDirectory: additionalSettingsStep.isAdvancedSettingsVisible
                ? additionalSettingsStep.dataDirectory
                : null,
            LogsPath: additionalSettingsStep.isAdvancedSettingsVisible ? additionalSettingsStep.logsPath : null,
            StaticIndexingEngineType: additionalSettingsStep.isAdvancedSettingsVisible
                ? additionalSettingsStep.staticIndexingEngineType
                : null,
            EnableExperimentalFeatures: additionalSettingsStep.postgresqlIntegration,
            LocalNodeTag: isPassive ? null : localNode.nodeTag,
            Environment: isPassive ? null : additionalSettingsStep.serverEnvironment,
            ZipOnly: setupMethodStep.method === "createPackage",
            NodeSetupInfos: getNodeSetupInfos(),
        };
    };

    const getSecuredDto = (): Raven.Server.Commercial.SetupInfo => {
        const { adminCertificateExpirationTime } = additionalSettingsStep;

        const ClientCertNotAfter = adminCertificateExpirationTime
            ? moment.utc().add(additionalSettingsStep.adminCertificateExpirationTime, "months").format()
            : null;

        return {
            AutoIndexingEngineType: additionalSettingsStep.isAdvancedSettingsVisible
                ? additionalSettingsStep.autoIndexingEngineType
                : null,
            DataDirectory: additionalSettingsStep.isAdvancedSettingsVisible
                ? additionalSettingsStep.dataDirectory
                : null,
            LogsPath: additionalSettingsStep.isAdvancedSettingsVisible ? additionalSettingsStep.logsPath : null,
            StaticIndexingEngineType: additionalSettingsStep.isAdvancedSettingsVisible
                ? additionalSettingsStep.staticIndexingEngineType
                : null,
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

    const getContinueWithPackageDto = (): Raven.Server.Commercial.ContinueSetupInfo => {
        return {
            NodeTag: usePackageStep.nodeTag,
            Zip: usePackageStep.fileZip,
            RegisterClientCert: usePackageStep.isZipSecure, // we should detect if the same cert is installed and if no then install it
        };
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

    const downloadConfigurationLog = (data: string[], fileName: string) => {
        if (!data || data.length === 0) {
            return;
        }

        const content = data.join("\n");

        const blob = new Blob([content], { type: "text/plain;charset=utf-8" });
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = `${fileName}.txt`;
        a.click();

        URL.revokeObjectURL(url);
    };

    return {
        getStudioUrl,
        getHttpPort,
        getTcpPort,
        getServerUrl,
        getRegularDto,
        getContinueWithPackageDto,
        getSubmitUrlBase,
        downloadConfigurationLog,
    };
}

function CertInstallationConfirm(props: { onCancel: () => void; onConfirm: () => void }) {
    const { onCancel, onConfirm } = props;

    const browser = useBrowser();
    const docsLink = useRavenLink({
        hash: "VD4R8E",
    });

    return (
        <Modal show onHide={onCancel} contentClassName="modal-border bulge-primary" size="lg">
            <Modal.Header closeButton onCloseClick={onCancel}>
                <Icon icon="certificate" color="primary" addon="check" className="fs-2" />
                <span className="lead">Confirm certificate installation</span>
            </Modal.Header>
            <Modal.Body className="pt-0">
                <NumberedList>
                    <NumberedListItem stepKey={1}>
                        <h4>Recognize certificate in your browser</h4>
                        <div className="browser-tab-container">
                            <Tab.Container id="summary-tabs" defaultActiveKey={browser}>
                                <Nav className="mb-2">
                                    <Nav.Item className="flex-grow">
                                        <Nav.Link eventKey="Chrome" className="chrome-tab">
                                            <Icon icon="chrome" />
                                            Chrome
                                        </Nav.Link>
                                    </Nav.Item>
                                    <Nav.Item className="flex-grow">
                                        <Nav.Link eventKey="Firefox" className="firefox-tab">
                                            <Icon icon="firefox" />
                                            Firefox
                                        </Nav.Link>
                                    </Nav.Item>
                                    <Nav.Item className="flex-grow">
                                        <Nav.Link eventKey="Safari" className="safari-tab">
                                            <Icon icon="safari" />
                                            Safari
                                        </Nav.Link>
                                    </Nav.Item>
                                    <Nav.Item className="flex-grow">
                                        <Nav.Link eventKey="Other" className="other-tab">
                                            <Icon icon="global" />
                                            Other
                                        </Nav.Link>
                                    </Nav.Item>
                                </Nav>
                                <Tab.Content className="p-2 text-break">
                                    <Tab.Pane eventKey="Chrome">
                                        Chrome (or any{" "}
                                        <a
                                            href="https://en.wikipedia.org/wiki/Chromium_(web_browser)#Browsers_based_on_Chromium"
                                            target="_blank"
                                        >
                                            Chromium-based browser
                                        </a>
                                        ) will let you select this certificate automatically. You may need to restart
                                        all instances of Chrome to make sure nothing is cached.
                                    </Tab.Pane>
                                    <Tab.Pane eventKey="Firefox">
                                        Firefox uses its own internal certificate store. After importing the certificate
                                        through Firefox settings, it will be available for use automatically. You may
                                        need to restart Firefox to ensure the new certificate is recognized properly.
                                    </Tab.Pane>
                                    <Tab.Pane eventKey="Safari">
                                        Safari uses the macOS Keychain to manage certificates. Once the certificate is
                                        imported and trusted in Keychain Access, Safari will select it automatically
                                        when needed. Restarting Safari or the system may help if it doesn’t appear right
                                        away.
                                    </Tab.Pane>
                                    <Tab.Pane eventKey="Other">
                                        Browsers that are not Chromium-based and don’t use the system certificate store
                                        typically require manual certificate import through their own settings or
                                        preferences. Behavior may vary, and restarting the browser is often recommended
                                        to ensure the certificate is applied.
                                    </Tab.Pane>
                                </Tab.Content>
                            </Tab.Container>
                        </div>
                    </NumberedListItem>
                    <NumberedListItem stepKey={2}>
                        <h4 className="mb-2">Restart server</h4>
                        <p className="mb-1">
                            Once you proceed with restart, pick your newly installed certificate from the list of
                            available certificates.
                        </p>
                        <p className="mb-0">
                            If Chrome doesn’t let you choose a certificate and instead you get a RavenDB authentication
                            error, please try again in the Incognito mode (or close all instances of Chrome). It can
                            happen because the browser caches the client certificates.
                        </p>
                    </NumberedListItem>
                </NumberedList>
            </Modal.Body>
            <Modal.Footer className="hstack justify-content-between">
                <a href={docsLink} target="_blank" className="btn btn-info rounded-pill">
                    See documentation <Icon icon="newtab" margin="m-0" />
                </a>
                <div className="hstack gap-2">
                    <Button variant="link" onClick={onCancel} className="link-muted">
                        Cancel
                    </Button>
                    <Button variant="primary" onClick={onConfirm} className="rounded-pill">
                        <Icon icon="reset" />
                        Restart server
                    </Button>
                </div>
            </Modal.Footer>
        </Modal>
    );
}

export function SetupWizardFinishStepFooter() {
    const { setupWizardService } = useServices();
    const { control } = useFormContext<SetupWizardFormData>();
    const { securityStep, setupMethodStep, usePackageStep } = useWatch({ control });
    const { value: isCertInstallationConfirmed, toggle: toggleIsCertInstallationConfirmed } = useBoolean(false);

    const { getStudioUrl } = useSetupWizardFinishUtils();

    const redirectToStudio = () => {
        window.location.href = getStudioUrl();
    };

    const resetServer = async () => {
        await setupWizardService.finishSetup();

        const waitBeforeRedirectInMs = 2000;
        setTimeout(() => {
            redirectToStudio();
        }, waitBeforeRedirectInMs);
    };

    const handleReset = () => {
        const isSecure =
            securityStep.securityOption !== "none" ||
            (setupMethodStep.method === "usePackage" && usePackageStep.isZipSecure);

        if (isSecure) {
            toggleIsCertInstallationConfirmed();
        } else {
            resetServer();
        }
    };

    return (
        <div className="d-flex justify-content-end">
            <Button variant="primary" onClick={handleReset} className="mt-2 rounded-pill">
                Restart server <Icon icon="reset" margin="m-0" />
            </Button>
            {isCertInstallationConfirmed && (
                <CertInstallationConfirm
                    onCancel={toggleIsCertInstallationConfirmed}
                    onConfirm={() => {
                        resetServer();
                        toggleIsCertInstallationConfirmed();
                    }}
                />
            )}
        </div>
    );
}
