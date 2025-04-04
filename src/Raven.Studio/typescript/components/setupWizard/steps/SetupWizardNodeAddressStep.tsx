import { Control, useFieldArray, useForm, useFormContext, UseFormReturn, useWatch } from "react-hook-form";
import { SetupWizardFormData, SetupWizardSecurityOption } from "../setupWizardValidation";
import { Icon } from "components/common/Icon";
import Button from "react-bootstrap/Button";
import {
    RichPanel,
    RichPanelActions,
    RichPanelDetailItem,
    RichPanelDetails,
    RichPanelHeader,
    RichPanelInfo,
    RichPanelName,
} from "components/common/RichPanel";
import Collapse from "react-bootstrap/Collapse";
import React, { useEffect, useMemo } from "react";
import Form from "react-bootstrap/Form";
import {
    FormGroup,
    FormInput,
    FormLabel,
    FormSelect,
    FormSelectCreatable,
    FormSwitch,
    OptionalLabel,
} from "components/common/Form";
import RichAlert from "components/common/RichAlert";
import * as yup from "yup";
import { yupResolver } from "@hookform/resolvers/yup";
import useConfirm from "components/common/ConfirmDialog";
import InputGroup from "react-bootstrap/InputGroup";
import { ConditionalPopover } from "components/common/ConditionalPopover";
import PopoverWithHoverWrapper from "components/common/PopoverWithHoverWrapper";
import { HrHeader } from "components/common/HrHeader";
import classNames from "classnames";
import genUtils from "common/generalUtils";
import { useAsync } from "react-async-hook";
import { useServices } from "hooks/useServices";
import { SelectOption } from "components/common/select/Select";
import { isEmpty } from "common/typeUtils";
import Row from "react-bootstrap/Row";
import Col from "react-bootstrap/esm/Col";

export function SetupWizardNodeAddressStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { fields, append, remove } = useFieldArray({
        control,
        name: "nodeAddressStep.nodes",
    });

    const {
        domainStep,
        securityStep: { securityOption },
    } = useWatch({ control });

    const hasDomainStep = domainStep?.domain && domainStep?.rootDomain;
    const fullDomain = `a.${domainStep.domain.toLocaleLowerCase()}.${domainStep.rootDomain}`;
    const addNewNode = () => {
        const existingTags = fields.map((field) => field.nodeTag);
        const nodeTags = generateAlphabeticTags(2); // validation allows us to contain 4 letters, but generated tags are around +- 500k length, so we limit it to 2. (700 tags)
        const availableNodeTag = nodeTags.find((tag) => !existingTags.includes(tag)) || "A";

        append({
            nodeTag: availableNodeTag,
            ipAddress: [
                {
                    ipAddress: "127.0.0.1",
                },
            ],
            isEditing: true,
            isNewlyAdded: true,
        });
    };
    // add default node
    useEffect(() => {
        if (fields.length === 0) {
            append({
                nodeTag: "A",
                ipAddress: [
                    {
                        ipAddress: "127.0.0.1",
                    },
                ],
                isEditing: true, // first node should be added with default values and in editing mode
                isNewlyAdded: false,
                isPassive: false,
                nodeUrl: hasDomainStep && securityOption !== "none" ? fullDomain : undefined,
                httpPort: 443,
                tcpPort: 38888,
                hasExternalConfig: false,
            });
        }
    }, []); // eslint-disable-line react-hooks/exhaustive-deps

    return (
        <div>
            <div className="mb-4">
                <h2>Node addresses</h2>
                <p>
                    Enter your server settings - IP addresses and ports to ensure clear communication and smooth work of
                    your database. If you are building a cluster this is the place to add nodes and configure them.
                </p>
            </div>
            <div className="vstack gap-3">
                {fields.map((field, index) => (
                    <NodeDetailsPanel key={field.id} control={control} index={index} onRemove={() => remove(index)} />
                ))}
                <AddAnotherNode onAddNode={addNewNode} />
            </div>
        </div>
    );
}

function generateAlphabeticTags(maxLength: number = 4): string[] {
    const tags: string[] = [];
    let currentTag = "";

    while (currentTag.length <= maxLength) {
        tags.push(getNextTag(currentTag));
        currentTag = getNextTag(currentTag);
    }

    return tags;
}

function getNextTag(current: string): string {
    if (!current) {
        return "A";
    }

    const chars = current.split("");
    let i = chars.length - 1;

    while (i >= 0) {
        if (chars[i] !== "Z") {
            chars[i] = String.fromCharCode(chars[i].charCodeAt(0) + 1);
            break;
        }
        chars[i] = "A";
        i--;
    }

    if (i < 0) {
        chars.unshift("A");
    }

    return chars.join("");
}

interface NodeDetailsPanelProps {
    control: Control<SetupWizardFormData>;
    index: number;
    onRemove: () => void;
}

function NodeDetailsPanel({ control, index, onRemove }: NodeDetailsPanelProps) {
    const { getValues } = useFormContext<SetupWizardFormData>();
    const nodeData = useWatch({
        control,
        name: `nodeAddressStep.nodes.${index}`,
    });

    const {
        securityStep: { securityOption },
    } = useWatch({
        control,
    });
    const { setupWizardService } = useServices();

    const asyncGetSetupParameters = useAsync(async () => setupWizardService.getSetupParameters(), []);

    const nodeAddressStep = getValues().nodeAddressStep;

    const editNodeForm = useForm<NodeEditFormData>({
        defaultValues: nodeData,
        mode: "onChange",
        resolver: yupResolver(nodeEditFormSchema),
        context: {
            nodeAddressStep,
            securityOption,
            currentIndex: index,
            isDocker: asyncGetSetupParameters.result?.IsDocker,
        },
    });

    return (
        <RichPanel hover>
            <NodeDetailsPanelHeader control={control} editNodeForm={editNodeForm} index={index} onRemove={onRemove} />
            {!nodeData.isEditing ? (
                <NodeDetailsPanelView index={index} control={control} />
            ) : (
                <NodeDetailsPanelEdit parentControl={control} editNodeForm={editNodeForm} />
            )}
        </RichPanel>
    );
}

interface NodeDetailsPanelHeaderProps {
    control: Control<SetupWizardFormData>;
    index: number;
    onRemove: () => void;
    editNodeForm: UseFormReturn<NodeEditFormData>;
}

function NodeDetailsPanelHeader({ control, index, onRemove, editNodeForm }: NodeDetailsPanelHeaderProps) {
    const { setValue } = useFormContext<SetupWizardFormData>();
    const nodeData = useWatch({
        control,
        name: `nodeAddressStep.nodes.${index}`,
    });
    
    const nodeAddressNodes = useWatch({
        control,
        name: "nodeAddressStep.nodes",
    })

    const domainData = useWatch({
        control,
        name: "domainStep",
    });

    const securityOption = useWatch({
        control,
        name: "securityStep.securityOption",
    });

    const { handleSubmit, reset, formState } = editNodeForm;

    const nodeName = `Node ${nodeData.nodeTag ?? "?"}`;

    const handleDiscardEdit = () => {
        if (nodeData.isNewlyAdded) {
            onRemove();
        } else {
            reset(nodeData);
            setValue(`nodeAddressStep.nodes.${index}`, {
                ...nodeData,
                isEditing: false,
            });
        }
    };

    const handleSaveEdit = handleSubmit(async (formData: NodeEditFormData) => {
        setValue(`nodeAddressStep.nodes.${index}`, {
            nodeUrl:
                securityOption !== "none"
                    ? `${formData.nodeTag.toLowerCase()}.${domainData.domain.toLocaleLowerCase()}.${domainData.rootDomain}`
                    : undefined,
            ...formData,
            nodeTag: formData.isPassive ? undefined : formData.nodeTag,
            isEditing: false,
            isNewlyAdded: false,
        });
    });

    const confirm = useConfirm();

    const handleDeleteNode = async () => {
        const isConfirmed = await confirm({
            title: (
                <>
                    You’re about to delete <b>Node {nodeData.nodeTag}</b>
                </>
            ),
            message: (
                <div className="d-flex w-100 justify-content-center">
                    Removing it may impact cluster stability and performance. This action cannot be undone.
                </div>
            ),
            icon: "trash",
            confirmText: "Delete",
            actionColor: "danger",
            size: "lg",
        });

        if (isConfirmed) {
            onRemove();
        }
    };

    return (
        <RichPanelHeader>
            <RichPanelInfo>
                <RichPanelName>
                    {nodeData.isNewlyAdded ? (
                        <>Creating new node</>
                    ) : nodeData.isEditing ? (
                        <>Editing node values</>
                    ) : (
                        <>
                            <Icon color="node" icon="node" />
                            {nodeName}{" "}
                            {index === 0 && (
                                <small className="text-muted">
                                    {nodeData.isPassive ? (
                                        <span className="text-info">(Passive)</span>
                                    ) : (
                                        "(current node)"
                                    )}
                                </small>
                            )}
                        </>
                    )}
                </RichPanelName>
            </RichPanelInfo>
            <RichPanelActions>
                {nodeData.isEditing ? (
                    <>
                        <ConditionalPopover
                            conditions={{
                                isActive: !isEmpty(formState.errors),
                                message: "Please fix the errors before saving.",
                            }}
                        >
                            <Button disabled={!isEmpty(formState.errors)} onClick={handleSaveEdit} variant="success">
                                <Icon icon="save" />
                                Save
                            </Button>
                        </ConditionalPopover>
                        <Button variant="secondary" onClick={handleDiscardEdit}>
                            <Icon icon="close" />
                            Discard
                        </Button>
                    </>
                ) : (
                    <>
                        <Button
                            variant="secondary"
                            onClick={() => setValue(`nodeAddressStep.nodes.${index}.isEditing`, true)}
                        >
                            <Icon icon="edit" margin="m-0" />
                        </Button>
                        {(nodeAddressNodes.filter(node => !node.isNewlyAdded).length > 1) && (
                            <Button variant="danger" onClick={handleDeleteNode}>
                                <Icon icon="trash" margin="m-0" />
                            </Button>
                        )}
                    </>
                )}
            </RichPanelActions>
        </RichPanelHeader>
    );
}

interface PopoverMessageProps {
    description: string | React.ReactNode;
    alert?: React.ReactNode;
    href?: string;
}

export function PopoverMessage({ description, href = "https://ravendb.net/docs/article-page/7.0/csharp/start/installation/manual", alert }: PopoverMessageProps) {
    // TODO: add link to documentation based on app version (useRavenLink) - waiting for access

    return (
        <>
            <p>{description}</p>
            {alert}
            <HrHeader />
            <span>
                <Icon icon="link" />
                Read more in our{" "}
                <a
                    href={href}
                    target="_blank"
                    className="text-primary fw-bold"
                >
                    documentation <Icon icon="newtab" />
                </a>
            </span>
        </>
    );
}

function NodeDetailsPanelView({ index, control }: { index: number; control: Control<SetupWizardFormData> }) {
    const nodeData = useWatch({
        control,
        name: `nodeAddressStep.nodes.${index}`,
    });

    const localIpPortAddress = `${nodeData.ipAddress[0].ipAddress}:${nodeData.httpPort}`;
    return (
        <RichPanelDetails>
            <RichPanelDetailItem>
                <div className="d-flex flex-column gap-1 w-100">
                    <span className="d-flex gap-1">
                        <b>Node URL</b>
                        <PopoverWithHoverWrapper
                            message={
                                <PopoverMessage description="Defines the address under which specific node will be available." />
                            }
                        >
                            <Icon icon="info" color="info" margin="m-0" />
                        </PopoverWithHoverWrapper>
                    </span>
                    <div className="text-truncate" title={nodeData.nodeUrl || localIpPortAddress}>
                        {nodeData.nodeUrl || localIpPortAddress}
                    </div>
                </div>
            </RichPanelDetailItem>

            {nodeData.dnsName && (
                <RichPanelDetailItem>
                    <div className="d-flex flex-column gap-1 w-100">
                        <span className="d-flex gap-1">
                            <b>DNS Name</b>
                            <PopoverWithHoverWrapper
                                message={
                                    <PopoverMessage description="Defines the address under which specific node will be available." />
                                }
                            >
                                <Icon icon="info" color="info" margin="m-0" />
                            </PopoverWithHoverWrapper>
                        </span>
                        <div className="text-truncate" title={nodeData.dnsName}>
                            {nodeData.dnsName}
                        </div>
                    </div>
                </RichPanelDetailItem>
            )}

            <RichPanelDetailItem>
                <div className="d-flex flex-column gap-1">
                    <span className="d-flex gap-1">
                        <b>HTTPS port</b>
                        <PopoverWithHoverWrapper
                            message={
                                <PopoverMessage
                                    description="Defines the private communication endpoint for clients and browsers. By default,
                                        this value is set to 443."
                                />
                            }
                        >
                            <Icon icon="info" color="info" margin="m-0" />
                        </PopoverWithHoverWrapper>
                    </span>
                    <div>{nodeData.httpPort}</div>
                </div>
            </RichPanelDetailItem>
            <RichPanelDetailItem>
                <div className="d-flex flex-column gap-1">
                    <span className="d-flex gap-1">
                        <b>TCP port</b>
                        <PopoverWithHoverWrapper
                            message={
                                <PopoverMessage
                                    description="Defines the privately accessible TCP endpoint for cluster nodes to communicate
                                        with each other. By default, this value is set to 38888."
                                />
                            }
                        >
                            <Icon icon="info" color="info" margin="m-0" />
                        </PopoverWithHoverWrapper>
                    </span>
                    <div>{nodeData.tcpPort}</div>
                </div>
            </RichPanelDetailItem>
            <RichPanelDetailItem>
                <div className="d-flex flex-column gap-1">
                    <span className="d-flex gap-1">
                        <b>IP address/Hostname</b>
                        <PopoverWithHoverWrapper
                            message={
                                <PopoverMessage description="Defines the private network endpoint where the server is accessible." />
                            }
                        >
                            <Icon size="xs" icon="info" color="info" margin="m-0" />
                        </PopoverWithHoverWrapper>
                    </span>
                    {nodeData.hasExternalConfig && nodeData.externalIpAddress ? (
                        <div>{nodeData.externalIpAddress}</div>
                    ) : (
                        <div>{nodeData.ipAddress.map((x) => x.ipAddress).join(", ")}</div>
                    )}
                </div>
            </RichPanelDetailItem>
        </RichPanelDetails>
    );
}

function NodeDetailsPanelEdit({
    editNodeForm,
    parentControl,
}: {
    editNodeForm: UseFormReturn<NodeEditFormData>;
    parentControl: Control<SetupWizardFormData>;
}) {
    const { control } = editNodeForm;
    const nodeData = useWatch({
        control,
    });

    const {
        domainStep,
        securityStep: { securityOption },
        setupMethodStep: { method: setupMethod },
        selfSignedCertificateStep: { cns, isWildcardCertificate },
        nodeAddressStep: { nodes },
    } = useWatch({
        control: parentControl,
    });

    const isLoopbackOnly = useMemo(() => {
        return nodeData.ipAddress.every((ip) => genUtils.isLocalhostIpAddress(ip.ipAddress));
    }, [nodeData.ipAddress]);

    const { isExternalRequired } = useHostnameDetectionSideEffects({ editNodeForm, parentControl });

    const isDNSVisible = securityOption === "ownCertificate" && !isWildcardCertificate;
    const isPassiveVisible = securityOption === "none" && setupMethod !== "createPackage" && nodes.length === 1;

    const canCustomizeExternalIpsAndPorts = securityOption === "letsEncrypt";
    const canCustomizeExternalTcpPorts = securityOption === "ownCertificate";

    const colWidth = isDNSVisible ? 6 : 4;

    const shouldDisplayUnsafeMode = nodeData.ipAddress.some(
        ({ ipAddress }) => !genUtils.isLocalhostIpAddress(ipAddress)
    );

    return (
        <RichPanelDetails>
            <Form className="w-100 p-2">
                {isPassiveVisible && (
                    <FormGroup>
                        <FormSwitch name="isPassive" control={control}>
                            Start node as Passive, not part of a cluster
                            <PopoverWithHoverWrapper
                                message={
                                    <PopoverMessage description="When enabled, the node remains passive and does not join any cluster. This is useful when the node is meant for monitoring, initialization, or handling setup tasks without actively participating in cluster operations. It can also be used to isolate the node for testing or debugging purposes." />
                                }
                            >
                                <Icon icon="info" margin="ms-1" color="info" />
                            </PopoverWithHoverWrapper>
                        </FormSwitch>
                    </FormGroup>
                )}
                <Row>
                    <Col md={colWidth}>
                        <FormGroup>
                            <FormLabel className="fw-bold">
                                Node tag
                                <PopoverWithHoverWrapper
                                    message={
                                        <PopoverMessage
                                            description="Defines a unique identifier for each node in the cluster."
                                            alert={
                                                <RichAlert variant="info" icon="info">
                                                    Node tag can contain maximum of 4 uppercase letters (A-Z).
                                                </RichAlert>
                                            }
                                        />
                                    }
                                >
                                    <Icon icon="info" margin="ms-1" color="info" />
                                </PopoverWithHoverWrapper>
                            </FormLabel>
                            <FormInput
                                disabled={nodeData.isPassive}
                                placeholder={nodeData.isPassive ? "Node will start in Passive state" : "Enter Node Tag"}
                                type="text"
                                name="nodeTag"
                                control={control}
                            />
                        </FormGroup>
                    </Col>
                    {isDNSVisible && (
                        <Col md={colWidth}>
                            <FormGroup>
                                <FormLabel className="fw-bold">
                                    DNS Name
                                    <PopoverWithHoverWrapper
                                        message={
                                            <PopoverMessage
                                                description={
                                                    <>
                                                        Domain name that will be used to reach the server on this node.
                                                        <br />
                                                        Note: It must be associated with the chosen IP Address below.
                                                    </>
                                                }
                                            />
                                        }
                                    >
                                        <Icon icon="info" margin="ms-1" color="info" />
                                    </PopoverWithHoverWrapper>
                                </FormLabel>
                                <FormSelect
                                    name="dnsName"
                                    control={control}
                                    placeholder="Select DNS Name"
                                    options={cns.map((x) => ({ value: x, label: x }))}
                                />
                            </FormGroup>
                        </Col>
                    )}
                    <Col md={colWidth}>
                        <FormGroup>
                            <FormLabel className="fw-bold">
                                HTTPS port
                                <PopoverWithHoverWrapper
                                    message={
                                        <PopoverMessage
                                            description="Defines the private communication endpoint for clients and browsers.
                                                    By default, this value is set to 443."
                                        />
                                    }
                                >
                                    <Icon icon="info" margin="ms-1" color="info" />
                                </PopoverWithHoverWrapper>
                            </FormLabel>
                            <FormInput type="number" name="httpPort" placeholder="Default: 443" control={control} />
                        </FormGroup>
                    </Col>
                    <Col md={colWidth}>
                        <FormGroup>
                            <FormLabel className="fw-bold">
                                TCP Port
                                <PopoverWithHoverWrapper
                                    message={
                                        <PopoverMessage
                                            description="Defines the privately accessible TCP endpoint for cluster nodes to
                                                    communicate with each other. By default, this value is set to 38888."
                                        />
                                    }
                                >
                                    <Icon icon="info" margin="ms-1" color="info" />
                                </PopoverWithHoverWrapper>
                            </FormLabel>
                            <FormInput type="number" name="tcpPort" placeholder="Default: 38888" control={control} />
                        </FormGroup>
                    </Col>
                </Row>
                <IpAddressList parentControl={parentControl} control={control} />
                <div className="d-flex flex-column gap-1">
                    {isLoopbackOnly && (
                        <RichAlert variant="warning" icon="warning">
                            This node won&#39;t be reachable from outside this machine.
                        </RichAlert>
                    )}
                    {securityOption === "letsEncrypt" && nodeData.ipAddress.length > 0 && (
                        <RichAlert variant="info" icon="info" className="my-3">
                            RavenDB will update the DNS record for{" "}
                            <a>{`${nodeData.nodeTag.toLowerCase()}.${domainStep.domain.toLowerCase()}.${domainStep.rootDomain}`}</a>{" "}
                            to IP{" "}
                            {nodeData.ipAddress.length > 1 && !nodeData.externalIpAddress ? "addresses" : "address"}:{" "}
                            {nodeData.externalIpAddress && nodeData.hasExternalConfig ? (
                                <a>{nodeData.externalIpAddress}</a>
                            ) : nodeData.ipAddress.length > 0 ? (
                                <a>{nodeData.ipAddress.map((x) => x.ipAddress).join(", ")}</a>
                            ) : (
                                <a>&lt;insert IP addresses&gt;</a>
                            )}
                        </RichAlert>
                    )}
                    {securityOption === "none" && shouldDisplayUnsafeMode && (
                        <RichAlert variant="warning" icon="warning">
                            Node IP is not configured for local network. By proceeding, you admit that you understand
                            the risk behind running RavenDB server in the Unsecure mode. Authentication is off, so
                            anyone who can access this IP will be granted <i>administrative privileges</i>.
                        </RichAlert>
                    )}
                </div>

                {(canCustomizeExternalIpsAndPorts || canCustomizeExternalTcpPorts) && (
                    <FormSwitch
                        name="hasExternalConfig"
                        color="primary"
                        disabled={isExternalRequired}
                        control={control}
                    >
                        <span className="d-flex gap-1">
                            Customize external IP and ports
                            <PopoverWithHoverWrapper
                                message={
                                    <PopoverMessage
                                        description="External overrides allow you to specify an alternative IP address, hostname, or
                                        HTTPS port that clients should use instead of the default settings."
                                    />
                                }
                            >
                                <Icon icon="info" margin="m-0" color="info" />
                            </PopoverWithHoverWrapper>
                        </span>
                    </FormSwitch>
                )}
                <Collapse in={nodeData.hasExternalConfig}>
                    <div className="hstack gap-1">
                        <EditFormExternalAddressInputs
                            control={control}
                            canCustomizeExternalIpsAndPorts={canCustomizeExternalIpsAndPorts}
                            canCustomizeExternalTcpPorts={canCustomizeExternalTcpPorts}
                        />
                    </div>
                </Collapse>
            </Form>
        </RichPanelDetails>
    );
}

function EditFormExternalAddressInputs({
    control,
    canCustomizeExternalIpsAndPorts,
    canCustomizeExternalTcpPorts,
}: {
    control: Control<NodeEditFormData>;
    canCustomizeExternalIpsAndPorts: boolean;
    canCustomizeExternalTcpPorts: boolean;
}) {
    return (
        <>
            <RichPanelDetailItem className="flex-grow">
                <FormGroup className="vstack w-100">
                    <FormLabel className="fw-bold">
                        <span className="d-flex gap-1">
                            External IP address
                            <PopoverWithHoverWrapper
                                message={
                                    <PopoverMessage
                                        description="Defines the public network endpoint from which the requests will be
                                            forwarded to the private IP address (which RavenDB listens on)."
                                    />
                                }
                            >
                                <Icon icon="info" margin="ms-1" color="info" />
                            </PopoverWithHoverWrapper>
                        </span>
                    </FormLabel>
                    <FormInput
                        type="text"
                        name="externalIpAddress"
                        placeholder="Enter Server IP A address/hostname"
                        control={control}
                    />
                </FormGroup>
            </RichPanelDetailItem>
            {canCustomizeExternalIpsAndPorts && (
                <RichPanelDetailItem className="flex-grow">
                    <FormGroup className="vstack w-100">
                        <FormLabel className="fw-bold">
                            <span className="d-flex gap-1">
                                External HTTPS port <OptionalLabel />
                                <PopoverWithHoverWrapper
                                    message={
                                        <PopoverMessage
                                            description="Defines the public HTTPS endpoint that clients and browsers should use
                                                instead of default binding."
                                        />
                                    }
                                >
                                    <Icon icon="info" margin="ms-1" color="info" />
                                </PopoverWithHoverWrapper>
                            </span>
                        </FormLabel>
                        <FormInput
                            type="number"
                            name="externalHttpPort"
                            placeholder="Enter external HTTPS port"
                            control={control}
                        />
                    </FormGroup>
                </RichPanelDetailItem>
            )}
            {(canCustomizeExternalIpsAndPorts || canCustomizeExternalTcpPorts) && (
                <RichPanelDetailItem className="flex-grow">
                    <FormGroup className="vstack w-100">
                        <FormLabel className="fw-bold">
                            <span className="d-flex gap-1">
                                External TCP Port <OptionalLabel />
                                <PopoverWithHoverWrapper
                                    message={
                                        <PopoverMessage
                                            description="Defines the publicly accessible TCP endpoint for inter-node communication
                                                and client connections."
                                        />
                                    }
                                >
                                    <Icon icon="info" margin="ms-1" color="info" />
                                </PopoverWithHoverWrapper>
                            </span>
                        </FormLabel>
                        <FormInput
                            type="number"
                            name="externalTcpPort"
                            placeholder="Enter external TCP port"
                            control={control}
                        />
                    </FormGroup>
                </RichPanelDetailItem>
            )}
        </>
    );
}

interface AddAnotherNodeProps {
    onAddNode: () => void;
}

function AddAnotherNode({ onAddNode }: AddAnotherNodeProps) {
    const { getValues } = useFormContext<SetupWizardFormData>();

    const licenseKeyStep = getValues("licenseKeyStep");
    const nodeData = getValues("nodeAddressStep.nodes");

    const maxClusterSize = licenseKeyStep?.licenseInfo?.maxClusterSize ?? 1; // Default to 1 (agpl license) if license is not available
    const isMaxClusterNodes = maxClusterSize === nodeData?.length;

    return (
        <div
            className={classNames(
                "w-100 d-flex rounded justify-content-center mb-2 align-items-center border-dashed border-2",
                isMaxClusterNodes ? "border-secondary" : "border-node"
            )}
        >
            <Button
                disabled={isMaxClusterNodes}
                variant={isMaxClusterNodes ? "outline-secondary" : "outline-node"}
                className="rounded-pill my-4"
                onClick={onAddNode}
            >
                <Icon icon="node-add" />
                Add another node
            </Button>
        </div>
    );
}

interface UseHostnameDetectionSideEffectsProps {
    editNodeForm: UseFormReturn<NodeEditFormData>;
    parentControl: Control<SetupWizardFormData>;
}

function useHostnameDetectionSideEffects({ editNodeForm, parentControl }: UseHostnameDetectionSideEffectsProps) {
    const { setValue, control, watch } = editNodeForm;
    const nodeData = useWatch({ control });
    const {
        securityStep: { securityOption },
    } = useWatch({ control: parentControl });

    const isHostname = useMemo(() => {
        return (
            nodeData.ipAddress.some((ip) => genUtils.isHostname(ip.ipAddress)) && securityOption === "ownCertificate"
        );
    }, [nodeData.ipAddress]); // eslint-disable-line react-hooks/exhaustive-deps

    const hasBindAllIp = useMemo(() => {
        return nodeData.ipAddress.some((ip) => genUtils.isBindAllIpAddress(ip.ipAddress));
    }, [nodeData.ipAddress]);

    const ipsContainHostname = useMemo(() => {
        return nodeData.ipAddress.some((ip) => genUtils.isHostname(ip.ipAddress));
    }, [nodeData.ipAddress]);

    useEffect(() => {
        const { unsubscribe } = watch((values) => {
            const ipsContainHostname = values.ipAddress.some((ip) => genUtils.isHostname(ip.ipAddress));
            // && securityOption === "ownCertificate";

            const hasBindAllIp = values.ipAddress.some((ip) => genUtils.isBindAllIpAddress(ip?.ipAddress));

            const requirePublicIpWhenBindAllUsed = securityOption === "letsEncrypt" && hasBindAllIp;

            // when node is passive, we need to clear the nodeTag value to show placeholder
            if (values.isPassive) {
                setValue("nodeTag", "");
            }

            // case: when user enter 0.0.0.0 ip address, and then remove it and uncheck the checkbox (external config). We need to clear the errors for external config.
            // if (!values.hasExternalConfig) {
            //     clearErrors(["externalIpAddress", "externalHttpPort", "externalTcpPort"]);
            // }

            // Automatically enable external configuration in these scenarios:
            // 1. When using Let's Encrypt with hostnames instead of IP addresses
            // 2. When using Let's Encrypt with bind-all address (0.0.0.0)
            // 3. When bind-all IP is used with Let's Encrypt (requires public IP specification)

            const isLetsEncryptWithHostname = ipsContainHostname && securityOption === "letsEncrypt";
            const isLetsEncryptWithBindAll = hasBindAllIp && securityOption === "letsEncrypt";
            const needsExternalConfig =
                (isLetsEncryptWithHostname || isLetsEncryptWithBindAll) && !values.hasExternalConfig;

            if (needsExternalConfig || requirePublicIpWhenBindAllUsed) {
                setValue("hasExternalConfig", true, {
                    shouldValidate: true,
                });
            }
        });

        return () => unsubscribe();
    }, [watch]); // eslint-disable-line react-hooks/exhaustive-deps

    return {
        isHostname,
        isExternalRequired: isHostname || hasBindAllIp || ipsContainHostname,
    };
}

function IpAddressList({
    control,
    parentControl,
}: {
    control: Control<NodeEditFormData>;
    parentControl: Control<SetupWizardFormData>;
}) {
    const { setupWizardService } = useServices();
    const { append, remove, fields } = useFieldArray<NodeEditFormData>({
        control,
        name: "ipAddress",
    });

    const {
        securityStep: { securityOption },
    } = useWatch({ control: parentControl });

    const addIpAddress = () => {
        append({ ipAddress: "" });
    };

    const asyncGetSetupLocalNodeIps = useAsync(async () => setupWizardService.getSetupLocalNodeIps(), []);

    const localIpAddresses: SelectOption[] = (asyncGetSetupLocalNodeIps.result || []).map((ip) => ({
        label: ip,
        value: ip,
    }));

    const ipAddressesOptions: SelectOption[] = [{ label: "0.0.0.0", value: "0.0.0.0" }, ...localIpAddresses];

    return (
        <FormGroup className="w-100">
            <FormLabel className="fw-bold w-100">
                <div className="hstack justify-content-between">
                    <span className="d-flex gap-1">
                        IP address/Hostname
                        <PopoverWithHoverWrapper
                            message={
                                <PopoverMessage description="Defines the private network endpoint where the server is accessible." />
                            }
                        >
                            <Icon icon="info" margin="m-0" color="info" />
                        </PopoverWithHoverWrapper>
                    </span>
                    {securityOption !== "none" && (
                        <Button variant="link" className="text-primary text-right fw-bold" onClick={addIpAddress}>
                            <Icon icon="plus" margin="me-1" color="primary" />
                            Add another IP Address
                        </Button>
                    )}
                </div>
            </FormLabel>
            <div className="vstack gap-2">
                {fields.map((field, ipIndex) => (
                    <InputGroup key={field.id}>
                        <FormSelectCreatable
                            control={control}
                            placeholder="Enter Server IP A address/hostname"
                            isLoading={asyncGetSetupLocalNodeIps.loading}
                            name={`ipAddress.${ipIndex}.ipAddress`}
                            options={ipAddressesOptions}
                        />
                        {ipIndex > 0 && (
                            <Button variant="outline-danger" size="sm" onClick={() => remove(ipIndex)}>
                                <Icon icon="trash" margin="m-0" />
                            </Button>
                        )}
                    </InputGroup>
                ))}
            </div>
        </FormGroup>
    );
}

export function SetupWizardNodeAddressStepFooter() {
    const { setValue, getValues } = useFormContext<SetupWizardFormData>();

    const nodeData = getValues("nodeAddressStep.nodes");

    const isEditing = nodeData?.some((node) => node.isEditing);

    const confirm = useConfirm();

    const areAllNodesIdentical = (() => {
        if (!nodeData || nodeData.length <= 1) {
            return false;
        }

        const firstNode = nodeData[0];

        const firstNodeIps = firstNode.ipAddress
            .map((ip) => ip.ipAddress)
            .sort()
            .join(",");
        const firstNodeTcpPort = firstNode.tcpPort;
        const firstNodeHttpPort = firstNode.httpPort;

        return nodeData.every((node) => {
            const nodeIps = node.ipAddress
                .map((ip) => ip.ipAddress)
                .sort()
                .join(",");

            return nodeIps === firstNodeIps && node.tcpPort === firstNodeTcpPort && node.httpPort === firstNodeHttpPort;
        });
    })();

    const hasDuplicateNodeConfigurations = (() => {
        const configurationMap = new Map();

        for (const node of nodeData) {
            for (const ip of node.ipAddress) {
                const configKey = `${ip.ipAddress}-${node.httpPort}-${node.tcpPort}`;

                if (configurationMap.has(configKey)) {
                    return true;
                }

                configurationMap.set(configKey, true);
            }
        }

        return false;
    })();

    const handleContinue = async () => {
        const nodeCount = nodeData.length;

        if (areAllNodesIdentical) {
            const firstNode = nodeData[0];
            const isConfirmed = await confirm({
                title: "Identical node configurations",
                message: (
                    <>
                        All nodes in the cluster are configured with the same settings:
                        <ul>
                            <li>IP address: {firstNode.ipAddress[0]?.ipAddress}</li>
                            <li>TCP port: {firstNode.tcpPort}</li>
                            <li>HTTPS port: {firstNode.httpPort}</li>
                        </ul>
                        Please confirm that these settings are correct before proceeding to the next step.
                    </>
                ),
                icon: "warning",
                confirmText: "Proceed",
                actionColor: "warning",
                size: "lg",
            });

            if (!isConfirmed) {
                return;
            }
        } else if (hasDuplicateNodeConfigurations) {
            const isConfirmed = await confirm({
                title: "Duplicate node configurations",
                message:
                    "You have multiple nodes with identical IP address, TCP port, and HTTPS port configurations. " +
                    "This may cause conflicts in your cluster. Are you sure you want to proceed?",
                icon: "warning",
                confirmText: "Proceed anyway",
                actionColor: "warning",
                size: "lg",
            });

            if (!isConfirmed) {
                return;
            }
        }

        // Then check for even node count
        if (nodeCount % 2 === 0) {
            const isConfirmed = await confirm({
                title: "Confirm even node count",
                message: `You've chosen an even number of nodes for your cluster. For optimal replication and database performance, an odd number of nodes is usually recommended.
                        Are you sure you want to proceed with an even node count?`,
                icon: "warning",
                confirmText: "Proceed",
                actionColor: "warning",
                size: "lg",
            });

            if (isConfirmed) {
                setValue("currentStep", "Additional settings");
            }
        } else {
            setValue("currentStep", "Additional settings");
        }
    };

    const handleBack = () => {
        const setupWizardFormData = getValues();
        switch (setupWizardFormData.securityStep.securityOption) {
            case "letsEncrypt":
            case "ownCertificate":
                setValue("currentStep", "Domain");
                break;
            case "none":
                setValue("currentStep", "Security");
                break;
        }
    };

    return (
        <div className="hstack justify-content-between">
            <Button variant="secondary" className="rounded-pill" onClick={handleBack}>
                <Icon icon="arrow-left" /> Back
            </Button>
            <Button disabled={isEditing} variant="primary" className="rounded-pill" onClick={handleContinue}>
                Continue <Icon icon="arrow-right" margin="m-0" />
            </Button>
        </div>
    );
}

export const ipAddressFormSchema = yup.object().shape({
    ipAddress: yup
        .string()
        .test(
            "not-url",
            "Expected valid IP Address/Hostname, not URL",
            (value) => !value?.startsWith("http://") && !value?.startsWith("https://")
        )
        .test(
            "not-localhost-on-docker",
            "A localhost IP Address is not allowed when running on Docker",
            function (value) {
                const { isDocker } = this.options.context || {};
                return (isDocker && !genUtils.isLocalhostIpAddress(value)) || !isDocker;
            }
        )
        .test("valid-ip-in-unsecure-mode", "In unsecure mode you cannot use hostnames", function (value) {
            const { securityOption } = this.options.context as {
                nodeAddressStep: SetupWizardFormData["nodeAddressStep"];
                securityOption: SetupWizardSecurityOption;
                currentIndex: number;
            };

            const isHostname = genUtils.isHostname(value);
            const isUnsecureMode = securityOption === "none";

            return !isHostname || !isUnsecureMode;
        })
        .required("Please define at least one IP for this node"),
});

export const nodeEditFormSchema = yup.object({
    isPassive: yup.boolean().default(false),
    nodeTag: yup.string().when("isPassive", {
        is: false,
        then: (schema) =>
            schema
                .required("Node tag is required")
                .matches(/^[A-Z]{1,4}$/, "Node tag must be 1 to 4 uppercase letters")
                .test("unique", "Node tag must be unique", function (value) {
                    const { nodeAddressStep, currentIndex } = this.options.context as {
                        nodeAddressStep: SetupWizardFormData["nodeAddressStep"];
                        setupWizardFormData: SetupWizardFormData;
                        currentIndex: number;
                    };

                    if (!nodeAddressStep || !nodeAddressStep.nodes) {
                        return true;
                    }

                    return (
                        nodeAddressStep.nodes.findIndex(
                            (node, idx) => node.nodeTag === value && idx !== currentIndex
                        ) === -1
                    );
                })
                .test("reserved-tag", "This node tag is reserved", (value) => {
                    return value !== "RAFT";
                }),
        otherwise: (schema) => schema,
    }),
    dnsName: yup.string().when("$securityOption", {
        is: "ownCertificate",
        then: (schema) => schema.required("DNS name is required"),
        otherwise: (schema) => schema,
    }),
    httpPort: yup
        .number()
        .default(8080)
        .typeError("HTTPS port must be a number")
        .min(1, "Port must be greater than 0")
        .max(65535, "Port must be less than 65536")
        .required("HTTPS port is required"),
    tcpPort: yup
        .number()
        .default(38888)
        .nullable()
        .min(1, "Port must be greater than 0")
        .max(65535, "Port must be less than 65536")
        .required("TCP port is required"),
    ipAddress: yup.array().of(ipAddressFormSchema).min(1, "At least one IP address is required"),
    hasExternalConfig: yup.boolean().default(false),
    externalIpAddress: yup.string().when(["hasExternalConfig", "ipAddress", "$securityOption"], {
        is: function (
            hasExtConfig: boolean,
            ipAddresses: NodeEditFormData["ipAddress"],
            securityOption: SetupWizardSecurityOption
        ) {
            if (securityOption === "none") {
                return false;
            }

            if (!hasExtConfig) {
                return false;
            }

            if (!ipAddresses?.length) {
                return false;
            }

            return ipAddresses.some((ip) => {
                const address = ip?.ipAddress;
                if (!address) {
                    return false;
                }
                if (address === "0.0.0.0") {
                    return true;
                }

                return !genUtils.regexIPv4.test(address);
            });
        },
        then: (schema) =>
            schema
                .required("External IP address is required when an address contains Hostname or 0.0.0.0")
                .test(
                    "not-url",
                    "Expected valid IP Address/Hostname, not URL",
                    (value) => !value?.startsWith("http://") && !value?.startsWith("https://")
                )
                .test(
                    "valid-ip-without-port",
                    "Please enter a valid IP address without port",
                    (value) => !value || (!/:\d+$/.test(value) && genUtils.regexIPv4.test(value))
                ),
        otherwise: (schema) => schema.nullable(),
    }),
    externalHttpPort: yup
        .number()
        .nullable()
        .when("hasExternalConfig", {
            is: true,
            then: (schema) => schema.min(1, "Port must be greater than 0").max(65535, "Port must be less than 65536"),
        }),
    externalTcpPort: yup
        .number()
        .nullable()
        .when("hasExternalConfig", {
            is: true,
            then: (schema) => schema.min(1, "Port must be greater than 0").max(65535, "Port must be less than 65536"),
        }),
});

export type NodeEditFormData = yup.InferType<typeof nodeEditFormSchema>;

