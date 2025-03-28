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

export function SetupWizardUsePackageStep() {
    const { control, setValue, watch } = useFormContext<SetupWizardFormData>();
    const {
        usePackageStep: { fileZip },
    } = useWatch({ control });

    const { setupWizardService } = useServices();

    // missing isZipSecure from continueSetup.ts (but we handle it later step)

    // TODO add what is this tooltip
    // TODO maybe add a tooltip explaining why node tag is disabled

    const asyncExtractNodeInfos = useAsync(async () => {
        const nodesInfo = await setupWizardService.extractNodesInfoFromPackage(fileZip);

        return nodesInfo;
    }, [fileZip]);

    const nodeInfos = useMemo(() => {
        return asyncExtractNodeInfos.result ?? [];
    }, [asyncExtractNodeInfos.result]);

    const nodeOptions: SelectOption[] = nodeInfos.map((nodeInfo) => ({
        value: nodeInfo.Tag,
        label: `Node ${nodeInfo.Tag} (${nodeInfo.PublicServerUrl || nodeInfo.ServerUrl})`,
    }));

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
            <h2>Use setup package</h2>
            <p>Here you can use an existing package to set up selected nodes in your cluster.</p>
            <FormGroup>
                <FileDropzone onChange={handleFileChange} validExtensions={["zip"]} maxFiles={1} />
            </FormGroup>
            {asyncExtractNodeInfos.status === "success" && (
                <FormGroup>
                    <FormLabel className="hstack justify-content-between">
                        <div>Node tag</div>
                        <div className="text-info">
                            <Icon icon="info" />
                            What is this?
                        </div>
                    </FormLabel>
                    <FormSelect
                        control={control}
                        name="usePackageStep.nodeTag"
                        placeholder="Select node tag"
                        options={nodeOptions}
                    />
                </FormGroup>
            )}
        </div>
    );
}

export function SetupWizardUsePackageStepFooter() {
    const { setValue } = useFormContext<SetupWizardFormData>();

    const handleContinue = () => {
        setValue("currentStep", "Summary");
    };

    return (
        <div className="hstack justify-content-end">
            <Button variant="primary" className="rounded-pill" onClick={handleContinue}>
                Continue <Icon icon="arrow-right" margin="m-0" />
            </Button>
        </div>
    );
}
