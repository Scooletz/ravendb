import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import Button from "react-bootstrap/Button";
import { Icon } from "components/common/Icon";
import FileDropzone from "components/common/FileDropzone";
import { FormGroup, FormLabel, FormSelect } from "components/common/Form";
import { useAsync } from "react-async-hook";
import { useServices } from "components/hooks/useServices";
import messagePublisher from "common/messagePublisher";
import { SelectOption } from "components/common/select/Select";
import { useEffect, useMemo } from "react";
import RichAlert from "components/common/RichAlert";
import PopoverWithHoverWrapper from "components/common/PopoverWithHoverWrapper";
import { PopoverMessage } from "components/setupWizard/steps/SetupWizardNodeAddressStep";
import { setupWizardGA4Prefixes } from "components/setupWizard/utils/setupWizardConstants";
import { useEventsCollector } from "hooks/useEventsCollector";

export function SetupWizardUsePackageStep() {
    const { control, setValue, watch } = useFormContext<SetupWizardFormData>();
    const {
        usePackageStep: { fileZip, isZipValid },
    } = useWatch({ control });

    const { setupWizardService } = useServices();

    const asyncExtractNodeInfos = useAsync(async () => {
        if (!fileZip) {
            return [];
        }

        return await setupWizardService.extractNodesInfoFromPackage(fileZip);
    }, [fileZip]);

    const nodeInfos = useMemo(() => {
        return asyncExtractNodeInfos.result ?? [];
    }, [asyncExtractNodeInfos.result]);

    const nodeOptions: SelectOption[] = nodeInfos.map((nodeInfo) => ({
        value: nodeInfo.Tag,
        label: `Node ${nodeInfo.Tag} (${nodeInfo.PublicServerUrl || nodeInfo.ServerUrl})`,
    }));

    useEffect(() => {
        if (nodeInfos.length === 1) {
            const singleNode = nodeInfos[0];
            setValue("usePackageStep.nodeTag", singleNode.Tag);
        }
    }, [nodeInfos, setValue]);

    useEffect(() => {
        const subscribe = watch((values, { name }) => {
            if (name === "usePackageStep.nodeTag") {
                const nodeInfo = nodeInfos.find((x) => x.Tag === values.usePackageStep.nodeTag);
                setValue("usePackageStep.publicServerUrl", nodeInfo?.PublicServerUrl);
                setValue("usePackageStep.serverUrl", nodeInfo?.ServerUrl);
            }
        });

        return () => {
            subscribe.unsubscribe();
        };
    }, [watch, setValue, nodeInfos]);

    // Handle isZipSecure
    useEffect(() => {
        if (!nodeInfos.length) {
            setValue("usePackageStep.isZipSecure", false);
            return;
        }

        const isSecure = nodeInfos.every(isNodeSecure);
        setValue("usePackageStep.isZipSecure", isSecure);
    }, [nodeInfos, setValue]);

    // Handle isZipValid
    useEffect(() => {
        if (!nodeInfos.length) {
            setValue("usePackageStep.isZipValid", true);
            return;
        }

        const firstNode = nodeInfos[0];

        if (firstNode.PublicServerUrl) {
            setValue("usePackageStep.isZipSecure", nodeInfos.every(isNodeSecure));
        } else {
            setValue("usePackageStep.isZipSecure", nodeInfos.every(isNodeUnsecure));
        }
    }, [nodeInfos, setValue]);

    const handleFileChange = (files: File[]) => {
        const file = files[0];
        const fileName = file.name;
        const reader = new FileReader();

        reader.onload = function () {
            const textResult = String(reader.result);

            const isFileSelected = fileName ? !!fileName.trim() : false;

            if (!isFileSelected) {
                clearFile();
                messagePublisher.reportError("Failed to load file");
                return;
            }

            setValue("usePackageStep.fileName", fileName.split(/(\\|\/)/g).pop());

            // dataUrl has following format: data:;base64,PD94bW... trim on first comma
            setValue("usePackageStep.fileZip", textResult.substring(textResult.indexOf(",") + 1));
        };

        reader.onerror = function () {
            clearFile();
            messagePublisher.reportError("Failed to load file", reader.error.message);
        };

        reader.readAsDataURL(file);
    };

    const clearFile = () => {
        setValue("usePackageStep.fileName", "");
        setValue("usePackageStep.fileZip", "");
    };

    return (
        <div>
            <h2 className="mb-1">Use setup package</h2>
            <p className="mb-4 text-muted">
                Here you can use an existing package to set up selected nodes in your cluster.
            </p>
            <FormGroup>
                <FileDropzone onChange={handleFileChange} validExtensions={["zip"]} maxFiles={1} />
            </FormGroup>
            {fileZip && asyncExtractNodeInfos.status === "success" && (
                <FormGroup>
                    <FormLabel className="hstack">
                        <div>Node tag</div>
                        <PopoverWithHoverWrapper
                            message={
                                <PopoverMessage description="Select which node of the predefined cluster would you like to set up now." />
                            }
                        >
                            <Icon icon="info-new" />
                        </PopoverWithHoverWrapper>
                    </FormLabel>
                    <FormSelect
                        control={control}
                        name="usePackageStep.nodeTag"
                        placeholder="Select node tag"
                        options={nodeOptions}
                    />
                </FormGroup>
            )}
            {!isZipValid && <RichAlert variant="danger">Invalid nodes configuration in zip file.</RichAlert>}
        </div>
    );
}

export function SetupWizardUsePackageStepFooter() {
    const { setValue, control } = useFormContext<SetupWizardFormData>();
    const { reportEvent } = useEventsCollector();

    const {
        usePackageStep: { isZipValid },
    } = useWatch({ control });

    const handleContinue = () => {
        setValue("currentStep", "Finish");
    };

    const handleBack = () => {
        reportEvent(setupWizardGA4Prefixes.usePackageStep, "back");
        setValue("currentStep", "Setup method");
    };

    return (
        <div className="d-flex justify-content-between">
            <Button variant="secondary" className="rounded-pill" onClick={handleBack}>
                <Icon icon="arrow-left" /> Back
            </Button>
            <Button variant="primary" className="rounded-pill" onClick={handleContinue} disabled={!isZipValid}>
                Continue <Icon icon="arrow-right" margin="m-0" />
            </Button>
        </div>
    );
}

const isNodeSecure = (node: Raven.Server.Web.System.ConfigurationNodeInfo): boolean => {
    return node.PublicServerUrl && node.PublicServerUrl.startsWith(securePrefix);
};

const isNodeUnsecure = (node: Raven.Server.Web.System.ConfigurationNodeInfo): boolean => {
    return node.ServerUrl && node.ServerUrl.startsWith(unsecurePrefix);
};

const securePrefix = "https://";
const unsecurePrefix = "http://";
